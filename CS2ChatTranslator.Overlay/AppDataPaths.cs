using System.IO;

namespace CS2ChatTranslator.Overlay;

public static class AppDataPaths
{
    private const string DataDirectoryEnvironmentVariable = "CS2_TRANSLATOR_DATA_DIR";

    public static string GetDataDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        var directory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CS2ChatTranslator")
            : Path.GetFullPath(overrideDirectory);

        Directory.CreateDirectory(directory);
        return directory;
    }
}
