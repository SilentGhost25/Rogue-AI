using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ControlPlane.Data;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ControlPlane.Auth;

public static class AdminAuth
{
    public const string Scheme = "AdminSession";

    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> FailedAdminAttempts = new();

    public static IServiceCollection AddAdminSessionAuth(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddAuthentication(Scheme)
            .AddCookie(Scheme, o =>
            {
                o.Cookie.Name = "rogue_admin";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(cfg.GetValue<int>("AdminAuth:SessionHours", 8));
                o.SlidingExpiration = false;
                o.LoginPath = "/admin/login.html";
                o.LogoutPath = "/admin/api/logout";
                o.AccessDeniedPath = "/admin/login.html";
            });
        return services;
    }

    /// <summary>
    /// Accepts EITHER a valid session cookie OR a valid X-Admin-Key header / ?key=.
    /// Loopback-only guard applies ONLY to key-based auth — session auth has its own
    /// password credential, so a valid session from a LAN IP is allowed.
    /// </summary>
    public static bool AuthorizeAdminOrSession(HttpContext ctx, Database db, IConfiguration cfg)
    {
        // Path 1: session cookie — authenticated user with admin role bypasses loopback check
        if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.IsInRole("admin"))
        {
            return true;
        }

        // Path 2: key-based authentication with loopback and rate-limiting enforcement
        return CheckAdminKey(ctx, db, cfg);
    }

    private static bool CheckAdminKey(HttpContext ctx, Database db, IConfiguration cfg)
    {
        var remoteIp = ctx.Connection.RemoteIpAddress;
        var ipStr = remoteIp?.ToString() ?? "127.0.0.1";
        var loopbackOnly = cfg.GetValue<bool>("Breach:AdminLoopbackOnly", true);
        var allowedIps = cfg.GetSection("Breach:AdminAllowedIps").Get<string[]>() ?? Array.Empty<string>();

        // 1. IP boundary check: block unauthorized LAN connections for key-based auth
        if (loopbackOnly && !IsAuthorizedIp(remoteIp, allowedIps))
        {
            db.Log("ADMIN_ACCESS_DENIED_LAN", payloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                ip = ipStr,
                path = ctx.Request.Path.Value
            }));
            return false;
        }

        // 2. Extract Key
        var key = ctx.Request.Headers["X-Admin-Key"].ToString();
        if (string.IsNullOrEmpty(key) && ctx.Request.Query.TryGetValue("key", out var qKey))
        {
            key = qKey.ToString();
        }

        var adminKey = cfg["Breach:AdminKey"] ?? Environment.GetEnvironmentVariable("BREACH_ADMIN_KEY") ?? "";

        // 3. Authenticate Key FIRST (Rate limiter lockout protection):
        // A valid admin key NEVER gets blocked or locked out by bad actor spam.
        var isValidKey = !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(adminKey) && FixedTimeEquals(key, adminKey);
        if (isValidKey)
        {
            FailedAdminAttempts.TryRemove(ipStr, out _);
            return true;
        }

        // 4. Rate-limiting applies ONLY to callers presenting invalid/missing keys
        var now = DateTime.UtcNow;
        var (count, windowStart) = FailedAdminAttempts.GetOrAdd(ipStr, _ => (0, now));
        if (now - windowStart > TimeSpan.FromMinutes(1))
        {
            count = 0;
            windowStart = now;
        }

        count++;
        FailedAdminAttempts[ipStr] = (count, windowStart);

        if (count > 5)
        {
            db.Log("ADMIN_RATE_LIMITED", payloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                ip = ipStr,
                attempts = count,
                path = ctx.Request.Path.Value
            }));
            return false;
        }

        db.Log("ADMIN_UNAUTHORIZED", payloadJson: System.Text.Json.JsonSerializer.Serialize(new
        {
            ip = ipStr,
            attempts = count,
            path = ctx.Request.Path.Value
        }));
        return false;
    }

    private static bool IsAuthorizedIp(IPAddress? ip, string[] allowedIps)
    {
        if (ip == null) return true;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(ip.MapToIPv4())) return true;
        var s = ip.ToString();
        if (s == "::1" || s == "127.0.0.1" || s == "::ffff:127.0.0.1") return true;
        if (allowedIps.Contains(s)) return true;
        return false;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
