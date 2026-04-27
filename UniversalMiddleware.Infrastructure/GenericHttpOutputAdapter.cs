using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Infrastructure;

public class GenericHttpOutputAdapter : IGenericHttpOutputAdapter {
    private readonly HttpClient _httpClient;

    public GenericHttpOutputAdapter(HttpClient httpClient) {
        _httpClient = httpClient;
    }

    public async Task<bool> SendToDestinationAsync(string transformedJson, TenantCredential credential) {
        try {
            var request = new HttpRequestMessage(HttpMethod.Post, credential.BaseUrl) {
                Content = new StringContent(transformedJson, Encoding.UTF8, "application/json")
            };
            
            if (!string.IsNullOrEmpty(credential.ApiKey)) {
                request.Headers.Add("Authorization", credential.ApiKey);
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return true;
        } catch (Exception) {
            throw; // Let the background worker handle the retry tracking
        }
    }
}
