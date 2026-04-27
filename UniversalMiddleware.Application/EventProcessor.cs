using System.Threading.Tasks;
namespace UniversalMiddleware.Application;

public class EventProcessor : IEventProcessor
{
    public Task ProcessPendingRawEventsAsync()
    {
        return Task.CompletedTask;
    }
}
