using FieldOps.Api.Configuration;
using FieldOps.Api.Extensions;
using FieldOps.Api.HealthChecks;
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

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
});

app.MapControllers();

app.Run();
