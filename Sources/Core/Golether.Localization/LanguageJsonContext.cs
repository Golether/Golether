using System.Text.Json.Serialization;

namespace Golether.Localization;

/// <summary>
/// The JSON of the language packs made at build time, so a trimmed build (the browser version) reads them too.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LanguageJsonContext : JsonSerializerContext;
