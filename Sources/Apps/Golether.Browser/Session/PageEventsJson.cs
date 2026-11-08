using System.Globalization;
using System.Text;
using System.Text.Json;
using Golether.Session;

namespace Golether.Browser.Session;

/// <summary>
/// Writes the events of a session the page shows as they come (chat, reactions, pen strokes) as JSON. The texts of
/// other people are passed as they are: the page puts them in as text, never as markup.
/// </summary>
internal static class PageEventsJson
{
    /// <summary>
    /// Writes a chat message or a reaction.
    /// </summary>
    /// <param name="entry">The message.</param>
    /// <returns>The JSON.</returns>
    public static string Chat(ChatEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Write(json =>
        {
            json.WriteString("id", entry.Id);
            json.WriteString("sender", entry.Sender.Value);
            json.WriteString("name", entry.SenderName);
            json.WriteNumber("kind", (int)entry.Kind);
            json.WriteString("text", entry.Text);
            json.WriteString("time", entry.Time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
            json.WriteBoolean("local", entry.IsLocal);
        });
    }

    /// <summary>
    /// Writes a part of a pen stroke.
    /// </summary>
    /// <param name="update">The part.</param>
    /// <returns>The JSON; the points are pairs of fractions of the picture.</returns>
    public static string Stroke(StrokeUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return Write(json =>
        {
            json.WriteString("sender", update.Sender.Value);
            json.WriteString("id", update.StrokeId);
            json.WriteNumber("phase", (int)update.Phase);
            json.WriteBoolean("local", update.IsLocal);
            json.WriteStartArray("points");
            foreach (var point in update.Points)
            {
                json.WriteNumberValue(point.X);
                json.WriteNumberValue(point.Y);
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// Writes one JSON object.
    /// </summary>
    /// <param name="body">Writes the properties.</param>
    /// <returns>The JSON text.</returns>
    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
