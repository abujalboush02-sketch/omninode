using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string TenantIdItemKey = "TenantId";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 1. Standardized Header Extraction
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new { Error = "API Key is missing." });
            return;
        }

        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var controllerName = context.ActionDescriptor.RouteValues["controller"];

        if (string.Equals(controllerName?.ToString(), "Tenants", StringComparison.OrdinalIgnoreCase))
        {
            var masterKey = configuration.GetValue<string>("MasterApiKey");

            // Fail secure if the master key is missing from environment variables
            if (string.IsNullOrEmpty(masterKey))
            {
                context.Result = new UnauthorizedObjectResult(new { Error = "Server configuration error." });
                return;
            }

            // 2. Cryptographic Timing Attack Prevention
            var masterKeyBytes = Encoding.UTF8.GetBytes(masterKey);
            var extractedKeyBytes = Encoding.UTF8.GetBytes(extractedApiKey.ToString());

            if (masterKeyBytes.Length != extractedKeyBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(masterKeyBytes, extractedKeyBytes))
            {
                context.Result = new UnauthorizedObjectResult(new { Error = "Invalid Master API Key." });
                return;
            }
        }
        else
        {
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

            // 3. Allocation-Free Hashing (.NET 10 Optimization)
            var hashedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(extractedApiKey.ToString()));
            var hashedString = Convert.ToBase64String(hashedBytes);

            // 4. Asynchronous, Non-Tracking Database Query
            var tenant = await dbContext.Tenants
                .AsNoTracking()
                .Where(t => t.ApiKeyHash == hashedString)
                .Select(t => new { t.Id }) // Fetch only the ID to reduce memory overhead
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            if (tenant == null)
            {
                context.Result = new UnauthorizedObjectResult(new { Error = "Invalid API Key." });
                return;
            }

            context.HttpContext.Items[TenantIdItemKey] = tenant.Id;
        }

        await next();
    }
}