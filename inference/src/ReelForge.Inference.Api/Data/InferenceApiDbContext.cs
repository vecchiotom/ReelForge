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
            entity.HasOne(e => e.Project)
                .WithMany(p => p.Files)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.SummaryStatus)
                .HasConversion<string>();
            entity.Property(e => e.StorageMetadataJson)
                .HasColumnType("jsonb");
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
            entity.Property(e => e.ConfigJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.OutputSchemaJson)
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
            entity.Property(e => e.ParallelAgentIdsJson)
                .HasColumnType("jsonb");
            entity.Property(e => e.StepType)
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
