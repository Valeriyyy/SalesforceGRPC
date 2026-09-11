using Application.Bindings;
using Application.Connections;
using Database.Models;
using Database.Repositories.Interfaces;
using Database.Targets;
using DTO;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Application.Targets;

/// <summary>
/// Thrown when a save would change which database the Target Connection points at.
/// </summary>
/// <remarks>
/// A conflict rather than a validation failure: the request was well formed, and the refusal is about what
/// it would destroy. Repointing is its own operation, with a preview and a confirmation, and the API maps
/// this to a response that says so.
/// </remarks>
public sealed class TargetConnectionIdentityChangedException : InvalidOperationException {
    public TargetConnectionIdentityChangedException(TargetConnectionIdentity stored, TargetConnectionIdentity requested)
        : base("These details point at a different database " +
               $"({Describe(requested)} rather than {Describe(stored)}), which would invalidate every Binding. " +
               "Use the repoint operation, which previews what will be destroyed and asks for confirmation.") { }

    private static string Describe(TargetConnectionIdentity identity) =>
        identity.FilePath is { } file ? $"{identity.Engine} {file}" : $"{identity.Engine} {identity.Host}/{identity.DatabaseName}";
}

/// <summary>
/// Setting up, proving, editing and repointing the Target Connection.
/// </summary>
/// <remarks>
/// Owns the whole user-facing lifecycle. The one state change that happens elsewhere is the worker recording
/// a write failure or a recovery, because the worker is where the connection is genuinely exercised.
/// </remarks>
public interface ITargetConnectionService {
    Task<TargetConnectionDTO> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Every engine, with the fields it asks for and whether it can be used. What a form renders from.</summary>
    IReadOnlyList<EngineDefinitionDTO> GetEngines();

    /// <summary>
    /// Stores the details and proves them. Creates the Target Connection, or edits the existing one
    /// non-destructively.
    /// </summary>
    /// <remarks>
    /// Always persists. A proof that fails leaves the connection Incomplete with the error — a user whose
    /// database is briefly unreachable must still be able to record what they typed.
    /// </remarks>
    Task<TargetConnectionDTO> SaveAsync(SaveTargetConnectionDTO request, CancellationToken cancellationToken = default);

    /// <summary>Proves the stored Target Connection again and records the outcome. The user's repair button.</summary>
    Task<TargetConnectionDTO> RetestAsync(CancellationToken cancellationToken = default);

    /// <summary>What a repoint would destroy. Call this before confirming one.</summary>
    Task<RepointPreviewDTO> PreviewRepointAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points the application at a different database, destroying every Binding.
    /// </summary>
    /// <remarks>
    /// All or nothing, unlike save: the new details are proved <em>before</em> anything is destroyed, because
    /// destroying Bindings and then discovering the new database is unreachable leaves the user with neither.
    /// </remarks>
    Task<TargetConnectionDTO> RepointAsync(RepointTargetConnectionDTO request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TargetConnectionService : ITargetConnectionService {
    private readonly ITargetConnectionRepository _repository;
    private readonly ITargetConnectionProvider _provider;
    private readonly ITargetEngineCatalog _engines;
    private readonly ISecretProtector _protector;
    private readonly IConfigurationChangeSignal _changeSignal;
    private readonly ILogger<TargetConnectionService> _logger;
    private readonly TimeProvider _time;

    public TargetConnectionService(ITargetConnectionRepository repository, ITargetConnectionProvider provider,
        ITargetEngineCatalog engines, ISecretProtector protector, IConfigurationChangeSignal changeSignal,
        ILogger<TargetConnectionService> logger, TimeProvider time) {
        _repository = repository;
        _provider = provider;
        _engines = engines;
        _protector = protector;
        _changeSignal = changeSignal;
        _logger = logger;
        _time = time;
    }

    public async Task<TargetConnectionDTO> GetAsync(CancellationToken cancellationToken = default) {
        var connection = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(connection);
    }

    public IReadOnlyList<EngineDefinitionDTO> GetEngines() =>
        _engines.All.Select(profile => new EngineDefinitionDTO {
            Engine = profile.Engine.ToString(),
            IsAvailable = profile.IsAvailable,
            UnavailableReason = profile.UnavailableReason,
            Fields = profile.Fields.Select(f => new FieldDefinitionDTO {
                Name = f.Name,
                Label = f.Label,
                Kind = f.Kind.ToString(),
                Required = f.Required,
                Default = f.Default,
                Choices = f.Choices?.ToList()
            }).ToList()
        }).ToList();

    public async Task<TargetConnectionDTO> SaveAsync(SaveTargetConnectionDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        var (profile, details) = Validate(request);
        var model = ToModel(details);

        var existing = await _provider.GetAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.Identity != model.Identity) {
            throw new TargetConnectionIdentityChangedException(existing.Identity, model.Identity);
        }

        await _repository.UpsertAsync(model, cancellationToken).ConfigureAwait(false);
        _provider.Invalidate();

        // The upsert reset the row to Incomplete, so a failed proof here stays Incomplete: what was just
        // typed has never worked, whatever the previous details did.
        await ProveAsync(profile, details, ConnectionState.Incomplete, cancellationToken).ConfigureAwait(false);

        _provider.Invalidate();
        _changeSignal.Signal();

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TargetConnectionDTO> RetestAsync(CancellationToken cancellationToken = default) {
        var stored = await _provider.GetAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new NoTargetDatabaseException();

        var profile = _engines.For(stored.Engine);
        await ProveAsync(profile, Decrypt(stored), stored.ConnectionState, cancellationToken).ConfigureAwait(false);

        _provider.Invalidate();
        _changeSignal.Signal();

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RepointPreviewDTO> PreviewRepointAsync(CancellationToken cancellationToken = default) {
        var counts = await _repository.CountBindingsAsync(cancellationToken).ConfigureAwait(false);
        return new RepointPreviewDTO { Bindings = counts.Bindings, FieldMappings = counts.FieldMappings };
    }

    public async Task<TargetConnectionDTO> RepointAsync(RepointTargetConnectionDTO request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        _ = await _provider.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NoTargetDatabaseException();

        var (profile, details) = Validate(request);

        // The confirmation names counts so a caller that has not looked at what it is destroying cannot
        // satisfy it by accident, and so a Binding created since the preview aborts rather than vanishes.
        var counts = await _repository.CountBindingsAsync(cancellationToken).ConfigureAwait(false);
        if (request.ExpectedBindings != counts.Bindings || request.ExpectedFieldMappings != counts.FieldMappings) {
            throw new ValidationException(
                $"The repoint was confirmed for {request.ExpectedBindings} Binding(s) and " +
                $"{request.ExpectedFieldMappings} Field Mapping(s), but there are now {counts.Bindings} " +
                $"and {counts.FieldMappings}. Nothing has been destroyed. Review the preview and confirm again.");
        }

        try {
            var repository = profile.CreateRepository(profile.BuildConnectionString(details));
            await repository.GetSchemaMetadata(cancellationToken: cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            throw new ValidationException(
                $"{Summarise(ex)} Nothing has been destroyed. The database said: {ex.Message}");
        }

        var (_, destroyed) = await _repository.RepointAsync(ToModel(details), cancellationToken).ConfigureAwait(false);
        await _repository.RecordSuccessAsync(_time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);

        _provider.Invalidate();
        _changeSignal.Signal();

        _logger.LogWarning("Repointed the Target Connection; {Bindings} Binding(s) and {Mappings} Field Mapping(s) were destroyed",
            destroyed.Bindings, destroyed.FieldMappings);

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the database and reads its schema metadata, recording the outcome as state.
    /// </summary>
    /// <remarks>
    /// Schema metadata rather than <c>SELECT 1</c>, because that is what configuring a Binding depends on:
    /// credentials that connect but cannot see any tables must fail here, not at the first Binding.
    /// </remarks>
    private async Task ProveAsync(ITargetEngineProfile profile, TargetConnectionDetails details,
        ConnectionState stateBefore, CancellationToken cancellationToken) {
        var now = _time.GetUtcNow().UtcDateTime;

        try {
            var repository = profile.CreateRepository(profile.BuildConnectionString(details));
            await repository.GetSchemaMetadata(cancellationToken: cancellationToken).ConfigureAwait(false);

            await _repository.RecordSuccessAsync(now, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Target Connection proved against {Engine} at {Address}", details.Engine, details.Host ?? details.FilePath);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // Never proved is not the same as broken. A connection that has never worked stays Incomplete
            // ("what you typed did not work"); one that used to work becomes Failed ("something changed").
            var summary = Summarise(ex);
            if (stateBefore is ConnectionState.Connected or ConnectionState.Failed) {
                await _repository.RecordFailureAsync(summary, ex.Message, now, cancellationToken).ConfigureAwait(false);
                _logger.LogError(ex, "The Target Connection failed its proof: {Summary}", summary);
            } else {
                await _repository.RecordIncompleteAsync(summary, ex.Message, now, cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(ex, "The Target Connection could not be proved: {Summary}", summary);
            }
        }
    }

    /// <summary>
    /// A one-line summary for the read model. The driver's own message is stored separately and shown
    /// alongside it, so this does not have to be complete — only useful.
    /// </summary>
    private static string Summarise(Exception ex) => ex switch {
        NotImplementedException => "The driver for this engine is not implemented.",
        _ => "The Target Database could not be reached, or its schema could not be read, with these details."
    };

    private (ITargetEngineProfile Profile, TargetConnectionDetails Details) Validate(SaveTargetConnectionDTO request) {
        if (!Enum.TryParse<TargetDatabaseEngine>(request.Engine, ignoreCase: true, out var engine)) {
            throw new ValidationException(
                $"'{request.Engine}' is not a supported engine. Choose one of: {string.Join(", ", Enum.GetNames<TargetDatabaseEngine>())}.");
        }

        var profile = _engines.For(engine);
        if (!profile.IsAvailable) {
            throw new ValidationException(
                $"{engine} cannot be used as a Target Database: {profile.UnavailableReason}");
        }

        if (!_protector.IsAvailable) {
            // Refused rather than stored in the clear. An application that protects nothing while claiming
            // to is worse than one that says it cannot yet.
            throw new ValidationException(
                "This application cannot store a database password because no protecting key is available. " +
                "Supply a protecting certificate through DataProtection:ProtectingCertificate and restart. " +
                $"Looked at: {_protector.ProtectingKeyDescription}.");
        }

        var details = new TargetConnectionDetails {
            Engine = engine,
            Host = request.Host?.Trim(),
            Port = request.Port,
            DatabaseName = request.DatabaseName?.Trim(),
            Username = request.Username?.Trim(),
            Password = request.Password,
            FilePath = request.FilePath?.Trim(),
            Options = request.Options
        };

        var errors = profile.Validate(details);
        if (errors.Count > 0) {
            throw new ValidationException(string.Join(" ", errors));
        }

        return (profile, details);
    }

    private TargetConnectionDetails Decrypt(TargetConnection connection) => new() {
        Engine = connection.Engine,
        Host = connection.Host,
        Port = connection.Port,
        DatabaseName = connection.DatabaseName,
        Username = connection.Username,
        Password = connection.PasswordEncrypted is { } cipher ? _protector.Unprotect(cipher) : null,
        FilePath = connection.FilePath,
        Options = connection.Options
    };

    private TargetConnection ToModel(TargetConnectionDetails details) => new() {
        Engine = details.Engine,
        Host = details.Host,
        Port = details.Port,
        DatabaseName = details.DatabaseName,
        Username = details.Username,
        PasswordEncrypted = details.Password is null ? null : _protector.Protect(details.Password),
        FilePath = details.FilePath,
        Options = new Dictionary<string, string>(details.Options, StringComparer.Ordinal)
    };

    private static TargetConnectionDTO ToDto(TargetConnection? connection) {
        if (connection is null) {
            return new TargetConnectionDTO { Exists = false };
        }

        return new TargetConnectionDTO {
            Exists = true,
            ConnectionState = connection.ConnectionState.ToString(),
            Engine = connection.Engine.ToString(),
            Host = connection.Host,
            Port = connection.Port,
            DatabaseName = connection.DatabaseName,
            Username = connection.Username,
            FilePath = connection.FilePath,
            Options = new Dictionary<string, string>(connection.Options, StringComparer.Ordinal),
            HasPassword = connection.PasswordEncrypted is not null,
            LastConnectedAt = connection.LastConnectedAt,
            LastError = string.IsNullOrWhiteSpace(connection.LastError) ? null : new TargetConnectionFailureDTO {
                Message = connection.LastError,
                RawResponse = connection.LastErrorRaw ?? "",
                OccurredAt = connection.LastErrorAt
            }
        };
    }
}
