using Application.Services.Interfaces;
using Database.Models;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public class FieldMappingService : IFieldMappingService {
    private readonly IConfiguration _config;
    private readonly ILogger<FieldMappingService> _logger;
    private readonly IRepository _dataRepository;

    public FieldMappingService(IConfiguration config, ILogger<FieldMappingService> logger, IRepository dataRepository) {
        _config = config;
        _logger = logger;
        _dataRepository = dataRepository;
    }

    public async Task<TableMetadata> GetTargetDatabaseTableMetadata(string dbTableName, string schemaName, CancellationToken cancellationToken = default) {
        // var metadata = await _postgresMetadataRepository.GetTableMetadata(dbTableName, schemaName, cancellationToken);
        var metadata = await _dataRepository.GetTableMetadata(dbTableName, schemaName, cancellationToken);
        
        Console.WriteLine(metadata.ToString());
        
        return metadata;
    }
}