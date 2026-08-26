using Application.Connections;
using DTO;
using Microsoft.AspNetCore.Mvc;
using Salesforce.Auth;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;

namespace SalesforceGrpc.Controllers;

/// <summary>
/// Setting up and managing the Org Connection: how this application authenticates to one Salesforce org.
/// </summary>
/// <remarks>
/// Nothing here returns a secret. The Signing Keypair's private key and the Bootstrap Consumer Secret are
/// absent from the read model by construction rather than by filtering, which is the point: this replaces
/// endpoints that returned the entire application configuration, connection strings included.
/// <para>
/// There is no authentication on this API by decision — the network perimeter is the security boundary. That
/// is a fine reason not to build a login. It is not a reason to serve secrets to anyone who asks.
/// </para>
/// <para>
/// Built to be consumed by a UI that does not exist yet: explicit request and response models, Connection
/// State as a real value rather than something inferred from null fields, and errors carrying both the
/// translated guidance and the raw Salesforce text.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class OrgConnectionController : ControllerBase {
    private readonly IOrgConnectionService _connections;
    private readonly ILogger<OrgConnectionController> _logger;

    public OrgConnectionController(IOrgConnectionService connections, ILogger<OrgConnectionController> logger) {
        _connections = connections;
        _logger = logger;
    }

    /// <summary>The Org Connection, its state, and what to do next. Safe on a fresh install.</summary>
    [HttpGet]
    public Task<ActionResult<OrgConnectionDTO>> Get(CancellationToken ct) =>
        Execute(() => _connections.GetAsync(ct));

    /// <summary>
    /// Stores connection details and generates the Signing Keypair.
    /// </summary>
    /// <remarks>
    /// Leaves the connection Incomplete: it now holds a real secret that has never authenticated, which is
    /// the expected state between setup and the first successful token.
    /// </remarks>
    [HttpPut]
    public Task<ActionResult<OrgConnectionDTO>> Save([FromBody] SaveOrgConnectionDTO request, CancellationToken ct) =>
        Execute(() => _connections.SaveAsync(request, ct));

    /// <summary>
    /// The Signing Certificate as PEM, for registering it in Setup by hand.
    /// </summary>
    /// <remarks>
    /// Public by nature — Salesforce holds the same bytes. Needed under Manual Registration, where the user
    /// uploads it themselves.
    /// </remarks>
    [HttpGet("certificate")]
    public async Task<IActionResult> GetCertificate(CancellationToken ct) {
        var result = await Execute(() => _connections.GetCertificatePemAsync(ct)).ConfigureAwait(false);

        return result.Value is { } pem
            ? File(System.Text.Encoding.ASCII.GetBytes(pem), "application/x-pem-file", "salesforcegrpc-signing.pem")
            : result.Result!;
    }

    /// <summary>
    /// Starts the Bootstrap, returning the Salesforce URL to send the user's browser to.
    /// </summary>
    /// <remarks>
    /// Returns the URL rather than redirecting, so a fetch-based UI can show the user where they are about to
    /// be sent before sending them. <see cref="RedirectToBootstrap"/> is the browser-navigable equivalent.
    /// </remarks>
    [HttpPost("bootstrap")]
    public Task<ActionResult<BootstrapStartDTO>> StartBootstrap(CancellationToken ct) =>
        Execute(() => _connections.StartBootstrapAsync(ct));

    /// <summary>
    /// Starts the Bootstrap and redirects straight to Salesforce.
    /// </summary>
    /// <remarks>
    /// For a plain link or a browser address bar, where there is no script to follow a URL returned as JSON.
    /// Each call issues a fresh <c>state</c>, so this is a navigation target rather than something to bookmark.
    /// </remarks>
    [HttpGet("bootstrap/start")]
    public async Task<IActionResult> RedirectToBootstrap(CancellationToken ct) {
        var result = await Execute(() => _connections.StartBootstrapAsync(ct)).ConfigureAwait(false);

        return result.Value is { } start
            ? Redirect(start.AuthorizeUrl)
            : result.Result!;
    }

    /// <summary>
    /// Where Salesforce sends the user's browser after they approve.
    /// </summary>
    /// <remarks>
    /// A GET because it is a browser redirect, not a call anything makes deliberately. It returns the
    /// connection's new state as JSON; once a setup UI exists this should redirect into it instead.
    /// </remarks>
    [HttpGet("bootstrap/callback")]
    public Task<ActionResult<OrgConnectionDTO>> BootstrapCallback(
        [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(error)) {
            // Salesforce reports a refused approval on the redirect rather than in a response body, so this
            // is the only place the user's "deny" is visible.
            _logger.LogWarning("The Bootstrap was not approved: {Error} {Description}", error, errorDescription);
            return Task.FromResult<ActionResult<OrgConnectionDTO>>(BadRequest(new {
                error = $"Salesforce did not approve the connection: {error}",
                errorDescription
            }));
        }

        return Execute(() => _connections.CompleteBootstrapAsync(code ?? "", state ?? "", ct));
    }

    /// <summary>
    /// Requests a token for both identities and records the outcome.
    /// </summary>
    /// <remarks>
    /// The retry path. Salesforce propagates permission and policy changes with a delay, so a connection that
    /// fails immediately after setup often succeeds a minute later with nothing else changed.
    /// </remarks>
    [HttpPost("verify")]
    public Task<ActionResult<OrgConnectionDTO>> Verify(CancellationToken ct) =>
        Execute(() => _connections.VerifyAsync(ct));

    /// <summary>What a Disconnect would destroy. Call this before confirming one.</summary>
    [HttpGet("disconnect/preview")]
    public Task<ActionResult<DisconnectPreviewDTO>> PreviewDisconnect(CancellationToken ct) =>
        Execute(() => _connections.PreviewDisconnectAsync(ct));

    /// <summary>
    /// Wipes the connection and every piece of org-specific state. The only supported route between orgs.
    /// </summary>
    /// <remarks>
    /// Requires a confirmation naming the counts from the preview, so a caller that has not looked at what it
    /// is destroying cannot satisfy it by accident. Nothing inside Salesforce is touched.
    /// </remarks>
    [HttpPost("disconnect")]
    public Task<ActionResult<DisconnectPreviewDTO>> Disconnect([FromBody] ConfirmDisconnectDTO confirmation, CancellationToken ct) =>
        Execute(() => _connections.DisconnectAsync(confirmation, ct));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action) {
        try {
            return Ok(await action().ConfigureAwait(false));
        } catch (ValidationException ex) {
            _logger.LogError(ex, ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (KeyNotFoundException ex) {
            return NotFound(new { error = ex.Message });
        } catch (NoOrgConnectionException ex) {
            return NotFound(new { error = ex.Message });
        } catch (OrgMismatchException ex) {
            // A conflict rather than a bad request: the request was well formed and the refusal is about the
            // state of the world. Both org ids travel with it, because that is the first thing anyone asks.
            _logger.LogError(ex, ex.Message);
            return Conflict(new { error = ex.Message, storedOrgId = ex.StoredOrgId, discoveredOrgId = ex.DiscoveredOrgId });
        } catch (SalesforceOAuthException ex) {
            // Both halves, always. The translation table is incomplete by construction and an unrecognised
            // error must never be replaced by a friendly guess.
            _logger.LogError(ex, "Salesforce rejected the request");
            return StatusCode(StatusCodes.Status502BadGateway, new {
                error = ex.Error.Error,
                errorDescription = ex.Error.ErrorDescription,
                guidance = ex.Error.Guidance,
                rawResponse = ex.Error.RawResponse
            });
        } catch (SecretsUnreadableException ex) {
            // Not the user's mistake and not fixable through this API, so it is reported as the service being
            // unable rather than the request being wrong.
            _logger.LogCritical(ex, "Stored credentials cannot be decrypted");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        } catch (SecretProtectionUnavailableException ex) {
            _logger.LogCritical(ex, "No protecting key is available");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        } catch (DbException ex) {
            // Setup is exactly when the App Database is most likely to be unmigrated, and a bare 500 during
            // setup sends the user looking at Salesforce for a fault that is on this side.
            _logger.LogError(ex, "The App Database rejected a request from the Org Connection API");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new {
                error = "The App Database could not be read. It may be unreachable, or the Org Connection " +
                        "tables may not exist yet — apply Database/Definitions/migrations/002-salesforce-org-connections.sql.",
                detail = ex.Message
            });
        }
    }
}
