using System.Text.Json.Nodes;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;

namespace FieldOps.UnitTests.AuditTrail;

/// <summary>Redaction (BR-12), summary text (BR-15), change rows (BR-13) and the sign-out audit failure path (BR-18).</summary>
public sealed class AuditRedactionTests
{
    // AC-06: nested objects and arrays, key matching by fragment and case, kept keys; never a secret in the output.
    [Fact]
    public void Redact_NestedSecretsAndContacts_AreMaskedAndSafeKeysKept()
    {
        var redacted = AuditRedactor.Redact(
            """
            {
              "Password": "hunter2",
              "newPasswordHash": "abc",
              "apiKey": "sk_live_x",
              "card": { "cardNumber": "4242424242424242", "cardLast4": "4242", "last4": "4242", "cvc": "123" },
              "bank": [ { "iban": "DE89", "accountNumber": "1", "routing": "2", "label": "Main" } ],
              "contact": { "primaryEmail": "a@b.co", "Phone": "+1 555", "email": null, "phoneNote": null },
              "headers": { "Authorization": "Bearer t", "setCookie": "x", "sessionToken": "y" },
              "name": "Carla"
            }
            """)!.ToJsonString();

        foreach (var secret in new[] { "hunter2", "sk_live_x", "4242424242424242", "123", "DE89", "Bearer", "a@b.co", "+1 555", "abc" })
        {
            Assert.DoesNotContain(secret, redacted);
        }

        var node = JsonNode.Parse(redacted)!;
        Assert.Equal("[redacted]", node["Password"]!.GetValue<string>());
        Assert.Equal("[redacted]", node["newPasswordHash"]!.GetValue<string>());
        Assert.Equal("[redacted]", node["card"]!["cardNumber"]!.GetValue<string>());
        Assert.Equal("[redacted]", node["card"]!["cvc"]!.GetValue<string>());
        Assert.Equal("4242", node["card"]!["cardLast4"]!.GetValue<string>());
        Assert.Equal("4242", node["card"]!["last4"]!.GetValue<string>());
        Assert.Equal("[redacted]", node["bank"]![0]!["iban"]!.GetValue<string>());
        Assert.Equal("[redacted]", node["bank"]![0]!["routing"]!.GetValue<string>());
        Assert.Equal("Main", node["bank"]![0]!["label"]!.GetValue<string>());
        Assert.Equal("[changed]", node["contact"]!["primaryEmail"]!.GetValue<string>());
        Assert.Equal("[changed]", node["contact"]!["Phone"]!.GetValue<string>());
        Assert.Null(node["contact"]!["email"]);
        Assert.Equal("[redacted]", node["headers"]!["Authorization"]!.GetValue<string>());
        Assert.Equal("Carla", node["name"]!.GetValue<string>());
        Assert.Null(AuditRedactor.Redact((string?)null));
        Assert.Null(AuditRedactor.Redact("not json"));
    }

    // AC-05, AC-06, BR-13, BR-15: the summary variants, truncation and change rows read only redacted data.
    [Fact]
    public void Summary_AndChanges_FollowTheRedactedData()
    {
        JsonNode? Redacted(string json) => AuditRedactor.Redact(json);

        Assert.Equal(
            "Status: new → quoted",
            AuditSummaryBuilder.Build("Status changed", "REQ-1", Redacted("""{"status":"new"}"""), Redacted("""{"status":"quoted"}""")));
        Assert.Equal(
            "First name: Ann → Anna (+2 more)",
            AuditSummaryBuilder.Build(
                "Customer updated", "Carla",
                Redacted("""{"firstName":"Ann","email":"a@b.co","city":"X","same":1}"""),
                Redacted("""{"firstName":"Anna","email":"c@d.co","city":"Y","same":1}""")));
        Assert.Equal(
            "Email: [changed] → [changed]",
            AuditSummaryBuilder.Build("Customer updated", "Carla", Redacted("""{"email":"a@b.co"}"""), Redacted("""{"email":"c@d.co"}""")));
        Assert.Equal("Payment recorded PAY-4", AuditSummaryBuilder.Build("Payment recorded", "PAY-4", null, Redacted("""{"amount":"10.00"}""")));
        Assert.Equal("Payment recorded", AuditSummaryBuilder.Build("Payment recorded", "—", null, Redacted("""{"amount":"10.00"}""")));
        Assert.Equal("Signed in", AuditSummaryBuilder.Build("Signed in", "—", null, null));
        Assert.Equal("Signed in", AuditSummaryBuilder.Build("Signed in", "—", Redacted("{}"), Redacted("{}")));

        var long160 = AuditSummaryBuilder.Build("Updated", "R", Redacted("""{"note":"a"}"""), Redacted($$"""{"note":"{{new string('b', 400)}}"}"""));
        Assert.Equal(160, long160.Length);
        Assert.EndsWith("…", long160, StringComparison.Ordinal);

        var changes = AuditSummaryBuilder.Changes(
            Redacted("""{"status":"new","amount":1}"""), Redacted("""{"status":"quoted","extra":{"k":true},"password":"p"}"""));
        Assert.Equal(
            [("status", "Status", "new", "quoted"), ("amount", "Amount", "1", "—"), ("extra", "Extra", "—", """{"k":true}"""), ("password", "Password", "—", "[redacted]")],
            changes.Select(change => (change.Field, change.Label, change.Before, change.After)).ToArray());
        Assert.Empty(AuditSummaryBuilder.Changes(null, null));
        Assert.Equal("First name", AuditSummaryBuilder.Humanize("firstName"));
        Assert.Equal("Is all branches", AuditSummaryBuilder.Humanize("is_all_branches"));
    }

    // BR-18: a failed sign-out insert never fails the sign-out and only the exception type name is reported.
    [Fact]
    public async Task SignOut_WhenTheInsertFails_ReturnsFalseAndReportsOnlyTheExceptionType()
    {
        var reported = new List<string>();
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var ok = new RecordingStore();

        Assert.True(await new SignOutHandler(ok).HandleAsync(userId, organizationId, null, reported.Add, CancellationToken.None));
        var row = Assert.Single(ok.Rows);
        Assert.Equal(
            ("auth.signed_out", "user", userId, userId, organizationId, "{}"),
            (row.Action, row.EntityType, row.ActorUserId, row.EntityId, row.OrganizationId, row.Metadata));
        Assert.Null(row.BranchId);
        Assert.Null(row.BeforeData);
        Assert.Null(row.AfterData);

        Assert.False(await new SignOutHandler(new RecordingStore { Fail = true }).HandleAsync(userId, organizationId, null, reported.Add, CancellationToken.None));
        Assert.Equal(["InvalidOperationException"], reported);
        Assert.DoesNotContain(userId.ToString(), reported[0], StringComparison.Ordinal);
    }

    private sealed class RecordingStore : IAuthenticationStore
    {
        public List<AuditLog> Rows { get; } = [];

        public bool Fail { get; set; }

        public Task RecordSignedOutAsync(AuditLog auditLog, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Insert failed for someone@example.com.");
            }

            Rows.Add(auditLog);

            return Task.CompletedTask;
        }

        public Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MembershipCandidate>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SessionView?> FindActiveSessionAsync(
            Guid userId, Guid organizationId, Guid membershipId, DateTimeOffset signedInAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveSignInAsync(User user, AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
