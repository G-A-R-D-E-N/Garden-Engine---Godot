using System;
using System.Windows.Forms;

namespace GardenDevLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // --check/--sync run the same engine without a window, which is how a
        // scheduled task uses it and how the git behaviour is checked on a real
        // repository.
        if (args.Length >= 2 && (args[0] == "--check" || args[0] == "--sync"))
        {
            return Headless.Run(args[0] == "--check", args[1], args.Length >= 3 ? args[2] : "main");
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
