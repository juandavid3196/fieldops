namespace FieldOps.Domain.WorkOrders;

// visit_time_entries.entry_type is a varchar(20) with a CHECK constraint in
// the relational model, not a native PostgreSQL enum type.
public enum VisitTimeEntryType
{
    Work,
    Pause,
    Travel,
}
