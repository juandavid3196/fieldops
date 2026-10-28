using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// Requires a writable key directory in Production so keys are never lost
/// with the container filesystem. The probe runs on start and names no values.
/// </summary>
public sealed class DataProtectionSettingsValidator(IHostEnvironment environment) : IValidateOptions<DataProtectionSettings>
{
    public ValidateOptionsResult Validate(string? name, DataProtectionSettings options)
    {
        var section = DataProtectionSettings.SectionName;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            failures.Add($"'{section}:ApplicationName' cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.KeysPath))
        {
            if (environment.IsProduction())
            {
                failures.Add($"'{section}:KeysPath' is required in Production.");
            }
        }
        else if (!IsWritableDirectory(options.KeysPath))
        {
            failures.Add($"'{section}:KeysPath' must be a directory the process can create and write to.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, [0]);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
