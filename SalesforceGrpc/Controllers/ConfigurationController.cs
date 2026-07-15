using Application.Services.Interfaces;
using Database.Models;
using Microsoft.AspNetCore.Mvc;
using SalesforceGrpc.Salesforce;

namespace SalesforceGrpc.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase {
    private readonly IConfiguration _config;
    private readonly SalesforceConfig _sfConfig;
    private readonly ISchemaService _schemaService;

    public ConfigurationController(IConfiguration config, SalesforceConfig sfConfig, ISchemaService schemaService) {
        _config = config;
        _sfConfig = sfConfig;
        _schemaService = schemaService;
    }
    
    [HttpGet]
    public IActionResult Get() {
        return Ok(_config);
    }
    
    [HttpGet("salesforce")]
    public ActionResult<SalesforceConfig> GetSalesforce() {
        return Ok(_sfConfig);
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

    [HttpPost("mappedfields/add")]
    public async Task<ActionResult> CreateMappedField() {
        return Ok();
    }
}