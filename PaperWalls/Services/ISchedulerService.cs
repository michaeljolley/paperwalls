using PaperWalls.Models;

namespace PaperWalls.Services;

public interface ISchedulerService
{
	Task StartAsync(CancellationToken cancellationToken);
	Task StopAsync(CancellationToken cancellationToken);
	DateTime? NextChangeTime { get; }
	bool? LastChangeSucceeded { get; }
	WallpaperChangeFailureReason LastChangeFailureReason { get; }
}
