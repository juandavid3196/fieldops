using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Users;

/// <summary>Invite, resend, revoke and edit access (FR-06, FR-08, FR-10, FR-13): AC-06, AC-07, AC-09, AC-11.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class UsersInvitationTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    [Fact]
    public async Task Invite_ValidatesPersistsHashOnlyAuditsAndRollsBackWhenDeliveryFails()
    {
        var org = await database.SeedOrganizationAsync();
        var otherOrg = await database.SeedOrganizationAsync();
        var alpha = await database.SeedBranchAsync(org, name: "Alpha");
        var inactive = await database.SeedBranchAsync(org, name: "Closed", isActive: false);
        var foreign = await database.SeedBranchAsync(otherOrg, name: "Foreign");
        var actor = await database.SeedMemberAsync(org, Owner, "Olivia", "Owner");
        var member = await database.SeedMemberAsync(org, Viewer, "Mia", "Member", email: "member@example.com");
        await database.SeedMemberAsync(org, Viewer, "Sam", "Suspended", email: "suspended@example.com", status: "suspended");
        await database.SeedInvitationAsync(
            org, actor.UserId, "Pat", "Pending", "pending@example.com", Viewer, DateTimeOffset.UtcNow.AddDays(-2));
        var otherMember = await database.SeedMemberAsync(otherOrg, Viewer, "Ola", "Elsewhere", email: "elsewhere@example.com");

        await using var host = UsersHost.Create(database, captureLogs: true);
        var cookie = await host.SignInAsync(actor.Email);
        var invitationCount = () => database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE organization_id = @o", ("o", org));

        // AC-06: valid body.
        var created = await host.PostAsync("/users/invitations", cookie, UsersSeed.Body(
            ("email", "  New.Person@Example.COM "),
            ("firstName", " Nina "),
            ("lastName", "Nguyen"),
            ("roleCode", "dispatcher"),
            ("isAllBranches", false),
            ("branchIds", new[] { alpha.Id }),
            ("linkTeamProfile", true)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("no-store", created.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var row = await host.ReadAsync(created);
        Assert.Equal("invitation", row["kind"]!.GetValue<string>());
        Assert.Equal("pending_invitation", row["status"]!.GetValue<string>());
        Assert.Equal("new.person@example.com", row["email"]!.GetValue<string>());
        Assert.Equal("Nina", row["firstName"]!.GetValue<string>());
        Assert.Equal(["Alpha"], row["branches"]!.AsArray().Select(b => b!["name"]!.GetValue<string>()).ToArray());
        Assert.NotNull(created.Headers.Location);

        var message = Assert.Single(host.Delivery.Messages);
        var raw = RecordingInvitationDelivery.TokenOf(message);
        var hash = RecordingInvitationDelivery.HashOf(raw);
        Assert.Equal("new.person@example.com", message.RecipientEmail);
        Assert.Equal("Nina", message.FirstName);
        Assert.Equal("Olivia Owner", message.InviterName);
        Assert.StartsWith($"{Api.FieldOpsApiFactory.AllowedOrigin}/auth/invitation?token=", message.AcceptLink, StringComparison.Ordinal);
        Assert.Equal(await database.TextAsync("SELECT name FROM roles WHERE code = 'dispatcher'"), message.RoleName);

        var id = row["id"]!.GetValue<Guid>();
        Assert.Equal(hash, await database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", id)));
        Assert.Equal(43, raw.Length);
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE token_hash = @t", ("t", raw)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM invitation_branches WHERE invitation_id = @i", ("i", id)));
        Assert.True(await database.ScalarAsync<bool>("SELECT link_team_profile FROM user_invitations WHERE id = @i", ("i", id)));
        var expires = await database.ScalarAsync<DateTimeOffset>("SELECT expires_at FROM user_invitations WHERE id = @i", ("i", id));
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromDays(7) - TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        Assert.Equal(1, await database.CountAuditAsync(org, "user.invited"));
        var (before, after, _) = await database.GetLatestAuditAsync(org, "user.invited");
        Assert.Null(before);
        Assert.DoesNotContain(raw, after!, StringComparison.Ordinal);
        Assert.DoesNotContain(hash, after!, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", after!, StringComparison.Ordinal);
        var invitedAfter = JsonNode.Parse(after)!;
        Assert.True(invitedAfter["linkTeamProfile"]!.GetValue<bool>());
        Assert.Equal("dispatcher", invitedAfter["roleCode"]!.GetValue<string>());
        Assert.Equal(alpha.Id.ToString(), invitedAfter["branchIds"]![0]!.GetValue<string>());
        Assert.NotNull(invitedAfter["expiresAt"]);

        // Expiry 3 and 14 days; forced roles; link flag dropped for non-applicable roles.
        foreach (var (email, days) in new[] { ("d3@example.com", 3), ("d14@example.com", 14) })
        {
            var ok = await host.PostAsync("/users/invitations", cookie, UsersSeed.Body(
                ("email", email), ("firstName", "A"), ("lastName", "B"), ("roleCode", "viewer"),
                ("isAllBranches", true), ("expiresInDays", days)));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
            var span = await database.ScalarAsync<DateTimeOffset>(
                "SELECT expires_at FROM user_invitations WHERE organization_id = @o AND email = @e", ("o", org), ("e", email))
                - DateTimeOffset.UtcNow;
            Assert.InRange(span, TimeSpan.FromDays(days) - TimeSpan.FromMinutes(2), TimeSpan.FromDays(days));
        }

        foreach (var forced in new[] { "owner", "operations_manager" })
        {
            var email = $"{forced}@example.com";
            var ok = await host.PostAsync("/users/invitations", cookie, UsersSeed.Body(
                ("email", email), ("firstName", "F"), ("lastName", "R"), ("roleCode", forced),
                ("isAllBranches", false), ("branchIds", new[] { alpha.Id })));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
            Assert.True(await database.ScalarAsync<bool>(
                "SELECT is_all_branches FROM user_invitations WHERE organization_id = @o AND email = @e", ("o", org), ("e", email)));
            Assert.Equal(0, await database.CountAsync(
                "SELECT COUNT(*) FROM invitation_branches ib JOIN user_invitations i ON i.id = ib.invitation_id WHERE i.email = @e",
                ("e", email)));
        }

        var accountingInvite = await host.PostAsync("/users/invitations", cookie, UsersSeed.Body(
            ("email", "acct@example.com"), ("firstName", "A"), ("lastName", "C"), ("roleCode", "accounting"),
            ("isAllBranches", true), ("linkTeamProfile", true)));
        Assert.Equal(HttpStatusCode.Created, accountingInvite.StatusCode);
        Assert.False(await database.ScalarAsync<bool>(
            "SELECT link_team_profile FROM user_invitations WHERE email = 'acct@example.com'"));

        // AC-07: one failure per rule; nothing changes.
        var countBefore = await invitationCount();
        var deliveriesBefore = host.Delivery.Messages.Count;
        var auditBefore = await database.CountAuditAsync(org, "user.invited");

        JsonObject Valid(params (string Key, object? Value)[] overrides)
        {
            var body = UsersSeed.Body(
                ("email", "valid@example.com"), ("firstName", "Val"), ("lastName", "Id"), ("roleCode", "dispatcher"),
                ("isAllBranches", false), ("branchIds", new[] { alpha.Id }));

            foreach (var (key, value) in overrides)
            {
                body[key] = UsersSeed.Body((key, value))[key]?.DeepClone();
            }

            return body;
        }

        var failures = new (JsonObject Body, HttpStatusCode Status, string Key, string Message)[]
        {
            (Valid(("email", "not-an-email")), HttpStatusCode.BadRequest, "email", "Enter a valid email address."),
            (Valid(("email", new string('a', 250) + "@x.com")), HttpStatusCode.BadRequest, "email", "Enter a valid email address."),
            (Valid(("firstName", "   ")), HttpStatusCode.BadRequest, "firstName", "Enter a first name."),
            (Valid(("lastName", null)), HttpStatusCode.BadRequest, "lastName", "Enter a last name."),
            (Valid(("roleCode", "admin")), HttpStatusCode.BadRequest, "roleCode", "Select a role."),
            (Valid(("branchIds", Array.Empty<Guid>())), HttpStatusCode.BadRequest, "branchIds", "Select at least one branch."),
            (Valid(("branchIds", new[] { foreign.Id })), HttpStatusCode.BadRequest, "branchIds", "Select a valid branch."),
            (Valid(("branchIds", new[] { inactive.Id })), HttpStatusCode.BadRequest, "branchIds", "Select a valid branch."),
            (Valid(("branchIds", new[] { Guid.NewGuid() })), HttpStatusCode.BadRequest, "branchIds", "Select a valid branch."),
            (Valid(("expiresInDays", 5)), HttpStatusCode.BadRequest, "expiresInDays", "Select a valid expiry."),
            (Valid(("email", member.Email.ToUpperInvariant())), HttpStatusCode.Conflict, "email", "This person already has access."),
            (Valid(("email", "suspended@example.com")), HttpStatusCode.Conflict, "email", "This person already has access."),
            (Valid(("email", " Pending@Example.com")), HttpStatusCode.Conflict, "email", "This email already has a pending invitation."),
            (Valid(("email", "new.person@example.com")), HttpStatusCode.Conflict, "email", "This email already has a pending invitation."),
        };

        foreach (var (body, status, key, expected) in failures)
        {
            var response = await host.PostAsync("/users/invitations", cookie, body);
            Assert.True(
                status == response.StatusCode,
                $"{body.ToJsonString()} -> {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            var messages = (await host.ReadAsync(response))["errors"]![key]!.AsArray().Select(m => m!.GetValue<string>());
            Assert.Contains(expected, messages);
        }

        Assert.Equal(countBefore, await invitationCount());
        Assert.Equal(deliveriesBefore, host.Delivery.Messages.Count);
        Assert.Equal(auditBefore, await database.CountAuditAsync(org, "user.invited"));

        // BR-10: an email that belongs to another organization is allowed.
        var elsewhere = await host.PostAsync("/users/invitations", cookie, Valid(("email", otherMember.Email)));
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);

        // A delivery failure returns 502 and stores nothing.
        host.Delivery.Fail = true;
        countBefore = await invitationCount();
        auditBefore = await database.CountAuditAsync(org, "user.invited");
        var branchLinksBefore = await database.CountAsync("SELECT COUNT(*) FROM invitation_branches");
        var failed = await host.PostAsync("/users/invitations", cookie, Valid(("email", "lost@example.com")));
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal("We couldn't send the invitation. Try again.", (await host.ReadAsync(failed))["title"]!.GetValue<string>());
        Assert.Equal(countBefore, await invitationCount());
        Assert.Equal(auditBefore, await database.CountAuditAsync(org, "user.invited"));
        Assert.Equal(branchLinksBefore, await database.CountAsync("SELECT COUNT(*) FROM invitation_branches"));
        host.Delivery.Fail = false;

        // No email or token reaches the logs.
        var logs = host.Logs!.AllText();
        Assert.DoesNotContain(raw, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("new.person", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lost@example.com", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResendAndRevoke_ReplaceTokenRevokeStopLinkAndRejectNonPendingInvitations()
    {
        var org = await database.SeedOrganizationAsync();
        var actor = await database.SeedMemberAsync(org, Owner, "Olivia", "Owner");
        var future = DateTimeOffset.UtcNow.AddDays(2);
        var openId = await database.SeedInvitationAsync(
            org, actor.UserId, "Open", "One", "open@example.com", Dispatcher, future, tokenHash: "old-hash-open");
        var expiredId = await database.SeedInvitationAsync(
            org, actor.UserId, "Exp", "Ired", "expired@example.com", Accounting, DateTimeOffset.UtcNow.AddDays(-1), tokenHash: "old-hash-expired");
        var acceptedId = await database.SeedInvitationAsync(
            org, actor.UserId, "Acc", "Epted", "accepted@example.com", Viewer, future, accepted: true, tokenHash: "old-hash-accepted");
        var revokedId = await database.SeedInvitationAsync(
            org, actor.UserId, "Rev", "Oked", "revoked@example.com", Viewer, future, revoked: true, tokenHash: "old-hash-revoked");
        var revokeTarget = await database.SeedInvitationAsync(
            org, actor.UserId, "Rev", "Target", "target@example.com", Viewer, future);

        await using var host = UsersHost.Create(database, captureLogs: true);
        var cookie = await host.SignInAsync(actor.Email);
        var hashOf = (Guid id) => database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", id));

        // AC-09: resend an open and an expired invitation.
        foreach (var (id, oldHash, email) in new[] { (openId, "old-hash-open", "open@example.com"), (expiredId, "old-hash-expired", "expired@example.com") })
        {
            var response = await host.PostAsync($"/users/invitations/{id}/resend", cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var row = await host.ReadAsync(response);
            Assert.False(row["isExpired"]!.GetValue<bool>());
            var message = host.Delivery.Messages.Last();
            Assert.Equal(email, message.RecipientEmail);
            var newHash = RecordingInvitationDelivery.HashOf(RecordingInvitationDelivery.TokenOf(message));
            Assert.Equal(newHash, await hashOf(id));
            Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE token_hash = @h", ("h", oldHash)));
            var expires = await database.ScalarAsync<DateTimeOffset>("SELECT expires_at FROM user_invitations WHERE id = @i", ("i", id));
            Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromDays(7) - TimeSpan.FromMinutes(2), TimeSpan.FromDays(7));
        }

        Assert.Equal(2, host.Delivery.Messages.Count);
        Assert.Equal(2, await database.CountAuditAsync(org, "user.invitation_resent"));

        // A resend whose delivery fails rolls back.
        var hashBefore = await hashOf(openId);
        host.Delivery.Fail = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await host.PostAsync($"/users/invitations/{openId}/resend", cookie)).StatusCode);
        host.Delivery.Fail = false;
        Assert.Equal(hashBefore, await hashOf(openId));
        Assert.Equal(2, await database.CountAuditAsync(org, "user.invitation_resent"));

        // Accepted and revoked invitations are not pending.
        foreach (var (id, oldHash) in new[] { (acceptedId, "old-hash-accepted"), (revokedId, "old-hash-revoked") })
        {
            foreach (var action in new[] { "resend", "revoke" })
            {
                var response = await host.PostAsync($"/users/invitations/{id}/{action}", cookie);
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("This invitation is no longer pending.", (await host.ReadAsync(response))["title"]!.GetValue<string>());
            }

            Assert.Equal(oldHash, await hashOf(id));
        }

        Assert.Equal(2, host.Delivery.Messages.Count);
        Assert.Equal(0, await database.CountAuditAsync(org, "user.invitation_revoked"));

        // Revoke: 204, the row leaves the list and the pending count, once.
        var pendingBefore = (await host.ReadAsync(await host.GetAsync("/users/summary", cookie)))["pendingInvitations"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync($"/users/invitations/{revokeTarget}/revoke", cookie)).StatusCode);
        Assert.NotNull(await database.ScalarAsync<object?>("SELECT revoked_at FROM user_invitations WHERE id = @i", ("i", revokeTarget)));
        var pendingAfter = (await host.ReadAsync(await host.GetAsync("/users/summary", cookie)))["pendingInvitations"]!.GetValue<int>();
        Assert.Equal(pendingBefore - 1, pendingAfter);
        Assert.DoesNotContain("target@example.com", UsersSeed.Items(await host.ReadAsync(await host.GetAsync("/users", cookie))));
        Assert.Equal(1, await database.CountAuditAsync(org, "user.invitation_revoked"));
        Assert.Equal(HttpStatusCode.Conflict, (await host.PostAsync($"/users/invitations/{revokeTarget}/revoke", cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "user.invitation_revoked"));

        Assert.Equal(HttpStatusCode.NotFound, (await host.PostAsync($"/users/invitations/{Guid.NewGuid()}/resend", cookie)).StatusCode);

        var logs = host.Logs!.AllText();
        Assert.DoesNotContain("open@example.com", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditAccess_ChangesMemberAndInvitationNormalizesForcedRolesAndAuditsOnlyRealChanges()
    {
        var org = await database.SeedOrganizationAsync();
        var otherOrg = await database.SeedOrganizationAsync();
        var alpha = await database.SeedBranchAsync(org, name: "Alpha");
        var beta = await database.SeedBranchAsync(org, name: "Beta");
        var inactive = await database.SeedBranchAsync(org, name: "Closed", isActive: false);
        var foreign = await database.SeedBranchAsync(otherOrg, name: "Foreign");
        var actor = await database.SeedMemberAsync(org, Owner, "Olivia", "Owner");
        var member = await database.SeedMemberAsync(org, Dispatcher, "Dee", "Spatch", isAllBranches: false);
        await database.LinkMembershipToBranchAsync(member.MembershipId, alpha.Id);
        var invitationId = await database.SeedInvitationAsync(
            org, actor.UserId, "Inv", "Itee", "invitee@example.com", Dispatcher, DateTimeOffset.UtcNow.AddDays(5),
            isAllBranches: false, branchIds: [alpha.Id], linkTeamProfile: true);
        var acceptedId = await database.SeedInvitationAsync(
            org, actor.UserId, "Acc", "Epted", "accepted@example.com", Viewer, DateTimeOffset.UtcNow.AddDays(5), accepted: true);
        var revokedId = await database.SeedInvitationAsync(
            org, actor.UserId, "Rev", "Oked", "revoked@example.com", Viewer, DateTimeOffset.UtcNow.AddDays(5), revoked: true);

        await using var host = UsersHost.Create(database);
        var cookie = await host.SignInAsync(actor.Email);
        var memberBranches = () => database.QueryGuidsAsync(
            "SELECT branch_id FROM organization_user_branches WHERE organization_user_id = @m", ("m", member.MembershipId));

        // Member: role and branches change, one audit row with before/after.
        var updated = await host.PutAsync($"/users/{member.MembershipId}/access", cookie, UsersSeed.Body(
            ("roleCode", "technician"), ("isAllBranches", false), ("branchIds", new[] { beta.Id })));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var row = await host.ReadAsync(updated);
        Assert.Equal("technician", row["roleCode"]!.GetValue<string>());
        Assert.Equal(["Beta"], row["branches"]!.AsArray().Select(b => b!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal([beta.Id], await memberBranches());
        Assert.Equal(1, await database.CountAuditAsync(org, "user.access_updated"));
        var (before, after, _) = await database.GetLatestAuditAsync(org, "user.access_updated");
        var beforeJson = JsonNode.Parse(before!)!;
        var afterJson = JsonNode.Parse(after!)!;
        Assert.Equal("dispatcher", beforeJson["roleCode"]!.GetValue<string>());
        Assert.False(beforeJson["isAllBranches"]!.GetValue<bool>());
        Assert.Equal([alpha.Id.ToString()], beforeJson["branchIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray());
        Assert.Equal("technician", afterJson["roleCode"]!.GetValue<string>());
        Assert.Equal([beta.Id.ToString()], afterJson["branchIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray());
        Assert.Equal("organization_user", await database.TextAsync(
            "SELECT entity_type FROM audit_logs WHERE organization_id = @o AND action = 'user.access_updated'", ("o", org)));

        // No-op: 200, no audit.
        var noOp = await host.PutAsync($"/users/{member.MembershipId}/access", cookie, UsersSeed.Body(
            ("roleCode", "technician"), ("isAllBranches", false), ("branchIds", new[] { beta.Id })));
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "user.access_updated"));

        // Forced role: all branches, no branch rows.
        var forced = await host.PutAsync($"/users/{member.MembershipId}/access", cookie, UsersSeed.Body(
            ("roleCode", "operations_manager"), ("isAllBranches", false), ("branchIds", new[] { alpha.Id })));
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        Assert.True((await host.ReadAsync(forced))["isAllBranches"]!.GetValue<bool>());
        Assert.Empty(await memberBranches());
        Assert.True(await database.ScalarAsync<bool>("SELECT is_all_branches FROM organization_users WHERE id = @m", ("m", member.MembershipId)));

        // Switching all-branches on for a scoped role also clears the rows.
        await host.PutAsync($"/users/{member.MembershipId}/access", cookie, UsersSeed.Body(
            ("roleCode", "dispatcher"), ("isAllBranches", false), ("branchIds", new[] { alpha.Id, beta.Id })));
        Assert.Equal(2, (await memberBranches()).Length);
        await host.PutAsync($"/users/{member.MembershipId}/access", cookie, UsersSeed.Body(
            ("roleCode", "dispatcher"), ("isAllBranches", true)));
        Assert.Empty(await memberBranches());
        var auditAfterMember = await database.CountAuditAsync(org, "user.access_updated");
        Assert.Equal(4, auditAfterMember);

        // Invalid bodies: 400 and no change.
        foreach (var (body, key) in new[]
        {
            (UsersSeed.Body(("roleCode", "admin"), ("isAllBranches", true)), "roleCode"),
            (UsersSeed.Body(("roleCode", "viewer"), ("isAllBranches", false)), "branchIds"),
            (UsersSeed.Body(("roleCode", "viewer"), ("isAllBranches", false), ("branchIds", new[] { foreign.Id })), "branchIds"),
            (UsersSeed.Body(("roleCode", "viewer"), ("isAllBranches", false), ("branchIds", new[] { inactive.Id })), "branchIds"),
        })
        {
            foreach (var path in new[] { $"/users/{member.MembershipId}/access", $"/users/invitations/{invitationId}/access" })
            {
                var invalid = await host.PutAsync(path, cookie, body);
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                Assert.NotNull((await host.ReadAsync(invalid))["errors"]![key]);
            }
        }

        Assert.Equal(auditAfterMember, await database.CountAuditAsync(org, "user.access_updated"));

        // Invitation: access changes, identity and flags stay.
        var expiresBefore = await database.ScalarAsync<DateTimeOffset>("SELECT expires_at FROM user_invitations WHERE id = @i", ("i", invitationId));
        var invited = await host.PutAsync($"/users/invitations/{invitationId}/access", cookie, UsersSeed.Body(
            ("roleCode", "accounting"), ("isAllBranches", false), ("branchIds", new[] { beta.Id })));
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        Assert.Equal("invitation", (await host.ReadAsync(invited))["kind"]!.GetValue<string>());
        Assert.Equal([beta.Id], await database.QueryGuidsAsync(
            "SELECT branch_id FROM invitation_branches WHERE invitation_id = @i", ("i", invitationId)));
        Assert.Equal("invitee@example.com", await database.TextAsync("SELECT email FROM user_invitations WHERE id = @i", ("i", invitationId)));
        Assert.Equal("Inv", await database.TextAsync("SELECT first_name FROM user_invitations WHERE id = @i", ("i", invitationId)));
        Assert.True(await database.ScalarAsync<bool>("SELECT link_team_profile FROM user_invitations WHERE id = @i", ("i", invitationId)));
        Assert.Equal(expiresBefore, await database.ScalarAsync<DateTimeOffset>("SELECT expires_at FROM user_invitations WHERE id = @i", ("i", invitationId)));
        Assert.Equal(auditAfterMember + 1, await database.CountAuditAsync(org, "user.access_updated"));
        Assert.Equal("user_invitation", await database.TextAsync(
            "SELECT entity_type FROM audit_logs WHERE entity_id = @i AND action = 'user.access_updated'", ("i", invitationId)));

        var invitationNoOp = await host.PutAsync($"/users/invitations/{invitationId}/access", cookie, UsersSeed.Body(
            ("roleCode", "accounting"), ("isAllBranches", false), ("branchIds", new[] { beta.Id })));
        Assert.Equal(HttpStatusCode.OK, invitationNoOp.StatusCode);
        Assert.Equal(auditAfterMember + 1, await database.CountAuditAsync(org, "user.access_updated"));

        var forcedInvitation = await host.PutAsync($"/users/invitations/{invitationId}/access", cookie, UsersSeed.Body(
            ("roleCode", "owner"), ("isAllBranches", false), ("branchIds", new[] { beta.Id })));
        Assert.True((await host.ReadAsync(forcedInvitation))["isAllBranches"]!.GetValue<bool>());
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM invitation_branches WHERE invitation_id = @i", ("i", invitationId)));

        // Accepted and revoked invitations: 409; kinds never cross routes.
        foreach (var id in new[] { acceptedId, revokedId })
        {
            var conflict = await host.PutAsync($"/users/invitations/{id}/access", cookie, UsersSeed.Body(("roleCode", "viewer"), ("isAllBranches", true)));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("This invitation is no longer pending.", (await host.ReadAsync(conflict))["title"]!.GetValue<string>());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await host.PutAsync(
            $"/users/{invitationId}/access", cookie, UsersSeed.Body(("roleCode", "viewer"), ("isAllBranches", true)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.PutAsync(
            $"/users/invitations/{member.MembershipId}/access", cookie, UsersSeed.Body(("roleCode", "viewer"), ("isAllBranches", true)))).StatusCode);
    }
}
