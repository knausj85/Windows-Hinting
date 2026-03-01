
using System;
using System.Windows.Forms;

namespace HintOverlay
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            using var controller = new HintController();
            Application.Run(controller.Overlay);
        }
    }
}
