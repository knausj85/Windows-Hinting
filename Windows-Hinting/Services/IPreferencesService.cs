using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    internal interface IPreferencesService
    {
        HintOverlayOptions Load();
        void Save(HintOverlayOptions options);
        bool Exists();
    }
}
