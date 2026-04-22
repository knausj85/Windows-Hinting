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
            // Logging. Minimum level and file logging can be forced via
            // preferences.json (see HintOverlayOptions.Logging). Falls back to
            // Info / file logging off when the preferences file is missing or
            // the Logging section is unset.
            services.AddSingleton<ILogger>(sp =>
            {
                var prefs = sp.GetRequiredService<IPreferencesService>().Load();
                var logger = new DebugLogger
                {
                    MinimumLevel = prefs.Logging?.MinimumLevel ?? LogLevel.Info,
                };
                if (prefs.Logging?.FileLoggingEnabled == true)
                {
                    logger.FileLoggingEnabled = true;
                }
                return logger;
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
            //services.AddSingleton<NamedPipeService>();
            services.AddSingleton<MouseClickService>();
            services.AddSingleton<StartupService>();
            services.AddSingleton<UpdateService>();

            // UI Components
            services.AddSingleton<OverlayForm>();
            services.AddSingleton<TrayIconManager>();

            // Application Controller
            services.AddSingleton<HintController>();

            return services;
        }
    }
}
