using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public interface ILlamaAgentService {
    // Passive evaluation, will return Incomplete if missing fields, does not trigger responses.
    Task<OrderDraft> ParsePassiveMessageAsync(Guid sessionId, string messageContent);
}

public interface IWebhookProcessor {
    Task EnqueueStructuredPayloadAsync(Guid endpointId, string jsonPayload);
    Task EnqueueUnstructuredPayloadAsync(Guid endpointId, string externalUserId, string messageContent);
}

public interface IEventProcessor {
    Task ProcessPendingRawEventsAsync();
}

public interface ITransformationService {
    string TransformPayload(string sourcePayload, Mapping mapping);
}

public interface ISchemaDiscoveryService {
    Task<IEnumerable<SchemaTemplate>> GetStandardTemplatesAsync();
    Task<SchemaTemplate?> GetTemplateByPlatformAsync(string platformName);
}
