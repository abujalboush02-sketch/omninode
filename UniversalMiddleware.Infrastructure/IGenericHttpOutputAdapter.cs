using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Infrastructure;

public interface IGenericHttpOutputAdapter {
    Task<bool> SendToDestinationAsync(string transformedJson, TenantCredential credential);
}
