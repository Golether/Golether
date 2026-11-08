using System.Text.Json;
using System.Text.Json.Serialization;

namespace Golether.Sync.Protocol;

/// <summary>
/// The JSON of the session messages made at build time: the same wire as reading them by reflection, but nothing has
/// to stay in a trimmed build (the browser version) for the reader to work.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, MaxDepth = 16)]
[JsonSerializable(typeof(SessionMessage))]
internal sealed partial class SessionJsonContext : JsonSerializerContext;
