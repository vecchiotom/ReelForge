namespace ReelForge.Shared.Auth;

/// <summary>
/// Provides access to the current authenticated user's identity.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Gets the user ID from the JWT sub claim.</summary>
    Guid UserId { get; }

    /// <summary>Gets the user's email from the JWT email claim.</summary>
    string Email { get; }

    /// <summary>Returns true if the user is authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Returns true if the user has admin privileges.</summary>
    bool IsAdmin { get; }
}
