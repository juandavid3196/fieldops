using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Infrastructure.Payments;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;
using Microsoft.Extensions.Options;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>Encrypted bank transfer details managed by the Owner (customer-invoice-payments AC-17, BR-24, BR-25).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BankDetailsSettingsTests(CompanySettingsDatabaseFixture database)
{
    private const string Path = "/organization-settings/bank-details";

    private static JsonObject Body(string? name = "First Bank", string? account = "123456789012", string? routing = "021000021", string? updatedAt = null) =>
        new() { ["bankName"] = name, ["accountNumber"] = account, ["routingNumber"] = routing, ["updatedAt"] = updatedAt };

    [Fact]
    public async Task BankDetails_AreOwnerOnlyEncryptedMaskedAndGuardedByTheUpdatedAtValue()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var settingsUpdatedAt = await database.GetOrganizationUpdatedAtAsync(world.Org);

        // Not configured: nulls, never cached.
        var empty = await host.SendAsync(HttpMethod.Get, Path, owner);
        var emptyBody = await BillingSeed.ReadAsync(empty);
        Assert.Equal("no-store", empty.Headers.CacheControl?.ToString());
        Assert.Equal((false, null, null, null, null), (emptyBody["configured"]!.GetValue<bool>(), emptyBody["bankName"]?.GetValue<string>(), emptyBody["accountNumberMasked"]?.GetValue<string>(), emptyBody["routingNumber"]?.GetValue<string>(), emptyBody["updatedAt"]?.GetValue<string>()));

        // The account number is required when nothing is stored; each field has its own message.
        foreach (var (body, field, message) in new (JsonObject, string, string)[]
        {
            (Body(account: null), "accountNumber", "Enter a valid account number."),
            (Body(name: "  "), "bankName", "Enter the bank name."),
            (Body(name: new string('b', 121)), "bankName", "Enter the bank name."),
            (Body(account: "123"), "accountNumber", "Enter a valid account number."),
            (Body(account: "123456789012345678"), "accountNumber", "Enter a valid account number."),
            (Body(account: "12ab34"), "accountNumber", "Enter a valid account number."),
            (Body(routing: "12345678"), "routingNumber", "Enter a 9-digit routing number."),
            (Body(routing: "02100002a"), "routingNumber", "Enter a 9-digit routing number."),
        })
        {
            var problem = await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Put, Path, owner, body), HttpStatusCode.BadRequest, errorKey: field);
            Assert.Equal(message, problem["errors"]![field]![0]!.GetValue<string>());
        }

        Assert.Equal(0, await database.CountAsync("organizations", "id = @o AND bank_name IS NOT NULL", ("o", world.Org)));

        // A valid save: trimmed name, masked response, ciphertext that decrypts to the plaintext and is not it.
        var saved = await host.SendAsync(HttpMethod.Put, Path, owner, Body(name: " First Bank "));
        var savedBody = await BillingSeed.ReadAsync(saved);
        var first = savedBody["updatedAt"]!.GetValue<string>();

        Assert.Equal(("First Bank", "•••• 9012", "021000021", true), (savedBody["bankName"]!.GetValue<string>(), savedBody["accountNumberMasked"]!.GetValue<string>(), savedBody["routingNumber"]!.GetValue<string>(), savedBody["configured"]!.GetValue<bool>()));
        Assert.DoesNotContain("123456789012", savedBody.ToJsonString(), StringComparison.Ordinal);

        var ciphertext = await database.ScalarAsync<byte[]>("SELECT bank_account_number_ciphertext FROM organizations WHERE id = @o", ("o", world.Org));
        var encryptor = new AesGcmBankDetailsEncryptor(Options.Create(new BankDetailsSettings { EncryptionKey = FieldOpsApiFactory.TestBankKey }));
        Assert.False(ciphertext.AsSpan().IndexOf("123456789012"u8) >= 0);
        Assert.Equal("123456789012", encryptor.Decrypt(ciphertext));
        Assert.Equal("9012", await database.ScalarAsync<string>("SELECT bank_account_last4 FROM organizations WHERE id = @o", ("o", world.Org)));

        var again = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Path, owner));
        Assert.Equal(savedBody.ToJsonString(), again.ToJsonString());

        // The bank save never moves the organization settings concurrency value.
        Assert.Equal(settingsUpdatedAt, await database.GetOrganizationUpdatedAtAsync(world.Org));

        // An empty account number keeps the stored one; a stale or missing updatedAt is 409 and changes nothing.
        var kept = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Put, Path, owner, Body(name: "Second Bank", account: "", routing: "111000025", updatedAt: first)));
        Assert.Equal(("Second Bank", "•••• 9012", "111000025"), (kept["bankName"]!.GetValue<string>(), kept["accountNumberMasked"]!.GetValue<string>(), kept["routingNumber"]!.GetValue<string>()));
        Assert.Equal(ciphertext, await database.ScalarAsync<byte[]>("SELECT bank_account_number_ciphertext FROM organizations WHERE id = @o", ("o", world.Org)));
        var current = kept["updatedAt"]!.GetValue<string>();
        Assert.NotEqual(first, current);

        var stored = await database.ScalarAsync<string>("SELECT bank_name || bank_routing_number || bank_details_updated_at FROM organizations WHERE id = @o", ("o", world.Org));
        foreach (var stale in new string?[] { first, null, "garbage" })
        {
            var problem = await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Put, Path, owner, Body(updatedAt: stale)), HttpStatusCode.Conflict, "bank_details_changed");
            Assert.Equal("These details changed. Refresh to see the latest.", problem["title"]!.GetValue<string>());
        }

        Assert.Equal(stored, await database.ScalarAsync<string>("SELECT bank_name || bank_routing_number || bank_details_updated_at FROM organizations WHERE id = @o", ("o", world.Org)));

        // A replaced account number gets a fresh nonce and ciphertext.
        await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Put, Path, owner, Body(account: "123456789012", updatedAt: current)));
        Assert.NotEqual(ciphertext, await database.ScalarAsync<byte[]>("SELECT bank_account_number_ciphertext FROM organizations WHERE id = @o", ("o", world.Org)));

        // Two saves racing on one updatedAt: exactly one wins.
        var latest = (await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Path, owner)))["updatedAt"]!.GetValue<string>();
        var racing = await Task.WhenAll(
            host.SendAsync(HttpMethod.Put, Path, owner, Body(name: "Left Bank", updatedAt: latest)),
            host.SendAsync(HttpMethod.Put, Path, owner, Body(name: "Right Bank", updatedAt: latest)));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], racing.Select(response => response.StatusCode).Order().ToArray());

        // Every other role is a 403 on both verbs, an anonymous caller a 401; the audit rows hold no bank values.
        var snapshot = await database.ScalarAsync<string>("SELECT bank_name || bank_routing_number || bank_details_updated_at FROM organizations WHERE id = @o", ("o", world.Org));

        foreach (var role in new[]
        {
            CompanySettingsDatabaseFixture.DispatcherRoleId,
            CompanySettingsDatabaseFixture.TechnicianRoleId,
            CompanySettingsDatabaseFixture.AccountingRoleId,
            CompanySettingsDatabaseFixture.OperationsManagerRoleId,
            CompanySettingsDatabaseFixture.ViewerRoleId,
        })
        {
            var (cookie, _) = await host.SignInAsync(database, world.Org, role, "Other", $"Role{role}");
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, Path, cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Put, Path, cookie, Body(name: "Hijack", updatedAt: latest))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, Path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Put, Path, null, Body())).StatusCode);
        Assert.Equal(snapshot, await database.ScalarAsync<string>("SELECT bank_name || bank_routing_number || bank_details_updated_at FROM organizations WHERE id = @o", ("o", world.Org)));

        Assert.Equal(
            [$"{{}}|{ownerMember.UserId}|{world.Org}"],
            await database.TextsAsync(
                "SELECT metadata::text || '|' || actor_user_id || '|' || entity_id FROM audit_logs WHERE organization_id = @o AND action = 'organization.bank_details_updated' ORDER BY id LIMIT 1",
                ("o", world.Org)));
        Assert.Equal(4, await database.AuditCountAsync(world.Org, "organization.bank_details_updated"));
        var audit = await database.OrgAuditTextAsync(world.Org);

        foreach (var secret in new[] { "First Bank", "Second Bank", "Left Bank", "Right Bank", "123456789012", "9012", "021000021", "111000025" })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BankDetails_WithoutTheEncryptionKeyAreUnavailableForReadsAndWrites()
    {
        var world = await database.SeedWorldAsync();
        await database.SeedBankAsync(world.Org);
        await using var host = OnlineHost.Create(database, bankKey: false);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var before = await database.ScalarAsync<string>("SELECT bank_name || bank_routing_number || bank_details_updated_at FROM organizations WHERE id = @o", ("o", world.Org));

        var read = await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, Path, owner), HttpStatusCode.ServiceUnavailable, "bank_details_unavailable");
        var write = await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Put, Path, owner, Body()), HttpStatusCode.ServiceUnavailable, "bank_details_unavailable");

        Assert.Equal("Bank details can't be saved right now.", read["title"]!.GetValue<string>());
        Assert.Equal(read["title"]!.GetValue<string>(), write["title"]!.GetValue<string>());
        Assert.Equal(before, await database.ScalarAsync<string>("SELECT bank_name || bank_routing_number || bank_details_updated_at FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Equal(0, await database.AuditCountAsync(world.Org, "organization.bank_details_updated"));
    }
}
