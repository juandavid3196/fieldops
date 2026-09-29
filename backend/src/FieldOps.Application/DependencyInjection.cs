using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Branches;
using FieldOps.Application.Features.Organizations;
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
        services.AddScoped<RegisterOrganizationHandler>();
        services.AddScoped<GetOrganizationSettingsHandler>();
        services.AddScoped<UpdateOrganizationSettingsHandler>();
        services.AddScoped<ListBranchesHandler>();
        services.AddScoped<GetBranchDetailHandler>();
        services.AddScoped<CreateBranchHandler>();
        services.AddScoped<UpdateBranchHandler>();
        services.AddScoped<DeactivateBranchHandler>();
        services.AddScoped<ReactivateBranchHandler>();
        services.AddScoped<SetMainBranchHandler>();
        services.AddScoped<GetOrganizationLogoHandler>();
        services.AddScoped<UploadOrganizationLogoHandler>();
        services.AddScoped<RemoveOrganizationLogoHandler>();

        return services;
    }
}
