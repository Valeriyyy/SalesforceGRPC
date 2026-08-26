using Application.Services.Interfaces;
using Database.Models;
using Microsoft.AspNetCore.Mvc;

namespace SalesforceGrpc.Controllers;

/// <summary>
/// Read-only views of stored Binding configuration.
/// </summary>
/// <remarks>
/// This controller used to expose two endpoints that returned the entire <c>IConfiguration</c> — every
/// connection string and password in it — and the whole <c>SalesforceConfig</c> including the client secret,
/// both unauthenticated. They are gone. Salesforce credentials are now managed through
/// <see cref="OrgConnectionController"/>, which never returns a secret.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase {
    private readonly ISchemaService _schemaService;

    public ConfigurationController(ISchemaService schemaService) {
        _schemaService = schemaService;
    }

    [HttpGet("schemas")]
    public async Task<ActionResult<List<CDCSchema>>> GetAllSchemas() {
        try {
            var schemas = await _schemaService.GetAllSchemas().ConfigureAwait(false);
            return Ok(schemas);
        } catch (Exception ex) {
            return BadRequest(ex.Message);
        }
    }
    
    [HttpGet("mappedfields/{schemaId}")]
    public async Task<ActionResult<List<MappedField>>> GetMappedFields(int schemaId) {
        try {
            var mappedFields = await _schemaService.GetMappedFields(schemaId).ConfigureAwait(false);
            return Ok(mappedFields);
        } catch (Exception ex) {
            return BadRequest(ex.Message);
        }
    }

}