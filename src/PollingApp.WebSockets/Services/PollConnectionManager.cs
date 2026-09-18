using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace PollingApp.WebSockets.Services;

/// <summary>
/// In-memory registry of every open WebSocket, grouped by the poll it is
/// subscribed to. This is the piece a library like SignalR would normally
/// hide from you: SignalR's "groups" concept is effectively this class, plus
/// a message-framing protocol on top of raw frames. Here it's just a
/// dictionary of sockets and a broadcast loop.
///
/// Thread-safety notes:
/// - <see cref="ConcurrentDictionary{TKey,TValue}"/> keeps the per-poll map itself
///   safe for concurrent GetOrAdd/TryGetValue calls from many request threads.
/// - The <see cref="List{WebSocket}"/> stored per poll is NOT thread-safe on its
///   own, so every read or mutation of a given list is done under a lock on
///   that list instance. Because each poll gets its own list, connect/disconnect/
///   broadcast traffic for different polls never contends on the same lock.
/// </summary>
public class PollConnectionManager
{
    private readonly ConcurrentDictionary<string, List<WebSocket>> _connectionsByPollId = new();

    /// <summary>Registers a socket as subscribed to a poll's live updates.</summary>
    public void AddConnection(string pollId, WebSocket socket)
    {
        var sockets = _connectionsByPollId.GetOrAdd(pollId, static _ => new List<WebSocket>());
        lock (sockets)
        {
            sockets.Add(socket);
        }
    }

    /// <summary>Unregisters a socket, e.g. once it closes or errors out.</summary>
    public void RemoveConnection(string pollId, WebSocket socket)
    {
        if (_connectionsByPollId.TryGetValue(pollId, out var sockets))
        {
            lock (sockets)
            {
                sockets.Remove(socket);
            }
        }
    }

    /// <summary>Number of sockets currently subscribed to a poll (used for diagnostics/UI).</summary>
    public int ConnectionCount(string pollId)
    {
        if (_connectionsByPollId.TryGetValue(pollId, out var sockets))
        {
            lock (sockets)
            {
                return sockets.Count;
            }
        }

        return 0;
    }

    /// <summary>
    /// Sends a JSON payload to every open socket currently subscribed to the given
    /// poll. Dead or non-open sockets are skipped here and get cleaned up by their
    /// own receive loop in the WebSocket endpoint (see Endpoints/WebSocketEndpoints.cs).
    /// </summary>
    public async Task BroadcastAsync(string pollId, string jsonPayload, CancellationToken cancellationToken = default)
    {
        if (!_connectionsByPollId.TryGetValue(pollId, out var sockets))
        {
            return;
        }

        List<WebSocket> targets;
        lock (sockets)
        {
            // Snapshot under the lock, then send outside it: SendAsync can be slow
            // (network I/O) and we don't want to hold the lock for the whole poll's
            // connection list while that happens.
            targets = sockets.Where(s => s.State == WebSocketState.Open).ToList();
        }

        if (targets.Count == 0)
        {
            return;
        }

        var buffer = Encoding.UTF8.GetBytes(jsonPayload);
        var segment = new ArraySegment<byte>(buffer);

        var sendTasks = targets.Select(socket => SendSafelyAsync(socket, segment, cancellationToken));
        await Task.WhenAll(sendTasks);
    }

    private static async Task SendSafelyAsync(WebSocket socket, ArraySegment<byte> payload, CancellationToken cancellationToken)
    {
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        catch (Exception)
        {
            // A broken/racing socket here just means the client is gone or going;
            // its receive loop will notice and remove it. Broadcasting is
            // best-effort and must never fault the caller (the vote request).
        }
    }
}
