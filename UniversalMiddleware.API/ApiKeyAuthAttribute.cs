using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
        {
            context.Result = new UnauthorizedObjectResult("API Key is missing.");
            return;
        }

        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var controllerName = context.ActionDescriptor.RouteValues["controller"];

        if (controllerName == "Tenants")
        {
            var masterKey = configuration.GetValue<string>("MasterApiKey");
            if (string.IsNullOrEmpty(masterKey) || extractedApiKey != masterKey)
            {
                context.Result = new UnauthorizedObjectResult("Invalid Master API Key.");
                return;
            }
        }
        else
        {
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            
            // Re-hash the provided key and check if it exists in the database
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(extractedApiKey));
            var hashedString = Convert.ToBase64String(hashedBytes);
            
            var tenant = dbContext.Tenants.FirstOrDefault(t => t.ApiKeyHash == hashedString);
            if (tenant == null)
            {
                context.Result = new UnauthorizedObjectResult("Invalid API Key.");
                return;
            }
            
            // Optional: Store TenantId in HttpContext items for further controller usage
            context.HttpContext.Items["TenantId"] = tenant.Id;
        }

        await next();
    }
}
