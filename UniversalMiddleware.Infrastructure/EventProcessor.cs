using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Application; // Grants access to interfaces

namespace UniversalMiddleware.Infrastructure; // Relocated namespace

public class EventProcessor : IEventProcessor
{
    private readonly AppDbContext _dbContext;
    private readonly ITransformationService _transformationService;
    private readonly IGenericHttpOutputAdapter _httpAdapter;
    private readonly ILogger<EventProcessor> _logger;

    public EventProcessor(
        AppDbContext dbContext,
        ITransformationService transformationService,
        IGenericHttpOutputAdapter httpAdapter,
        ILogger<EventProcessor> logger)
    {
        _dbContext = dbContext;
        _transformationService = transformationService;
        _httpAdapter = httpAdapter;
        _logger = logger;
    }

    public async Task ProcessAsync(RawEvent evt)
    {
        _logger.LogDebug("Initiating routing pipeline for Event {EventId}", evt.Id);

        var endpoint = await _dbContext.Endpoints
            .Include(e => e.Connection)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == evt.EndpointId);

        if (endpoint == null)
            throw new InvalidOperationException($"Routing failed: Endpoint {evt.EndpointId} no longer exists.");

        if (endpoint.Connection == null)
            throw new InvalidOperationException($"Routing failed: No active connection mapped to Endpoint {evt.EndpointId}.");

        var rules = await _dbContext.FieldMappingRules
            .Where(r => r.EndpointId == evt.EndpointId)
            .AsNoTracking()
            .ToListAsync();

        if (!rules.Any())
            _logger.LogWarning("Event {EventId} has no mapping rules. Payload will be forwarded as-is.", evt.Id);

        var transformedPayload = await _transformationService.TransformAsync(evt.Payload, rules);

        var result = await _httpAdapter.SendAsync(endpoint.Connection, transformedPayload);

        if (!result.IsSuccess)
        {
            throw new Exception($"Destination CRM rejected the payload. Status: {result.StatusCode}. Error: {result.ErrorMessage}");
        }

        _logger.LogInformation("Successfully routed Event {EventId} to destination.", evt.Id);
    }

    public Task ProcessPendingRawEventsAsync()
    {
        _logger.LogWarning("ProcessPendingRawEventsAsync is obsolete. The EventProcessingWorker now orchestrates polling.");
        return Task.CompletedTask;
    }
}