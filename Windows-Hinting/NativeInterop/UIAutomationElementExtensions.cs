using Windows.Win32.UI.Accessibility;

namespace WindowsHinting.NativeInterop
{
    /// <summary>
    /// String accessors for <see cref="IUIAutomationElement"/>. CsWin32 projects the
    /// <c>Cached*</c>/<c>Current*</c> string properties as raw <c>BSTR</c>s the caller would
    /// have to free, so these read through the VARIANT property getters instead, which the
    /// marshaller converts to a managed string and frees.
    /// </summary>
    internal static class UIAutomationElementExtensions
    {
        public static string? GetCachedName(this IUIAutomationElement element)
            => element.GetCachedPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId) as string;

        public static string? GetCurrentClassName(this IUIAutomationElement element)
            => element.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ClassNamePropertyId) as string;
    }
}
