using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReelForge.WorkflowEngine.Agents.Tools;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Services.Storage;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class ProjectFileAgentToolsTests
{
    private static ProjectFileAgentTools CreateTools(
        IProjectFileWorkspace workspace, IWorkflowExecutionContextAccessor accessor) =>
        new(
            workspace,
            accessor,
            Mock.Of<IHttpClientFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<ProjectFileAgentTools>.Instance);

    [Fact]
    public async Task ReadProjectFile_returns_the_guardrail_message_instead_of_throwing_for_a_binary_file()
    {
        // Regression test: found live producing a real promo video. ProjectFileWorkspace.
        // ReadFileAsync deliberately throws InvalidOperationException rather than decoding a
        // binary file (a music/SFX/video candidate) as UTF-8 text — but that exception, left
        // uncaught here, propagated out of the whole agent run and failed a step that otherwise
        // had everything it needed (the model's own unnecessary curiosity about an unrelated
        // file). The tool must catch it and return the message as an ordinary tool result instead.
        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ReadFileAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "File 'music.mp3' (audio/mpeg, 3,080,358 bytes) is a binary file and cannot be read as text. " +
                "Use its file name and any metadata you were already given instead of reading its content."));

        var accessor = new WorkflowExecutionContextAccessor();
        using IDisposable _ = accessor.BeginScope(Guid.NewGuid(), Guid.NewGuid(), "test");

        ProjectFileAgentTools tools = CreateTools(workspace.Object, accessor);

        string result = await tools.ReadProjectFile("music.mp3");

        result.Should().StartWith("Error:");
        result.Should().Contain("binary file and cannot be read as text");
    }

    [Fact]
    public async Task ReadProjectFile_returns_file_content_unchanged_on_success()
    {
        var workspace = new Mock<IProjectFileWorkspace>();
        workspace
            .Setup(w => w.ReadFileAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync("export const x = 1;");

        var accessor = new WorkflowExecutionContextAccessor();
        using IDisposable _ = accessor.BeginScope(Guid.NewGuid(), Guid.NewGuid(), "test");

        ProjectFileAgentTools tools = CreateTools(workspace.Object, accessor);

        string result = await tools.ReadProjectFile("src/index.ts");

        result.Should().Be("export const x = 1;");
    }
}
