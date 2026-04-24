namespace WinPaperWalls.Services;

internal interface ILogBundleService
{
	Task<string> CreateBugReportAsync();
}
