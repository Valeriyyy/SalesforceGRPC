using Application.Connections;
using Application.Targets;
using DTO;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;

namespace SalesforceGrpc.Controllers;

/// <summary>
/// Setting up and managing the Target Connection: how this application reaches the Target Database.
/// </summary>
/// <remarks>
/// Nothing here returns the password. It is absent from the read model by construction rather than by
/// filtering; every other detail is returned in full so the user can review and edit it.
/// <para>
/// There is no authentication on this API by decision — the network perimeter is the security boundary. That
/// is a fine reason not to build a login. It is not a reason to serve secrets to anyone who asks.
/// </para>
/// <para>
/// Built to be consumed by a UI that does not exist yet. The engine definitions endpoint is what lets that UI
/// render its form from data: a fifth engine is a new entry there, not a new form.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class TargetConnectionController : ControllerBase {
    private readonly ITargetConnectionService _connections;
    private readonly ILogger<TargetConnectionController> _logger;

    public TargetConnectionController(ITargetConnectionService connections, ILogger<TargetConnectionController> logger) {
        _connections = connections;
        _logger = logger;
    }

    /// <summary>The Target Connection, its state, and its last error. Safe on a fresh install.</summary>
    [HttpGet]
    public Task<ActionResult<TargetConnectionDTO>> Get(CancellationToken ct) =>
        Execute(() => _connections.GetAsync(ct));

    /// <summary>
    /// Every engine, with the fields it asks for and whether it can be used.
    /// </summary>
    /// <remarks>
    /// Unavailable engines are listed with their reason rather than omitted, so a client can explain why an
    /// option is greyed out instead of leaving the user to wonder where SQL Server went.
    /// </remarks>
    [HttpGet("engines")]
    public ActionResult<IReadOnlyList<EngineDefinitionDTO>> GetEngines() => Ok(_connections.GetEngines());

    /// <summary>
    /// Stores the details and proves them. Creates the Target Connection, or edits the existing one.
    /// </summary>
    /// <remarks>
    /// Always persists: a failed proof leaves the connection Incomplete with the error, rather than refusing
    /// to record what was typed. Returns a conflict for any change to the engine, host, database name or file
    /// path — that is a repoint, which has its own endpoint with a preview and a confirmation.
    /// </remarks>
    [HttpPut]
    public Task<ActionResult<TargetConnectionDTO>> Save([FromBody] SaveTargetConnectionDTO request, CancellationToken ct) =>
        Execute(() => _connections.SaveAsync(request, ct));

    /// <summary>Proves the stored connection again and records the outcome. The repair button.</summary>
    [HttpPost("retest")]
    public Task<ActionResult<TargetConnectionDTO>> Retest(CancellationToken ct) =>
        Execute(() => _connections.RetestAsync(ct));

    /// <summary>What a repoint would destroy. Call this before confirming one.</summary>
    [HttpGet("repoint/preview")]
    public Task<ActionResult<RepointPreviewDTO>> PreviewRepoint(CancellationToken ct) =>
        Execute(() => _connections.PreviewRepointAsync(ct));

    /// <summary>
    /// Points the application at a different database, destroying every Binding.
    /// </summary>
    /// <remarks>
    /// Requires a confirmation naming the counts from the preview, so a caller that has not looked at what
    /// it is destroying cannot satisfy it by accident. The new database is proved before anything is
    /// destroyed.
    /// </remarks>
    [HttpPost("repoint")]
    public Task<ActionResult<TargetConnectionDTO>> Repoint([FromBody] RepointTargetConnectionDTO request, CancellationToken ct) =>
        Execute(() => _connections.RepointAsync(request, ct));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action) {
        try {
            return Ok(await action().ConfigureAwait(false));
        } catch (ValidationException ex) {
            _logger.LogError(ex, ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (NoTargetDatabaseException ex) {
            return NotFound(new { error = ex.Message });
        } catch (TargetConnectionIdentityChangedException ex) {
            // A conflict rather than a bad request: the request was well-formed and the refusal is about what
            // it would destroy. The message names the repoint operation, which is where the caller goes next.
            _logger.LogWarning(ex.Message);
            return Conflict(new { error = ex.Message });
        } catch (SecretsUnreadableException ex) {
            // Not the user's mistake and not fixable through this API, so it is reported as the service being
            // unable rather than the request being wrong.
            _logger.LogCritical(ex, "The stored database password cannot be decrypted");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        } catch (SecretProtectionUnavailableException ex) {
            _logger.LogCritical(ex, "No protecting key is available");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        } catch (DbException ex) {
            // Setup is exactly when the App Database is most likely to be unmigrated, and a bare 500 during
            // setup sends the user looking at their target database for a fault that is on this side.
            _logger.LogError(ex, "The App Database rejected a request from the Target Connection API");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new {
                error = "The App Database could not be read. It may be unreachable, or the Target Connection " +
                        "table may not exist yet — apply Database/Definitions/migrations/003-target-connection.sql.",
                detail = ex.Message
            });
        }
    }
}
