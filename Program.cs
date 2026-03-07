using System;
using System.Windows.Forms;
using HintOverlay.Logging;
using HintOverlay.Services;

namespace HintOverlay
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Create logger
            var logger = new DebugLogger
            {
                MinimumLevel = LogLevel.Debug
            };
            
            // Create services
            var preferencesService = new PreferencesService();
            var uiaService = new UIAutomationService(logger);
            var keyboardService = new KeyboardHookService();
            var windowManager = new WindowManager();
            var overlay = new OverlayForm();
            var trayIcon = new TrayIconManager();
            
            // Create controller with dependencies
            using var controller = new HintController(
                overlay,
                uiaService,
                keyboardService,
                preferencesService,
                windowManager,
                logger,
                trayIcon);
            
            Application.Run();
        }
    }
}
