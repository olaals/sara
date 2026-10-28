using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using api.Configurations;
using api.Controllers;
using api.Database.Context;
using api.MQTT;
using api.Services;
using api.Services.HostedServices;
using api.Services.ResultHandlers.AnalysisResultHandlers;
using api.Services.ResultHandlers.WorkflowResultHandlers;
using api.Utilities;
using Azure.Core;
using k8s;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine($"\nENVIRONMENT IS SET TO '{builder.Environment.EnvironmentName}'\n");

builder.AddDotEnvironmentVariables(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

if (builder.Configuration.GetSection("KeyVault").GetValue<bool>("UseKeyVault"))
{
    string? vaultUri = builder.Configuration.GetSection("KeyVault")["VaultUri"];
    if (!string.IsNullOrEmpty(vaultUri))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(vaultUri),
            CustomServiceConfigurations.CreateCredential(builder.Configuration)
        );
    }
    else
    {
        Console.WriteLine("NO KEYVAULT IN CONFIG");
    }
}

var runtimeCredential = CustomServiceConfigurations.CreateRuntimeCredential(builder.Configuration);
builder.Services.AddSingleton<TokenCredential>(runtimeCredential);

var applicationName = builder.Configuration["AppName"] ?? "SaraBackend";

builder.ConfigureLogger();

builder.Services.ConfigureDatabase(
    builder.Configuration,
    builder.Environment.EnvironmentName,
    runtimeCredential
);
builder.Services.ConfigureMQTT();

var openTelemetryEnabled = builder.Configuration.GetValue<bool?>("OpenTelemetry:Enabled") ?? false;
var otelActivitySource = new ActivitySource(applicationName);
var otelMeter = new Meter($"{applicationName}.Metrics", "0.0.1");
if (openTelemetryEnabled)
{
    builder.AddCustomOpenTelemetry(otelActivitySource, otelMeter);
}

builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection("AzureAd"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<EndpointConfig>(builder.Configuration.GetSection("EndpointConfig"));
builder.Services.Configure<DashboardOptions>(
    builder.Configuration.GetSection(DashboardOptions.SectionName)
);
builder
    .Services.AddOptions<AnalysisOptions>()
    .Bind(builder.Configuration.GetSection(AnalysisOptions.SectionName))
    .Validate(
        options => options.Workflows.Values.All(w => w.IsGate == (w.SkipChainIf is not null)),
        "Invalid Analysis.Workflows configuration: IsGate and SkipChainIf must both be set or both be unset on every workflow."
    )
    .Validate(
        options =>
            options.Workflows.Values.All(workflow =>
                !string.IsNullOrWhiteSpace(workflow.WorkflowTemplateName)
            ),
        "Invalid Analysis.Workflows configuration: WorkflowTemplateName is required."
    )
    .Validate(
        options =>
            options.Workflows.Values.All(workflow =>
                !string.IsNullOrWhiteSpace(workflow.OutputStorageAccount)
            ),
        "Invalid Analysis.Workflows configuration: OutputStorageAccount is required and must be set per environment."
    )
    .Validate(
        options =>
            options
                .Analyses.Values.SelectMany(analysis => analysis.Workflows)
                .All(options.Workflows.ContainsKey),
        "Invalid Analysis configuration: every workflow in an analysis chain must exist in Analysis.Workflows."
    )
    .Validate(
        options =>
            options
                .DefaultAnalysisByInspectionType.Values.SelectMany(analyses => analyses)
                .All(options.Analyses.ContainsKey),
        "Invalid Analysis.DefaultAnalysisByInspectionType configuration: every default analysis must exist in Analysis.Analyses."
    )
    .ValidateOnStart();

builder.Services.AddScoped<IReferencePolygonMetadataService, ReferencePolygonMetadataService>();

// Singleton so the delegation key is shared across requests, not refetched per
// scope. BlobStorageService stays scoped.
builder.Services.AddSingleton<IUserDelegationKeyProvider, UserDelegationKeyProvider>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IThermalImageService, ThermalImageService>();
builder.Services.AddScoped<IInspectionRecordService, InspectionRecordService>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddScoped<IAnalysisGroupService, AnalysisGroupService>();
builder.Services.AddScoped<IAnalysisRunService, AnalysisRunService>();
builder.Services.AddScoped<IMqttPublisherService, MqttPublisherService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

builder.Services.AddScoped<IFeedbackService, FeedbackService>();

builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IAnalysisWorkflowGraphBuilder, AnalysisWorkflowGraphBuilder>();
builder.Services.AddScoped<IAnalysisWorkflowService, AnalysisWorkflowService>();
builder.Services.AddSingleton<IKubernetes>(_ => new Kubernetes(
    KubernetesClientConfiguration.BuildDefaultConfig()
));
builder.Services.AddSingleton<IArgoWorkflowClient, ArgoWorkflowClient>();
builder.Services.AddScoped<IArgoWorkflowEventProcessor, ArgoWorkflowEventProcessor>();
builder.Services.AddScoped<ITriggerPayloadEnricher, AnonymizerPayloadEnricher>();
builder.Services.AddScoped<ITriggerPayloadEnricher, ThermalReadingPayloadEnricher>();
builder.Services.AddScoped<ITriggerPayloadEnricher, FencillaPayloadEnricher>();
builder.Services.AddScoped<ITriggerPayloadEnricher, UtilitiesPayloadEnricher>();

// Per-workflow result handlers — fire on each successful Workflow step.
builder.Services.AddScoped<IWorkflowResultHandler, AnonymizerResultHandler>();
builder.Services.AddScoped<IWorkflowResultHandler, CLOEResultHandler>();
builder.Services.AddScoped<IWorkflowResultHandler, CopyRawToVisualizedResultHandler>();
builder.Services.AddScoped<IWorkflowResultHandler, FencillaResultHandler>();
builder.Services.AddScoped<IWorkflowResultHandler, ThermalReadingResultHandler>();

// Per-analysis result handlers — fire once per successful AnalysisRun for cross-step
// / aggregate reporting. Interface defined for future use; no implementations
// registered yet, so dispatch is a no-op. Add registrations here when needed:
//   builder.Services.AddScoped<IAnalysisResultHandler, MyAggregateResultHandler>();
builder.Services.AddScoped<IAnalysisTriggerService, AnalysisTriggerService>();
builder.Services.AddScoped<ITimeseriesService, TimeseriesService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAnalysisGroupTimeoutProcessor, AnalysisGroupTimeoutProcessor>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<InstallationAccess>();

builder.Services.AddHostedService<MqttEventHandler>();
builder.Services.AddHostedService<MqttService>();
builder.Services.AddHostedService<AnalysisGroupTimeoutService>();
builder.Services.AddHostedService<ArgoWorkflowWatcherService>();

builder
    .Services.AddControllers(options =>
    {
        options.Conventions.Add(new ApiRoutePrefixConvention("api"));
        options.Filters.Add<InstallationAccessExceptionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureSwagger(builder.Configuration);

builder.Services.ConfigureAuthentication(builder.Configuration, builder.Environment);

builder.Services.ConfigureJwtBearerLogging();

builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        InstallationAccess.ObserverPolicy,
        policy =>
            policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    InstallationAccess.HasAccess(context.User, AccessLevel.Observer)
                )
    )
    .AddPolicy(
        InstallationAccess.AdminPolicy,
        policy =>
            policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    InstallationAccess.HasAccess(context.User, AccessLevel.Admin)
                )
    )
    .AddFallbackPolicy("RequireAuthenticatedUser", policy => policy.RequireAuthenticatedUser());

var app = builder.Build();

if (openTelemetryEnabled)
{
    app.Lifetime.ApplicationStarted.Register(api.Telemetry.SaraMetrics.InitializeBaselines);
}

string basePath = builder.Configuration["ApiBaseRoute"] ?? "";
app.UseSwagger(c =>
{
    var swaggerScheme = builder.Configuration["EndpointConfig:DefaultScheme"] ?? "https";
    swaggerScheme = swaggerScheme.Trim().TrimEnd(':');

    c.PreSerializeFilters.Add(
        (swaggerDoc, httpReq) =>
        {
            swaggerDoc.Servers =
            [
                new() { Url = $"{swaggerScheme}://{httpReq.Host.Value}{basePath}" },
            ];
        }
    );
});
app.UseSwaggerUI(c =>
{
    c.OAuthClientId(builder.Configuration["AzureAd:ClientId"]);
    // The following parameter represents the "audience" of the access token.
    c.OAuthAdditionalQueryStringParams(
        new Dictionary<string, string>
        {
            {
                "Resource",
                builder.Configuration["AzureAd:ClientId"]
                    ?? throw new ArgumentException("No Azure Ad ClientId")
            },
        }
    );
    c.OAuthUsePkce();
});

var enableFrontend = builder.Configuration.GetValue<bool?>("Frontend:Enabled") ?? true;

if (enableFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
else
{
    var option = new RewriteOptions();
    option.AddRedirect("^$", "swagger");
    app.UseRewriter(option);
}

string[] allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
app.UseCors(corsBuilder =>
    corsBuilder
        .WithOrigins(allowedOrigins)
        .SetIsOriginAllowedToAllowWildcardSubdomains()
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.MapGet(
        "/api/config",
        (IConfiguration configuration) =>
            new
            {
                AzureAd = new
                {
                    ClientId = configuration["AzureAd:ClientId"] ?? "",
                    TenantId = configuration["AzureAd:TenantId"] ?? "",
                },
                BasePath = (configuration["ApiBaseRoute"] ?? "").TrimEnd('/'),
                FlotillaBaseUrl = (configuration["FlotillaBaseUrl"] ?? "").TrimEnd('/'),
                ArgoWorkflowsBaseUrl = (configuration["ArgoWorkflowsBaseUrl"] ?? "").TrimEnd('/'),
                ArgoWorkflowsNamespace = configuration["ArgoWorkflowsNamespace"] ?? "",
            }
    )
    .AllowAnonymous();

app.MapGet(
        "/api/config/analyses",
        (Microsoft.Extensions.Options.IOptions<AnalysisOptions> opts) =>
            opts.Value.Analyses.Select(kvp => new
            {
                Name = kvp.Key,
                Workflows = kvp.Value.Workflows,
            })
    )
    .RequireAuthorization(InstallationAccess.ObserverPolicy);

if (enableFrontend)
{
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    var displayUrl = builder.Configuration["DISPLAY_URL"];
    if (!string.IsNullOrEmpty(displayUrl))
    {
        Console.WriteLine($"Now listening on: \x1b[36m{displayUrl}\x1b[0m");
    }
    else
    {
        foreach (var url in app.Urls)
        {
            Console.WriteLine($"Now listening on: \x1b[36m{url}\x1b[0m");
        }
    }
});

app.Run();
