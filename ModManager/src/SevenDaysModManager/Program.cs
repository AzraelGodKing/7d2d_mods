using System.Runtime.InteropServices;

namespace SevenDaysModManager;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The downloaded program installing itself once the old one has closed (see SelfUpdate.HandOff).
        if (args.Length > 0 && args[0] == SelfUpdate.FinishArg)
            return SelfUpdate.Finish(args[1..], message =>
                MessageBox.Show(message, "7 Days Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning));

        var settings = AppSettings.Load();
        if (Cli.IsCommand(args))
        {
            // A Windows app has no console of its own; write to the one it was started from.
            AttachConsole(-1);
            var o = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            o.WriteLine();
            return Cli.Run(args, o, settings);
        }

        ApplicationConfiguration.Initialize();
        Theme.TextScale = settings.TextScale is >= 1f and <= 2f ? settings.TextScale : 1f;
        Theme.Set(settings.Theme);
        var u = Array.IndexOf(args, SelfUpdate.UpdatedFromArg);
        var updatedFrom = u >= 0 && u + 1 < args.Length ? args[u + 1] : null;
        // What an earlier update left behind: the old exe, and downloads already installed.
        Task.Run(() =>
        {
            try
            {
                if (SelfUpdate.RunningExe() is { } exe) SelfUpdate.CleanUp(exe);
                SelfUpdate.CleanStaging(UpdateCheck.CurrentVersion);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        });
        Application.Run(new MainForm(settings, updatedFrom));
        return 0;
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
