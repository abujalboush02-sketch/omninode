using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

// 1. Moved from Infrastructure up to Application
public class HttpAdapterResult
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public interface IGenericHttpOutputAdapter
{
    // 2. The missing signature the compiler is looking for
    Task<HttpAdapterResult> SendAsync(Connection connection, string transformedJson);

    // Legacy support
    Task<bool> SendToDestinationAsync(string transformedJson, TenantCredential credential);
}