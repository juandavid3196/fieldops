using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Branches;
using FieldOps.Application.Features.Catalog;
using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.Invitations;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.PasswordResets;
using FieldOps.Application.Features.Users;
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
        services.AddScoped<ListCatalogItemsHandler>();
        services.AddScoped<GetCatalogSummaryHandler>();
        services.AddScoped<GetCatalogItemHandler>();
        services.AddScoped<CreateCatalogItemHandler>();
        services.AddScoped<UpdateCatalogItemHandler>();
        services.AddScoped<SetCatalogItemActiveHandler>();
        services.AddScoped<GetCatalogImageHandler>();
        services.AddScoped<UploadCatalogImageHandler>();
        services.AddScoped<RemoveCatalogImageHandler>();
        services.AddScoped<ExportCatalogItemsHandler>();
        services.AddScoped<ImportCatalogItemsHandler>();
        services.AddScoped<ListCustomersHandler>();
        services.AddScoped<GetCustomerMetricsHandler>();
        services.AddScoped<GetCustomerHandler>();
        services.AddScoped<CreateCustomerHandler>();
        services.AddScoped<UpdateCustomerHandler>();
        services.AddScoped<SetCustomerActiveHandler>();
        services.AddScoped<CheckCustomerDuplicatesHandler>();
        services.AddScoped<ListCustomerTagsHandler>();
        services.AddScoped<GetCustomerBranchOptionsHandler>();
        services.AddScoped<CreateCustomerTagHandler>();
        services.AddScoped<PreviewCustomerImportHandler>();
        services.AddScoped<ImportCustomersHandler>();
        services.AddScoped<ListUsersHandler>();
        services.AddScoped<GetUsersSummaryHandler>();
        services.AddSingleton<GetPermissionMatrixHandler>();
        services.AddScoped<InviteUserHandler>();
        services.AddScoped<ResendInvitationHandler>();
        services.AddScoped<RevokeInvitationHandler>();
        services.AddScoped<UpdateMemberAccessHandler>();
        services.AddScoped<UpdateInvitationAccessHandler>();
        services.AddScoped<SuspendMemberHandler>();
        services.AddScoped<ReactivateMemberHandler>();
        services.AddScoped<ValidateInvitationHandler>();
        services.AddScoped<AcceptInvitationHandler>();
        services.AddScoped<AcceptExistingInvitationHandler>();
        services.AddScoped<RequestPasswordResetHandler>();
        services.AddScoped<ValidatePasswordResetHandler>();
        services.AddScoped<ConfirmPasswordResetHandler>();

        return services;
    }
}
