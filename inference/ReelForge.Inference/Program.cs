using System.Text;
using Amazon.S3;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ReelForge.Inference.Agents;
using ReelForge.Inference.Agents.Analysis;
using ReelForge.Inference.Agents.FileProcessing;
using ReelForge.Inference.Agents.Production;
using ReelForge.Inference.Agents.Quality;
using ReelForge.Inference.Agents.Translation;
using ReelForge.Inference.Data;
using ReelForge.Inference.Services.Auth;
using ReelForge.Inference.Services.Background;
using ReelForge.Inference.Services.Storage;
using ReelForge.Inference.Workflows;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --- Database ---
builder.Services.AddDbContext<ReelForgeDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// --- Authentication ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"] ?? string.Empty)),
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorization();

// --- HTTP Context & Current User ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// --- MinIO / S3 ---
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    AmazonS3Config config = new()
    {
        ServiceURL = builder.Configuration["MinIO:Endpoint"] ?? "http://localhost:9000",
        ForcePathStyle = true
    };
    return new AmazonS3Client(
        builder.Configuration["MinIO:AccessKey"] ?? string.Empty,
        builder.Configuration["MinIO:SecretKey"] ?? string.Empty,
        config);
});
builder.Services.AddSingleton<IFileStorageService, S3FileStorageService>();

// --- AI Chat Client ---
builder.Services.AddSingleton<IChatClient>(sp =>
{
    string endpoint = builder.Configuration["AzureOpenAI:Endpoint"] ?? string.Empty;
    string apiKey = builder.Configuration["AzureOpenAI:ApiKey"] ?? string.Empty;
    string deploymentName = builder.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";

    AzureOpenAIClient client = new(
        new Uri(endpoint),
        new System.ClientModel.ApiKeyCredential(apiKey));

    return client.GetChatClient(deploymentName).AsIChatClient();
});

// --- Agents ---
builder.Services.AddSingleton<IReelForgeAgent, CodeStructureAnalyzerAgent>();
builder.Services.AddSingleton<IReelForgeAgent, DependencyAnalyzerAgent>();
builder.Services.AddSingleton<IReelForgeAgent, ComponentInventoryAnalyzerAgent>();
builder.Services.AddSingleton<IReelForgeAgent, RouteAndApiAnalyzerAgent>();
builder.Services.AddSingleton<IReelForgeAgent, StyleAndThemeExtractorAgent>();
builder.Services.AddSingleton<IReelForgeAgent, RemotionComponentTranslatorAgent>();
builder.Services.AddSingleton<IReelForgeAgent, AnimationStrategyAgentImpl>();
builder.Services.AddSingleton<IReelForgeAgent, DirectorAgentImpl>();
builder.Services.AddSingleton<IReelForgeAgent, ScriptwriterAgentImpl>();
builder.Services.AddSingleton<IReelForgeAgent, AuthorAgentImpl>();
builder.Services.AddSingleton<IReelForgeAgent, ReviewAgentImpl>();
builder.Services.AddSingleton<IReelForgeAgent, FileSummarizerAgentImpl>();

// --- Agent Registry ---
builder.Services.AddSingleton<IAgentRegistry, AgentRegistry>();

// --- Workflow Engine ---
builder.Services.AddScoped<WorkflowExecutorService>();

// --- Background Task Queues ---
builder.Services.AddSingleton<IBackgroundTaskQueue<FileSummarizationTask>, ChannelBackgroundTaskQueue<FileSummarizationTask>>();
builder.Services.AddSingleton<IBackgroundTaskQueue<WorkflowExecutionTask>, ChannelBackgroundTaskQueue<WorkflowExecutionTask>>();

// --- Background Services ---
builder.Services.AddHostedService<FileSummarizationService>();
builder.Services.AddHostedService<WorkflowExecutionBackgroundService>();

// --- Controllers & Swagger ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ReelForge Inference API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

WebApplication app = builder.Build();

// --- Middleware pipeline ---
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// --- Seed database ---
await DatabaseSeeder.SeedAsync(app.Services);

app.Run();
