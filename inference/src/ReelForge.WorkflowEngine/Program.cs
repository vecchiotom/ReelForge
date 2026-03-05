using System.Text;
using Azure.AI.OpenAI;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ReelForge.WorkflowEngine.Agents;
using ReelForge.WorkflowEngine.Agents.Analysis;
using ReelForge.WorkflowEngine.Agents.Production;
using ReelForge.WorkflowEngine.Agents.Quality;
using ReelForge.WorkflowEngine.Agents.Translation;
using ReelForge.WorkflowEngine.Consumers;
using ReelForge.WorkflowEngine.Data;
using ReelForge.WorkflowEngine.Execution;
using ReelForge.WorkflowEngine.Execution.StepExecutors;
using ReelForge.WorkflowEngine.Observability;
using ReelForge.WorkflowEngine.Workers;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --- Database ---
builder.Services.AddDbContext<WorkflowEngineDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Workflow")));

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
builder.Services.AddSingleton<IAgentRegistry, AgentRegistry>();

// --- Step Executors ---
builder.Services.AddSingleton<IStepExecutor, AgentStepExecutor>();
builder.Services.AddSingleton<IStepExecutor, ConditionalStepExecutor>();
builder.Services.AddSingleton<IStepExecutor, ForEachStepExecutor>();
builder.Services.AddSingleton<IStepExecutor, ReviewLoopStepExecutor>();

// --- Workflow Executor ---
builder.Services.AddScoped<WorkflowExecutorService>();

// --- MassTransit / RabbitMQ ---
int maxConcurrency = builder.Configuration.GetValue("WorkflowEngine:MaxConcurrency", 4);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<WorkflowExecutionRequestedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("workflow-execution", e =>
        {
            e.UseMessageRetry(r => r.Intervals(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)));
            e.PrefetchCount = maxConcurrency;
            e.ConcurrentMessageLimit = maxConcurrency;
            e.ConfigureConsumer<WorkflowExecutionRequestedConsumer>(context);
        });
    });
});

// --- Background Workers ---
builder.Services.AddHostedService<WorkflowWorkerPool>();

// --- OpenTelemetry ---
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ReelForgeDiagnostics.ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(ReelForgeDiagnostics.ServiceName)
        .AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter(ReelForgeDiagnostics.ServiceName)
        .AddAspNetCoreInstrumentation());

// --- Controllers & Swagger ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ReelForge Workflow Engine", Version = "v1" });
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
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// --- Apply migrations ---
using (IServiceScope scope = app.Services.CreateScope())
{
    WorkflowEngineDbContext db = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
