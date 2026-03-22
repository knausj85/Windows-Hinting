using UIAutomationClient;

namespace WindowsHinting.Services
{
    public interface IElementActivator
    {
        bool TryActivate(IUIAutomationElement element);
    }
}