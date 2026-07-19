namespace RefreshToggle.Windows.Deployment;

internal sealed record InstallationPaths(string InstallDirectory, string InstalledExecutablePath)
{
    public const string ApplicationDirectoryName = "RefreshToggle";
    public const string ExecutableName = "RefreshToggle.exe";

    public static InstallationPaths ForCurrentUser()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The LocalApplicationData directory is unavailable.");
        }

        var directory = Path.GetFullPath(Path.Combine(localAppData, ApplicationDirectoryName));
        return new InstallationPaths(directory, Path.Combine(directory, ExecutableName));
    }
}
