using System.Reflection;
using System.Runtime.InteropServices;

namespace RefreshToggle.Windows.Deployment;

internal enum DeploymentFlavor
{
    DevelopmentPortable,
    Distribution
}

internal sealed record DeploymentManifest(
    DeploymentFlavor Flavor,
    string RuntimeIdentifier,
    bool IsSelfContained,
    bool IsSingleFile,
    string? Reason)
{
    private const string MetadataPrefix = "RefreshToggle.";

    public bool MayMutateInstallation =>
        Flavor == DeploymentFlavor.Distribution && IsSelfContained && IsSingleFile;

    public static DeploymentManifest InspectCurrentProcess(Assembly? entryAssembly = null)
    {
        entryAssembly ??= Assembly.GetEntryAssembly();
        if (entryAssembly is null)
        {
            return Portable("The entry assembly could not be determined.");
        }

        var metadata = entryAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal))
            .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.Ordinal);

        var distribution = IsTrue(metadata, MetadataPrefix + "DistributionBuild");
        var declaredSingleFile = IsTrue(metadata, MetadataPrefix + "SingleFile");
        var selfContained = IsTrue(metadata, MetadataPrefix + "SelfContained");
        metadata.TryGetValue(MetadataPrefix + "RuntimeIdentifier", out var declaredRid);

        // Assembly.Location is documented to be empty for assemblies embedded in a
        // single-file bundle. Requiring both the build marker and this runtime fact
        // prevents a normal source/apphost build from installing itself accidentally.
        var actuallyBundled = string.IsNullOrEmpty(entryAssembly.Location);
        var processPath = Environment.ProcessPath;
        var executableLooksValid =
            OperatingSystem.IsWindows() &&
            !string.IsNullOrWhiteSpace(processPath) &&
            string.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase);

        var runtimeRid = RuntimeInformation.RuntimeIdentifier;
        var ridMatches = !string.IsNullOrWhiteSpace(declaredRid) &&
                         RuntimeIdentifiersMatch(declaredRid, runtimeRid);

        if (!distribution || !declaredSingleFile || !selfContained || !actuallyBundled ||
            !executableLooksValid || !ridMatches)
        {
            var failedChecks = new List<string>();
            if (!distribution) failedChecks.Add("distribution marker missing");
            if (!declaredSingleFile) failedChecks.Add("single-file marker missing");
            if (!selfContained) failedChecks.Add("self-contained marker missing");
            if (!actuallyBundled) failedChecks.Add("entry assembly is not bundled");
            if (!executableLooksValid) failedChecks.Add("current process is not a Windows executable");
            if (!ridMatches) failedChecks.Add("declared RID does not match the runtime");

            return new DeploymentManifest(
                DeploymentFlavor.DevelopmentPortable,
                declaredRid ?? runtimeRid,
                selfContained,
                declaredSingleFile && actuallyBundled,
                "Portable mode: " + string.Join(", ", failedChecks) + ".");
        }

        return new DeploymentManifest(
            DeploymentFlavor.Distribution,
            declaredRid!,
            IsSelfContained: true,
            IsSingleFile: true,
            Reason: null);
    }

    private static DeploymentManifest Portable(string reason) => new(
        DeploymentFlavor.DevelopmentPortable,
        RuntimeInformation.RuntimeIdentifier,
        IsSelfContained: false,
        IsSingleFile: false,
        reason);

    private static bool IsTrue(Dictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private static bool RuntimeIdentifiersMatch(string declaredRid, string runtimeRid)
    {
        if (string.Equals(declaredRid, runtimeRid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A self-contained win-x64/win-arm64 executable may report a versioned RID
        // such as win10-x64. Architecture equality plus a Windows prefix is the
        // security-relevant constraint here.
        var declaredArchitecture = declaredRid.Split('-').LastOrDefault();
        var runtimeArchitecture = runtimeRid.Split('-').LastOrDefault();
        return declaredRid.StartsWith("win", StringComparison.OrdinalIgnoreCase) &&
               runtimeRid.StartsWith("win", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(declaredArchitecture, runtimeArchitecture, StringComparison.OrdinalIgnoreCase);
    }
}
