namespace PaperWalls.Services;

public interface ISchedulerService
{
	Task StartAsync(CancellationToken cancellationToken);
	Task StopAsync(CancellationToken cancellationToken);
	DateTime? NextChangeTime { get; }
}
