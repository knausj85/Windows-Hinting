using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static System.Windows.Forms.DataFormats;

namespace WindowsHinting
{
    internal static class Program
    {
        private const string appGuid = "{B7E4F2A1-3C8D-4F6E-9A2B-1D5E7F8C0B3A}";

        [STAThread]
        static void Main()
        {
            // PROTOTYPE (#83, branch prototype/taskbar-overlay-probe): run the
            // throwaway taskbar/tray event telemetry probe instead of the app.
            // Branches before the single-instance mutex so it can run standalone.
            if (Array.Exists(Environment.GetCommandLineArgs(), a =>
                    a.Equals("--probe-taskbar", StringComparison.OrdinalIgnoreCase)))
            {
                TaskbarOverlayProbe.Run();
                return;
            }

            using (Mutex mutex = new Mutex(false, appGuid))
            {
                if (!mutex.WaitOne(0, false))
                {
                    MessageBox.Show("Windows-Hinting is already running!");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var host = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        services.AddHintOverlayServices();
                    })
                    .Build();

                using (var scope = host.Services.CreateScope())
                {
                    var controller = scope.ServiceProvider.GetRequiredService<HintController>();
                    Application.Run();
                }

                host.Dispose();
            }
        }
    }
}
