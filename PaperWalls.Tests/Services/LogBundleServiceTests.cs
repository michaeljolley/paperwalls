using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PaperWalls.Services;

namespace PaperWalls.Tests.Services;

public class LogBundleServiceTests : IDisposable
{
    private readonly ILogger<LogBundleService> _logger;
    private readonly string _testLogsDir;
    private readonly string _testOutputDir;

    public LogBundleServiceTests()
    {
        _logger = Substitute.For<ILogger<LogBundleService>>();
        _testLogsDir = Path.Combine(Path.GetTempPath(), $"LogBundleTest_Logs_{Guid.NewGuid():N}");
        _testOutputDir = Path.Combine(Path.GetTempPath(), $"LogBundleTest_Output_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLogsDir);
        Directory.CreateDirectory(_testOutputDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_testLogsDir)) Directory.Delete(_testLogsDir, true);
        if (Directory.Exists(_testOutputDir)) Directory.Delete(_testOutputDir, true);
    }

    [Fact]
    public async Task CreateBugReportAsync_WithLogFiles_CreatesZipContainingAllLogs()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testLogsDir, "app-20260101.log"), "log content 1");
        File.WriteAllText(Path.Combine(_testLogsDir, "app-20260102.log"), "log content 2");

        var service = new LogBundleService(_logger, _testLogsDir, _testOutputDir);

        // Act
        var zipPath = await service.CreateBugReportAsync();

        // Assert
        zipPath.Should().StartWith(_testOutputDir);
        File.Exists(zipPath).Should().BeTrue();

        using var archive = ZipFile.OpenRead(zipPath);
        archive.Entries.Should().HaveCount(2);
        archive.Entries.Select(e => e.Name).Should().Contain("app-20260101.log");
        archive.Entries.Select(e => e.Name).Should().Contain("app-20260102.log");
    }

    [Fact]
    public async Task CreateBugReportAsync_WithNoLogFiles_CreatesEmptyZip()
    {
        // Arrange — logs dir exists but is empty
        var service = new LogBundleService(_logger, _testLogsDir, _testOutputDir);

        // Act
        var zipPath = await service.CreateBugReportAsync();

        // Assert
        zipPath.Should().StartWith(_testOutputDir);
        File.Exists(zipPath).Should().BeTrue();

        using var archive = ZipFile.OpenRead(zipPath);
        archive.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBugReportAsync_WithNonExistentLogsDir_CreatesDirectoryAndEmptyZip()
    {
        // Arrange — use a path that doesn't exist yet
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"LogBundleTest_Missing_{Guid.NewGuid():N}");
        var service = new LogBundleService(_logger, nonExistentDir, _testOutputDir);

        try
        {
            // Act
            var zipPath = await service.CreateBugReportAsync();

            // Assert
            Directory.Exists(nonExistentDir).Should().BeTrue("service should create missing logs directory");
            File.Exists(zipPath).Should().BeTrue();

            using var archive = ZipFile.OpenRead(zipPath);
            archive.Entries.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(nonExistentDir)) Directory.Delete(nonExistentDir, true);
        }
    }

    [Fact]
    public async Task CreateBugReportAsync_WithLockedFile_SkipsLockedFileAndIncludesOthers()
    {
        // Arrange
        var goodFile = Path.Combine(_testLogsDir, "good.log");
        var lockedFile = Path.Combine(_testLogsDir, "locked.log");
        File.WriteAllText(goodFile, "good content");
        File.WriteAllText(lockedFile, "locked content");

        // Lock the file exclusively so the service can't read it
        using var lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var service = new LogBundleService(_logger, _testLogsDir, _testOutputDir);

        // Act
        var zipPath = await service.CreateBugReportAsync();

        // Assert — locked file skipped, good file included
        using var archive = ZipFile.OpenRead(zipPath);
        archive.Entries.Should().HaveCount(1);
        archive.Entries[0].Name.Should().Be("good.log");
    }
}
