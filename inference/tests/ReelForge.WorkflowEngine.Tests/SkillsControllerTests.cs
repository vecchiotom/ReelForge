using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Controllers;
using ReelForge.Inference.Api.Controllers.Dto;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Skills;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Coverage for the "skill assignment is editable ONLY for custom agents, never built-in ones"
/// rule (SkillsController.SetSkills) plus the AgentDefinitionResponse EffectiveSkills resolution
/// (AgentsController.MapToResponse). This is deliberately the OPPOSITE behavior from
/// AgentsController.SetInferenceProvider, which allows built-ins — see both classes' doc
/// comments.
/// </summary>
public class SkillsControllerTests
{
    private static InferenceApiDbContext CreateContext()
    {
        DbContextOptions<InferenceApiDbContext> options = new DbContextOptionsBuilder<InferenceApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new InferenceApiDbContext(options);
    }

    private sealed class FakeCurrentUser(Guid userId, bool isAdmin) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
        public string Email { get; } = "test@example.com";
        public bool IsAuthenticated { get; } = true;
        public bool IsAdmin { get; } = isAdmin;
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Test";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }

    private static AgentDefinition BuiltInAgent(AgentType agentType, string? assignedSkillsJson = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = agentType.ToString(),
        Description = "d",
        SystemPrompt = "p",
        AgentType = agentType,
        IsBuiltIn = true,
        AssignedSkillsJson = assignedSkillsJson,
        CreatedAt = DateTime.UtcNow
    };

    private static AgentDefinition CustomAgent(string? assignedSkillsJson = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "MyCustomAgent",
        Description = "d",
        SystemPrompt = "p",
        AgentType = AgentType.Custom,
        IsBuiltIn = false,
        OwnerId = Guid.NewGuid(),
        AssignedSkillsJson = assignedSkillsJson,
        CreatedAt = DateTime.UtcNow
    };

    // --- The single most important test: 403 for built-in agents ---

    [Fact]
    public async Task SetSkills_returns_403_forbidden_for_a_built_in_agent()
    {
        using InferenceApiDbContext db = CreateContext();
        AgentDefinition agent = BuiltInAgent(AgentType.AuthorAgent);
        db.AgentDefinitions.Add(agent);
        await db.SaveChangesAsync();

        SkillsController controller = new(db, new FakeCurrentUser(Guid.NewGuid(), isAdmin: true), new FakeWebHostEnvironment());

        ActionResult<AgentDefinitionResponse> result = await controller.SetSkills(
            agent.Id, new SetAgentSkillsRequest(["remotion-markup"]), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();

        // The column must be left untouched — the request must not have been persisted.
        AgentDefinition reloaded = await db.AgentDefinitions.SingleAsync(a => a.Id == agent.Id);
        reloaded.AssignedSkillsJson.Should().BeNull();
    }

    [Fact]
    public async Task SetSkills_succeeds_and_persists_for_a_custom_agent()
    {
        using InferenceApiDbContext db = CreateContext();
        AgentDefinition agent = CustomAgent();
        db.AgentDefinitions.Add(agent);
        await db.SaveChangesAsync();

        SkillsController controller = new(db, new FakeCurrentUser(Guid.NewGuid(), isAdmin: true), new FakeWebHostEnvironment());

        ActionResult<AgentDefinitionResponse> result = await controller.SetSkills(
            agent.Id, new SetAgentSkillsRequest(["remotion-markup", "remotion-render"]), CancellationToken.None);

        OkObjectResult ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        AgentDefinitionResponse response = ok.Value.Should().BeOfType<AgentDefinitionResponse>().Subject;
        response.AssignedSkills.Should().BeEquivalentTo(["remotion-markup", "remotion-render"]);
        response.EffectiveSkills.Should().BeEquivalentTo(["remotion-markup", "remotion-render"]);

        AgentDefinition reloaded = await db.AgentDefinitions.SingleAsync(a => a.Id == agent.Id);
        JsonSerializer.Deserialize<string[]>(reloaded.AssignedSkillsJson!)
            .Should().BeEquivalentTo(["remotion-markup", "remotion-render"]);
    }

    [Fact]
    public async Task SetSkills_returns_400_for_unknown_skill_names()
    {
        using InferenceApiDbContext db = CreateContext();
        AgentDefinition agent = CustomAgent();
        db.AgentDefinitions.Add(agent);
        await db.SaveChangesAsync();

        SkillsController controller = new(db, new FakeCurrentUser(Guid.NewGuid(), isAdmin: true), new FakeWebHostEnvironment());

        ActionResult<AgentDefinitionResponse> result = await controller.SetSkills(
            agent.Id, new SetAgentSkillsRequest(["remotion-markup", "not-a-real-skill"]), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();

        AgentDefinition reloaded = await db.AgentDefinitions.SingleAsync(a => a.Id == agent.Id);
        reloaded.AssignedSkillsJson.Should().BeNull();
    }

    [Fact]
    public async Task SetSkills_returns_403_for_non_admin_even_on_a_custom_agent()
    {
        using InferenceApiDbContext db = CreateContext();
        AgentDefinition agent = CustomAgent();
        db.AgentDefinitions.Add(agent);
        await db.SaveChangesAsync();

        SkillsController controller = new(db, new FakeCurrentUser(agent.OwnerId!.Value, isAdmin: false), new FakeWebHostEnvironment());

        ActionResult<AgentDefinitionResponse> result = await controller.SetSkills(
            agent.Id, new SetAgentSkillsRequest(["remotion-markup"]), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    // --- EffectiveSkills resolution (AgentsController.MapToResponse) ---

    [Fact]
    public void MapToResponse_EffectiveSkills_matches_SkillCatalog_DefaultsFor_a_built_in_agent()
    {
        AgentDefinition agent = BuiltInAgent(AgentType.AuthorAgent);

        AgentDefinitionResponse response = AgentsController.MapToResponse(agent);

        response.AssignedSkills.Should().BeNull();
        response.EffectiveSkills.Should().BeEquivalentTo(SkillCatalog.DefaultsFor(AgentType.AuthorAgent));
    }

    [Fact]
    public void MapToResponse_EffectiveSkills_matches_stored_AssignedSkillsJson_for_a_custom_agent()
    {
        AgentDefinition agent = CustomAgent(JsonSerializer.Serialize(new[] { "remotion-captions" }));

        AgentDefinitionResponse response = AgentsController.MapToResponse(agent);

        response.AssignedSkills.Should().BeEquivalentTo(["remotion-captions"]);
        response.EffectiveSkills.Should().BeEquivalentTo(["remotion-captions"]);
    }

    [Fact]
    public void MapToResponse_EffectiveSkills_is_empty_not_null_for_a_custom_agent_with_no_assignment()
    {
        AgentDefinition agent = CustomAgent(assignedSkillsJson: null);

        AgentDefinitionResponse response = AgentsController.MapToResponse(agent);

        response.AssignedSkills.Should().BeNull();
        response.EffectiveSkills.Should().BeEmpty();
    }

    // --- Skill listing ---

    [Fact]
    public async Task List_computes_assignedAgentCount_only_from_custom_agent_rows()
    {
        using InferenceApiDbContext db = CreateContext();
        db.AgentDefinitions.Add(BuiltInAgent(AgentType.AuthorAgent)); // never counted, even if set
        db.AgentDefinitions.Add(CustomAgent(JsonSerializer.Serialize(new[] { "remotion-markup" })));
        db.AgentDefinitions.Add(CustomAgent(JsonSerializer.Serialize(new[] { "remotion-markup", "remotion-render" })));
        await db.SaveChangesAsync();

        SkillsController controller = new(db, new FakeCurrentUser(Guid.NewGuid(), isAdmin: false), new FakeWebHostEnvironment());

        ActionResult<List<SkillSummaryResponse>> result = await controller.List(CancellationToken.None);

        OkObjectResult ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        List<SkillSummaryResponse> skills = ok.Value.Should().BeAssignableTo<List<SkillSummaryResponse>>().Subject;
        skills.Single(s => s.Name == "remotion-markup").AssignedAgentCount.Should().Be(2);
        skills.Single(s => s.Name == "remotion-render").AssignedAgentCount.Should().Be(1);
        skills.Single(s => s.Name == "remotion-captions").AssignedAgentCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_returns_404_for_unknown_skill_name()
    {
        using InferenceApiDbContext db = CreateContext();
        SkillsController controller = new(db, new FakeCurrentUser(Guid.NewGuid(), isAdmin: false), new FakeWebHostEnvironment());

        ActionResult<SkillDetailResponse> result = await controller.Get("does-not-exist", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
