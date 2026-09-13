using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;
using System.Net;

namespace ReelForge.Inference.Api.Controllers;

/// <summary>
/// Streams the large, non-playable JSON artifact (full video-analysis document or a compile
/// step's edit-decision-list audit trail) referenced by a workflow step result's
/// <see cref="WorkflowStepResult.ArtifactStorageKey"/>. Mirrors <see cref="OutputsController"/>'s
/// authorization/validation guards exactly, but validates against the artifact key prefix
/// (<c>projects/{projectId}/agentFiles/video-analysis</c>, per the WorkflowEngine's
/// ProjectFileWorkspace.UploadArtifactAsync key construction and
/// <see cref="WorkflowStepResult.ArtifactStorageKey"/>'s own doc comment) rather than the
/// outputFiles prefix, so this endpoint can never be coerced into serving an unrelated object —
/// including another project's rendered mp4 under outputFiles.
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/step-results/{stepResultId:guid}/artifact")]
[Authorize]
public class StepResultArtifactsController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public StepResultArtifactsController(
        InferenceApiDbContext db,
        ICurrentUser currentUser,
        IAmazonS3 s3Client,
        IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _s3Client = s3Client;
        _bucketName = configuration["MinIO:BucketName"] ?? "reelforge";
    }

    [HttpGet]
    public async Task<IActionResult> GetArtifact(Guid projectId, Guid stepResultId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowStepResult? stepResult = await _db.WorkflowStepResults
            .Include(r => r.WorkflowExecution)
            .FirstOrDefaultAsync(r => r.Id == stepResultId && r.WorkflowExecution.ProjectId == projectId, ct);

        if (stepResult == null) return NotFound();
        if (stepResult.ArtifactStorageKey == null) return NotFound("This step result has no artifact.");

        // Validate storage key scope: must live under this project's video-analysis artifact
        // prefix — never the outputFiles prefix (a playable render/edit) and never another
        // project's objects, even though the WorkflowExecution join above already scopes the
        // step result to this project.
        // Trailing slash makes this a true path-segment prefix — without it, a key like
        // "projects/{id}/agentFiles/video-analysis-foreign/..." would also match (found by
        // Copilot review), letting this endpoint reach objects outside its intended scope.
        string expectedKeyPrefix = $"projects/{projectId}/agentFiles/video-analysis/";
        if (!stepResult.ArtifactStorageKey.StartsWith(expectedKeyPrefix, StringComparison.Ordinal))
            return Forbid();

        GetObjectResponse s3Response;
        try
        {
            s3Response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = stepResult.ArtifactStorageKey
            }, ct);
        }
        catch (AmazonS3Exception ex) when (IsMissingObjectError(ex))
        {
            return NotFound("Artifact not found in storage.");
        }

        return File(s3Response.ResponseStream, "application/json");
    }

    private static bool IsMissingObjectError(AmazonS3Exception ex)
    {
        return ex.StatusCode == HttpStatusCode.NotFound ||
               string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.Ordinal) ||
               string.Equals(ex.ErrorCode, "NotFound", StringComparison.Ordinal);
    }
}
