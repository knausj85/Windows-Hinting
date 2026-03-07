using UIAutomationClient;

namespace HintOverlay.Services
{
    public interface IElementActivator
    {
        bool TryActivate(IUIAutomationElement element);
    }
}