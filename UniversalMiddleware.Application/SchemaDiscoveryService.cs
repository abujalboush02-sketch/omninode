using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public class SchemaDiscoveryService : ISchemaDiscoveryService
{
    public Task<IEnumerable<SchemaTemplate>> GetStandardTemplatesAsync()
    {
        return Task.FromResult<IEnumerable<SchemaTemplate>>(new List<SchemaTemplate>());
    }

    public Task<SchemaTemplate?> GetTemplateByPlatformAsync(string platformName)
    {
        return Task.FromResult<SchemaTemplate?>(null);
    }
}
