namespace FieldOps.Application.Features.OperationsDashboard;

/// <summary>Messages and limits of the operations dashboard (operations-dashboard BR-02 to BR-05, BR-14).</summary>
public static class OperationsDashboardMessages
{
    public const int MaxDateOffsetDays = 365;

    public const int MaxActivityEvents = 50;

    public const int MaxDurationCategories = 4;

    public const string BranchInvalid = "Select a valid branch.";

    public const string DateInvalid = "Enter a valid date.";

    public const string DateOutOfRange = "Select a date within a year of today.";

    public const string TechnicianInvalid = "Select a valid technician.";

    public const string CategoryInvalid = "Select a valid service category.";

    public const string PeriodInvalid = "Select 7 or 30 days.";
}
