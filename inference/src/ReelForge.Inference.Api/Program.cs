using System.Text;
using Amazon.S3;
using Azure.AI.OpenAI;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ReelForge.Inference.Api.Agents;
using ReelForge.Inference.Api.Agents.FileProcessing;
using ReelForge.Inference.Api.Data;
using ReelForge.Inference.Api.Services.Auth;
using ReelForge.Inference.Api.Services.Background;
using ReelForge.Inference.Api.Services.Storage;
using ReelForge.Shared.Auth;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --- Database ---
builder.Services.AddDbContext<InferenceApiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Api")));

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

// --- AI Chat Client (for file summarization) ---
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

// --- File Summarizer Agent ---
builder.Services.AddSingleton<IReelForgeAgent, FileSummarizerAgentImpl>();
builder.Services.AddSingleton<IAgentRegistry, AgentRegistry>();

// --- Background Task Queue (file summarization) ---
builder.Services.AddSingleton<IBackgroundTaskQueue<FileSummarizationTask>, ChannelBackgroundTaskQueue<FileSummarizationTask>>();
builder.Services.AddHostedService<FileSummarizationService>();

// --- MassTransit / RabbitMQ ---
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

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
