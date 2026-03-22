using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WindowsHinting
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
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
