using Windows.Win32.UI.Accessibility;

namespace WindowsHinting.Services
{
    internal interface IElementActivator
    {
        bool TryActivate(IUIAutomationElement element);
    }
}
