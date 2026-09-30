using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Features.Users;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;

namespace FieldOps.UnitTests.Users;

public class UserAccessRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InvitationTokensAndCatalog_GenerateHashOnlyTokensAndExposeTheBr07Catalog()
    {
        var (raw, hash) = InvitationTokens.Generate();
        var (otherRaw, _) = InvitationTokens.Generate();

        Assert.Equal(43, raw.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", raw);
        Assert.NotEqual(raw, otherRaw);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), hash);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(raw, hash, StringComparison.Ordinal);

        Assert.Equal(
            ["owner", "operations_manager", "dispatcher", "technician", "accounting", "viewer"],
            PermissionCatalog.Roles.Select(role => role.Code));
        Assert.Equal(["owner", "operations_manager"], PermissionCatalog.Roles.Where(r => r.ForcesAllBranches).Select(r => r.Code));
        Assert.Equal(
            ["operations_manager", "dispatcher", "technician"],
            PermissionCatalog.Roles.Where(r => r.HasTeamProfile).Select(r => r.Code));
        Assert.Equal(10, PermissionCatalog.Modules.Count);
        Assert.Equal("products_services", PermissionCatalog.Modules[5].Key);
        Assert.Equal("Products & services", PermissionCatalog.Modules[5].Name);
        Assert.Equal(
            ["full", "edit", "view", "none", "view", "view"],
            PermissionCatalog.Roles.Select(role => PermissionCatalog.Modules[5].Levels[role.Code].Level));
        Assert.All(PermissionCatalog.Modules, module => Assert.Equal(6, module.Levels.Count));
        Assert.Equal("none", PermissionCatalog.Modules.Single(m => m.Key == "company_settings").Levels["viewer"].Level);
    }

    [Fact]
    public void InvitationAndMembership_EnforceLifecycleGuardsAndReportRealChangesOnly()
    {
        var invitation = UserInvitation.Create(
            Guid.NewGuid(), "  Mixed@Example.COM ", " Ada ", " Lovelace ", roleId: 2, isAllBranches: false,
            linkTeamProfile: true, tokenHash: "hash-1", invitedByUserId: Guid.NewGuid(), expiresAt: Now.AddDays(7));

        Assert.Equal("mixed@example.com", invitation.Email);
        Assert.Equal("Ada", invitation.FirstName);
        Assert.Equal("Lovelace", invitation.LastName);
        Assert.True(invitation.IsOpen);
        Assert.False(invitation.IsExpired(Now));
        Assert.True(invitation.IsExpired(Now.AddDays(7)));
        Assert.False(invitation.UpdateAccess(2, isAllBranches: false));
        Assert.True(invitation.UpdateAccess(3, isAllBranches: false));
        Assert.Throws<ArgumentException>(() => UserInvitation.Create(
            Guid.NewGuid(), "a@b.co", "  ", "L", 2, false, false, "h", Guid.NewGuid(), Now.AddDays(1)));

        invitation.Resend("hash-2", Now.AddDays(14));
        Assert.Equal("hash-2", invitation.TokenHash);
        Assert.True(invitation.IsOpen);

        invitation.Revoke(Now);
        Assert.False(invitation.IsOpen);
        Assert.Throws<InvalidOperationException>(() => invitation.Resend("hash-3", Now.AddDays(7)));
        Assert.Throws<InvalidOperationException>(() => invitation.Revoke(Now));
        Assert.Throws<InvalidOperationException>(() => invitation.UpdateAccess(4, isAllBranches: true));

        var member = OrganizationUser.Create(Guid.NewGuid(), Guid.NewGuid(), roleId: 2);
        Assert.False(member.Reactivate(Now));
        Assert.True(member.Suspend(Now));
        Assert.Equal(UserStatus.Suspended, member.Status);
        Assert.False(member.Suspend(Now));
        Assert.True(member.Reactivate(Now));
        Assert.False(member.ChangeAccess(2, isAllBranches: false, Now));
        Assert.True(member.ChangeAccess(2, isAllBranches: true, Now));
        Assert.True(member.ChangeAccess(1, isAllBranches: true, Now));
        Assert.Equal(1, member.RoleId);
    }
}
