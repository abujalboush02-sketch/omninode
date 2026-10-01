using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Application; // Grants access to HttpAdapterResult

namespace UniversalMiddleware.Infrastructure;

public class GenericHttpOutputAdapter : IGenericHttpOutputAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GenericHttpOutputAdapter> _logger;

    public GenericHttpOutputAdapter(IHttpClientFactory httpClientFactory, ILogger<GenericHttpOutputAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HttpAdapterResult> SendAsync(Connection connection, string transformedJson)
    {
        var client = _httpClientFactory.CreateClient("OutboundCrmClient");
        client.Timeout = TimeSpan.FromSeconds(15);

        var request = new HttpRequestMessage(HttpMethod.Post, connection.BaseUrl)
        {
            Content = new StringContent(transformedJson, Encoding.UTF8, "application/json")
        };

        ApplyAuthentication(request, connection);

        try
        {
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return new HttpAdapterResult { IsSuccess = true, StatusCode = (int)response.StatusCode };
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            return new HttpAdapterResult
            {
                IsSuccess = false,
                StatusCode = (int)response.StatusCode,
                ErrorMessage = errorContent
            };
        }
        catch (TaskCanceledException)
        {
            return new HttpAdapterResult { IsSuccess = false, StatusCode = 408, ErrorMessage = "Destination API timed out." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network failure when contacting {BaseUrl}", connection.BaseUrl);
            return new HttpAdapterResult { IsSuccess = false, StatusCode = 500, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> SendToDestinationAsync(string transformedJson, TenantCredential credential)
    {
        var connection = new Connection
        {
            BaseUrl = credential.BaseUrl,
            ApiKey = credential.ApiKey,
            AuthType = "Header"
        };

        var result = await SendAsync(connection, transformedJson);
        if (!result.IsSuccess)
        {
            throw new Exception($"Destination rejected payload. Code: {result.StatusCode}. Error: {result.ErrorMessage}");
        }
        return true;
    }

    private void ApplyAuthentication(HttpRequestMessage request, Connection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.ApiKey)) return;

        switch (connection.AuthType?.ToLowerInvariant())
        {
            case "bearer":
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
                break;
            case "basic":
                var base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(connection.ApiKey));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);
                break;
            case "header":
            default:
                request.Headers.TryAddWithoutValidation(
                    string.IsNullOrWhiteSpace(connection.AuthHeaderName) ? "Authorization" : connection.AuthHeaderName,
                    connection.ApiKey);
                break;
        }
    }
}