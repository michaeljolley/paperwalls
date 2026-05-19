namespace PaperWalls.Services;

public interface IStartupManager
{
    void SetStartWithWindows(bool enabled);
    bool IsStartWithWindows();
}
