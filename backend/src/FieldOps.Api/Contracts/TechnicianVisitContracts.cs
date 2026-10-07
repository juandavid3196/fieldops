namespace FieldOps.Api.Contracts;

/// <summary>PATCH …/tasks/{taskId} (mobile-job-progress BR-08): at least one field; unknown keys are ignored.</summary>
public sealed record UpdateVisitTaskBody(bool? IsCompleted, string? Notes);

public sealed record AddVisitTaskBody(string? Label);

public sealed record PlannedMaterialUsedBody(decimal? UsedQuantity);

/// <summary>POST …/materials (BR-10): <c>CatalogItemId</c>, or <c>Description</c> with <c>Unit</c>.</summary>
public sealed record AddVisitMaterialBody(decimal? Quantity, Guid? CatalogItemId, string? Description, string? Unit);

public sealed record VisitMaterialQuantityBody(decimal? Quantity);

public sealed record TechnicianNotesBody(string? Notes);
