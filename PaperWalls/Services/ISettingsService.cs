using PaperWalls.Models;

namespace PaperWalls.Services;

public interface ISettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    event EventHandler? SettingsChanged;
}
