using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace CS2ChatTranslator.Common;

public static partial class CS2LogPathResolver
{
    private const string Cs2LogRelativePath =
        @"steamapps\common\Counter-Strike Global Offensive\game\csgo\console.log";

    public static string Resolve(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath);
            if (File.Exists(expandedPath))
                return Path.GetFullPath(expandedPath);
        }

        foreach (var libraryPath in GetSteamLibraryPaths())
        {
            var logPath = Path.Combine(libraryPath, Cs2LogRelativePath);
            if (File.Exists(logPath))
                return Path.GetFullPath(logPath);
        }

        return configuredPath;
    }

    private static IEnumerable<string> GetSteamLibraryPaths()
    {
        var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPath(steamPaths, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam"));
        AddPath(steamPaths, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Steam"));

        if (OperatingSystem.IsWindows())
        {
            AddRegistryPath(steamPaths, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistryPath(steamPaths, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            AddRegistryPath(steamPaths, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        }

        var libraryPaths = new HashSet<string>(steamPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var steamPath in steamPaths)
        {
            var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
                continue;

            try
            {
                foreach (var line in File.ReadLines(libraryFile))
                {
                    var match = SteamLibraryPathPattern().Match(line);
                    if (!match.Success)
                        continue;

                    AddPath(libraryPaths, match.Groups["path"].Value.Replace(@"\\", @"\"));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return libraryPaths;
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryPath(
        ISet<string> paths,
        RegistryKey root,
        string subKeyName,
        string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyName);
            AddPath(paths, key?.GetValue(valueName) as string);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AddPath(ISet<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            paths.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryPathPattern();
}
