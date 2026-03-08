using HintOverlay.Logging;
using HintOverlay.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HintOverlay
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
            
            // Core Services
            services.AddSingleton<IPreferencesService, PreferencesService>();
            services.AddSingleton<IUIAutomationService, UIAutomationService>();
            services.AddSingleton<IKeyboardHookService, KeyboardHookService>();
            services.AddSingleton<IWindowManager, WindowManager>();
            
            // UI Components
            services.AddSingleton<OverlayForm>();
            services.AddSingleton<TrayIconManager>();
            
            // Application Controller
            services.AddSingleton<HintController>();
            
            return services;
        }
    }
}