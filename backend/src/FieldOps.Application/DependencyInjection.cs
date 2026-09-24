using FieldOps.Application.Authentication;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<SignInCommandValidator>(
            ServiceLifetime.Singleton);

        services.AddScoped<SignInHandler>();
        services.AddScoped<GetCurrentSessionHandler>();

        return services;
    }
}
