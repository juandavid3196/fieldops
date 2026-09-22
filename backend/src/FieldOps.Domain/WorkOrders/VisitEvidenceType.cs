namespace FieldOps.Domain.WorkOrders;

// visit_evidence.evidence_type is a varchar(30) with a CHECK constraint in
// the relational model, not a native PostgreSQL enum type.
public enum VisitEvidenceType
{
    Before,
    During,
    After,
    Incident,
    Other,
}
