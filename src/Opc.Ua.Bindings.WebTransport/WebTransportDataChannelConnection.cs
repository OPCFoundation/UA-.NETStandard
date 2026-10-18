// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;
using DataChannelCarrierReservation = Opc.Ua.DataChannels.DataChannelApplicationBudget.DataChannelCarrierReservation;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Owns one exact native WebTransport session and its common flow context.
    /// HTTP/3 framing belongs to the native engine; records, readiness and resource authorization belong to Core.
    /// </summary>
    public sealed partial class WebTransportDataChannelConnection : IDataChannelCarrier, IAsyncDisposable
    {
        /// <summary>
        /// Adopts authenticated transport, real critical stream allocations and pre-admitted resources.
        /// </summary>
        private WebTransportDataChannelConnection(
            NativeQuicConnection native,
            Http3Handle handle,
            DataChannelCarrierReservation reservation,
            Uri endpointUrl,
            WebTransportDataChannelOptions options,
            IWebTransportConnectAuthorizer? authorizer,
            TimeProvider clock)
        {
            NativeConnection = native;
            m_handle = handle;
            m_reservation = reservation;
            EndpointUrl = endpointUrl;
            m_options = options;
            m_authorizer = authorizer;
            m_clock = clock;
            m_lifetime = m_shutdown.Token;
            m_streamSends = new SemaphoreSlim(options.ConcurrentStreamSends, options.ConcurrentStreamSends);
            Identity = new DataChannelPeerIdentity(
                native.Credentials.Credentials.ApplicationUri, native.Peer.ApplicationUri,
                native.Credentials.LocalKeyThumbprint, native.Peer.SpkiSha256,
                DataChannelProtectionMode.Confidential, native.Peer.IsClient);
        }

        /// <summary>
        /// Gets the common application facade after HELLO. No flow is opened implicitly.
        /// </summary>
        /// <exception cref="InvalidOperationException">The common context has not been attached.</exception>
        public DataChannelContext Context => m_context ??
            throw new InvalidOperationException("The common data context is not attached.");

        /// <summary>
        /// Gets the owned transport for diagnostics only. Do not independently receive, send or release its streams.
        /// </summary>
        public NativeQuicConnection NativeConnection { get; }

        /// <summary>
        /// Gets the exact native data endpoint, not a discovery or Session endpoint.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <summary>
        /// Gets completion of common/native/H3/host drainage, including spontaneous transport loss.
        /// </summary>
        public Task Completion => m_completed.Task;

        /// <inheritdoc/>
        public DataChannelCarrierKind Kind => DataChannelCarrierKind.WebTransport;

        /// <inheritdoc/>
        public DataChannelPeerIdentity Identity { get; }

        /// <inheritdoc/>
        public int MaxRecordSize => m_options.MaxRecordSize;

        /// <inheritdoc/>
        public int MaxDatagramSize => Math.Max(0,
            Math.Min(NativeConnection.MaxDatagramSize, Math.Min(m_options.Http3OutputBytes, 65536)) - 1);

        /// <inheritdoc/>
        public ulong ManagementStreamId => m_management?.Stream.StreamId ??
            throw new InvalidOperationException("The initiator's WebTransport management stream is not associated.");

        /// <summary>
        /// Gets the retained terminal diagnostic for scoped integration validation.
        /// </summary>
        internal Exception? Failure => m_failure;

        /// <summary>
        /// Adopts a dedicated h3 connection, including failure cleanup. The context is created only after
        /// exact H3 establishment and management association; ingress is attached before StartAsync.
        /// </summary>
        public static async ValueTask<WebTransportDataChannelConnection> EstablishAsync(
            NativeQuicConnection connection,
            NativeHttp3Library http3Library,
            IDataChannelContextFactory contextFactory,
            DataChannelApplicationBudget applicationBudget,
            Uri endpointUrl,
            WebTransportDataChannelOptions options,
            IWebTransportConnectAuthorizer? connectAuthorizer = null,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            WebTransportDataChannelConnection? carrier = null;
            DataChannelCarrierReservation? reservation = null;
            Http3Handle? handle = null;
            try
            {
                ArgumentNullException.ThrowIfNull(http3Library);
                ArgumentNullException.ThrowIfNull(contextFactory);
                ArgumentNullException.ThrowIfNull(applicationBudget);
                ArgumentNullException.ThrowIfNull(options);
                options.Validate();
                WebTransportEndpoint.Address(endpointUrl, false);
                ValidateEvidence(connection.Peer);
                if (!connection.Peer.IsClient && connectAuthorizer == null)
                {
                    throw new InvalidOperationException("Explicit CONNECT authorization is required.");
                }
                TimeProvider clock = timeProvider ?? TimeProvider.System;
                using var deadline = new CancellationTokenSource(options.EstablishmentTimeout, clock);
                // Native closure completes the carrier's readiness sources with its retained failure.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, deadline.Token);
                await http3Library.ValidateAsync(linked.Token).ConfigureAwait(false);
                if (!applicationBudget.TryReserveCarrier(
                    options.ReservedBytes, 7 + options.MaxPendingStreams,
                    options.Native.MaxPendingOperations + options.MaxHostJobs, out reservation))
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                NativeQuicStream? connect = null;
                if (connection.Peer.IsClient)
                {
                    connect = await connection.OpenStreamAsync(cancellationToken: linked.Token).ConfigureAwait(false);
                    if (connect.StreamId != 0 || !connect.IsLocal || !connect.IsBidirectional)
                    {
                        throw new DataChannelException(DataChannelResult.SecurityFailure);
                    }
                }
                NativeQuicStream control = await connection.OpenStreamAsync(true, linked.Token).ConfigureAwait(false);
                NativeQuicStream encoder = await connection.OpenStreamAsync(true, linked.Token).ConfigureAwait(false);
                NativeQuicStream decoder = await connection.OpenStreamAsync(true, linked.Token).ConfigureAwait(false);
                handle = CreatePeer(connection, endpointUrl, options, control, encoder, decoder);
                carrier = new WebTransportDataChannelConnection(
                    connection, handle, reservation, endpointUrl, options, connectAuthorizer, clock);
                handle = null;
                reservation = null;
                carrier.AddPhysicalStream(control);
                carrier.AddPhysicalStream(encoder);
                carrier.AddPhysicalStream(decoder);
                if (connect != null)
                {
                    carrier.AddPhysicalStream(connect);
                }
                carrier.StartIngress();
                await carrier.m_established.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                if (connection.Peer.IsClient)
                {
                    await carrier.OpenWebTransportStreamAsync(true, linked.Token).ConfigureAwait(false);
                }
                await carrier.m_managementReady.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                carrier.m_context = contextFactory.Create(carrier);
                carrier.m_attached.TrySetResult();
                await carrier.Context.StartAsync(linked.Token).ConfigureAwait(false);
                return carrier;
            }
            catch
            {
                if (carrier != null)
                {
                    await carrier.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await connection.CloseAsync((ulong)DataChannelResult.SecurityFailure).ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                handle?.Dispose();
                reservation?.Dispose();
            }
        }

        /// <inheritdoc/>
        public ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
        {
            return OpenWebTransportStreamAsync(false, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<bool> BindStreamAsync(
            ulong streamId,
            uint channelId,
            bool locallyOpened,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (m_closing ||
                    channelId == 0 ||
                    m_management?.Stream.StreamId == streamId ||
                    m_channels.ContainsKey(channelId) ||
                    !m_streams.TryGetValue(streamId, out StreamState? state) ||
                    !state.Associated ||
                    state.ChannelId != 0 ||
                    state.Closing ||
                    state.Retired ||
                    !ReferenceEquals(state.Stream.Connection, NativeConnection) ||
                    !state.Stream.IsBidirectional ||
                    state.Stream.IsLocal != locallyOpened ||
                    !NativeConnection.TryGetStream(streamId, out NativeQuicStream? actual) ||
                    !ReferenceEquals(actual, state.Stream))
                {
                    return ValueTask.FromResult(false);
                }
                state.ChannelId = channelId;
                m_channels.Add(channelId, state);
                state.Bound.TrySetResult();
                m_pendingStreams--;
                return ValueTask.FromResult(true);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DataChannelResult> SendAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(write);
            if (write.Payload.Length > (write.Path == DataChannelPath.Datagram ? MaxDatagramSize : MaxRecordSize))
            {
                return DataChannelResult.TooLarge;
            }
            StreamState? stream;
            lock (m_gate)
            {
                if (m_closing)
                {
                    return DataChannelResult.Closed;
                }
                stream = write.Path == DataChannelPath.Management
                    ? m_management
                    : m_channels.GetValueOrDefault(write.ChannelId);
                if (stream == null ||
                    stream.Stream.StreamId != write.StreamId ||
                    (write.Path == DataChannelPath.Datagram && !write.IsData) ||
                    write.Path is not (DataChannelPath.Management or DataChannelPath.Flow or DataChannelPath.Datagram))
                {
                    return DataChannelResult.InvalidState;
                }
            }
            try
            {
                return write.Path == DataChannelPath.Datagram
                    ? await SendDatagramRecordAsync(write, cancellationToken).ConfigureAwait(false)
                    : await SendStreamRecordAsync(stream, write, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return DataChannelResult.Closed;
            }
            catch (ObjectDisposedException)
            {
                return DataChannelResult.Closed;
            }
            catch (WebTransportException exception)
            {
                DataChannelResult result = MapHttp3(exception.NativeCode);
                if (write.Path == DataChannelPath.Management || exception.Scope is 3 or 4)
                {
                    RequestClose(result, exception);
                }
                return result;
            }
        }

        /// <inheritdoc/>
        public async ValueTask CloseFlowAsync(
            ulong streamId,
            DataChannelResult result,
            CancellationToken cancellationToken = default)
        {
            StreamState? stream;
            lock (m_gate)
            {
                if (m_closing || !m_streams.TryGetValue(streamId, out stream) || stream.Retired)
                {
                    return;
                }
                if (ReferenceEquals(stream, m_management))
                {
                    RequestClose(result);
                    return;
                }
                if (!stream.Associated)
                {
                    throw new DataChannelException(DataChannelResult.InvalidState);
                }
                if (!stream.Closing)
                {
                    stream.Closing = true;
                    stream.ResetRequested = true;
                    stream.Bound.TrySetResult();
                    if (stream.PendingSend is { Leased: false } pending)
                    {
                        pending.Completion.TrySetResult(DataChannelResult.Closed);
                    }
                    CommandLocked(17, streamId, (uint)result);
                }
            }
            try
            {
                await stream.ResetComplete.Task.WaitAsync(m_lifetime).ConfigureAwait(false);
                ulong wireError = stream.ResetError ??
                    throw new WebTransportException("The native reset wire error was not observed.");
                await stream.Stream.CloseAsync(wireError).ConfigureAwait(false);
            }
            catch (Exception) when (m_closing)
            {
                // Whole-connection loss supersedes per-stream reset evidence and owns actual native drainage.
                await m_transportClosed.Task.ConfigureAwait(false);
                return;
            }
            lock (m_gate)
            {
                RetireStreamLocked(stream);
            }
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(
            DataChannelResult result,
            CancellationToken cancellationToken = default)
        {
            RequestClose(result);
            await m_transportClosed.Task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            RequestClose(DataChannelResult.Closed);
            await Completion.ConfigureAwait(false);
        }

        /// <summary>
        /// Returns independent host and native ownership observations, not a synthesized transport-success flag.
        /// </summary>
        /// <exception cref="WebTransportException">The native resource observation failed.</exception>
        public WebTransportResourceUsage GetResourceUsage()
        {
            lock (m_gate)
            {
                Http3Resources resources = default;
                if (!m_handle.IsClosed && Http3Native.Resources(m_handle, out resources) != 0)
                {
                    throw new WebTransportException("The HTTP/3 resource observation failed.");
                }
                return new WebTransportResourceUsage(m_hostBytes, m_jobs.Count, m_leases.Count,
                    resources.Queued, resources.Leased, resources.Sends, resources.ReservedPayload);
            }
        }

        /// <summary>
        /// Serializes actual allocation and H3 registration, retaining the same ID through WT credit backpressure.
        /// </summary>
        private async ValueTask<ulong> OpenWebTransportStreamAsync(
            bool management,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            await m_allocator.WaitAsync(linked.Token).ConfigureAwait(false);
            bool allocated = false;
            bool reserved = false;
            try
            {
                lock (m_gate)
                {
                    if (m_closing ||
                        m_draining ||
                        m_pendingStreams >= m_options.MaxPendingStreams ||
                        m_streams.Count >= m_options.MaxStreamHistory ||
                        !HasBidirectionalCapacityLocked())
                    {
                        throw new DataChannelException(DataChannelResult.WouldBlock);
                    }
                    m_reservedBidirectional++;
                    reserved = true;
                }
                NativeQuicStream native = await NativeConnection.OpenStreamAsync(cancellationToken: linked.Token)
                    .ConfigureAwait(false);
                allocated = true;
                StreamState stream;
                lock (m_gate)
                {
                    m_reservedBidirectional--;
                    reserved = false;
                    stream = AddPhysicalStream(native);
                }
                stream.Associated = true;
                if (management)
                {
                    lock (m_gate)
                    {
                        if (m_management != null || !native.IsLocal || !NativeConnection.Peer.IsClient)
                        {
                            throw new DataChannelException(DataChannelResult.InvalidState);
                        }
                        m_management = stream;
                        stream.Bound.TrySetResult();
                        m_pendingStreams--;
                    }
                }
                await RetryCommandAsync(12, 0, native.StreamId, default, linked.Token).ConfigureAwait(false);
                await stream.PrefixComplete.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                if (management)
                {
                    m_managementReady.TrySetResult();
                }
                return native.StreamId;
            }
            catch (NativeQuicTransportException exception) when (!allocated &&
                (exception.Completion == null ||
                    exception.Completion.Status is NativeQuicStatus.Limit or NativeQuicStatus.Busy))
            {
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            catch
            {
                // A cancelled allocation/registration must not allow later IDs to overtake an abandoned prefix.
                if (allocated)
                {
                    RequestClose(DataChannelResult.Closed);
                }
                throw;
            }
            finally
            {
                if (reserved)
                {
                    lock (m_gate)
                    {
                        m_reservedBidirectional--;
                        PulseLocked();
                    }
                }
                m_allocator.Release();
            }
        }

        /// <summary>
        /// Starts shutdown once without awaiting Core callbacks or native I/O under the H3 gate.
        /// </summary>
        private void RequestClose(DataChannelResult result, Exception? error = null)
        {
            lock (m_gate)
            {
                if (m_closing)
                {
                    return;
                }
                m_closing = true;
                m_closeResult = result;
                m_failure = error ?? new DataChannelException(result);
                m_established.TrySetException(m_failure);
                m_managementReady.TrySetException(m_failure);
                m_attached.TrySetCanceled();
                foreach (StreamState stream in m_streams.Values)
                {
                    stream.PrefixComplete.TrySetException(m_failure);
                    stream.PendingSend?.Completion.TrySetResult(result);
                    stream.ResetComplete.TrySetCanceled();
                    stream.Bound.TrySetResult();
                }
                m_datagramSend?.Completion.TrySetResult(result);
                PulseLocked();
                _ = DisposeCoreAsync();
            }
        }

        /// <summary>
        /// Closes actual transport first to release native writes/reads, then joins Core and host work.
        /// The Core CloseAsync callback waits only for transport closure, never for its own ingress job.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            await Task.Yield();
            Exception? cleanupFailure = null;
            await m_shutdown.CancelAsync().ConfigureAwait(false);
            try
            {
                await NativeConnection.CloseAsync((ulong)m_closeResult).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
            finally
            {
                m_transportClosed.TrySetResult();
            }
            try
            {
                if (m_context != null)
                {
                    await m_context.InvalidateAsync(m_closeResult).ConfigureAwait(false);
                    await m_context.DisposeAsync().ConfigureAwait(false);
                }
                while (true)
                {
                    Task[] jobs;
                    lock (m_gate)
                    {
                        jobs = [.. m_jobs.Values];
                    }
                    if (jobs.Length == 0)
                    {
                        break;
                    }
                    await Task.WhenAll(jobs).ConfigureAwait(false);
                }
                await m_allocator.WaitAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }
            finally
            {
                lock (m_gate)
                {
                    if (m_leases.Count != 0 || m_hostBytes != 0)
                    {
                        cleanupFailure ??= new WebTransportException("HTTP/3 host ownership did not drain.");
                    }
                    m_handle.Dispose();
                    m_reservation.Dispose();
                    foreach (StreamState stream in m_streams.Values)
                    {
                        stream.Writer.Dispose();
                    }
                }
                m_allocator.Dispose();
                m_datagramWriter.Dispose();
                m_streamSends.Dispose();
                m_criticalSends.Dispose();
                m_shutdown.Dispose();
                if (cleanupFailure == null)
                {
                    m_completed.TrySetResult();
                }
                else
                {
                    m_completed.TrySetException(cleanupFailure);
                }
            }
        }

        /// <summary>
        /// Signals state/credit changes without retaining a waiter list.
        /// </summary>
        private void PulseLocked()
        {
            TaskCompletionSource changed = m_changed;
            m_changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult();
        }

        /// <summary>
        /// Releases live mapping/provisional admission while retaining the bounded physical-stream tombstone.
        /// </summary>
        private void RetireStreamLocked(StreamState stream)
        {
            if (!stream.Retired)
            {
                stream.Retired = true;
                if (stream.ChannelId == 0 && stream.Stream.IsBidirectional && stream.Stream.StreamId != 0)
                {
                    m_pendingStreams--;
                }
                else if (stream.ChannelId != 0)
                {
                    m_channels.Remove(stream.ChannelId);
                }
                PulseLocked();
            }
        }

        /// <summary>
        /// Reserves CONNECT, six H3 uni resources and both pending accept operations before data allocation.
        /// </summary>
        private bool HasBidirectionalCapacityLocked()
        {
            return m_reservedBidirectional +
                m_streams.Values.Count(state => !state.Retired &&
                    state.Stream.IsBidirectional &&
                    state.Stream.StreamId != 0) < m_options.Native.MaxStreams - 9;
        }

        /// <summary>
        /// Maps typed local HTTP3 failures into the one generated common result vocabulary.
        /// </summary>
        private static DataChannelResult MapHttp3(uint code)
        {
            return code switch
            {
                0 => DataChannelResult.Closed,
                8 => DataChannelResult.SecurityFailure,
                15 or 16 or 17 => DataChannelResult.WouldBlock,
                22 or 26 or 27 => DataChannelResult.Unsupported,
                23 or 24 or 25 => DataChannelResult.NotAuthorized,
                1 or 2 or 3 or 4 or 5 or 6 or 7 or 9 or 10 => DataChannelResult.InvalidArgument,
                _ => DataChannelResult.Closed
            };
        }

        /// <summary>
        /// One actual stream association and its independent ordered send/receive work.
        /// </summary>
        private sealed class StreamState
        {
            /// <summary>
            /// Captures one actual native owner.
            /// </summary>
            internal StreamState(NativeQuicStream stream)
            {
                Stream = stream;
                Closed = stream.Closed;
            }

            /// <summary>
            /// Actual owned stream.
            /// </summary>
            internal NativeQuicStream Stream { get; }

            /// <summary>
            /// Cached directional-resource lifetime signal, safe to observe after handle disposal.
            /// </summary>
            internal CancellationToken Closed { get; }

            /// <summary>
            /// Core channel mapping, zero before binding.
            /// </summary>
            internal uint ChannelId { get; set; }

            /// <summary>
            /// Validated local registration or peer WT association.
            /// </summary>
            internal bool Associated { get; set; }

            /// <summary>
            /// Explicit send/flow shutdown requested.
            /// </summary>
            internal bool Closing { get; set; }

            /// <summary>
            /// Native handle released; its ID remains a tombstone.
            /// </summary>
            internal bool Retired { get; set; }

            /// <summary>
            /// Whether H3 reset owns the send direction, independently of an outstanding body lease.
            /// </summary>
            internal bool ResetRequested { get; set; }

            /// <summary>
            /// Actual full-width wire error emitted by the native H3 reset action.
            /// </summary>
            internal ulong? ResetError { get; set; }

            /// <summary>
            /// Exact native-engine STOP error, when a rejection selected one.
            /// </summary>
            internal ulong? StopError { get; set; }

            /// <summary>
            /// Outstanding native-engine STOP action.
            /// </summary>
            internal Task? StopAction { get; set; }

            /// <summary>
            /// Actual received-reset evidence, shared by the read and independent reset observer.
            /// </summary>
            internal NativeQuicCompletion? PeerReset { get; set; }

            /// <summary>
            /// Actual peer STOP evidence, which may win a simultaneous send-side reset.
            /// </summary>
            internal TaskCompletionSource<NativeQuicCompletion> PeerStopped { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Current H3 application send owner.
            /// </summary>
            internal PendingSend? PendingSend { get; set; }

            /// <summary>
            /// Active native body/prefix lease.
            /// </summary>
            internal SendLease? ActiveLease { get; set; }

            /// <summary>
            /// Serial receive continuation, including EOF and reset.
            /// </summary>
            internal Task ReceiveTail { get; set; } = Task.CompletedTask;

            /// <summary>
            /// Serial complete common-record writer.
            /// </summary>
            internal SemaphoreSlim Writer { get; } = new(1, 1);

            /// <summary>
            /// Actual prefix transmission and reliable-boundary completion.
            /// </summary>
            internal TaskCompletionSource PrefixComplete { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Actual local reset evidence acknowledged by H3.
            /// </summary>
            internal TaskCompletionSource ResetComplete { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// The requested wire error has been published to the native send cancellation control.
            /// </summary>
            internal TaskCompletionSource ResetSignalled { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Binding/provisional lifetime completion.
            /// </summary>
            internal TaskCompletionSource Bound { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// One native H3 send command awaiting actual transport completion.
        /// </summary>
        private sealed class PendingSend
        {
            /// <summary>
            /// Whether H3 has leased the output to a native job.
            /// </summary>
            internal bool Leased { get; set; }

            /// <summary>
            /// Exact terminal send outcome.
            /// </summary>
            internal TaskCompletionSource<DataChannelResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Owns a polled H3 send token and its copied payload until actual native release.
        /// </summary>
        private sealed class SendLease(Http3Event metadata, ByteString payload, PendingSend? pending)
        {
            /// <summary>
            /// Original immutable lease metadata.
            /// </summary>
            internal Http3Event Metadata { get; } = metadata;

            /// <summary>
            /// Owned immutable payload retained through native completion.
            /// </summary>
            internal ByteString Payload { get; } = payload;

            /// <summary>
            /// Optional Core send waiting for this lease.
            /// </summary>
            internal PendingSend? Pending { get; } = pending;

            /// <summary>
            /// Cumulative accepted bytes for this token only.
            /// </summary>
            internal ulong Accepted { get; set; }

            /// <summary>
            /// Completion of this actual native lease and H3 receipt.
            /// </summary>
            internal TaskCompletionSource<DataChannelResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly Lock m_gate = new();
        private readonly Http3Handle m_handle;
        private readonly DataChannelCarrierReservation m_reservation;
        private readonly WebTransportDataChannelOptions m_options;
        private readonly IWebTransportConnectAuthorizer? m_authorizer;
        private readonly TimeProvider m_clock;
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly CancellationToken m_lifetime;
        private readonly SemaphoreSlim m_allocator = new(1, 1);
        private readonly SemaphoreSlim m_datagramWriter = new(1, 1);
        private readonly SemaphoreSlim m_criticalSends = new(1, 1);
        private readonly SemaphoreSlim m_streamSends;
        private readonly Dictionary<ulong, StreamState> m_streams = [];
        private readonly Dictionary<uint, StreamState> m_channels = [];
        private readonly Dictionary<ulong, SendLease> m_leases = [];
        private readonly Dictionary<long, Task> m_jobs = [];
        private readonly TaskCompletionSource m_established = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource m_managementReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource m_attached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource m_transportClosed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource m_completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource m_changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DataChannelContext? m_context;
        private StreamState? m_management;
        private PendingSend? m_datagramSend;
        private Exception? m_failure;
        private DataChannelResult m_closeResult;
        private long m_nextJob;
        private long m_hostBytes;
        private int m_pendingStreams;
        private int m_reservedBidirectional;
        private bool m_closing;
        private bool m_draining;
        private bool m_connectStarted;
        private bool m_authorizationStarted;
    }

    /// <summary>
    /// Independent live host/native ownership counters for one connection.
    /// </summary>
    public readonly record struct WebTransportResourceUsage(
        long HostBufferedBytes,
        int HostJobs,
        int HostSendLeases,
        ulong NativeQueuedBytes,
        ulong NativeLeasedBytes,
        ulong NativePendingSends,
        ulong NativeReservedPayload);
}
