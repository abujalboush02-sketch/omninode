using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public interface ILlamaAgentService
{
    Task<OrderDraft> ParsePassiveMessageAsync(Guid sessionId, string messageContent);
}

// 1. Updated to match the parallel architecture from Phase 1
public interface IEventProcessor
{
    Task ProcessAsync(RawEvent evt);
    Task ProcessPendingRawEventsAsync(); // Legacy support
}

// 2. Updated to match the robust transformation engine from Phase 1
public interface ITransformationService
{
    Task<string> TransformAsync(string sourcePayload, IEnumerable<FieldMappingRule> rules);
    string TransformPayload(string sourcePayload, Mapping mapping); // Legacy support
}

public interface ISchemaDiscoveryService
{
    Task<IEnumerable<SchemaTemplate>> GetStandardTemplatesAsync();
    Task<SchemaTemplate?> GetTemplateByPlatformAsync(string platformName);
}