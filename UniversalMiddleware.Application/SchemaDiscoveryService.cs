using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.Application;

public class SchemaDiscoveryService : ISchemaDiscoveryService
{
    private readonly AppDbContext _context;

    public SchemaDiscoveryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<SchemaTemplate>> GetStandardTemplatesAsync()
    {
        // Fetches the real schemas populated by your DataSeeder
        return await _context.SchemaTemplates.AsNoTracking().ToListAsync();
    }

    public async Task<SchemaTemplate?> GetTemplateByPlatformAsync(string platformName)
    {
        return await _context.SchemaTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.PlatformName == platformName);
    }
}