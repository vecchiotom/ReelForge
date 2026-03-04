using System.Security.Claims;

namespace ReelForge.Inference.Services.Auth;

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

    /// <inheritdoc />
    public Guid UserId
    {
        get
        {
            string? sub = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(sub, out Guid id) ? id : Guid.Empty;
        }
    }

    /// <inheritdoc />
    public string Email =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("email")
        ?? string.Empty;

    /// <inheritdoc />
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            string? claim = _httpContextAccessor.HttpContext?.User.FindFirstValue("isAdmin");
            return bool.TryParse(claim, out bool isAdmin) && isAdmin;
        }
    }
}
