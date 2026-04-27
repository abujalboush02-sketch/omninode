using System;
using System.Threading.Tasks;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Application;

public class ConnectionWizardService : IConnectionWizardService {
    public Task AdvanceStateAsync(Connection connection, string nextState) {
        connection.Status = nextState;
        return Task.CompletedTask;
    }
    
    public Task HandleIncomingWebhookForListeningConnection(Connection connection, string payload) {
        if (connection.Status == "Listening") {
            connection.Status = "SchemaReceived";
        }
        return Task.CompletedTask;
    }
}
