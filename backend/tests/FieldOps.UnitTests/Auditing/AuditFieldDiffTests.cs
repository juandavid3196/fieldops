using FieldOps.Application.Auditing;

namespace FieldOps.UnitTests.Auditing;

/// <summary>
/// Pure, stateless BR-12 diff/masking logic (AC-16, AC-48's audit-adjacent
/// sibling: masking on create and update).
/// </summary>
public class AuditFieldDiffTests
{
    [Fact]
    public void ForCreate_WithEmailAndPhoneSet_MasksBothAndKeepsOtherFieldsReal()
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = "Main Branch",
            ["email"] = "branch@acme.com",
            ["phone"] = "+1 555 987 6543",
        };

        var (before, after) = AuditFieldDiff.ForCreate(fields);

        Assert.Null(before);
        Assert.Contains("\"name\":\"Main Branch\"", after);
        Assert.Contains("\"email\":\"[changed]\"", after);
        Assert.Contains("\"phone\":\"[changed]\"", after);
    }

    [Fact]
    public void ForCreate_WithNullEmail_KeepsNullInsteadOfMasking()
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal) { ["email"] = null };

        var (_, after) = AuditFieldDiff.ForCreate(fields);

        Assert.Contains("\"email\":null", after);
    }

    [Theory]
    [InlineData("Old Name", "New Name", "old@acme.com", "old@acme.com")]
    [InlineData("Same Name", "Same Name", "old@acme.com", "new@acme.com")]
    public void ForUpdate_WithChangedName_IncludesItUnmaskedWhenChanged(
        string beforeName, string afterName, string beforeEmail, string afterEmail)
    {
        var before = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = beforeName,
            ["email"] = beforeEmail,
        };

        var after = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = afterName,
            ["email"] = afterEmail,
        };

        var (beforeJson, afterJson) = AuditFieldDiff.ForUpdate(before, after);

        var nameChanged = beforeName != afterName;
        var emailChanged = beforeEmail != afterEmail;

        Assert.Equal(nameChanged, beforeJson!.Contains($"\"name\":\"{beforeName}\""));
        Assert.Equal(emailChanged, afterJson!.Contains("\"email\":\"[changed]\""));
        Assert.DoesNotContain(beforeEmail, beforeJson);
        Assert.DoesNotContain(afterEmail, afterJson);
    }

    [Fact]
    public void ForUpdate_WithNoChangedFields_ReturnsEmptyObjectsOnBothSides()
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Same" };

        var (before, after) = AuditFieldDiff.ForUpdate(fields, fields);

        Assert.Equal("{}", before);
        Assert.Equal("{}", after);
    }

    [Fact]
    public void ForStateChange_DeactivateThenReactivate_ReflectsBeforeAndAfterOnBothSides()
    {
        var (deactivateBefore, deactivateAfter) = AuditFieldDiff.ForStateChange(before: true, after: false);

        Assert.Equal("""{"isActive":true}""", deactivateBefore);
        Assert.Equal("""{"isActive":false}""", deactivateAfter);
    }
}
