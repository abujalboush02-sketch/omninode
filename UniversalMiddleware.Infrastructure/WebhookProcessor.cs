using System;
using System.Threading.Tasks;
using UniversalMiddleware.Application;
using UniversalMiddleware.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UniversalMiddleware.Infrastructure;

public class WebhookProcessor : IWebhookProcessor {
    private readonly AppDbContext _context;

    public WebhookProcessor(AppDbContext context) {
        _context = context;
    }

    public async Task EnqueueStructuredPayloadAsync(Guid endpointId, string jsonPayload) {
        var rawEvent = new RawEvent {
            EndpointId = endpointId,
            Payload = jsonPayload,
            Status = "Pending",
            RetryCount = 0,
            MaxRetries = 5,
            ReceivedAt = DateTime.UtcNow
        };
        
        _context.RawEvents.Add(rawEvent);
        await _context.SaveChangesAsync();
    }

    public async Task EnqueueUnstructuredPayloadAsync(Guid endpointId, string externalUserId, string messageContent) {
        // Wrap message into a JSON payload tracking the userId
        var payloadObj = new JsonObject {
            ["userId"] = externalUserId,
            ["message"] = messageContent
        };
        
        var rawEvent = new RawEvent {
            EndpointId = endpointId,
            Payload = payloadObj.ToJsonString(),
            Status = "Pending",
            RetryCount = 0,
            MaxRetries = 5,
            ReceivedAt = DateTime.UtcNow
        };
        
        _context.RawEvents.Add(rawEvent);
        await _context.SaveChangesAsync();
    }
}
