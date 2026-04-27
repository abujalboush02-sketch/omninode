using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public interface IConnectionWizardService {
    Task AdvanceStateAsync(Connection connection, string nextState);
    Task HandleIncomingWebhookForListeningConnection(Connection connection, string payload);
}
