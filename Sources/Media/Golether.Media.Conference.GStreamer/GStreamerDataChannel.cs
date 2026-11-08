using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Golether.Media.Conference.GStreamer.Native;
using Golether.Transports.WebRtc;

namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// A data channel of <c>webrtcbin</c> (<c>GstWebRTCDataChannel</c>) as an <see cref="IDataChannel"/>. Its callbacks
/// come on the threads of GStreamer; messages that arrive before anybody listens are kept for the first listener.
/// </summary>
internal sealed class GStreamerDataChannel : IDataChannel
{
    /// <summary>
    /// <c>GST_WEBRTC_DATA_CHANNEL_STATE_OPEN</c>.
    /// </summary>
    private const int STATE_OPEN = 2;

    /// <summary>
    /// The unsent amount above which sends wait.
    /// </summary>
    private const ulong HIGH_WATER = 1024 * 1024;

    /// <summary>
    /// The unsent amount below which waiting sends go on.
    /// </summary>
    private const ulong LOW_WATER = 256 * 1024;

    /// <summary>
    /// The most messages kept for a listener that is not there yet.
    /// </summary>
    private const int MAX_EARLY = 256;

    /// <summary>
    /// The channel.
    /// </summary>
    private readonly nint _channel;

    /// <summary>
    /// The log.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The handle the callbacks find this object by.
    /// </summary>
    private readonly GCHandle _self;

    /// <summary>
    /// Guards the native channel and the state below.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The messages that came before the first listener.
    /// </summary>
    private readonly Queue<DataChannelMessage> _early = new();

    /// <summary>
    /// The handlers connected to the channel, disconnected before it is released.
    /// </summary>
    private readonly List<nuint> _handlers = [];

    /// <summary>
    /// Released when the unsent amount fell low.
    /// </summary>
    private TaskCompletionSource _drained = NewDrained();

    /// <summary>
    /// The listeners of the messages.
    /// </summary>
    private EventHandler<DataChannelMessage>? _received;

    /// <summary>
    /// Whether the channel is open.
    /// </summary>
    private volatile bool _open;

    /// <summary>
    /// Set once the channel is closed or released.
    /// </summary>
    private bool _ended;

    /// <summary>
    /// Initializes a new instance of the <see cref="GStreamerDataChannel"/> class.
    /// </summary>
    /// <param name="channel">The channel, with a reference this object owns.</param>
    /// <param name="logger">The log.</param>
    public unsafe GStreamerDataChannel(nint channel, ILogger logger)
    {
        _channel = channel;
        _logger = logger;
        _self = GCHandle.Alloc(this);
        Label = Signals.GetString(channel, "label") ?? string.Empty;

        var data = GCHandle.ToIntPtr(_self);
        Gst.UtilSetObjectArg(channel, "buffered-amount-low-threshold", LOW_WATER.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _handlers.Add(Gst.SignalConnectData(channel, "on-open", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnOpen, data, 0, 0));
        _handlers.Add(Gst.SignalConnectData(channel, "on-close", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClose, data, 0, 0));
        _handlers.Add(Gst.SignalConnectData(channel, "on-message-data", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnData, data, 0, 0));
        _handlers.Add(Gst.SignalConnectData(channel, "on-message-string", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnString, data, 0, 0));
        _handlers.Add(Gst.SignalConnectData(channel, "on-buffered-amount-low", (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnDrained, data, 0, 0));
        _open = Signals.GetEnum(channel, "ready-state") == STATE_OPEN;
    }

    /// <inheritdoc />
    public event EventHandler<DataChannelMessage>? MessageReceived
    {
        add
        {
            DataChannelMessage[] early;
            lock (_gate)
            {
                _received += value;
                early = [.. _early];
                _early.Clear();
            }

            foreach (var message in early)
            {
                value?.Invoke(this, message);
            }
        }

        remove
        {
            lock (_gate)
            {
                _received -= value;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <summary>
    /// Raised once when the channel opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <inheritdoc />
    public string Label { get; }

    /// <inheritdoc />
    public bool IsOpen => _open;

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (_ended || !_open)
                {
                    throw new IOException("The data channel is closed.");
                }

                if (Signals.GetUInt64(_channel, "buffered-amount") <= HIGH_WATER)
                {
                    Send(data.Span);
                    return;
                }

                if (_drained.Task.IsCompleted)
                {
                    _drained = NewDrained();
                }

                wait = _drained.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void SendText(string text)
    {
        lock (_gate)
        {
            if (_ended || !_open)
            {
                throw new IOException("The data channel is closed.");
            }

            Signals.Emit(_channel, "send-string", SignalArg.String(text));
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_ended)
            {
                return ValueTask.CompletedTask;
            }

            _ended = true;
            _open = false;
            try
            {
                Signals.Emit(_channel, "close");
            }
            catch (GstException ex)
            {
                _logger.LogDebug("A data channel did not close: {Reason}", ex.Message);
            }

            foreach (var handler in _handlers)
            {
                Gst.SignalHandlerDisconnect(_channel, handler);
            }

            Gst.GstObjectUnref(_channel);
        }

        _drained.TrySetResult();
        Closed?.Invoke(this, EventArgs.Empty);
        _self.Free();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Makes the wait for a drained channel.
    /// </summary>
    /// <returns>The source.</returns>
    private static TaskCompletionSource NewDrained() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Finds the object of a callback.
    /// </summary>
    /// <param name="data">The handle.</param>
    /// <returns>The channel, or <see langword="null"/> once it is released.</returns>
    private static GStreamerDataChannel? From(nint data)
    {
        try
        {
            return GCHandle.FromIntPtr(data).Target as GStreamerDataChannel;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Handles <c>on-open</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnOpen(nint channel, nint data)
    {
        if (From(data) is { } self && !self._ended)
        {
            self._open = true;
            self.Raise(self.Opened);
        }
    }

    /// <summary>
    /// Handles <c>on-close</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClose(nint channel, nint data)
    {
        if (From(data) is { } self && self._open)
        {
            self._open = false;
            self._drained.TrySetResult();
            self.Raise(self.Closed);
        }
    }

    /// <summary>
    /// Handles <c>on-buffered-amount-low</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDrained(nint channel, nint data) => From(data)?._drained.TrySetResult();

    /// <summary>
    /// Handles <c>on-message-data</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="bytes">The <c>GBytes</c>.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnData(nint channel, nint bytes, nint data)
    {
        if (bytes == 0 || From(data) is not { } self)
        {
            return;
        }

        nuint size;
        var pointer = Gst.BytesGetData(bytes, &size);
        if (pointer != null && size <= int.MaxValue)
        {
            self.Deliver(new DataChannelMessage(new ReadOnlySpan<byte>(pointer, (int)size).ToArray(), null));
        }
    }

    /// <summary>
    /// Handles <c>on-message-string</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="text">The UTF-8 text.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnString(nint channel, nint text, nint data)
    {
        if (text != 0 && From(data) is { } self)
        {
            self.Deliver(new DataChannelMessage(null, Marshal.PtrToStringUTF8(text) ?? string.Empty));
        }
    }

    /// <summary>
    /// Sends a binary message; the caller holds the gate.
    /// </summary>
    /// <param name="data">The message.</param>
    private unsafe void Send(ReadOnlySpan<byte> data)
    {
        nint bytes;
        fixed (byte* pointer = data)
        {
            bytes = Gst.BytesNew(pointer, (nuint)data.Length);
        }

        try
        {
            Signals.Emit(_channel, "send-data", SignalArg.Boxed(Gst.BytesGetType(), bytes));
        }
        finally
        {
            Gst.BytesUnref(bytes);
        }
    }

    /// <summary>
    /// Passes a message to the listeners, or keeps it for the first one.
    /// </summary>
    /// <param name="message">The message.</param>
    private void Deliver(DataChannelMessage message)
    {
        EventHandler<DataChannelMessage>? handlers;
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            handlers = _received;
            if (handlers is null)
            {
                if (_early.Count < MAX_EARLY)
                {
                    _early.Enqueue(message);
                }

                return;
            }
        }

        try
        {
            handlers(this, message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception must not leave a native callback: it would end the process.
            _logger.LogWarning(ex, "A message of a data channel failed");
        }
    }

    /// <summary>
    /// Raises an event without letting an exception reach native code.
    /// </summary>
    /// <param name="handler">The event.</param>
    private void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "A handler of a data channel failed");
        }
    }
}
