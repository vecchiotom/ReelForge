using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReelForge.Inference.Api.Data;
using ReelForge.Shared.Auth;
using ReelForge.Shared.Data.Models;

namespace ReelForge.Inference.Api.Controllers;

/// <summary>
/// Provides access to video/image artifacts produced by workflow step executions.
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/outputs")]
[Authorize]
public class OutputsController : ControllerBase
{
    private readonly InferenceApiDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public OutputsController(
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

    /// <summary>
    /// Lists all workflow step results that produced a media output for this project,
    /// ordered most-recent first.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<OutputVideoResponse>>> ListOutputs(Guid projectId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        List<OutputVideoResponse> outputs = await _db.WorkflowStepResults
            .Where(r =>
                r.OutputStorageKey != null &&
                r.WorkflowExecution.ProjectId == projectId)
            .OrderByDescending(r => r.CompletedAt ?? r.ExecutedAt)
            .Select(r => new OutputVideoResponse(
                r.Id,
                r.WorkflowExecutionId,
                r.WorkflowStepId,
                r.OutputStorageKey!,
                Path.GetFileName(r.OutputStorageKey!),
                r.CompletedAt ?? r.ExecutedAt))
            .ToListAsync(ct);

        return Ok(outputs);
    }

    /// <summary>
    /// Streams the media artifact for a specific step result directly from S3/MinIO.
    /// The Content-Disposition header is set to inline so browsers can play it directly.
    /// </summary>
    [HttpGet("{stepResultId:guid}/download")]
    public async Task<IActionResult> DownloadOutput(Guid projectId, Guid stepResultId, CancellationToken ct)
    {
        Project? project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound();
        if (project.OwnerId != _currentUser.UserId) return Forbid();

        WorkflowStepResult? stepResult = await _db.WorkflowStepResults
            .Include(r => r.WorkflowExecution)
            .FirstOrDefaultAsync(r => r.Id == stepResultId && r.WorkflowExecution.ProjectId == projectId, ct);

        if (stepResult == null) return NotFound();
        if (stepResult.OutputStorageKey == null) return NotFound("This step result has no media output.");

        // Validate storage key scope: must be in outputs/ prefix for security
        if (!stepResult.OutputStorageKey.StartsWith("outputs/", StringComparison.Ordinal))
            return Forbid();

        GetObjectResponse s3Response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = stepResult.OutputStorageKey
        }, ct);

        string fileName = Path.GetFileName(stepResult.OutputStorageKey);
        string contentType = s3Response.Headers.ContentType ?? InferContentType(fileName);

        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");

        return File(s3Response.ResponseStream, contentType, enableRangeProcessing: true);
    }

    private static string InferContentType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".gif" => "image/gif",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}

public record OutputVideoResponse(
    Guid StepResultId,
    Guid WorkflowExecutionId,
    Guid WorkflowStepId,
    string StorageKey,
    string FileName,
    DateTime ProducedAt);
