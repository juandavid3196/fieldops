using FieldOps.Api.Configuration;
using FieldOps.Api.Extensions;
using FieldOps.Api.HealthChecks;
using FieldOps.Api.Middleware;
using FieldOps.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddApiErrorHandling();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiHealthChecks();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
// First so it observes the final status code, including handled 500s.
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "FieldOps API v1"));
}

app.UseHttpsRedirection();

app.UseCors(CorsSettings.PolicyName);

app.UseAuthorization();

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
