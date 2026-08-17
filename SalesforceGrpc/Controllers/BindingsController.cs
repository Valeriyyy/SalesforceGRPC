using Application.Services.Interfaces;
using DTO;
using Microsoft.AspNetCore.Mvc;
using Salesforce.Clients;
using System.ComponentModel.DataAnnotations;

namespace SalesforceGrpc.Controllers;

/// <summary>
/// Configuring Bindings: which Entity lands in which Target Table, and which field feeds which column.
/// </summary>
/// <remarks>
/// Error mapping follows <see cref="PlatformEventManagementController"/> — a validation failure is the user's
/// to fix (400), a missing row is not found (404), and a Salesforce failure is upstream (502).
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class BindingsController : ControllerBase {
    private readonly IBindingService _bindings;
    private readonly ILogger<BindingsController> _logger;

    public BindingsController(IBindingService bindings, ILogger<BindingsController> logger) {
        _bindings = bindings;
        _logger = logger;
    }

    #region Discovery

    /// <summary>The Entity fields a Channel Member can map, already flattened, with their Field Types.</summary>
    [HttpGet("members/{memberId:int}/bindable-fields")]
    public Task<ActionResult<IReadOnlyList<BindableFieldDTO>>> GetBindableFields(int memberId, CancellationToken ct) =>
        Execute(() => _bindings.GetBindableFieldsAsync(memberId, ct));

    /// <summary>Tables in the Target Database, each marked with the Entity already bound to it.</summary>
    [HttpGet("target-tables")]
    public Task<ActionResult<IReadOnlyList<TargetTableDTO>>> GetTargetTables(
        [FromQuery] string schema, CancellationToken ct) =>
        Execute(() => _bindings.GetTargetTablesAsync(schema, ct));

    /// <summary>Columns of one Target Table, each marked with the Salesforce field mapped to it.</summary>
    [HttpGet("target-tables/{schema}/{table}/columns")]
    public Task<ActionResult<IReadOnlyList<TargetColumnDTO>>> GetTargetColumns(
        string schema, string table, [FromQuery] int? bindingId, CancellationToken ct) =>
        Execute(() => _bindings.GetTargetColumnsAsync(schema, table, bindingId, ct));

    #endregion

    #region Bindings

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<BindingDTO>>> GetBindings(CancellationToken ct) =>
        Execute(() => _bindings.GetBindingsAsync(ct));

    [HttpGet("{bindingId:int}")]
    public Task<ActionResult<BindingDTO>> GetBinding(int bindingId, CancellationToken ct) =>
        Execute(() => _bindings.GetBindingAsync(bindingId, ct));

    /// <summary>Creates an Incomplete Binding for a Channel Member's Entity.</summary>
    [HttpPost("members/{memberId:int}")]
    public Task<ActionResult<BindingDTO>> CreateBinding(int memberId, [FromBody] CreateBindingDTO dto, CancellationToken ct) =>
        Execute(() => _bindings.CreateBindingAsync(memberId, dto, ct));

    /// <summary>Replaces the Binding's Field Mapping set. The Key Mapping is unaffected.</summary>
    [HttpPut("{bindingId:int}/field-mappings")]
    public Task<ActionResult<BindingDTO>> SetFieldMappings(int bindingId, [FromBody] SetFieldMappingsDTO dto, CancellationToken ct) =>
        Execute(() => _bindings.SetFieldMappingsAsync(bindingId, dto, ct));

    /// <summary>Names the Target Column holding the Salesforce record ID.</summary>
    [HttpPut("{bindingId:int}/key-mapping")]
    public Task<ActionResult<BindingDTO>> SetKeyMapping(int bindingId, [FromBody] SetKeyMappingDTO dto, CancellationToken ct) =>
        Execute(() => _bindings.SetKeyMappingAsync(bindingId, dto, ct));

    [HttpPut("{bindingId:int}/soft-delete")]
    public Task<ActionResult<BindingDTO>> SetSoftDelete(int bindingId, [FromBody] SetSoftDeleteDTO dto, CancellationToken ct) =>
        Execute(() => _bindings.SetSoftDeleteAsync(bindingId, dto, ct));

    /// <summary>Runs validation without changing the Binding's state.</summary>
    [HttpPost("{bindingId:int}/validate")]
    public Task<ActionResult<BindingValidationDTO>> Validate(int bindingId, CancellationToken ct) =>
        Execute(() => _bindings.ValidateBindingAsync(bindingId, ct));

    [HttpPost("{bindingId:int}/activate")]
    public Task<ActionResult<BindingDTO>> Activate(int bindingId, CancellationToken ct) =>
        Execute(() => _bindings.ActivateAsync(bindingId, ct));

    [HttpPost("{bindingId:int}/deactivate")]
    public Task<ActionResult<BindingDTO>> Deactivate(int bindingId, CancellationToken ct) =>
        Execute(() => _bindings.DeactivateAsync(bindingId, ct));

    [HttpDelete("{bindingId:int}")]
    public Task<ActionResult> Delete(int bindingId, CancellationToken ct) =>
        Execute(() => _bindings.DeleteBindingAsync(bindingId, ct));

    #endregion

    #region Primary channel

    /// <summary>The Primary Channel's local ID, or null when none has been selected.</summary>
    [HttpGet("primary-channel")]
    public Task<ActionResult<int?>> GetPrimaryChannel(CancellationToken ct) =>
        Execute(() => _bindings.GetPrimaryChannelIdAsync(ct));

    /// <summary>Selects the Primary Channel. The worker picks this up without a restart.</summary>
    [HttpPut("primary-channel")]
    public Task<ActionResult> SetPrimaryChannel([FromBody] SetPrimaryChannelDTO dto, CancellationToken ct) =>
        Execute(() => _bindings.SetPrimaryChannelAsync(dto.ChannelId, ct));

    #endregion

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action) {
        try {
            return Ok(await action().ConfigureAwait(false));
        } catch (ValidationException ex) {
            _logger.LogError(ex, ex.Message);
            return BadRequest(new { error = ex.Message });
        } catch (KeyNotFoundException ex) {
            _logger.LogError(ex, ex.Message);
            return NotFound(new { error = ex.Message });
        } catch (SalesforceToolingException ex) {
            _logger.LogError(ex, ex.Message);
            _logger.LogError(ex, "Salesforce rejected the request");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        } catch (NotImplementedException ex) {
            _logger.LogCritical(ex, ex.Message);
            // The SQL Server and MySQL drivers are stubs; say so rather than returning a 500.
            return BadRequest(new { error = $"The configured target database driver does not support this operation. {ex.Message}" });
        }
    }

    private async Task<ActionResult> Execute(Func<Task> action) {
        var result = await Execute<object?>(async () => {
            await action().ConfigureAwait(false);
            return null;
        }).ConfigureAwait(false);

        return result.Result ?? NoContent();
    }
}
