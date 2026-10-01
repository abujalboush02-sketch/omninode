using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.Application;

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

    // 1. New method matching the EventProcessingWorker parallel architecture
    public async Task ProcessAsync(RawEvent evt)
    {
        _logger.LogDebug("Initiating routing pipeline for Event {EventId}", evt.Id);

        // 2. Eager Load Routing Architecture (Performance Optimization)
        var endpoint = await _dbContext.Endpoints
            .Include(e => e.Connection)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == evt.EndpointId);

        if (endpoint == null)
            throw new InvalidOperationException($"Routing failed: Endpoint {evt.EndpointId} no longer exists.");

        if (endpoint.Connection == null)
            throw new InvalidOperationException($"Routing failed: No active connection mapped to Endpoint {evt.EndpointId}.");

        // 3. Fetch Field Mapping Rules
        var rules = await _dbContext.FieldMappingRules
            .Where(r => r.EndpointId == evt.EndpointId)
            .AsNoTracking()
            .ToListAsync();

        if (!rules.Any())
            _logger.LogWarning("Event {EventId} has no mapping rules. Payload will be forwarded as-is.", evt.Id);

        // 4. Execute Transformation Pipeline (Structured Mapping or Unstructured LLM Parsing)
        var transformedPayload = await _transformationService.TransformAsync(evt.Payload, rules);

        // 5. Dispatch to Destination External CRM via Adapter
        var result = await _httpAdapter.SendAsync(endpoint.Connection, transformedPayload);

        // 6. Exception Bubble-up for Retry Logic
        if (!result.IsSuccess)
        {
            // Throwing an exception here safely passes the failure reason back to the EventProcessingWorker
            // so it can increment the retry count, record the specific CRM error, and attempt again later.
            throw new Exception($"Destination CRM rejected the payload. Status: {result.StatusCode}. Error: {result.ErrorMessage}");
        }

        _logger.LogInformation("Successfully routed Event {EventId} to destination.", evt.Id);
    }

    // 7. Legacy Interface Fulfillment
    public Task ProcessPendingRawEventsAsync()
    {
        // This is kept to satisfy the interface if required elsewhere, but deprecated by the background worker.
        _logger.LogWarning("ProcessPendingRawEventsAsync is obsolete. The EventProcessingWorker now orchestrates polling.");
        return Task.CompletedTask;
    }
}