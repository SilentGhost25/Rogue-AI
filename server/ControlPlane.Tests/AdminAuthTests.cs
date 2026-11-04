using System.Net;
using System.Security.Claims;
using ControlPlane.Auth;
using ControlPlane.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlane.Tests;

public class AdminAuthTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly Database _db;
    private readonly IConfiguration _cfg;
    private const string ValidAdminKey = "test-crypto-random-admin-key-123456";

    public AdminAuthTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), "admin_auth_test_" + Guid.NewGuid().ToString("N") + ".db");
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Breach:DatabasePath", _testDbPath},
            {"Breach:AdminKey", ValidAdminKey},
            {"Breach:AdminLoopbackOnly", "true"},
            {"AdminAuth:Username", "admin"},
            {"AdminAuth:PasswordHash", "$2b$12$ZY7Y1XfC82gAGLgLqHhCpOLZJ3l1CdTvxpT5ypMq5E54NnzpSiO8C"} // admin123456
        };
        _cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _db = new Database(_cfg);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
    }

    [Fact]
    public void Session_cookie_auth_bypasses_loopback_restriction_from_lan_ip()
    {
        var ctx = new DefaultHttpContext();
        // Simulate remote LAN IP
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");

        // Authenticated user with admin role
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, "admin")
        }, AdminAuth.Scheme);
        ctx.User = new ClaimsPrincipal(identity);

        var isAuthorized = AdminAuth.AuthorizeAdminOrSession(ctx, _db, _cfg);
        Assert.True(isAuthorized, "Session cookie auth must succeed from LAN IP bypassing loopback check.");
    }

    [Fact]
    public void Key_auth_from_remote_lan_ip_is_blocked_when_loopback_only()
    {
        var ctx = new DefaultHttpContext();
        // Simulate remote LAN IP presenting valid key
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        ctx.Request.Headers["X-Admin-Key"] = ValidAdminKey;

        var isAuthorized = AdminAuth.AuthorizeAdminOrSession(ctx, _db, _cfg);
        Assert.False(isAuthorized, "Key-based auth from remote IP must be blocked when AdminLoopbackOnly is true.");
    }

    [Fact]
    public void Key_auth_from_loopback_ip_succeeds()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        ctx.Request.Headers["X-Admin-Key"] = ValidAdminKey;

        var isAuthorized = AdminAuth.AuthorizeAdminOrSession(ctx, _db, _cfg);
        Assert.True(isAuthorized, "Key-based auth from loopback IP must succeed.");
    }

    [Fact]
    public void BCrypt_verifies_configured_password_correctly()
    {
        var hash = _cfg["AdminAuth:PasswordHash"]!;
        Assert.True(BCrypt.Net.BCrypt.Verify("admin123456", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("wrongpassword", hash));
    }
}
