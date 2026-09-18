using Microsoft.EntityFrameworkCore;
using ReelForge.Shared;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Data;

/// <summary>
/// EF Core context for the Inference API service.
/// Owns: application_users, projects, project_files, agent_definitions.
/// References (ExcludeFromMigrations): workflow_* tables, review_scores.
/// </summary>
public class InferenceApiDbContext : DbContext
{
    public InferenceApiDbContext(DbContextOptions<InferenceApiDbContext> options) : base(options) { }

    // Owned tables
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectFile> ProjectFiles => Set<ProjectFile>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<InferenceProvider> InferenceProviders => Set<InferenceProvider>();

    // Referenced tables (for navigation properties, not migrations)
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<WorkflowStepResult> WorkflowStepResults => Set<WorkflowStepResult>();
    public DbSet<ReviewScore> ReviewScores => Set<ReviewScore>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply snake_case naming convention
        SnakeCaseNamingHelper.ApplySnakeCaseNaming(modelBuilder);

        // --- Owned tables ---

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Owner)
                .WithMany(u => u.Projects)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Status)
                .HasConversion<string>();
        });

        modelBuilder.Entity<ProjectFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProjectId, e.Category, e.DirectoryPath });
            entity.HasIndex(e => new { e.ProjectId, e.Category, e.UploadedAt });
            entity.HasOne(e => e.Project)
                .WithMany(p => p.Files)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.SummaryStatus)
                .HasConversion<string>();
            entity.Property(e => e.IndexingStatus)
                .HasConversion<string>();
            entity.Property(e => e.StorageMetadataJson)
                .HasColumnType("jsonb");

            // New fields for folder structure and categorization
            entity.Property(e => e.OriginalPath)
                .HasMaxLength(1000);
            entity.Property(e => e.DirectoryPath)
                .HasMaxLength(1000);
            entity.Property(e => e.Category)
                .HasMaxLength(50)
                .HasDefaultValue("userFiles");
            entity.Property(e => e.StorageFileName)
                .HasMaxLength(260);
        });

        modelBuilder.Entity<AgentDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Owner)
                .WithMany(u => u.AgentDefinitions)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.AgentType)
                .HasConversion<string>();
            entity.Property(e => e.ContextMode)
                .HasConversion<string>()
                .HasDefaultValue(ContextMode.LastStep);
            entity.Property(e => e.ConfigJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.OutputSchemaJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.AssignedSkillsJson)
                .HasColumnType("jsonb");
            entity.HasOne(e => e.InferenceProvider)
                .WithMany(p => p.AgentDefinitions)
                .HasForeignKey(e => e.InferenceProviderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InferenceProvider>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            // One default row PER capability (a Chat default and a Transcription default
            // coexist independently) — replaces the old UNIQUE(is_default) index, not a
            // supplement to it (see R4: the old index would still admit two IsDefault rows,
            // one per capability, which is exactly what we now want, but only via this
            // composite shape).
            entity.HasIndex(e => new { e.Capability, e.IsDefault }).IsUnique().HasFilter("is_default");
            entity.Property(e => e.Kind)
                .HasConversion<string>();
            entity.Property(e => e.Capability)
                .HasConversion<string>()
                .HasDefaultValue(InferenceProviderCapability.Chat);
            entity.Property(e => e.ExtraHeadersJson)
                .HasColumnType("jsonb");
        });

        // --- Referenced tables (excluded from migrations) ---

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Project)
                .WithMany(p => p.WorkflowDefinitions)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("workflow_definitions", t => t.ExcludeFromMigrations());
        });

        modelBuilder.Entity<WorkflowStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.WorkflowDefinition)
                .WithMany(w => w.Steps)
                .HasForeignKey(e => e.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AgentDefinition)
                .WithMany(a => a.WorkflowSteps)
                .HasForeignKey(e => e.AgentDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.EdgeConditionJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.InputMappingJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.SelectedPriorStepOrdersJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.ParallelAgentIdsJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.ExtractConfigJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.VideoAnalyzeConfigJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.VideoCompileConfigJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.EditRoomConfigJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.GraphicsRoomConfigJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.StepType)
                .HasConversion<string>();
            entity.Property(e => e.AgentInputContextMode)
                .HasConversion<string>();
            entity.ToTable("workflow_steps", t => t.ExcludeFromMigrations());
        });

        modelBuilder.Entity<WorkflowExecution>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.WorkflowDefinition)
                .WithMany(w => w.Executions)
                .HasForeignKey(e => e.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Project)
                .WithMany(p => p.WorkflowExecutions)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CurrentStep)
                .WithMany()
                .HasForeignKey(e => e.CurrentStepId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.Status)
                .HasConversion<string>();
            entity.Property(e => e.ResultJson)
                .HasColumnType("jsonb");
            entity.ToTable("workflow_executions", t => t.ExcludeFromMigrations());
        });

        modelBuilder.Entity<WorkflowStepResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.WorkflowExecution)
                .WithMany(e => e.StepResults)
                .HasForeignKey(e => e.WorkflowExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.WorkflowStep)
                .WithMany(s => s.Results)
                .HasForeignKey(e => e.WorkflowStepId)
                // allow steps to be removed even if historic results exist; we clean them up automatically
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.InputJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.OutputJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.ToolCallsJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.ReasoningJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.ChatTranscriptJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.Status)
                .HasConversion<string>();
            entity.ToTable("workflow_step_results", t => t.ExcludeFromMigrations());
        });

        modelBuilder.Entity<ReviewScore>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.WorkflowExecution)
                .WithMany(e => e.ReviewScores)
                .HasForeignKey(e => e.WorkflowExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("review_scores", t => t.ExcludeFromMigrations());
        });
    }
}
