namespace FieldOps.Api.Middleware;

/// <summary>
/// Marks an action that the read-only demo mode lets through although it is not a GET: a POST that only computes or
/// previews, view state of the signed-in account (read markers, language, theme) or a session-only record such as
/// sign-out. Everything else that is not GET/HEAD/OPTIONS is refused for demo accounts before the controller runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DemoReadOnlySafeAttribute : Attribute;

/// <summary>
/// Marks an action that changes the credentials or identity of the signed-in user. It is refused when the user is a demo
/// account even if the active organization is not a demo one.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DemoCredentialsAttribute : Attribute;
