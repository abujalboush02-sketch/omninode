using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string ApiKeyQueryName = "key";
    private const string ActAsHeaderName = "X-Tenant-Id";
    private readonly UserRole[] _allowedRoles;

    public ApiKeyAuthAttribute(params UserRole[] allowedRoles)
    {
        _allowedRoles = allowedRoles;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var dbContext = httpContext.RequestServices.GetRequiredService<AppDbContext>();

        // 1. Check for Human JWT Bearer Authentication First
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var roleClaim = httpContext.User.FindFirstValue(ClaimTypes.Role);
            var tenantClaim = httpContext.User.FindFirstValue("TenantId");
            var agencyClaim = httpContext.User.FindFirstValue("AgencyId");

            if (Enum.TryParse<UserRole>(roleClaim, out var role))
            {
                // Verify role permissions if specific roles are required
                if (_allowedRoles.Length > 0 && !_allowedRoles.Contains(role))
                {
                    context.Result = new ObjectResult(new { Error = "Forbidden: Insufficient privileges." })
                    {
                        StatusCode = 403
                    };
                    return;
                }

                httpContext.Items["AuthRole"] = role;

                if (Guid.TryParse(tenantClaim, out var tenantId))
                {
                    httpContext.Items["TenantId"] = tenantId;
                }

                if (Guid.TryParse(agencyClaim, out var agencyId))
                {
                    httpContext.Items["AgencyId"] = agencyId;
                }

                // "Act as client": agency owners (their own clients only) and SuperAdmin (any tenant)
                // send X-Tenant-Id to work inside a client's workspace with the normal tenant endpoints.
                if (httpContext.Request.Headers.TryGetValue(ActAsHeaderName, out var actAsRaw) &&
                    !string.IsNullOrWhiteSpace(actAsRaw))
                {
                    if (!Guid.TryParse(actAsRaw.ToString().Trim(), out var targetTenantId))
                    {
                        context.Result = new BadRequestObjectResult(new { Error = "X-Tenant-Id must be a GUID." });
                        return;
                    }

                    bool allowed = role switch
                    {
                        UserRole.SuperAdmin => await dbContext.Tenants.AsNoTracking().AnyAsync(t => t.Id == targetTenantId),
                        UserRole.AgencyOwner when Guid.TryParse(agencyClaim, out var ownerAgencyId) =>
                            await dbContext.Tenants.AsNoTracking().AnyAsync(t => t.Id == targetTenantId && t.AgencyId == ownerAgencyId),
                        _ => false
                    };

                    if (!allowed)
                    {
                        context.Result = new ObjectResult(new { Error = "You don't have access to this client workspace." }) { StatusCode = 403 };
                        return;
                    }

                    httpContext.Items["TenantId"] = targetTenantId;
                    httpContext.Items["ActingAsTenant"] = true;
                }

                await next();
                return;
            }
        }

        // 2. Machine-to-Machine Authentication: X-Api-Key header, or ?key= query parameter
        //    (query form is for senders that can't set headers; prefer the header or the secret URL)
        string? providedKey = null;
        if (httpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
            providedKey = headerKey.ToString().Trim();
        else if (httpContext.Request.Query.TryGetValue(ApiKeyQueryName, out var queryKey) && !string.IsNullOrWhiteSpace(queryKey))
            providedKey = queryKey.ToString().Trim();

        if (string.IsNullOrEmpty(providedKey))
        {
            context.Result = new UnauthorizedObjectResult(new { Error = "Missing authentication token or API key." });
            return;
        }

        // 2a. Check if Master API Key (SuperAdmin)
        var masterKey = configuration["MasterApiKey"];
        if (!string.IsNullOrEmpty(masterKey) && providedKey == masterKey)
        {
            httpContext.Items["AuthRole"] = UserRole.SuperAdmin;
            await next();
            return;
        }

        // 2b. Check Tenant Hashed API Key
        var hashedKey = ComputeSha256Hash(providedKey);
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ApiKeyHash == hashedKey);

        if (tenant == null)
        {
            context.Result = new UnauthorizedObjectResult(new { Error = "Invalid API key." });
            return;
        }

        // Agency clients: an inactive agency blocks its clients' keys too
        if (tenant.AgencyId is Guid tenantAgencyId)
        {
            var agencyStatus = await dbContext.Agencies.AsNoTracking()
                .Where(a => a.Id == tenantAgencyId).Select(a => a.BillingStatus).FirstOrDefaultAsync();
            if (agencyStatus == "Suspended")
            {
                context.Result = new ObjectResult(new { Error = "The agency account is suspended due to unpaid invoices." }) { StatusCode = 402 };
                return;
            }
        }

        if (tenant.BillingStatus == "Suspended")
        {
            context.Result = new ObjectResult(new { Error = "Tenant account is suspended due to unpaid invoices." })
            {
                StatusCode = 402 // Payment Required
            };
            return;
        }

        // Check allowed roles for machine calls
        if (_allowedRoles.Length > 0 && !_allowedRoles.Contains(UserRole.TenantUser))
        {
            context.Result = new ObjectResult(new { Error = "Forbidden: Key does not have the required permissions." })
            {
                StatusCode = 403
            };
            return;
        }

        httpContext.Items["AuthRole"] = UserRole.TenantUser;
        httpContext.Items["TenantId"] = tenant.Id;
        httpContext.Items["AgencyId"] = tenant.AgencyId;

        await next();
    }

    private static string ComputeSha256Hash(string rawData)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToBase64String(bytes);
    }
}