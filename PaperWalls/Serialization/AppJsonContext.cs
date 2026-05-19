using System.Text.Json;
using System.Text.Json.Serialization;
using PaperWalls.Models;

namespace PaperWalls.Serialization;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<GitHubContentItem>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class AppJsonContext : JsonSerializerContext;
