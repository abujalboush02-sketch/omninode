using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API;

public class EventProcessingWorker : BackgroundService {
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventProcessingWorker> _logger;

    public EventProcessingWorker(IServiceProvider serviceProvider, ILogger<EventProcessingWorker> logger) {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Fetch pending events or events ready for their next retry
                var pendingEvents = await dbContext.RawEvents
                    .Where(e => e.Status == "Pending" && (!e.LastAttemptAt.HasValue || e.LastAttemptAt.Value.AddMinutes(e.RetryCount * 5) <= DateTime.UtcNow))
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var evt in pendingEvents) {
                    _logger.LogInformation("Processing RawEvent {EventId}, Attempt {AttemptCount}", evt.Id, evt.RetryCount + 1);
                    
                    try {
                        // Simulate sending to destination/processing
                        // genericHttpAdapter.SendToDestinationAsync(...)
                        evt.Status = "Processed";
                    } catch (Exception ex) {
                        evt.RetryCount++;
                        evt.LastAttemptAt = DateTime.UtcNow;
                        evt.FailureReason = ex.Message;
                        
                        if (evt.RetryCount >= evt.MaxRetries) {
                            evt.Status = "Failed_ManualReviewRequired";
                            _logger.LogWarning("Event {EventId} reached max retries and requires manual review.", evt.Id);
                        } else {
                            evt.Status = "Pending";
                            _logger.LogInformation("Event {EventId} failed. Scheduled for retry {RetryCount}", evt.Id, evt.RetryCount);
                        }
                    }
                }
                
                if (pendingEvents.Any()) {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing events");
            }
            
            await Task.Delay(5000, stoppingToken);
        }
    }
}
