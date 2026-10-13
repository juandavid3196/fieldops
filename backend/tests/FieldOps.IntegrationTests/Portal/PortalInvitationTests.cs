using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Users;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Portal;

/// <summary>Staff invite, resend and remove, activation of new and existing users, and unusable tokens (customer portal BR-10 … BR-14, AC-05 … AC-09).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PortalInvitationTests(CompanySettingsDatabaseFixture database)
{
    private const string NewPassword = "Brand new passphrase 1";

    [Fact]
    public async Task PortalInvitation_InviteResendAndRemove_FollowsPermissionsStatesAndLeavesTheUserUntouched()
    {
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        await database.ExecuteAsync("UPDATE customer_contacts SET email = @e WHERE id = @c", ("e", email), ("c", world.Contact));
        var orgName = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId, "Vera", "Viewer");
        var invitePath = $"/customers/{world.Customer}/portal-invitation";
        var accessPath = $"/customers/{world.Customer}/portal-access";

        // A read role sees the status and nothing more.
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, invitePath, viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Delete, accessPath, viewer)).StatusCode);
        var viewed = await PortalApi.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{world.Customer}/detail", viewer));
        Assert.Equal("not_invited", viewed["contact"]!["portalStatus"]!.GetValue<string>());

        // Another organization's customer is a 404; unavailable contacts are a 409.
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, $"/customers/{foreign.Customer}/portal-invitation", owner)).StatusCode);
        var noEmail = await database.SeedCustomerAsync(world.Org, world.BranchA, "No Email", email: null);
        var inactive = await database.SeedCustomerAsync(world.Org, world.BranchA, "Archived", email: "archived@example.com", active: false);

        foreach (var customer in new[] { noEmail, inactive })
        {
            var refused = await host.SendAsync(HttpMethod.Post, $"/customers/{customer}/portal-invitation", owner);
            await BillingSeed.ProblemAsync(refused, HttpStatusCode.Conflict, "portal_invite_unavailable");
        }

        // Invite: one open invitation, the email after the commit with the fragment link, no email in the audit.
        var invited = await PortalApi.ReadAsync(await host.SendAsync(HttpMethod.Post, invitePath, owner));
        Assert.Equal("invited", invited["portalStatus"]!.GetValue<string>());
        Assert.Equal(BillingSeed.DaysFromToday("UTC", 7), invited["invitationExpiresOn"]!.GetValue<string>());
        var first = Assert.Single(host.Sender.Messages);
        Assert.Equal(email, first.To);
        Assert.Equal($"{orgName} invited you to your customer portal", first.Subject);
        Assert.Contains("https://app.fieldops.test/portal/activate#token=", first.TextBody, StringComparison.Ordinal);
        var firstToken = RecordingEmailSender.TokenOf(first);
        Assert.Equal(InvitationTokens.Hash(firstToken), await database.ScalarAsync<string>("SELECT token_hash FROM customer_portal_invitations WHERE contact_id = @c", ("c", world.Contact)));
        Assert.Equal(1, await database.CountAuditLogsAsync(world.Org, "portal.invitation_sent"));
        Assert.DoesNotContain(email, await database.OrgAuditTextAsync(world.Org), StringComparison.OrdinalIgnoreCase);

        var detail = await PortalApi.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{world.Customer}/detail", owner));
        Assert.Equal("invited", detail["contact"]!["portalStatus"]!.GetValue<string>());

        // Resend: the earlier invitation is revoked and only the new token validates.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, invitePath, owner)).StatusCode);
        var secondToken = RecordingEmailSender.TokenOf(host.Sender.Messages[^1]);
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(1, await database.CountAsync("customer_portal_invitations", "contact_id = @c AND accepted_at IS NULL AND revoked_at IS NULL", ("c", world.Contact)));
        Assert.Equal(1, await database.CountAsync("customer_portal_invitations", "contact_id = @c AND revoked_at IS NOT NULL", ("c", world.Contact)));
        Assert.Equal(HttpStatusCode.Gone, (await Validate(host, firstToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Validate(host, secondToken)).StatusCode);

        // A failing mail provider never fails the invite.
        var other = await database.SeedSiblingAsync(world);
        host.Sender.Fail = true;
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, $"/customers/{other.Customer}/portal-invitation", owner)).StatusCode);
        host.Sender.Fail = false;
        Assert.DoesNotContain("@example.com", new CapturingLogs(host.Logs).AllText(), StringComparison.OrdinalIgnoreCase);

        // Remove while invited: open invitations are revoked, repeated calls change nothing.
        var removed = await PortalApi.ReadAsync(await host.SendAsync(HttpMethod.Delete, accessPath, owner));
        Assert.Equal("not_invited", removed["portalStatus"]!.GetValue<string>());
        Assert.Equal(0, await database.CountAsync("customer_portal_invitations", "contact_id = @c AND accepted_at IS NULL AND revoked_at IS NULL", ("c", world.Contact)));
        Assert.Equal(HttpStatusCode.Gone, (await Validate(host, secondToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Delete, accessPath, owner)).StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(world.Org, "portal.access_removed"));

        // An active link: no invitation (409); removal unlinks only this contact, never the user, its membership or other links.
        var staff = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dan", "Dispatcher");
        var elsewhere = await database.LinkPortalAsync(foreign.Org, foreign.Contact, existingUser: staff.UserId);
        await database.LinkPortalAsync(world.Org, world.Contact, existingUser: staff.UserId);
        await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Post, invitePath, owner), HttpStatusCode.Conflict, "portal_invite_unavailable");
        var active = await PortalApi.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{world.Customer}/detail", owner));
        Assert.Equal("active", active["contact"]!["portalStatus"]!.GetValue<string>());
        Assert.NotNull(active["contact"]!["portalLinkedOn"]);

        Assert.Equal("not_invited", (await PortalApi.ReadAsync(await host.SendAsync(HttpMethod.Delete, accessPath, owner)))["portalStatus"]!.GetValue<string>());
        Assert.True(await database.ScalarAsync<bool>("SELECT portal_user_id IS NULL AND portal_linked_at IS NULL FROM customer_contacts WHERE id = @c", ("c", world.Contact)));
        Assert.Equal(2, await database.CountAuditLogsAsync(world.Org, "portal.access_removed"));
        Assert.Equal(1, await database.CountAsync("organization_users", "user_id = @u AND status = 'active'", ("u", staff.UserId)));
        Assert.Equal("active", await database.ScalarAsync<string>("SELECT status::text FROM users WHERE id = @u", ("u", staff.UserId)));

        var remaining = await PortalApi.ReadAsync(await PortalApi.SignInAsync(host.Client, elsewhere.Email));
        Assert.Equal(foreign.Contact, remaining["account"]!["contactId"]!.GetValue<Guid>());
        Assert.Single(remaining["accounts"]!.AsArray());
    }

    [Fact]
    public async Task PostPortalInvitationsAccept_NewAndExistingUsers_ActivateOnceWithoutDuplicatesOrMembershipChanges()
    {
        await using var host = OnlineHost.Create(database);
        var staff = await database.SeedUserAsync();

        // A new user: the contact has a last name, the invitation is single use.
        var withName = await database.SeedWorldAsync();
        var emailOne = await ContactEmailAsync(withName.Contact);
        var tokenOne = await InviteAsync(withName, staff, emailOne);
        var details = await PortalApi.ReadAsync(await Validate(host, tokenOne));
        Assert.Equal("Pat", details["firstName"]!.GetValue<string>());
        Assert.Equal(emailOne, details["email"]!.GetValue<string>());
        Assert.False(details["accountExists"]!.GetValue<bool>());
        Assert.False(details["lastNameRequired"]!.GetValue<bool>());

        var accepted = await Accept(host, tokenOne, NewPassword);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var cookie = PortalApi.IssuedCookie(accepted);
        Assert.Equal(1, await database.CountAsync("users", "email = @e AND status = 'active' AND email_verified_at IS NOT NULL AND first_name = 'Pat' AND last_name = 'Contact'", ("e", emailOne)));
        var userId = await database.ScalarAsync<Guid>("SELECT id FROM users WHERE email = @e", ("e", emailOne));
        Assert.Equal(1, await database.CountAsync("customer_contacts", "id = @c AND portal_user_id = @u AND portal_linked_at IS NOT NULL", ("c", withName.Contact), ("u", userId)));
        Assert.Equal(1, await database.CountAsync("customer_portal_invitations", "contact_id = @c AND accepted_at IS NOT NULL", ("c", withName.Contact)));
        Assert.Equal(1, await database.CountAuditLogsAsync(withName.Org, "portal.access_activated"));
        Assert.Equal(HttpStatusCode.OK, (await PortalApi.GetAsync(host.Client, "/portal/sessions/current", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Validate(host, tokenOne)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Accept(host, tokenOne, NewPassword)).StatusCode);

        // A contact without a last name: required on the new-user path, stored on the user and the contact in one step.
        var withoutName = await database.SeedWorldAsync();
        await database.ExecuteAsync("UPDATE customer_contacts SET last_name = NULL WHERE id = @c", ("c", withoutName.Contact));
        var emailTwo = await ContactEmailAsync(withoutName.Contact);
        var tokenTwo = await InviteAsync(withoutName, staff, emailTwo);
        Assert.True((await PortalApi.ReadAsync(await Validate(host, tokenTwo)))["lastNameRequired"]!.GetValue<bool>());

        var missing = await BillingSeed.ProblemAsync(await Accept(host, tokenTwo, NewPassword), HttpStatusCode.BadRequest, errorKey: "lastName");
        Assert.Equal("Enter your last name.", missing["errors"]!["lastName"]![0]!.GetValue<string>());
        var tooLong = await BillingSeed.ProblemAsync(await Accept(host, tokenTwo, NewPassword, new string('x', 101)), HttpStatusCode.BadRequest, errorKey: "lastName");
        Assert.Equal("Last name must be 100 characters or fewer.", tooLong["errors"]!["lastName"]![0]!.GetValue<string>());
        Assert.Equal(0, await database.CountAsync("users", "email = @e", ("e", emailTwo)));

        Assert.Equal(HttpStatusCode.OK, (await Accept(host, tokenTwo, NewPassword, "  Smith  ")).StatusCode);
        Assert.Equal(1, await database.CountAsync("users", "email = @e AND last_name = 'Smith'", ("e", emailTwo)));
        Assert.Equal("Smith", await database.ScalarAsync<string>("SELECT last_name FROM customer_contacts WHERE id = @c", ("c", withoutName.Contact)));

        // An existing staff user proves the password; no second user, no membership or status change, no last name asked.
        var existing = await database.SeedWorldAsync();
        await database.ExecuteAsync("UPDATE customer_contacts SET last_name = NULL WHERE id = @c", ("c", existing.Contact));
        var emailThree = await ContactEmailAsync(existing.Contact);
        var member = await database.SeedMemberAsync(existing.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dan", "Dispatcher", email: emailThree);
        var tokenThree = await InviteAsync(existing, staff, emailThree);
        var known = await PortalApi.ReadAsync(await Validate(host, tokenThree));
        Assert.True(known["accountExists"]!.GetValue<bool>());

        await BillingSeed.ProblemAsync(await Accept(host, tokenThree, NewPassword, "Dispatcher"), HttpStatusCode.Conflict, "portal_invitation_ineligible");
        var wrong = await AcceptExisting(host, tokenThree, "Not the right password 1");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Empty(PortalApi.PortalCookies(wrong));
        Assert.True(await database.ScalarAsync<bool>("SELECT portal_user_id IS NULL FROM customer_contacts WHERE id = @c", ("c", existing.Contact)));

        var linked = await AcceptExisting(host, tokenThree, CompanySettingsDatabaseFixture.Password);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal("Dispatcher", (await PortalApi.ReadAsync(linked))["user"]!["lastName"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAsync("users", "email = @e", ("e", emailThree)));
        Assert.Equal(member.UserId, await database.ScalarAsync<Guid>("SELECT portal_user_id FROM customer_contacts WHERE id = @c", ("c", existing.Contact)));
        Assert.Equal(1, await database.CountAsync("organization_users", "user_id = @u AND status = 'active'", ("u", member.UserId)));
        Assert.True(await database.ScalarAsync<bool>("SELECT last_name IS NULL FROM customer_contacts WHERE id = @c", ("c", existing.Contact)));

        // A user already linked to another contact of the organization cannot be linked again.
        var taken = await database.SeedWorldAsync();
        var sibling = await database.SeedSiblingAsync(taken);
        var emailFour = await ContactEmailAsync(taken.Contact);
        var holder = await database.SeedMemberAsync(taken.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Hal", "Holder", email: emailFour);
        await database.LinkPortalAsync(taken.Org, sibling.Contact, existingUser: holder.UserId);
        var tokenFour = await InviteAsync(taken, staff, emailFour);
        await BillingSeed.ProblemAsync(await AcceptExisting(host, tokenFour, CompanySettingsDatabaseFixture.Password), HttpStatusCode.Conflict, "portal_invitation_ineligible");
        Assert.Equal(0, await database.CountAsync("customer_contacts", "id = @c AND portal_user_id IS NOT NULL", ("c", taken.Contact)));
    }

    [Fact]
    public async Task PostPortalInvitationsValidate_EveryUnusableToken_ReturnsTheIdenticalGone()
    {
        await using var host = OnlineHost.Create(database);
        var staff = await database.SeedUserAsync();
        var world = await database.SeedWorldAsync();
        var scenarios = new List<(string Name, string Token)>();

        async Task<string> Seeded(string name, Func<RequestWorld, Task>? tweak = null)
        {
            var sibling = await database.SeedSiblingAsync(world, name);
            var email = await ContactEmailAsync(sibling.Contact);
            var token = await InviteAsync(sibling, staff, email);

            if (tweak is not null)
            {
                await tweak(sibling);
            }

            scenarios.Add((name, token));

            return token;
        }

        var usable = await Seeded("usable");
        await Seeded("expired", s => database.ExecuteAsync("UPDATE customer_portal_invitations SET created_at = now() - interval '2 days', expires_at = now() - interval '1 day' WHERE contact_id = @c", ("c", s.Contact)));
        await Seeded("revoked", s => database.ExecuteAsync("UPDATE customer_portal_invitations SET revoked_at = now() WHERE contact_id = @c", ("c", s.Contact)));
        await Seeded("accepted", s => database.ExecuteAsync("UPDATE customer_portal_invitations SET accepted_at = now() WHERE contact_id = @c", ("c", s.Contact)));
        await Seeded("contact inactive", s => database.ExecuteAsync("UPDATE customer_contacts SET is_active = false WHERE id = @c", ("c", s.Contact)));
        await Seeded("not primary", s => database.ExecuteAsync("UPDATE customer_contacts SET is_primary = false WHERE id = @c", ("c", s.Contact)));
        await Seeded("already linked", async s => await database.LinkPortalAsync(s.Org, s.Contact));
        await Seeded("email changed", s => database.ExecuteAsync("UPDATE customer_contacts SET email = 'changed@example.com' WHERE id = @c", ("c", s.Contact)));
        await Seeded("customer inactive", s => database.ExecuteAsync("UPDATE customers SET is_active = false WHERE id = @c", ("c", s.Customer)));
        scenarios.Add(("unknown", PortalSeed.NewToken()));
        scenarios.Add(("malformed", "not-a-token"));

        var inactiveOrg = await database.SeedWorldAsync();
        var orgToken = await InviteAsync(inactiveOrg, staff, await ContactEmailAsync(inactiveOrg.Contact));
        await database.ExecuteAsync("UPDATE organizations SET is_active = false WHERE id = @o", ("o", inactiveOrg.Org));
        scenarios.Add(("organization inactive", orgToken));

        Assert.Equal(HttpStatusCode.OK, (await Validate(host, usable)).StatusCode);

        var bodies = new HashSet<string>();

        foreach (var (name, token) in scenarios.Where(scenario => scenario.Name != "usable"))
        {
            var response = await Validate(host, token);

            Assert.True(response.StatusCode == HttpStatusCode.Gone, $"{name}: expected 410 but was {(int)response.StatusCode}");
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            bodies.Add(await PortalApi.BodyWithoutTraceAsync(response));
        }

        var only = Assert.Single(bodies);
        Assert.Equal("portal_invitation_unavailable", JsonNode.Parse(only)!["code"]!.GetValue<string>());
    }

    private static Task<HttpResponseMessage> Validate(OnlineHost host, string token) =>
        PortalApi.PostAsync(host.Client, "/portal/invitations/validate", null, new JsonObject { ["token"] = token });

    private static Task<HttpResponseMessage> Accept(OnlineHost host, string token, string password, string? lastName = null) =>
        PortalApi.PostAsync(host.Client, "/portal/invitations/accept", null, new JsonObject { ["token"] = token, ["password"] = password, ["lastName"] = lastName });

    private static Task<HttpResponseMessage> AcceptExisting(OnlineHost host, string token, string password) =>
        PortalApi.PostAsync(host.Client, "/portal/invitations/accept-existing", null, new JsonObject { ["token"] = token, ["password"] = password });

    // A unique contact email, so that no existing user owns it.
    private async Task<string> ContactEmailAsync(Guid contact)
    {
        var email = $"contact-{Guid.NewGuid():N}@example.com";
        await database.ExecuteAsync("UPDATE customer_contacts SET email = @e WHERE id = @c", ("e", email), ("c", contact));

        return email;
    }

    private async Task<string> InviteAsync(RequestWorld world, Guid staff, string email)
    {
        var token = PortalSeed.NewToken();
        await database.SeedInvitationAsync(world.Org, world.Contact, staff, token, email);

        return token;
    }
}
