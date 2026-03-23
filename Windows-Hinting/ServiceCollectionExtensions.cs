using Microsoft.Extensions.DependencyInjection;
using WindowsHinting.Configuration;
using WindowsHinting.Forms;
using WindowsHinting.Logging;
using WindowsHinting.Services;

namespace WindowsHinting
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddHintOverlayServices(this IServiceCollection services)
        {
            // Logging
            services.AddSingleton<ILogger>(sp => new DebugLogger
            {
                MinimumLevel = LogLevel.Info
            });

            // Configuration
            services.AddSingleton<WindowRuleRegistry>();

            // Core Services
            services.AddSingleton<IPreferencesService, PreferencesService>();
            services.AddSingleton<IUIAutomationService, UIAutomationService>();
            services.AddSingleton<IKeyboardHookService, KeyboardHookService>();
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddSingleton<HintStateManager>();
            services.AddSingleton<HintInputHandler>();
            services.AddSingleton<ElementActivatorChain>();
            services.AddSingleton<CommandFileService>();
            services.AddSingleton<MouseClickService>();

            // UI Components
            services.AddSingleton<OverlayForm>();
            services.AddSingleton<TrayIconManager>();

            // Application Controller
            services.AddSingleton<HintController>();

            return services;
        }
    }
}
