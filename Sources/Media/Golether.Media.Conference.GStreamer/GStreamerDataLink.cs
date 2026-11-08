using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Golether.Media.Conference.GStreamer.Native;
using Golether.Transports.WebRtc;

namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// A WebRTC connection with data channels only, made by a <c>webrtcbin</c> of its own: the transport of a session with
/// a browser. <c>webrtcbin</c> does not compare the DTLS certificate of the other side with the fingerprint of its
/// description, so this class does: no channel is handed out before they match, and a mismatch fails the connection.
/// </summary>
internal sealed class GStreamerDataLink : IDataLink
{
    /// <summary>
    /// The pipeline: one <c>webrtcbin</c>, all sections bundled. A lone element would be parsed into that element
    /// itself (<c>webrtcbin</c> is a bin), so an idle sink beside it makes the parser build a pipeline around both.
    /// </summary>
    private const string DESCRIPTION = "webrtcbin name=webrtc bundle-policy=max-bundle fakesink name=idle async=false";

    /// <summary>
    /// The result of a promise that <c>webrtcbin</c> answered.
    /// </summary>
    private const int PROMISE_REPLIED = 2;

    /// <summary>
    /// <c>GST_WEBRTC_SDP_TYPE_OFFER</c>.
    /// </summary>
    private const int SDP_OFFER = 1;

    /// <summary>
    /// <c>GST_WEBRTC_SDP_TYPE_ANSWER</c>.
    /// </summary>
    private const int SDP_ANSWER = 3;

    /// <summary>
    /// <c>GST_WEBRTC_ICE_CONNECTION_STATE_FAILED</c>.
    /// </summary>
    private const int ICE_FAILED = 4;

    /// <summary>
    /// <c>GST_WEBRTC_DTLS_TRANSPORT_STATE_FAILED</c>.
    /// </summary>
    private const int DTLS_FAILED = 2;

    /// <summary>
    /// <c>GST_WEBRTC_DTLS_TRANSPORT_STATE_CONNECTED</c>.
    /// </summary>
    private const int DTLS_CONNECTED = 4;

    /// <summary>
    /// How often the DTLS state is read until the certificate is checked.
    /// </summary>
    private static readonly TimeSpan _verificationInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The pipeline.
    /// </summary>
    private readonly GstPipeline _pipeline;

    /// <summary>
    /// The log.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The handle the callbacks find this object by.
    /// </summary>
    private readonly GCHandle _self;

    /// <summary>
    /// Guards the native objects and the state below.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Lets one step of the negotiation run at a time.
    /// </summary>
    private readonly SemaphoreSlim _negotiation = new(1, 1);

    /// <summary>
    /// Completes when the certificate of the other side matched.
    /// </summary>
    private readonly TaskCompletionSource _verified = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The channels of this connection, released with it.
    /// </summary>
    private readonly List<GStreamerDataChannel> _channels = [];

    /// <summary>
    /// The fingerprint of the certificate the other side announced.
    /// </summary>
    private byte[]? _expectedFingerprint;

    /// <summary>
    /// Set once the connection failed.
    /// </summary>
    private int _failed;

    /// <summary>
    /// Set once the connection is released.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GStreamerDataLink"/> class.
    /// </summary>
    /// <param name="stunServer">The STUN server (<c>stun://host:port</c>), or <see langword="null"/>.</param>
    /// <param name="turnServer">The TURN server, or <see langword="null"/>.</param>
    /// <param name="logger">The log.</param>
    public unsafe GStreamerDataLink(string? stunServer, string? turnServer, ILogger logger)
    {
        _logger = logger;
        _pipeline = new GstPipeline("data link", DESCRIPTION, logger);
        _pipeline.Failed += (_, error) => Fail("pipeline: " + error);
        _self = GCHandle.Alloc(this);
        var webrtc = _pipeline.Element("webrtc");
        if (!string.IsNullOrEmpty(stunServer))
        {
            Gst.UtilSetObjectArg(webrtc, "stun-server", stunServer);
        }

        if (!string.IsNullOrEmpty(turnServer))
        {
            Gst.UtilSetObjectArg(webrtc, "turn-server", turnServer);
        }

        var data = GCHandle.ToIntPtr(_self);
        Gst.SignalConnectData(webrtc, "on-ice-candidate", (nint)(delegate* unmanaged[Cdecl]<nint, uint, nint, nint, void>)&OnIceCandidate, data, 0, 0);
        Gst.SignalConnectData(webrtc, "notify::ice-connection-state", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnIceConnectionState, data, 0, 0);
        Gst.SignalConnectData(webrtc, "on-data-channel", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnDataChannel, data, 0, 0);
        _pipeline.Play();
        _ = Task.Run(VerifyAsync);
    }

    /// <inheritdoc />
    public event EventHandler<IceCandidate>? CandidateReady;

    /// <inheritdoc />
    public event EventHandler<IDataChannel>? ChannelOpened;

    /// <inheritdoc />
    public event EventHandler? Failed;

    /// <inheritdoc />
    public string? LocalDescription { get; private set; }

    /// <inheritdoc />
    public string? RemoteDescription { get; private set; }

    /// <inheritdoc />
    public async Task<IDataChannel> OpenChannelAsync(string label, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(label);
        GStreamerDataChannel channel;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed)
            {
                throw new IOException("The connection is closed.");
            }

            var native = Signals.EmitForObject(_pipeline.Element("webrtc"), "create-data-channel",
                SignalArg.String(label), SignalArg.Boxed(Gst.StructureGetType(), 0));
            if (native == 0)
            {
                throw new IOException("The data channel was not created.");
            }

            channel = new GStreamerDataChannel(native, _logger);
            channel.Opened += (_, _) => opened.TrySetResult();
            channel.Closed += (_, _) => opened.TrySetException(new IOException("The data channel closed."));
            _channels.Add(channel);
        }

        if (channel.IsOpen)
        {
            opened.TrySetResult();
        }

        try
        {
            await opened.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _verified.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return channel;
        }
        catch
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<string> CreateOfferAsync(CancellationToken cancellationToken)
        => NegotiateAsync(() => LocalDescription = Describe("create-offer", "offer"), cancellationToken);

    /// <inheritdoc />
    public Task<string> AnswerAsync(string offer, CancellationToken cancellationToken)
        => NegotiateAsync(() =>
        {
            SetRemoteDescription(SDP_OFFER, offer);
            return LocalDescription = Describe("create-answer", "answer");
        }, cancellationToken);

    /// <inheritdoc />
    public Task ApplyAnswerAsync(string answer, CancellationToken cancellationToken)
        => NegotiateAsync(() =>
        {
            SetRemoteDescription(SDP_ANSWER, answer);
            return answer;
        }, cancellationToken);

    /// <inheritdoc />
    public void AddCandidate(IceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            if (!_disposed)
            {
                Signals.Emit(_pipeline.Element("webrtc"), "add-ice-candidate", SignalArg.UInt(candidate.Line), SignalArg.String(candidate.Candidate));
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        GStreamerDataChannel[] channels;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            channels = [.. _channels];
            _channels.Clear();
        }

        foreach (var channel in channels)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        lock (_gate)
        {
            _pipeline.Dispose();
        }

        _verified.TrySetException(new IOException("The connection is closed."));
        _negotiation.Dispose();
        _self.Free();
    }

    /// <summary>
    /// Finds the object of a callback.
    /// </summary>
    /// <param name="data">The handle.</param>
    /// <returns>The connection, or <see langword="null"/> once it is released.</returns>
    private static GStreamerDataLink? From(nint data)
    {
        try
        {
            return GCHandle.FromIntPtr(data).Target as GStreamerDataLink;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Handles <c>on-ice-candidate</c>.
    /// </summary>
    /// <param name="element">The webrtcbin.</param>
    /// <param name="line">The m-line index.</param>
    /// <param name="candidate">The candidate text.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnIceCandidate(nint element, uint line, nint candidate, nint data)
    {
        if (From(data) is not { _disposed: false } self)
        {
            return;
        }

        var text = Marshal.PtrToStringUTF8(candidate) ?? string.Empty;
        if (text.Length > 0)
        {
            self.Raise(() => self.CandidateReady?.Invoke(self, new IceCandidate(line, text)));
        }
    }

    /// <summary>
    /// Handles a change of <c>ice-connection-state</c>: a failed search is final.
    /// </summary>
    /// <param name="element">The webrtcbin.</param>
    /// <param name="property">The property.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnIceConnectionState(nint element, nint property, nint data)
    {
        if (From(data) is { _disposed: false } self && Signals.GetEnum(element, "ice-connection-state") == ICE_FAILED)
        {
            self.Fail("no network path");
        }
    }

    /// <summary>
    /// Handles <c>on-data-channel</c>: a channel the other side opened is handed out once the connection is verified.
    /// </summary>
    /// <param name="element">The webrtcbin.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="data">The handle.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataChannel(nint element, nint channel, nint data)
    {
        if (From(data) is not { } self)
        {
            return;
        }

        GStreamerDataChannel wrapped;
        lock (self._gate)
        {
            if (self._disposed)
            {
                return;
            }

            wrapped = new GStreamerDataChannel(Gst.ObjectRef(channel), self._logger);
            self._channels.Add(wrapped);
        }

        _ = self.HandOverAsync(wrapped);
    }

    /// <summary>
    /// Hands a channel of the other side out once it is open and the connection verified.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>A task that completes when the channel is handed out or dropped.</returns>
    private async Task HandOverAsync(GStreamerDataChannel channel)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Opened += (_, _) => opened.TrySetResult();
        channel.Closed += (_, _) => opened.TrySetException(new IOException("The data channel closed."));
        if (channel.IsOpen)
        {
            opened.TrySetResult();
        }

        try
        {
            await opened.Task.ConfigureAwait(false);
            await _verified.Task.ConfigureAwait(false);
        }
        catch (IOException)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            return;
        }

        Raise(() => ChannelOpened?.Invoke(this, channel));
    }

    /// <summary>
    /// Runs a step of the negotiation on a thread of its own: the promises of <c>webrtcbin</c> are waited for.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description the step gives.</returns>
    private async Task<string> NegotiateAsync(Func<string> step, CancellationToken cancellationToken)
    {
        await _negotiation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                lock (_gate)
                {
                    return _disposed ? throw new IOException("The connection is closed.") : step();
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is GstException or FormatException)
        {
            throw new IOException("The negotiation failed: " + ex.Message, ex);
        }
        finally
        {
            _negotiation.Release();
        }
    }

    /// <summary>
    /// Makes a local description and sets it; the caller holds the gate.
    /// </summary>
    /// <param name="action"><c>create-offer</c> or <c>create-answer</c>.</param>
    /// <param name="field">The field of the reply.</param>
    /// <returns>The SDP.</returns>
    private unsafe string Describe(string action, string field)
    {
        var webrtc = _pipeline.Element("webrtc");
        var promise = Gst.PromiseNew();
        try
        {
            Signals.Emit(webrtc, action, SignalArg.Boxed(Gst.StructureGetType(), 0), SignalArg.Boxed(Gst.PromiseGetType(), promise));
            if (WaitForPromise(promise) != PROMISE_REPLIED)
            {
                throw new GstException($"{action} was not answered.");
            }

            var reply = Gst.PromiseGetReply(promise);
            var value = reply == 0 ? null : Gst.StructureGetValue(reply, field);
            var description = value == null ? 0 : Gst.ValueGetBoxed(value);
            if (description == 0)
            {
                throw new GstException($"{action} returned no description.");
            }

            var sdp = Gst.TakeString(Gst.SdpMessageAsText(Marshal.ReadIntPtr(description, sizeof(nint))));
            Signals.Emit(webrtc, "set-local-description",
                SignalArg.Boxed(Gst.SessionDescriptionGetType(), description), SignalArg.Boxed(Gst.PromiseGetType(), 0));
            return sdp;
        }
        finally
        {
            Gst.MiniObjectUnref(promise);
        }
    }

    /// <summary>
    /// Sets the description of the other side and remembers the fingerprint it announces; the caller holds the gate.
    /// </summary>
    /// <param name="type">The SDP type.</param>
    /// <param name="text">The SDP.</param>
    /// <exception cref="FormatException">The SDP is invalid or announces no single fingerprint.</exception>
    private unsafe void SetRemoteDescription(int type, string text)
    {
        var fingerprint = WebRtcPeer.ParseFingerprint(text);
        nint message = 0;
        if (Gst.SdpMessageNewFromText(text, &message) != 0 || message == 0)
        {
            throw new FormatException("The SDP is invalid.");
        }

        _expectedFingerprint = fingerprint;
        RemoteDescription = text;
        var description = Gst.SessionDescriptionNew(type, message);
        var promise = Gst.PromiseNew();
        try
        {
            Signals.Emit(_pipeline.Element("webrtc"), "set-remote-description",
                SignalArg.Boxed(Gst.SessionDescriptionGetType(), description), SignalArg.Boxed(Gst.PromiseGetType(), promise));
            if (WaitForPromise(promise) != PROMISE_REPLIED)
            {
                throw new GstException("set-remote-description was not answered.");
            }
        }
        finally
        {
            Gst.MiniObjectUnref(promise);
            Gst.SessionDescriptionFree(description);
        }
    }

    /// <summary>
    /// Waits for a promise of <c>webrtcbin</c> no longer than a step of the negotiation may take.
    /// </summary>
    /// <param name="promise">The promise.</param>
    /// <returns>The result of the promise.</returns>
    private static int WaitForPromise(nint promise)
    {
        var wait = Task.Run(() => Gst.PromiseWait(promise));
        if (!wait.Wait(ConnectionRules.NegotiationTimeout))
        {
            Gst.PromiseInterrupt(promise);
            wait.Wait();
        }

        return wait.Result;
    }

    /// <summary>
    /// Reads the DTLS state until the connection is made, then compares the certificate of the other side with the
    /// fingerprint it announced.
    /// </summary>
    /// <returns>A task that completes when the certificate is checked or the connection is gone.</returns>
    private async Task VerifyAsync()
    {
        while (!_disposed && Volatile.Read(ref _failed) == 0)
        {
            await Task.Delay(_verificationInterval, TimeProvider.System).ConfigureAwait(false);
            var (state, pem) = ReadDtlsState();
            if (state == DTLS_FAILED)
            {
                Fail("DTLS failed");
                return;
            }

            if (state != DTLS_CONNECTED || _expectedFingerprint is not { } expected)
            {
                continue;
            }

            var actual = WebRtcPeer.ComputeFingerprint(pem);
            if (actual is not null && CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                _verified.TrySetResult();
            }
            else
            {
                _logger.LogWarning("The DTLS certificate of a data connection does not match the signalled fingerprint; disconnecting");
                Fail("certificate mismatch");
            }

            return;
        }
    }

    /// <summary>
    /// Reads the state and the remote certificate of the DTLS transport under the SCTP association.
    /// </summary>
    /// <returns>The state and, once connected, the certificate in PEM.</returns>
    private (int State, string? Pem) ReadDtlsState()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return (-1, null);
            }

            nint sctp = 0, transport = 0;
            try
            {
                sctp = Signals.GetObject(_pipeline.Element("webrtc"), "sctp-transport");
                transport = sctp == 0 ? 0 : Signals.GetObject(sctp, "transport");
                if (transport == 0)
                {
                    return (-1, null);
                }

                var state = Signals.GetEnum(transport, "state");
                return (state, state == DTLS_CONNECTED ? Signals.GetString(transport, "remote-certificate") : null);
            }
            finally
            {
                foreach (var instance in new[] { transport, sctp })
                {
                    if (instance != 0)
                    {
                        Gst.GstObjectUnref(instance);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Fails the connection once.
    /// </summary>
    /// <param name="reason">Why, for the log.</param>
    private void Fail(string reason)
    {
        if (_disposed || Interlocked.Exchange(ref _failed, 1) != 0)
        {
            return;
        }

        _logger.LogInformation("A WebRTC data connection failed: {Reason}", reason);
        _verified.TrySetException(new IOException("The connection failed: " + reason));
        Raise(() => Failed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Runs a handler without letting an exception reach native code.
    /// </summary>
    /// <param name="action">The handler.</param>
    private void Raise(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "A handler of a data connection failed");
        }
    }
}
