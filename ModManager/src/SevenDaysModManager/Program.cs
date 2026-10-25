using System.Runtime.InteropServices;

namespace SevenDaysModManager;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
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
        Application.Run(new MainForm(settings));
        return 0;
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
