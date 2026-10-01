using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Users;
using FieldOps.Infrastructure.Authentication;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Invitations;

/// <summary>
/// Invitation acceptance (validate, accept, accept-existing): AC-01, AC-03 to
/// AC-15 and AC-24. Each test groups one behavior and risk; unusable tokens
/// never reveal account, membership or identity state (BR-18).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvitationAcceptanceTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const string Password = "Valid password 123";
    private const string AcceptedAction = "user.invitation_accepted";

    private sealed record OrgSetup(Guid Org, SeededMember Owner, SeededBranch Alpha, SeededBranch Zeta, SeededBranch Closed);

    [Fact]
    public async Task InviteResendValidate_UseFragmentLinkReturnPublicDetailsAndIdenticalGoneForUnusableTokens()
    {
        var s = await SeedOrgAsync();
        await using var host = UsersHost.Create(database);
        var ownerCookie = await host.SignInAsync(s.Owner.Email);

        // AC-01: the accept link uses the fragment; resend replaces the token.
        var inviteeEmail = CompanySettingsDatabaseFixture.NewEmail();
        var created = await host.PostAsync("/users/invitations", ownerCookie, UsersSeed.Body(
            ("email", inviteeEmail), ("firstName", "Ivy"), ("lastName", "Invitee"), ("roleCode", "dispatcher"),
            ("isAllBranches", true)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitationId = (await host.ReadAsync(created))["id"]!.GetValue<Guid>();

        var firstToken = RecordingInvitationDelivery.TokenOf(Assert.Single(host.Delivery.Messages));
        Assert.Equal(
            $"{FieldOpsApiFactory.AllowedOrigin}/auth/invitation#token={firstToken}", host.Delivery.Messages[0].AcceptLink);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", firstToken);
        Assert.Equal(
            RecordingInvitationDelivery.HashOf(firstToken),
            await database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", invitationId)));

        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync($"/users/invitations/{invitationId}/resend", ownerCookie)).StatusCode);
        var secondToken = RecordingInvitationDelivery.TokenOf(host.Delivery.Messages[1]);
        Assert.NotEqual(firstToken, secondToken);
        Assert.DoesNotContain('?', host.Delivery.Messages[1].AcceptLink);
        Assert.Equal(
            RecordingInvitationDelivery.HashOf(secondToken),
            await database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", invitationId)));
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(host, "/invitations/validate", TokenBody(firstToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "/invitations/validate", TokenBody(secondToken))).StatusCode);

        // AC-03: only the BR-04 fields, active branches ordered by name.
        var (limitedToken, limitedId) = await SeedInviteAsync(
            s, CompanySettingsDatabaseFixture.NewEmail(), isAll: false, branches: [s.Zeta.Id, s.Closed.Id, s.Alpha.Id]);
        var details = await PostAsync(host, "/invitations/validate", TokenBody(limitedToken));
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Contains("no-store", details.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var text = await details.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(
            ["branches", "email", "expiresAt", "firstName", "inviterName", "isAllBranches", "lastName", "organizationName", "role"],
            body.Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Olivia Owner", body["inviterName"]!.GetValue<string>());
        Assert.Equal("dispatcher", body["role"]!["code"]!.GetValue<string>());
        Assert.Equal(["code", "name"], body["role"]!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray());
        Assert.False(body["isAllBranches"]!.GetValue<bool>());
        Assert.Equal(["Alpha", "Zeta"], body["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>()).ToArray());
        Assert.All(body["branches"]!.AsArray(), branch => Assert.Equal(["name"], branch!.AsObject().Select(pair => pair.Key).ToArray()));
        Assert.DoesNotContain(s.Org.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(limitedId.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(limitedToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(RecordingInvitationDelivery.HashOf(limitedToken), text, StringComparison.Ordinal);

        var (allToken, _) = await SeedInviteAsync(s, CompanySettingsDatabaseFixture.NewEmail(), isAll: true);
        var all = JsonNode.Parse(await (await PostAsync(host, "/invitations/validate", TokenBody(allToken))).Content.ReadAsStringAsync())!;
        Assert.True(all["isAllBranches"]!.GetValue<bool>());
        Assert.Empty(all["branches"]!.AsArray());

        var (noneToken, _) = await SeedInviteAsync(s, CompanySettingsDatabaseFixture.NewEmail(), isAll: false, branches: [s.Closed.Id]);
        var none = JsonNode.Parse(await (await PostAsync(host, "/invitations/validate", TokenBody(noneToken))).Content.ReadAsStringAsync())!;
        Assert.False(none["isAllBranches"]!.GetValue<bool>());
        Assert.Empty(none["branches"]!.AsArray());

        // AC-04: unusable tokens give identical 410s on all three endpoints; malformed ones a token-only 400.
        var sessionEmail = CompanySettingsDatabaseFixture.NewEmail();
        var sessionOrg = await database.SeedOrganizationAsync();
        var sessionUser = await database.SeedMemberAsync(sessionOrg, Dispatcher, "Sam", "Session", email: sessionEmail);
        var sessionCookie = await host.SignInAsync(sessionEmail);

        var inactive = await SeedOrgAsync(active: false);
        var unusable = new List<string>
        {
            RandomToken(),
            firstToken,
            (await SeedInviteAsync(await SeedOrgAsync(), sessionEmail, expiresIn: TimeSpan.FromDays(-1))).Token,
            (await SeedInviteAsync(await SeedOrgAsync(), sessionEmail, revoked: true)).Token,
            (await SeedInviteAsync(await SeedOrgAsync(), sessionEmail, accepted: true)).Token,
            (await SeedInviteAsync(inactive, sessionEmail)).Token,
        };

        string? expected = null;

        foreach (var token in unusable)
        {
            foreach (var response in await PostAllThreeAsync(host, token, sessionCookie))
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
                var problem = await SessionApi.ReadProblemWithoutTraceIdAsync(response);
                expected ??= problem;
                Assert.Equal(expected, problem);
                Assert.DoesNotContain("detail", problem, StringComparison.Ordinal);
                Assert.DoesNotContain("errors", problem, StringComparison.Ordinal);
                Assert.DoesNotContain(token, problem, StringComparison.Ordinal);
                Assert.DoesNotContain(sessionEmail, problem, StringComparison.Ordinal);
            }
        }

        foreach (var malformed in new[] { "abc", new string('a', 44), new string('a', 42) + "+", string.Empty })
        {
            foreach (var response in await PostAllThreeAsync(host, malformed, sessionCookie))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
                Assert.Equal(["token"], errors.Select(pair => pair.Key).ToArray());
                Assert.Equal("Enter a valid value.", errors["token"]![0]!.GetValue<string>());
            }
        }

        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM organization_users WHERE user_id = @u", ("u", sessionUser.UserId)));
    }

    [Fact]
    public async Task Accept_NewUser_CreatesUserMembershipBranchesAuditAndSessionAndLinksProfileOnlyPerBr11()
    {
        var s = await SeedOrgAsync();
        await using var host = UsersHost.Create(database);
        var email = CompanySettingsDatabaseFixture.NewEmail();
        var (token, invitationId) = await SeedInviteAsync(
            s, email, isAll: false, branches: [s.Alpha.Id, s.Closed.Id], link: true);
        await SeedProfileAsync(s, " " + email.ToUpperInvariant() + " ");

        var ip = SessionApi.NewClientIp();
        var response = await PostAsync(
            host, "/invitations/accept", AcceptBody(token, "  Nina ", " Nguyen  "), ip: ip);

        // AC-05: session body, cookie and rows.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var set = Assert.Single(SessionApi.GetSessionSetCookies(response));
        Assert.Null(set.Expires);
        var cookie = SessionApi.GetIssuedCookie(response);
        var session = await SessionApi.ReadSessionAsync(response);
        Assert.Equal(s.Org, session.Organization.Id);
        Assert.Equal("dispatcher", session.Role.Code);
        Assert.Equal(email, session.User.Email);
        Assert.Equal("Nina", session.User.FirstName);

        var current = await SessionApi.ReadSessionAsync(await SessionApi.GetCurrentAsync(host.Client, cookie));
        Assert.Equal(s.Org, current.Organization.Id);

        Assert.Equal("active", await database.TextAsync("SELECT status::text FROM users WHERE id = @u", ("u", session.User.Id)));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT email_verified_at IS NOT NULL AND last_login_at IS NOT NULL FROM users WHERE id = @u", ("u", session.User.Id)));
        Assert.Equal("Nguyen", await database.TextAsync("SELECT last_name FROM users WHERE id = @u", ("u", session.User.Id)));
        var hash = await database.TextAsync("SELECT password_hash FROM users WHERE id = @u", ("u", session.User.Id));
        Assert.NotEqual(Password, hash);
        Assert.True(new Pbkdf2PasswordHasher().Verify(Password, hash));

        var membership = await database.ScalarAsync<Guid>(
            "SELECT id FROM organization_users WHERE organization_id = @o AND user_id = @u", ("o", s.Org), ("u", session.User.Id));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT status = 'active' AND role_id = 2 AND NOT is_all_branches AND invited_by_user_id = @i AND joined_at IS NOT NULL FROM organization_users WHERE id = @m",
            ("i", s.Owner.UserId), ("m", membership)));
        Assert.Equal(
            [s.Alpha.Id],
            await database.QueryGuidsAsync("SELECT branch_id FROM organization_user_branches WHERE organization_user_id = @m", ("m", membership)));
        Assert.NotNull(await database.ScalarAsync<object>("SELECT accepted_at FROM user_invitations WHERE id = @i", ("i", invitationId)));

        Assert.Equal(1, await database.CountAuditAsync(s.Org, AcceptedAction));
        var (before, after, metadata) = await database.GetLatestAuditAsync(s.Org, AcceptedAction);
        Assert.Null(before);
        Assert.Equal("{}", metadata);
        var audit = JsonNode.Parse(after!)!;
        Assert.Equal(membership.ToString(), audit["membershipId"]!.GetValue<string>());
        Assert.Equal("dispatcher", audit["roleCode"]!.GetValue<string>());
        Assert.False(audit["isAllBranches"]!.GetValue<bool>());
        Assert.Equal(s.Alpha.Id.ToString(), audit["branchIds"]![0]!.GetValue<string>());
        Assert.True(audit["accountCreated"]!.GetValue<bool>());
        Assert.True(audit["teamProfileLinked"]!.GetValue<bool>());
        Assert.True(await database.ScalarAsync<bool>(
            """
            SELECT actor_user_id = @u AND entity_id = @i AND entity_type = 'user_invitation' AND branch_id IS NULL AND host(ip_address) = @ip
            FROM audit_logs WHERE organization_id = @o AND action = @a
            """,
            ("u", session.User.Id), ("i", invitationId), ("ip", ip), ("o", s.Org), ("a", AcceptedAction)));
        Assert.Equal(membership, await database.ScalarAsync<Guid>(
            "SELECT organization_user_id FROM technician_profiles WHERE organization_id = @o", ("o", s.Org)));

        // AC-11: profile link table; AC-10: all-branches assigns no branch rows.
        foreach (var (link, matching, linked) in new[] { (true, 0, false), (false, 1, false), (true, 1, true) })
        {
            var t = await SeedOrgAsync();
            var e = CompanySettingsDatabaseFixture.NewEmail();
            var (t1, _) = await SeedInviteAsync(t, e, isAll: true, link: link);

            for (var i = 0; i < matching; i++)
            {
                await SeedProfileAsync(t, e);
            }

            var ok = await PostAsync(host, "/invitations/accept", AcceptBody(t1));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var userId = (await SessionApi.ReadSessionAsync(ok)).User.Id;
            Assert.Equal(matching, await database.CountAsync("SELECT COUNT(*) FROM technician_profiles WHERE organization_id = @o", ("o", t.Org)));
            Assert.Equal(linked ? 1 : 0, await database.CountAsync(
                "SELECT COUNT(*) FROM technician_profiles WHERE organization_id = @o AND organization_user_id IS NOT NULL", ("o", t.Org)));
            Assert.Equal(0, await database.CountAsync(
                "SELECT COUNT(*) FROM organization_user_branches b JOIN organization_users m ON m.id = b.organization_user_id WHERE m.user_id = @u",
                ("u", userId)));
            Assert.Equal(linked, JsonNode.Parse((await database.GetLatestAuditAsync(t.Org, AcceptedAction)).After!)!["teamProfileLinked"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task Accept_InvalidFieldsExistingAccountOrNoActiveBranch_ReturnsFieldErrorsOrConflictWithoutChanges()
    {
        var s = await SeedOrgAsync();
        await using var host = UsersHost.Create(database);
        var email = CompanySettingsDatabaseFixture.NewEmail();
        var (token, invitationId) = await SeedInviteAsync(s, email);
        var before = await SnapshotAsync(s.Org, invitationId, email);

        // AC-06: one failed rule per body.
        var failures = new (JsonObject Body, string Key, string Message)[]
        {
            (AcceptBody(token, first: "   "), "firstName", "Enter a first name."),
            (AcceptBody(token, first: new string('f', 101)), "firstName", "Use 100 characters or fewer."),
            (AcceptBody(token, last: string.Empty), "lastName", "Enter a last name."),
            (AcceptBody(token, last: new string('l', 101)), "lastName", "Use 100 characters or fewer."),
            (AcceptBody(token, password: string.Empty), "password", "This field is required."),
            (WithoutProperty(AcceptBody(token), "password"), "password", "This field is required."),
            (AcceptBody(token, password: new string('p', 11)), "password", "Use 12 to 128 characters."),
            (AcceptBody(token, password: new string('p', 129)), "password", "Use 12 to 128 characters."),
            (AcceptBody(token, password: email.ToUpperInvariant()), "password", "Choose a password that is different from your email."),
        };

        foreach (var (body, key, message) in failures)
        {
            var response = await PostAsync(host, "/invitations/accept", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
            Assert.Equal([key], errors.Select(pair => pair.Key).ToArray());
            Assert.Equal(message, errors[key]![0]!.GetValue<string>());
        }

        Assert.Equal(before, await SnapshotAsync(s.Org, invitationId, email));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "/invitations/validate", TokenBody(token))).StatusCode);

        // AC-07: an existing account in any status is never changed or reused.
        foreach (var status in new[] { "active", "suspended", "disabled" })
        {
            var t = await SeedOrgAsync();
            var existingEmail = CompanySettingsDatabaseFixture.NewEmail();
            await database.ExecuteAsync(
                $"""
                INSERT INTO users (email, password_hash, first_name, last_name, status)
                VALUES (@e, @h, 'Ex', 'Isting', '{status}')
                """,
                ("e", existingEmail), ("h", database.PasswordHash));
            var userBefore = await database.TextAsync(
                "SELECT password_hash || first_name || last_name || status::text FROM users WHERE email = @e", ("e", existingEmail));
            var (t1, t1Id) = await SeedInviteAsync(t, existingEmail);
            var snapshot = await SnapshotAsync(t.Org, t1Id, existingEmail);

            var conflict = await PostAsync(host, "/invitations/accept", AcceptBody(t1));

            await AssertConflictAsync(conflict, "account_exists", existingEmail, t1);
            Assert.Equal(userBefore, await database.TextAsync(
                "SELECT password_hash || first_name || last_name || status::text FROM users WHERE email = @e", ("e", existingEmail)));
            Assert.Equal(snapshot, await SnapshotAsync(t.Org, t1Id, existingEmail));
        }

        // AC-10: only inactive invited branches left.
        var unavailableEmail = CompanySettingsDatabaseFixture.NewEmail();
        var (unavailableToken, unavailableId) = await SeedInviteAsync(s, unavailableEmail, isAll: false, branches: [s.Closed.Id]);
        var unavailableBefore = await SnapshotAsync(s.Org, unavailableId, unavailableEmail);
        await AssertConflictAsync(
            await PostAsync(host, "/invitations/accept", AcceptBody(unavailableToken)), "access_unavailable", unavailableEmail, unavailableToken);
        Assert.Equal(unavailableBefore, await SnapshotAsync(s.Org, unavailableId, unavailableEmail));
    }

    [Fact]
    public async Task AcceptExisting_ExistingUser_JoinsOrganizationKeepingOtherDataAndRejectsUnsafeCases()
    {
        var a = await SeedOrgAsync();
        var b = await SeedOrgAsync();
        await using var host = UsersHost.Create(database);
        var email = CompanySettingsDatabaseFixture.NewEmail();
        var member = await database.SeedMemberAsync(a.Org, Dispatcher, "Una", "Usual", email: email, isAllBranches: false);
        await database.LinkMembershipToBranchAsync(member.MembershipId, a.Alpha.Id);
        var passwordBefore = await database.TextAsync("SELECT password_hash FROM users WHERE id = @u", ("u", member.UserId));
        var cookieA = await host.SignInAsync(email);
        var (token, invitationId) = await SeedInviteAsync(b, email, isAll: false, branches: [b.Alpha.Id, b.Closed.Id], first: "Other", last: "Names");

        // AC-08.
        var response = await PostAsync(host, "/invitations/accept-existing", TokenBody(token), cookieA);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookieB = SessionApi.GetIssuedCookie(response);
        Assert.NotEqual(cookieA, cookieB);
        var session = await SessionApi.ReadSessionAsync(response);
        Assert.Equal(b.Org, session.Organization.Id);
        Assert.Equal(member.UserId, session.User.Id);
        Assert.Equal("Una", session.User.FirstName);
        Assert.Equal(b.Org, (await SessionApi.ReadSessionAsync(await SessionApi.GetCurrentAsync(host.Client, cookieB))).Organization.Id);

        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM users WHERE email = @e", ("e", email)));
        Assert.Equal(passwordBefore, await database.TextAsync("SELECT password_hash FROM users WHERE id = @u", ("u", member.UserId)));
        Assert.Equal("Una|Usual", await database.TextAsync("SELECT first_name || '|' || last_name FROM users WHERE id = @u", ("u", member.UserId)));
        Assert.Equal(1, await database.CountAsync(
            "SELECT COUNT(*) FROM organization_users WHERE id = @m AND organization_id = @o AND status = 'active' AND role_id = 2 AND NOT is_all_branches",
            ("m", member.MembershipId), ("o", a.Org)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM organization_user_branches WHERE organization_user_id = @m", ("m", member.MembershipId)));
        var newMembership = await database.ScalarAsync<Guid>(
            "SELECT id FROM organization_users WHERE organization_id = @o AND user_id = @u AND status = 'active'", ("o", b.Org), ("u", member.UserId));
        Assert.Equal([b.Alpha.Id], await database.QueryGuidsAsync(
            "SELECT branch_id FROM organization_user_branches WHERE organization_user_id = @m", ("m", newMembership)));
        Assert.False(JsonNode.Parse((await database.GetLatestAuditAsync(b.Org, AcceptedAction)).After!)!["accountCreated"]!.GetValue<bool>());
        Assert.Equal(1, await database.CountAuditAsync(b.Org, AcceptedAction));

        // AC-09: 401, identity mismatch, existing memberships (suspended stays suspended), no active branch.
        var stranger = await database.SeedMemberAsync(a.Org, Dispatcher, "Stan", "Stranger");
        var strangerCookie = await host.SignInAsync(stranger.Email);
        var cases = new List<(string Name, string? Cookie, string Token, Guid Org, Guid InvitationId, string Email, HttpStatusCode Status, string? Code)>();

        async Task AddAsync(string name, string? cookie, string inviteEmail, OrgSetup org, HttpStatusCode status, string? code,
            bool isAll = true, Guid[]? branches = null)
        {
            var (t, id) = await SeedInviteAsync(org, inviteEmail, isAll: isAll, branches: branches);
            cases.Add((name, cookie, t, org.Org, id, inviteEmail, status, code));
        }

        var c1 = await SeedOrgAsync();
        await AddAsync("no session", null, email, c1, HttpStatusCode.Unauthorized, null);
        var c2 = await SeedOrgAsync();
        await AddAsync("identity mismatch", strangerCookie, email, c2, HttpStatusCode.Conflict, "identity_mismatch");
        var c3 = await SeedOrgAsync();
        await database.SeedMemberAsync(c3.Org, Dispatcher, "Una", "Usual", email: email, status: "suspended", existingUserId: member.UserId);
        await AddAsync("suspended membership", cookieB, email, c3, HttpStatusCode.Conflict, "membership_exists");
        var c4 = await SeedOrgAsync();
        await database.SeedMemberAsync(c4.Org, Dispatcher, "Una", "Usual", email: email, existingUserId: member.UserId);
        await AddAsync("active membership", cookieB, email, c4, HttpStatusCode.Conflict, "membership_exists");
        var c5 = await SeedOrgAsync();
        await AddAsync("no active branch", cookieB, email, c5, HttpStatusCode.Conflict, "access_unavailable", isAll: false, branches: [c5.Closed.Id]);

        foreach (var c in cases)
        {
            var snapshot = await SnapshotAsync(c.Org, c.InvitationId, c.Email);
            var denied = await PostAsync(host, "/invitations/accept-existing", TokenBody(c.Token), c.Cookie);
            Assert.Equal(c.Status, denied.StatusCode);

            if (c.Code is not null)
            {
                await AssertConflictAsync(denied, c.Code, c.Email, c.Token);
            }

            Assert.Equal(snapshot, await SnapshotAsync(c.Org, c.InvitationId, c.Email));
        }

        Assert.Equal("suspended", await database.TextAsync(
            "SELECT status::text FROM organization_users WHERE organization_id = @o AND user_id = @u", ("o", c3.Org), ("u", member.UserId)));
        Assert.Equal(0, await database.CountAsync(
            "SELECT COUNT(*) FROM organization_users WHERE organization_id = @o AND user_id = @u", ("o", c2.Org), ("u", stranger.UserId)));
    }

    [Fact]
    public async Task Accept_CheckOrderAndBodyRules_FollowBr18()
    {
        var s = await SeedOrgAsync();
        await using var host = UsersHost.Create(database);
        var email = CompanySettingsDatabaseFixture.NewEmail();
        var sessionEmail = CompanySettingsDatabaseFixture.NewEmail();
        var sessionUser = await database.SeedMemberAsync(s.Org, Dispatcher, "Sam", "Session", email: sessionEmail);
        var sessionCookie = await host.SignInAsync(sessionEmail);

        // AC-24: an unusable token never reveals account, membership or identity state.
        var accountEmail = CompanySettingsDatabaseFixture.NewEmail();
        await database.SeedMemberAsync(s.Org, Dispatcher, "Ann", "Account", email: accountEmail);
        var (expiredForAccount, _) = await SeedInviteAsync(await SeedOrgAsync(), accountEmail, expiresIn: TimeSpan.FromDays(-1));
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(host, "/invitations/accept", AcceptBody(expiredForAccount))).StatusCode);

        var memberOrg = await SeedOrgAsync();
        await database.SeedMemberAsync(memberOrg.Org, Dispatcher, "Sam", "Session", email: sessionEmail, existingUserId: sessionUser.UserId);
        var (expiredForMember, _) = await SeedInviteAsync(memberOrg, sessionEmail, expiresIn: TimeSpan.FromDays(-1));
        var (expiredForOther, _) = await SeedInviteAsync(await SeedOrgAsync(), CompanySettingsDatabaseFixture.NewEmail(), expiresIn: TimeSpan.FromDays(-1));

        foreach (var token in new[] { expiredForMember, expiredForOther })
        {
            Assert.Equal(HttpStatusCode.Gone, (await PostAsync(host, "/invitations/accept-existing", TokenBody(token), sessionCookie)).StatusCode);
        }

        // Field shape is checked before the token; the email rule only for a usable token.
        var invalid = await PostAsync(host, "/invitations/accept", AcceptBody(expiredForOther, first: " "));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(
            ["firstName"],
            JsonNode.Parse(await invalid.Content.ReadAsStringAsync())!["errors"]!.AsObject().Select(pair => pair.Key).ToArray());
        Assert.Equal(
            HttpStatusCode.Gone,
            (await PostAsync(host, "/invitations/accept", AcceptBody(expiredForAccount, password: accountEmail))).StatusCode);

        // Usable token: password-equals-email (400) precedes eligibility (409).
        await database.SeedMemberAsync(s.Org, Dispatcher, "Una", "Usual", email: email);
        var (usable, _) = await SeedInviteAsync(await SeedOrgAsync(), email);
        var emailAsPassword = await PostAsync(host, "/invitations/accept", AcceptBody(usable, password: email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.BadRequest, emailAsPassword.StatusCode);
        Assert.Equal(
            ["password"],
            JsonNode.Parse(await emailAsPassword.Content.ReadAsStringAsync())!["errors"]!.AsObject().Select(pair => pair.Key).ToArray());
        await AssertConflictAsync(await PostAsync(host, "/invitations/accept", AcceptBody(usable)), "account_exists", email, usable);

        // Session first (401 before 415/413/400), then content type, size and body.
        var oversized = new string('x', 5000);
        foreach (var (mediaType, raw) in new (string? MediaType, string Raw)[] { ("application/json", "{bad"), ("text/plain", "{}"), (null, "{}"), ("application/json", oversized) })
        {
            var anonymous = await PostAsync(host, "/invitations/accept-existing", null, raw: raw, mediaType: mediaType);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostAsync(host, "/invitations/validate", null, raw: "{}", mediaType: "text/plain")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostAsync(host, "/invitations/validate", null, raw: "{}", mediaType: null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostAsync(host, "/invitations/accept-existing", null, sessionCookie, raw: "{}", mediaType: "text/plain")).StatusCode);

        foreach (var path in new[] { "/invitations/validate", "/invitations/accept", "/invitations/accept-existing" })
        {
            foreach (var raw in new[] { "{bad", "null", string.Empty })
            {
                var response = await PostAsync(host, path, null, path.EndsWith("existing", StringComparison.Ordinal) ? sessionCookie : null, raw: raw);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.False(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject().ContainsKey("errors"));
            }
        }
    }

    [Fact]
    public async Task Accept_ConcurrentAndRepeatedAcceptance_CreatesExactlyOneMembershipAndAuditRow()
    {
        var s = await SeedOrgAsync();
        await using var host = UsersHost.Create(database);
        var email = CompanySettingsDatabaseFixture.NewEmail();
        var (token, invitationId) = await SeedInviteAsync(s, email);

        // AC-12: two concurrent acceptances, then a third.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => PostAsync(host, "/invitations/accept", AcceptBody(token))));
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Gone],
            responses.Select(response => response.StatusCode).Order().ToArray());
        Assert.Equal(HttpStatusCode.Gone, (await PostAsync(host, "/invitations/accept", AcceptBody(token))).StatusCode);
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM users WHERE email = @e", ("e", email)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM organization_users WHERE organization_id = @o AND role_id = 2", ("o", s.Org)));
        Assert.Equal(1, await database.CountAuditAsync(s.Org, AcceptedAction));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE id = @i AND accepted_at IS NOT NULL", ("i", invitationId)));

        // BR-10: the same new email invited by two organizations gives one account and a 409, never a 500.
        var other = await SeedOrgAsync();
        var sharedEmail = CompanySettingsDatabaseFixture.NewEmail();
        var (t1, _) = await SeedInviteAsync(s, sharedEmail);
        var (t2, _) = await SeedInviteAsync(other, sharedEmail);
        var raced = await Task.WhenAll(
            PostAsync(host, "/invitations/accept", AcceptBody(t1)),
            PostAsync(host, "/invitations/accept", AcceptBody(t2)));
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            raced.Select(response => response.StatusCode).Order().ToArray());
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM users WHERE email = @e", ("e", sharedEmail)));
    }

    [Fact]
    public async Task Accept_WhenAuditInsertFails_RollsBackEverythingAndIssuesNoCookie()
    {
        var s = await SeedOrgAsync();
        await using var host = UsersHost.Create(database);
        var email = CompanySettingsDatabaseFixture.NewEmail();
        var (token, invitationId) = await SeedInviteAsync(s, email, isAll: false, branches: [s.Alpha.Id], link: true);
        await SeedProfileAsync(s, email);

        var existingEmail = CompanySettingsDatabaseFixture.NewEmail();
        var existing = await database.SeedMemberAsync(await database.SeedOrganizationAsync(), Dispatcher, "Una", "Usual", email: existingEmail);
        var existingCookie = await host.SignInAsync(existingEmail);
        var (existingToken, existingInvitation) = await SeedInviteAsync(s, existingEmail, isAll: false, branches: [s.Alpha.Id]);

        var suffix = s.Org.ToString("N");
        await database.ExecuteAsync(
            $"""
            CREATE FUNCTION fail_acceptance_audit_{suffix}() RETURNS trigger AS $$
            BEGIN
              IF NEW.action = 'user.invitation_accepted' AND NEW.organization_id = '{s.Org}' THEN
                RAISE EXCEPTION 'forced audit failure';
              END IF;
              RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_acceptance_audit_{suffix} BEFORE INSERT ON audit_logs
              FOR EACH ROW EXECUTE FUNCTION fail_acceptance_audit_{suffix}();
            """);

        try
        {
            // AC-13.
            var failed = await PostAsync(host, "/invitations/accept", AcceptBody(token));
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            SessionApi.AssertNoSessionCookie(failed);
            var problem = await failed.Content.ReadAsStringAsync();
            Assert.DoesNotContain(email, problem, StringComparison.Ordinal);
            Assert.DoesNotContain("forced", problem, StringComparison.Ordinal);
            Assert.Equal("An unexpected error occurred.", JsonNode.Parse(problem)!["title"]!.GetValue<string>());

            Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM users WHERE email = @e", ("e", email)));
            Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM organization_users WHERE organization_id = @o", ("o", s.Org)));
            Assert.Equal(0, await database.CountAsync(
                "SELECT COUNT(*) FROM organization_user_branches b JOIN organization_users m ON m.id = b.organization_user_id WHERE m.organization_id = @o AND m.role_id = 2",
                ("o", s.Org)));
            Assert.Equal(0, await database.CountAsync(
                "SELECT COUNT(*) FROM technician_profiles WHERE organization_id = @o AND organization_user_id IS NOT NULL", ("o", s.Org)));
            Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE id = @i AND accepted_at IS NOT NULL", ("i", invitationId)));
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "/invitations/validate", TokenBody(token))).StatusCode);

            // Existing-user path: the previous session cookie stays in place.
            var failedExisting = await PostAsync(host, "/invitations/accept-existing", TokenBody(existingToken), existingCookie);
            Assert.Equal(HttpStatusCode.InternalServerError, failedExisting.StatusCode);
            SessionApi.AssertNoSessionCookie(failedExisting);
            Assert.Equal(0, await database.CountAsync(
                "SELECT COUNT(*) FROM organization_users WHERE organization_id = @o AND user_id = @u", ("o", s.Org), ("u", existing.UserId)));
            Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE id = @i AND accepted_at IS NOT NULL", ("i", existingInvitation)));
            Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, existingCookie)).StatusCode);
        }
        finally
        {
            await database.ExecuteAsync(
                $"DROP TRIGGER fail_acceptance_audit_{suffix} ON audit_logs; DROP FUNCTION fail_acceptance_audit_{suffix}();");
        }
    }

    [Fact]
    public async Task InvitationEndpoints_RateLimitPerIpAcrossEndpointsAndLeakNoSecrets()
    {
        var s = await SeedOrgAsync();
        await using var host = UsersHost.Create(database, captureLogs: true);
        var ownerCookie = await host.SignInAsync(s.Owner.Email);
        var email = CompanySettingsDatabaseFixture.NewEmail();
        var created = await host.PostAsync("/users/invitations", ownerCookie, UsersSeed.Body(
            ("email", email), ("firstName", "Ivy"), ("lastName", "Invitee"), ("roleCode", "dispatcher"), ("isAllBranches", true)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitationId = (await host.ReadAsync(created))["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync($"/users/invitations/{invitationId}/resend", ownerCookie)).StatusCode);
        var replaced = RecordingInvitationDelivery.TokenOf(host.Delivery.Messages[0]);
        var token = RecordingInvitationDelivery.TokenOf(host.Delivery.Messages[1]);
        var bodies = new List<string>();

        // Successful, failed and repeated requests.
        var accepted = await PostAsync(host, "/invitations/accept", AcceptBody(token));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        foreach (var response in new[]
        {
            await PostAsync(host, "/invitations/accept", AcceptBody(token)),
            await PostAsync(host, "/invitations/validate", TokenBody(replaced)),
            await PostAsync(host, "/invitations/accept", AcceptBody(RandomToken(), password: "short")),
        })
        {
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        // AC-14: 20 requests per IP across the three endpoints, then 429.
        var fresh = await SeedInviteAsync(s, CompanySettingsDatabaseFixture.NewEmail());
        var ip = SessionApi.NewClientIp();

        for (var i = 0; i < 20; i++)
        {
            // Every request counts, including a rejected media type.
            var response = (i % 3) switch
            {
                0 when i == 0 => await PostAsync(host, "/invitations/validate", null, ip: ip, raw: "{}", mediaType: "text/plain"),
                0 => await PostAsync(host, "/invitations/validate", TokenBody("bad"), ip: ip),
                1 => await PostAsync(host, "/invitations/accept", AcceptBody("bad"), ip: ip),
                _ => await PostAsync(host, "/invitations/accept-existing", TokenBody("bad"), ip: ip),
            };
            Assert.Contains(
                response.StatusCode,
                new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.UnsupportedMediaType });
        }

        var limited = await PostAsync(host, "/invitations/accept", AcceptBody(fresh.Token), ip: ip, origin: FieldOpsApiFactory.AllowedOrigin);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        SignInRateLimitTests.AssertRetryAfter(limited, maxSeconds: 300);
        Assert.Contains("no-store", limited.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Equal(FieldOpsApiFactory.AllowedOrigin, Assert.Single(limited.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(
            HttpStatusCode.TooManyRequests, (await PostAsync(host, "/invitations/validate", TokenBody(fresh.Token), ip: ip)).StatusCode);
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE id = @i AND accepted_at IS NOT NULL", ("i", fresh.Id)));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "/invitations/validate", TokenBody(fresh.Token))).StatusCode);
        bodies.Add(await limited.Content.ReadAsStringAsync());

        // AC-15: no raw token, hash, email or password in logs, audit rows or error bodies.
        var audit = await database.TextAsync(
            "SELECT COALESCE(string_agg(COALESCE(before_data::text, '') || COALESCE(after_data::text, '') || metadata::text, ''), '') FROM audit_logs WHERE organization_id = @o",
            ("o", s.Org));
        var everything = string.Join('\n', [host.Logs!.AllText(), audit, .. bodies]);

        foreach (var secret in new[] { token, replaced, fresh.Token, RecordingInvitationDelivery.HashOf(token), RecordingInvitationDelivery.HashOf(replaced), email, Password })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }

    private async Task<OrgSetup> SeedOrgAsync(bool active = true)
    {
        var org = await database.SeedOrganizationAsync(isActive: active);
        var owner = await database.SeedMemberAsync(org, Owner, "Olivia", "Owner");
        var alpha = await database.SeedBranchAsync(org, name: "Alpha");
        var zeta = await database.SeedBranchAsync(org, name: "Zeta");
        var closed = await database.SeedBranchAsync(org, name: "Closed", isActive: false);

        return new OrgSetup(org, owner, alpha, zeta, closed);
    }

    private async Task<(string Token, Guid Id)> SeedInviteAsync(
        OrgSetup s,
        string email,
        bool isAll = true,
        Guid[]? branches = null,
        bool accepted = false,
        bool revoked = false,
        TimeSpan? expiresIn = null,
        bool link = false,
        string first = "Ivy",
        string last = "Invitee")
    {
        var (token, hash) = InvitationTokens.Generate();
        var id = await database.SeedInvitationAsync(
            s.Org, s.Owner.UserId, first, last, email, Dispatcher, DateTimeOffset.UtcNow + (expiresIn ?? TimeSpan.FromDays(7)),
            accepted, revoked, isAll, branches, hash, link);

        return (token, id);
    }

    private Task SeedProfileAsync(OrgSetup s, string email) =>
        database.ExecuteAsync(
            """
            INSERT INTO technician_profiles (organization_id, branch_id, first_name, last_name, email)
            VALUES (@o, @b, 'Tech', 'Nician', @e)
            """,
            ("o", s.Org), ("b", s.Alpha.Id), ("e", email));

    // Rows an acceptance would touch, for "nothing changed" comparisons.
    private async Task<string> SnapshotAsync(Guid org, Guid invitationId, string email) =>
        string.Join(
            '|',
            await database.CountAsync("SELECT COUNT(*) FROM organization_users WHERE organization_id = @o", ("o", org)),
            await database.CountAsync(
                "SELECT COUNT(*) FROM organization_user_branches b JOIN organization_users m ON m.id = b.organization_user_id WHERE m.organization_id = @o",
                ("o", org)),
            await database.CountAuditAsync(org, AcceptedAction),
            await database.CountAsync("SELECT COUNT(*) FROM users WHERE email = @e", ("e", email)),
            await database.TextAsync("SELECT COALESCE(accepted_at::text, '') FROM user_invitations WHERE id = @i", ("i", invitationId)),
            await database.TextAsync(
                "SELECT COALESCE(string_agg(id::text || status::text, ',' ORDER BY id), '') FROM organization_users WHERE organization_id = @o",
                ("o", org)));

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code, string email, string token)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var problem = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(code, problem["code"]!.GetValue<string>());
        Assert.False(problem.ContainsKey("detail"));
        Assert.DoesNotContain(email, text, StringComparison.Ordinal);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage[]> PostAllThreeAsync(UsersHost host, string token, string cookie) =>
    [
        await PostAsync(host, "/invitations/validate", TokenBody(token)),
        await PostAsync(host, "/invitations/accept", AcceptBody(token)),
        await PostAsync(host, "/invitations/accept-existing", TokenBody(token), cookie),
    ];

    private static Task<HttpResponseMessage> PostAsync(
        UsersHost host,
        string path,
        JsonObject? body,
        string? cookie = null,
        string? ip = null,
        string? origin = null,
        string? raw = null,
        string? mediaType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = SessionApi.CreateContent(raw ?? body!.ToJsonString(), mediaType),
        };

        return SessionApi.SendAsync(host.Client, request, ip, cookie, origin, null);
    }

    private static JsonObject TokenBody(string token) => new() { ["token"] = token };

    private static JsonObject AcceptBody(
        string token, string first = "Nina", string last = "Nguyen", string password = Password) =>
        new() { ["token"] = token, ["firstName"] = first, ["lastName"] = last, ["password"] = password };

    private static JsonObject WithoutProperty(JsonObject body, string name)
    {
        body.Remove(name);

        return body;
    }

    private static string RandomToken() => InvitationTokens.Generate().Raw;
}
