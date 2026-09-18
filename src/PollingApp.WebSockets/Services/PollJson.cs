using System.Text.Json;

namespace PollingApp.WebSockets.Services;

/// <summary>
/// Single shared JSON serializer configuration used for WebSocket frames, so the
/// payloads pushed over the socket use the same casing (camelCase) as the
/// Minimal API JSON responses the demo page also consumes.
/// </summary>
public static class PollJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
