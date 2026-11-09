using System.Text.Json.Serialization;

namespace IacAgent.Host.Recording;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(RunDocument))]
internal sealed partial class RunJsonContext : JsonSerializerContext;
