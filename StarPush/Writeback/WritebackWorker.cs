using Application.Bindings;
using Application.Services.Interfaces;
using Application.Targets;
using Application.Writeback;
using Database.Repositories.Interfaces;
using Salesforce.Writeback;
using StarPush.Telemetry;

namespace StarPush.Writeback;

/// <summary>
/// Writes Target Table changes back to Salesforce for every Two-way Binding that is Active.
/// </summary>
/// <remarks>
/// <para>
/// What to write back is decided by <see cref="IBindingService.GetWritebackPlanAsync"/>, which re-validates each
/// Two-way Binding first. This class owns the loop: setting the Target Database up for exactly those tables,
/// reading its changes a transaction at a time, handing each to <see cref="WritebackProcessor"/>, and confirming
/// the transaction only once every change in it reached Salesforce or was recorded as a Writeback Failure.
/// </para>
/// <para>
/// Delivery is at-least-once, like the inbound flow (ADR 0005): an interrupted transaction is sent again after a
/// restart or a failure that may pass. When no Two-way Binding is left, the database is set back so it no
/// longer keeps changes for a reader that is gone. One instance per App Database is assumed.
/// </para>
/// </remarks>
public class WritebackWorker : BackgroundService {
    private readonly ILogger<WritebackWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfigurationChangeSignal _changeSignal;
    private readonly ITargetConnectionProvider _target;
    private readonly ITargetConnectionRepository _targetConnections;
    private readonly WritebackHealth _health;
    private readonly TimeProvider _time;

    /// <summary>How long to wait before trying again after a failure, so a persistent fault is not a hot loop.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the reader's lag behind the Target Database is measured.</summary>
    public TimeSpan LagInterval { get; init; } = TimeSpan.FromMinutes(1);

    public WritebackWorker(
        ILogger<WritebackWorker> logger,
        IServiceScopeFactory scopeFactory,
        IConfigurationChangeSignal changeSignal,
        ITargetConnectionProvider target,
        ITargetConnectionRepository targetConnections,
        WritebackHealth health,
        TimeProvider time) {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _changeSignal = changeSignal;
        _target = target;
        _targetConnections = targetConnections;
        _health = health;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                // Subscribed before planning, so a change made while the plan is read is not missed.
                var changed = _changeSignal.WaitForChangeAsync(stoppingToken);

                WritebackPlan plan;
                try {
                    plan = await GetPlan(stoppingToken).ConfigureAwait(false);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    _logger.LogError(ex, "Could not build a Writeback plan; retrying in {Delay}s.", RetryDelay.TotalSeconds);
                    _health.Set(WritebackRunState.Retrying, ex.Message);
                    await Task.Delay(RetryDelay, _time, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!plan.HasWork) {
                    await DisarmIfArmed(plan, stoppingToken).ConfigureAwait(false);
                    _health.Set(WritebackRunState.Idle);
                    await changed.ConfigureAwait(false);
                    continue;
                }

                if (!plan.CanRun) {
                    var reason = plan.BlockedReason ?? "Writeback is not set up for the Target Database.";
                    _logger.LogWarning("{Count} Binding(s) are Two-way, but Writeback cannot run: {Reason}",
                        plan.Bindings.Count, reason);
                    _health.Set(WritebackRunState.Blocked, reason);
                    await changed.ConfigureAwait(false);
                    continue;
                }

                await RunAsync(plan, stoppingToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Writeback worker stopping");
        }
    }

    private async Task<WritebackPlan> GetPlan(CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var bindings = scope.ServiceProvider.GetRequiredService<IBindingService>();
        return await bindings.GetWritebackPlanAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads and writes back until the configuration changes, a failure needs a pause, or the host stops.
    /// </summary>
    private async Task RunAsync(WritebackPlan plan, CancellationToken stoppingToken) {
        using var planScope = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var watch = WatchForConfigurationChange(planScope);
        // Resolved per run rather than held for the life of the host, so the Salesforce HTTP client it writes
        // through is renewed each time Writeback re-plans.
        using var services = _scopeFactory.CreateScope();
        var processor = services.ServiceProvider.GetRequiredService<WritebackProcessor>();
        var capability = plan.Capability!;
        var endpoints = plan.Endpoints!;

        try {
            var tables = plan.Bindings.Select(b => b.Table).ToList();
            await capability.ArmAsync(endpoints, tables, planScope.Token).ConfigureAwait(false);

            // From here on every write this application makes to the Target Database is tagged, so neither the
            // inbound flow's writes nor the IDs stored below are read back as changes.
            await _targetConnections.SetWritebackArmedAsync(_time.GetUtcNow().UtcDateTime, planScope.Token).ConfigureAwait(false);
            _target.Invalidate();
            var tagged = await _target.GetRepositoryAsync(planScope.Token).ConfigureAwait(false);

            await using var stream = await capability.OpenStreamAsync(endpoints, planScope.Token).ConfigureAwait(false);
            _health.Set(WritebackRunState.Running);
            _logger.LogInformation("Writing back {Count} Two-way Binding(s): {Tables}", plan.Bindings.Count,
                string.Join(", ", tables));

            var lag = MeasureLag(plan, planScope.Token);

            await foreach (var transaction in stream.ReadAsync(planScope.Token).ConfigureAwait(false)) {
                using var activity = WritebackTelemetry.ActivitySource.StartActivity(WritebackTelemetry.TransactionSpan);
                activity?.SetTag("starpush.writeback.changes", transaction.Changes.Count);

                var summary = await processor.ApplyAsync(transaction, plan, tagged, planScope.Token).ConfigureAwait(false);
                await stream.ConfirmAsync(transaction.Position, planScope.Token).ConfigureAwait(false);

                WritebackTelemetry.ChangesRead.Add(transaction.Changes.Count);
                WritebackTelemetry.RecordsSent.Add(summary.Sent);
                WritebackTelemetry.RecordsFailed.Add(summary.Failed);
                _health.Applied(_time.GetUtcNow());
            }
        } catch (OperationCanceledException) when (planScope.IsCancellationRequested && !stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Writeback configuration changed; re-planning");
        } catch (WritebackTransientException ex) {
            WritebackTelemetry.Retries.Add(1);
            await PauseAfter(ex, "Writeback paused by a failure that may pass; the unconfirmed changes are sent again in {Delay}s: {Reason}",
                stoppingToken).ConfigureAwait(false);
        } catch (SalesforceUnauthorizedException ex) {
            await PauseAfter(ex, "Salesforce refused the Run-as User, so Writeback is paused; retrying in {Delay}s: {Reason}",
                stoppingToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            await PauseAfter(ex, "Writeback stopped unexpectedly; retrying in {Delay}s: {Reason}", stoppingToken).ConfigureAwait(false);
        } finally {
            await planScope.CancelAsync().ConfigureAwait(false);
            await watch.ConfigureAwait(false);
        }
    }

    private async Task PauseAfter(Exception ex, string message, CancellationToken stoppingToken) {
        _logger.LogWarning(ex, message, RetryDelay.TotalSeconds, ex.Message);
        _health.Set(WritebackRunState.Retrying, ex.Message);
        await Task.Delay(RetryDelay, _time, stoppingToken).ConfigureAwait(false);
    }

    private async Task WatchForConfigurationChange(CancellationTokenSource planScope) {
        var changed = _changeSignal.WaitForChangeAsync(planScope.Token);
        try {
            await changed.ConfigureAwait(false);
            await planScope.CancelAsync().ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // The run ended first; nothing to do.
        }
    }

    /// <summary>Measures, every <see cref="LagInterval"/>, how much the Target Database is keeping for us.</summary>
    private async Task MeasureLag(WritebackPlan plan, CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    _health.Lag(await plan.Capability!.GetLagBytesAsync(plan.Endpoints!, cancellationToken).ConfigureAwait(false));
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    _logger.LogDebug(ex, "Could not measure the Writeback lag");
                }
                await Task.Delay(LagInterval, _time, cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
            // The run ended.
        }
    }

    /// <summary>
    /// With no Two-way Binding left, sets the Target Database back so it stops keeping changes for Writeback.
    /// </summary>
    /// <remarks>
    /// Without a capability or a Replication Credential there is nothing this can reach; the lag warning stays
    /// the user's signal that the database is still keeping changes.
    /// </remarks>
    private async Task DisarmIfArmed(WritebackPlan plan, CancellationToken cancellationToken) {
        try {
            var connection = await _target.GetAsync(cancellationToken).ConfigureAwait(false);
            if (connection?.WritebackArmedAt is null || plan.Capability is null || plan.Endpoints is null) {
                return;
            }

            await plan.Capability.DisarmAsync(plan.Endpoints, cancellationToken).ConfigureAwait(false);
            await _targetConnections.SetWritebackArmedAsync(null, cancellationToken).ConfigureAwait(false);
            _target.Invalidate();
            _logger.LogInformation("No Binding is Two-way any more; the Target Database no longer keeps changes for Writeback");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogWarning(ex, "Could not take Writeback down in the Target Database; it may keep changes until it is");
        }
    }
}
