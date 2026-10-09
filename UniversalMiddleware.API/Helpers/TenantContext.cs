using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace UniversalMiddleware.API.Helpers;

public static class TenantContext
{
    // API-key requests: set by [ApiKeyAuth]. JWT requests: "TenantId" claim from TokenService.
    public static Guid? GetTenantId(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("TenantId", out var o) && o is Guid fromKey)
            return fromKey;
        return Guid.TryParse(ctx.User.FindFirst("TenantId")?.Value, out var fromJwt) ? fromJwt : null;
    }
}

public static class ApiKeys
{
    public static (string Raw, string Hash) Generate()
    {
        var raw = "um_live_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return (raw, Hash(raw));
    }

    // Same algorithm TenantsController uses: SHA256 -> Base64 of UTF8 bytes
    public static string Hash(string raw) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}