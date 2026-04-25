namespace PaperWalls.Services;

internal interface ILogBundleService
{
	Task<string> CreateBugReportAsync();
}
