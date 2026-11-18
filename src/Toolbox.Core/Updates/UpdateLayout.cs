namespace Toolbox.Updates;

public static class UpdateLayout
{
    public const string WorkDirectoryName = ".toolbox-update";
    public const string OldFileMarker = ".toolbox-old";

    public static string WorkDirectory(string installDirectory) => Path.Combine(installDirectory, WorkDirectoryName);

    public static string StagingDirectory(string installDirectory) => Path.Combine(WorkDirectory(installDirectory), "staged");

    public static string MarkerPath(string installDirectory) => Path.Combine(WorkDirectory(installDirectory), "staged.json");

    public static string JournalPath(string installDirectory) => Path.Combine(WorkDirectory(installDirectory), "apply.journal");
}
