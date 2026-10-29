using FieldOps.Api.Configuration;
using FieldOps.Api.Extensions;
using FieldOps.Api.HealthChecks;
using FieldOps.Api.Middleware;
using FieldOps.Application;
using FieldOps.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApiHosting(builder.Configuration);
builder.Services.AddApiErrorHandling();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiHealthChecks();
builder.Services.AddApiAuthentication();
builder.Services.AddApiRateLimiting();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
var behindProxy = builder.Configuration.UsesForwardedHeaders();

// Before everything that reads the scheme or client IP (logging, HSTS,
// rate limiting, audit). Trust comes from ForwardedHeaders settings only.
if (behindProxy)
{
    app.UseForwardedHeaders();
}

// First so it observes the final status code, including handled 500s.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<InvitationNoStoreMiddleware>();
app.UseMiddleware<PasswordResetNoStoreMiddleware>();
app.UseMiddleware<PortalNoStoreMiddleware>();
app.UseMiddleware<MessagesNoStoreMiddleware>();
app.UseMiddleware<QuoteLinkPublicHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "FieldOps API v1"));
}

// Behind a TLS-terminating proxy the proxy owns the redirect: redirecting
// here would loop or break plain-HTTP health checks.
if (behindProxy)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors(CorsSettings.PolicyName);

// After UseCors so rejections and 401s carry CORS headers.
app.UseRateLimiter();
app.UseAuthentication();

// Deletes a session cookie that did not authenticate, on every endpoint.
app.UseMiddleware<InvalidSessionCookieMiddleware>();
app.UseAuthorization();

// Read-only demo: after authorization so roles and 401/403 decide first; before any controller, handler or email.
app.UseMiddleware<DemoReadOnlyMiddleware>();

// Readiness runs every registered check, including the database.
var readinessOptions = new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
};

// Kept for frontend compatibility; identical to /health/ready.
app.MapHealthChecks("/health", readinessOptions);
app.MapHealthChecks("/health/ready", readinessOptions);

// Liveness checks the process only and never touches dependencies.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Name == ApiServiceCollectionExtensions.SelfHealthCheckName,
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
});

app.MapControllers();

app.Run();
