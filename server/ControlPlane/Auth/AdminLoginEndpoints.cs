using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace ControlPlane.Auth;

public static class AdminLoginEndpoints
{
    public static void MapAdminLogin(this WebApplication app)
    {
        var cfg = app.Configuration;
        var log = app.Logger;

        app.MapPost("/admin/api/login", async (HttpContext ctx, LoginBody body) =>
        {
            var user = cfg["AdminAuth:Username"] ?? "admin";
            var hash = cfg["AdminAuth:PasswordHash"] ?? "";

            if (string.IsNullOrEmpty(hash))
            {
                return Results.Problem("AdminAuth:PasswordHash is not configured.");
            }

            var usernameMatches = string.Equals(body.Username, user, StringComparison.Ordinal);
            var passwordMatches = !string.IsNullOrEmpty(body.Password) && BCrypt.Net.BCrypt.Verify(body.Password, hash);

            if (!usernameMatches || !passwordMatches)
            {
                log.LogWarning("Failed admin login attempt for user '{User}' from {Remote}", body.Username, ctx.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user),
                new Claim(ClaimTypes.Role, "admin")
            }, AdminAuth.Scheme);

            await ctx.SignInAsync(AdminAuth.Scheme, new ClaimsPrincipal(identity));
            log.LogInformation("Admin logged in successfully from {Remote}", ctx.Connection.RemoteIpAddress);
            return Results.Ok(new { ok = true, username = user });
        }).AllowAnonymous();

        app.MapPost("/admin/api/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(AdminAuth.Scheme);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization(AdminAuth.Scheme);

        app.MapGet("/admin/api/whoami", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { username = ctx.User.Identity.Name })
                : Results.Unauthorized()
        ).RequireAuthorization(AdminAuth.Scheme);
    }
}

public record LoginBody(string Username, string Password);
