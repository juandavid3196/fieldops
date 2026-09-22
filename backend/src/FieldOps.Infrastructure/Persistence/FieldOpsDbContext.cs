using FieldOps.Domain.Branches;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed class FieldOpsDbContext(
    DbContextOptions<FieldOpsDbContext> options)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();

    public DbSet<OrganizationUserBranch> OrganizationUserBranches =>
        Set<OrganizationUserBranch>();

    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();

    public DbSet<InvitationBranch> InvitationBranches =>
        Set<InvitationBranch>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();

    public DbSet<Property> Properties => Set<Property>();

    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();

    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();

    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();

    public DbSet<Skill> Skills => Set<Skill>();

    public DbSet<TechnicianProfile> TechnicianProfiles => Set<TechnicianProfile>();

    public DbSet<TechnicianSkill> TechnicianSkills => Set<TechnicianSkill>();

    public DbSet<TechnicianWeeklyAvailability> TechnicianWeeklyAvailabilities =>
        Set<TechnicianWeeklyAvailability>();

    public DbSet<TechnicianBreak> TechnicianBreaks => Set<TechnicianBreak>();

    public DbSet<TechnicianException> TechnicianExceptions => Set<TechnicianException>();

    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();

    public DbSet<RequestAttachment> RequestAttachments => Set<RequestAttachment>();

    public DbSet<RequestMessage> RequestMessages => Set<RequestMessage>();

    public DbSet<RequestStatusHistory> RequestStatusHistories => Set<RequestStatusHistory>();

    public DbSet<Assessment> Assessments => Set<Assessment>();

    public DbSet<AssessmentAttachment> AssessmentAttachments => Set<AssessmentAttachment>();

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<QuoteVersion> QuoteVersions => Set<QuoteVersion>();

    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();

    public DbSet<QuoteResponse> QuoteResponses => Set<QuoteResponse>();

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<WorkOrderRequiredSkill> WorkOrderRequiredSkills =>
        Set<WorkOrderRequiredSkill>();

    public DbSet<WorkOrderChecklistTemplate> WorkOrderChecklistTemplates =>
        Set<WorkOrderChecklistTemplate>();

    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<VisitAssignment> VisitAssignments => Set<VisitAssignment>();

    public DbSet<VisitStatusHistory> VisitStatusHistories => Set<VisitStatusHistory>();

    public DbSet<VisitTimeEntry> VisitTimeEntries => Set<VisitTimeEntry>();

    public DbSet<VisitChecklistItem> VisitChecklistItems => Set<VisitChecklistItem>();

    public DbSet<VisitMaterial> VisitMaterials => Set<VisitMaterial>();

    public DbSet<VisitEvidence> VisitEvidences => Set<VisitEvidence>();

    public DbSet<VisitIncident> VisitIncidents => Set<VisitIncident>();

    public DbSet<CustomerSignoff> CustomerSignoffs => Set<CustomerSignoff>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Declares the enums with labels in enum declaration order.
        modelBuilder.HasPostgresEnum<UserStatus>(name: "user_status");
        modelBuilder.HasPostgresEnum<CustomerType>(name: "customer_type");
        modelBuilder.HasPostgresEnum<CatalogItemType>(name: "catalog_item_type");
        modelBuilder.HasPostgresEnum<RequestStatus>(name: "request_status");
        modelBuilder.HasPostgresEnum<AssessmentStatus>(name: "assessment_status");
        modelBuilder.HasPostgresEnum<MessageVisibility>(name: "message_visibility");
        modelBuilder.HasPostgresEnum<QuoteStatus>(name: "quote_status");
        modelBuilder.HasPostgresEnum<WorkOrderStatus>(name: "work_order_status");
        modelBuilder.HasPostgresEnum<VisitStatus>(name: "visit_status");
        modelBuilder.HasPostgresEnum<InvoiceStatus>(name: "invoice_status");
        modelBuilder.HasPostgresEnum<PaymentMethod>(name: "payment_method");

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FieldOpsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
