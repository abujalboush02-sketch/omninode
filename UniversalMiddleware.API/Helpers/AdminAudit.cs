using System;
using System.Security.Claims;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Helpers;

public static class AdminAudit
{
    /// <summary>Adds an audit row to the context; it's saved with the caller's SaveChanges.</summary>
    public static void Log(AppDbContext db, ClaimsPrincipal admin, string action, string targetType, Guid? targetId, string details = "")
    {
        Guid.TryParse(admin.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId);
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminUserId = adminId,
            AdminEmail = admin.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details.Length > 2000 ? details[..2000] : details,
            At = DateTime.UtcNow
        });
    }
}
