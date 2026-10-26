namespace SevenDaysModManager;

/// <summary>Windows shortcuts (.lnk) to this program with arguments, made through the shell's own COM object.</summary>
static class Shortcut
{
    /// <summary>Makes (or replaces) a shortcut on the desktop that runs this program with <paramref name="args"/>; returns its path.</summary>
    public static string CreateOnDesktop(string name, string args, string description)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where this program is.");
        var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Backups.Safe(name) + ".lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(lnk);
            link.TargetPath = exe;
            link.Arguments = args;
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.IconLocation = exe + ",0";
            link.Description = description;
            link.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        return lnk;
    }
}
