using System.Security.Claims;
using ReelForge.Shared.Auth;

namespace ReelForge.Inference.Api.Services.Auth;

/// <summary>
/// Extracts current user identity from the HTTP context JWT claims.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            string? sub = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(sub, out Guid id) ? id : Guid.Empty;
        }
    }

    public string Email =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("email")
        ?? string.Empty;

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public bool IsAdmin
    {
        get
        {
            string? claim = _httpContextAccessor.HttpContext?.User.FindFirstValue("isAdmin");
            return bool.TryParse(claim, out bool isAdmin) && isAdmin;
        }
    }
}
