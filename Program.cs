using System;
using System.Windows.Forms;
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
            
            // Create services
            var preferencesService = new PreferencesService();
            var uiaService = new UIAutomationService();
            var keyboardService = new KeyboardHookService();
            var overlay = new OverlayForm();
            var trayIcon = new TrayIconManager();
            
            // Create controller with dependencies
            var controller = new HintController(
                overlay,
                uiaService,
                keyboardService,
                preferencesService,
                trayIcon);
            
            Application.Run();
            
            // Cleanup
            controller.Dispose();
        }
    }
}
