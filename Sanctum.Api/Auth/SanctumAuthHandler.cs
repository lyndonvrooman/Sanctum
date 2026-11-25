using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sanctum.Api.Data;
using Sanctum.Api.Domain.Entities;

namespace Sanctum.Api.Auth;

public class SanctumAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SanctumDbContext db,
    IConfiguration config,
    IHostEnvironment env,
    IHttpClientFactory httpClientFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static JsonWebKeySet? _cachedJwks;
    private static DateTime _jwksFetchedAtUtc = DateTime.MinValue;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Mobile App Path: Cloudflare Service Token + Device Bearer Token
        if (Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var rawToken = authHeader.ToString()["Bearer ".Length..].Trim();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
            var device = await db.DeviceTokens.Include(d => d.User).FirstOrDefaultAsync(d => d.TokenHash == hash);
            if (device is null) return AuthenticateResult.Fail("Invalid device token.");

            device.LastUsedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return BuildSuccessTicket(device.User);
        }

        // 2. Web UI Path: Cloudflare Zero Trust JWT Assertion
        if (Request.Headers.TryGetValue("Cf-Access-Jwt-Assertion", out var cfJwt))
        {
            var email = await ValidateCloudflareJwtAsync(cfJwt.ToString());
            if (string.IsNullOrWhiteSpace(email)) return AuthenticateResult.Fail("Invalid Cloudflare Access JWT.");

            var user = await GetOrProvisionUserAsync(email);
            return BuildSuccessTicket(user);
        }

        // 3. Local Development Mode Fallback
        if (env.IsDevelopment())
        {
            var devEmail = Request.Headers["X-Dev-User-Email"].FirstOrDefault()
                           ?? config["Sanctum:DomEmail"] ?? config["Sanctum:DefaultDevEmail"]
                           ?? "dom@kinkyfairytales.ca";
            var user = await GetOrProvisionUserAsync(devEmail);
            return BuildSuccessTicket(user);
        }

        return AuthenticateResult.NoResult();
    }

    private async Task<string?> ValidateCloudflareJwtAsync(string jwt)
    {
        var teamDomain = config["Cloudflare:TeamDomain"]?.TrimEnd('/');
        var policyAud = config["Cloudflare:PolicyAud"];
        if (string.IsNullOrWhiteSpace(teamDomain) || string.IsNullOrWhiteSpace(policyAud)) return null;

        if (_cachedJwks is null || DateTime.UtcNow - _jwksFetchedAtUtc > TimeSpan.FromHours(6))
        {
            var client = httpClientFactory.CreateClient();
            var jwksJson = await client.GetStringAsync($"{teamDomain}/cdn-cgi/access/certs");
            _cachedJwks = new JsonWebKeySet(jwksJson);
            _jwksFetchedAtUtc = DateTime.UtcNow;
        }

        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = teamDomain,
            ValidAudience = policyAud,
            IssuerSigningKeys = _cachedJwks.GetSigningKeys(),
            ValidateLifetime = true
        };

        try
        {
            var principal = handler.ValidateToken(jwt, parameters, out _);
            return principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
        }
        catch
        {
            return null;
        }
    }

    private async Task<AppUser> GetOrProvisionUserAsync(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
        if (user is not null) return user;

        user = new AppUser { Id = Guid.NewGuid(), Email = normalized, DisplayName = normalized.Split('@')[0] };
        db.Users.Add(user);

        var space = await db.DynamicSpaces.FirstOrDefaultAsync();
        if (space is not null)
        {
            db.DynamicMembers.Add(new DynamicMember
            {
                Id = Guid.NewGuid(),
                DynamicSpaceId = space.Id,
                UserId = user.Id,
                RoleTitle = "Partner"
            });
        }

        await db.SaveChangesAsync();
        return user;
    }

    private AuthenticateResult BuildSuccessTicket(AppUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

