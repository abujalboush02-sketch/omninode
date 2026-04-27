using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using UniversalMiddleware.Application;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Infrastructure;

public class LlamaAgentService : ILlamaAgentService {
    private readonly HttpClient _httpClient;
    private readonly LlamaSettings _settings;

    public LlamaAgentService(HttpClient httpClient, IOptions<LlamaSettings> settings) {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public Task<OrderDraft> ParsePassiveMessageAsync(Guid sessionId, string messageContent) {
        // Ensure headers are set from configuration
        if (!string.IsNullOrEmpty(_settings.ApiKey)) {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }
        
        // Mock Llama parsing logic returning dummy OrderDraft
        return Task.FromResult(new OrderDraft {
            SessionId = sessionId,
            CustomerName = "John Doe",
            Product = "Pizza",
            PhoneNumber = "1234567890",
            Location = "123 Main St",
            Status = "Incomplete" // Marked for manual review, passive
        });
    }
}
