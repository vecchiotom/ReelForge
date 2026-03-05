namespace ReelForge.Shared.Data.Models;

/// <summary>
/// Represents an application user (tenant).
/// </summary>
public class ApplicationUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool MustChangePassword { get; set; } = true;

    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<AgentDefinition> AgentDefinitions { get; set; } = new List<AgentDefinition>();
}
