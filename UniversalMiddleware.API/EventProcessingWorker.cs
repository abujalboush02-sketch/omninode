using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalMiddleware.Infrastructure;
using UniversalMiddleware.Application;

namespace UniversalMiddleware.API;

public class EventProcessingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventProcessingWorker> _logger;

    public EventProcessingWorker(IServiceProvider serviceProvider, ILogger<EventProcessingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventProcessingWorker started. Listening for queued webhooks...");

        while (!stoppingToken.IsCancellationRequested)
        {
            var executedEventsCount = 0;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 1. Transactional Claim Pattern (Prevents duplicate processing across scaled containers)
                var strategy = dbContext.Database.CreateExecutionStrategy();

                var eventsToProcess = await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

                    var pending = await dbContext.RawEvents
                        .Where(e => e.Status == "Pending" &&
                                    (!e.LastAttemptAt.HasValue || e.LastAttemptAt.Value.AddMinutes(e.RetryCount * 5) <= DateTime.UtcNow))
                        .OrderBy(e => e.ReceivedAt) // Process oldest first (FIFO)
                        .Take(50) // Increased batch size for enterprise throughput
                        .ToListAsync(stoppingToken);

                    if (pending.Any())
                    {
                        foreach (var evt in pending)
                        {
                            evt.Status = "Processing"; // Lock the row
                        }
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }

                    await transaction.CommitAsync(stoppingToken);
                    return pending;
                });

                executedEventsCount = eventsToProcess.Count;

                if (eventsToProcess.Any())
                {
                    // 2. Parallel Processing (Massive throughput increase for Nginx bursts)
                    await Parallel.ForEachAsync(eventsToProcess, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 10,
                        CancellationToken = stoppingToken
                    }, async (evt, ct) =>
                    {
                        // 3. Isolated Dependency Injection Scope per Event
                        using var innerScope = _serviceProvider.CreateScope();
                        var innerDbContext = innerScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var eventProcessor = innerScope.ServiceProvider.GetRequiredService<IEventProcessor>();

                        // Attach the disconnected entity to the thread-safe context
                        innerDbContext.RawEvents.Attach(evt);

                        _logger.LogInformation("Processing RawEvent {EventId}, Attempt {AttemptCount}", evt.Id, evt.RetryCount + 1);

                        try
                        {
                            // 4. Actual execution replacing the simulation placeholder
                            await eventProcessor.ProcessAsync(evt);

                            evt.Status = "Processed";
                            evt.FailureReason = null;
                        }
                        catch (Exception ex)
                        {
                            evt.RetryCount++;
                            evt.LastAttemptAt = DateTime.UtcNow;
                            evt.FailureReason = ex.Message;

                            if (evt.RetryCount >= evt.MaxRetries)
                            {
                                evt.Status = "Failed_ManualReviewRequired";
                                _logger.LogWarning("Event {EventId} reached max retries and requires manual review. Reason: {Reason}", evt.Id, ex.Message);
                            }
                            else
                            {
                                evt.Status = "Pending";
                                _logger.LogInformation("Event {EventId} failed. Scheduled for retry {RetryCount}. Reason: {Reason}", evt.Id, evt.RetryCount, ex.Message);
                            }
                        }

                        // Save the isolated event state
                        await innerDbContext.SaveChangesAsync(ct);
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in EventProcessingWorker loop.");
            }

            // 5. Adaptive Polling Delay (Reduces VPS CPU load when idle)
            var delayMs = executedEventsCount > 0 ? 500 : 3000;
            await Task.Delay(delayMs, stoppingToken);
        }
    }
}