/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Owns one explicitly selected authenticated data context. Configured flows have no
    /// Session dependency; optional Session policy is injected through the same admission seam.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed partial class DataChannelContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates a common runtime over verified carrier evidence. Reuse the same application
        /// budget across contexts. Attach receive callbacks before calling StartAsync.
        /// </summary>
        public DataChannelContext(
            IDataChannelCarrier carrier,
            IDataChannelAuthorizer authorizer,
            DataChannelOptions options,
            DataChannelApplicationBudget budget,
            TimeProvider timeProvider,
            ITelemetryContext telemetry)
        {
            m_carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
            m_authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_budget = budget ?? throw new ArgumentNullException(nameof(budget));
            m_clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<DataChannelContext>();
            m_options.Validate();
            m_wireCarrier = FlowModel.ToWire(carrier.Kind);
            m_codecLimits = new FlowCodecLimits(
                options.MaxMessageSize,
                options.MaxControlBytes,
                IsNative ? carrier.MaxRecordSize : checked(carrier.MaxRecordSize + FlowRecordCodec.HeaderSize));
            m_directAssociations = new FlowDirectAssociationRegistry(options.MaxStreamHistory, budget);
            Initialize();
        }

        /// <summary>
        /// Gets the explicitly selected carrier profile.
        /// </summary>
        public DataChannelCarrierKind CarrierKind => m_carrier.Kind;

        /// <summary>
        /// Gets the verified context identity after HELLO confirmation.
        /// </summary>
        public DataChannelContextIdentity Identity => GetIdentity();

        /// <summary>
        /// Gets whether both protected HELLO confirmations have completed.
        /// </summary>
        public bool IsReady => GetIsReady();

        /// <summary>
        /// Gets a bounded snapshot of active flow facades.
        /// </summary>
        public ArrayOf<DataChannel> Channels => GetChannels();

        /// <summary>
        /// Reports generated lifecycle snapshots outside runtime synchronization.
        /// Handlers must not block; exceptions are isolated from protocol processing.
        /// </summary>
        public event Action<DataChannelStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Reports data-context closure exactly once, including contexts that never opened a flow.
        /// A control-transport failure alone must not call this event or invalidate this context.
        /// </summary>
        public event Action<DataChannelResult>? Closed;

        /// <summary>
        /// Exchanges protected HELLO and awaits mutual confirmation or the open deadline.
        /// Cancellation does not turn an unconfirmed carrier into a ready context.
        /// </summary>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return StartCoreAsync(cancellationToken);
        }

        /// <summary>
        /// Waits for confirmed HELLO without starting it. This is safe during factory registration,
        /// before the transport has completed attachment; cancellation affects only the wait.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        public async ValueTask WaitForReadyAsync(CancellationToken cancellationToken = default)
        {
            DataChannelResult result = await m_ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result != DataChannelResult.Good)
            {
                throw new DataChannelException(result);
            }
            lock (m_gate)
            {
                ThrowIfClosedLocked();
            }
        }

        /// <summary>
        /// Reserves a fresh local ChannelId for a subsequent Session-grant Method call.
        /// Unused reservations expire at OpenTimeout and their identifiers remain retired.
        /// </summary>
        public uint ReserveChannelId()
        {
            return ReserveChannelIdCore();
        }

        /// <summary>
        /// Releases an unused reservation after failed/cancelled grant issuance. The identifier
        /// remains retired and cannot be used by a later OpenAsync call.
        /// </summary>
        public bool ReleaseChannelId(uint channelId)
        {
            lock (m_gate)
            {
                if (!m_reservations.Remove(channelId))
                {
                    return false;
                }
                m_budget.Release(FlowResource.Flow, 1);
                m_budget.Release(FlowResource.Operation, 1);
                PulseCapacityLocked();
                return true;
            }
        }

        /// <summary>
        /// Establishes and authorizes a flow, returning only after its opener has sent READY.
        /// Pass a reserved ChannelId when obtaining a Session grant before OPEN.
        /// </summary>
        /// <exception cref="DataChannelException">The flow was refused or direct binding timed out.</exception>
        /// <exception cref="OperationCanceledException">The caller cancelled flow establishment.</exception>
        public ValueTask<DataChannel> OpenAsync(
            DataChannelParametersDataType parameters,
            DataChannelAuthorizationMode authorization = DataChannelAuthorizationMode.Configured,
            Guid? grantId = null,
            uint channelId = 0,
            CancellationToken cancellationToken = default)
        {
            return OpenCoreAsync(parameters, authorization, grantId, channelId, cancellationToken);
        }

        /// <summary>
        /// Returns a remotely opened flow only after receiving READY; null marks context closure.
        /// </summary>
        public ValueTask<DataChannel?> AcceptAsync(CancellationToken cancellationToken = default)
        {
            return AcceptCoreAsync(cancellationToken);
        }

        /// <summary>
        /// Receives one authenticated final FLO/FLC body or native WebTransport management MSG.
        /// It borrows input until return and retains owned complete-message storage.
        /// </summary>
        public ValueTask<DataChannelResult> ReceiveUascAsync(
            DataChannelUascMessageKind kind,
            byte chunkType,
            uint requestId,
            ByteString verifiedPlaintext,
            CancellationToken cancellationToken = default)
        {
            if (kind is not (DataChannelUascMessageKind.Flo or DataChannelUascMessageKind.Flc or
                DataChannelUascMessageKind.Msg))
            {
                return new ValueTask<DataChannelResult>(DataChannelResult.Unsupported);
            }
            return ReceiveUascCoreAsync(kind, chunkType, requestId, verifiedPlaintext, cancellationToken);
        }

        /// <summary>
        /// Receives arbitrary native stream segmentation with bounded pre-allocation admission.
        /// Await and serialize calls on each stream to propagate real transport backpressure.
        /// The input storage remains borrowed until completion. A scoped management refusal does
        /// not discard later complete records in the segment and does not imply context failure.
        /// </summary>
        public ValueTask<DataChannelResult> ReceiveStreamAsync(
            ulong streamId,
            ByteString segment,
            bool endOfStream = false,
            CancellationToken cancellationToken = default)
        {
            return ReceiveStreamCoreAsync(streamId, segment, endOfStream, cancellationToken);
        }

        /// <summary>
        /// Receives exactly one real native datagram, applying capacity/rate/replay/revision policy.
        /// Dropped input never allocates queued overflow state or claims delivery.
        /// </summary>
        public ValueTask<DataChannelResult> ReceiveDatagramAsync(
            ByteString datagram,
            CancellationToken cancellationToken = default)
        {
            return ReceiveDatagramCoreAsync(datagram, cancellationToken);
        }

        /// <summary>
        /// Receives an actual native RESET/STOP signal outside the blocked DATA read path.
        /// It retires bound or provisional ingress immediately without fabricating EOF or CONTROL bytes.
        /// Local cleanup is tracked and joined by flow/context disposal, not by this transport callback.
        /// </summary>
        public ValueTask<DataChannelResult> ReceiveStreamResetAsync(
            ulong streamId,
            DataChannelResult result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!IsNative)
                {
                    return new ValueTask<DataChannelResult>(DataChannelResult.Unsupported);
                }
                if (m_closed)
                {
                    return new ValueTask<DataChannelResult>(DataChannelResult.Closed);
                }
                if (streamId == m_carrier.ManagementStreamId)
                {
                    CloseLocked(result == DataChannelResult.Good ? DataChannelResult.Closed : result);
                    return new ValueTask<DataChannelResult>(DataChannelResult.Good);
                }
                if (m_streams.TryGetValue(streamId, out FlowStreamInput? input))
                {
                    if (input.FlowId != 0 && m_flows.TryGetValue(input.FlowId, out FlowState? flow))
                    {
                        TerminateFlowLocked(flow, result);
                        QueueFlowReleaseLocked(flow, Task.CompletedTask, result);
                    }
                    else
                    {
                        ReleaseStreamInputLocked(input, result);
                        m_streams.Remove(streamId);
                    }
                    PulseCapacityLocked();
                    WakeLocked();
                    return new ValueTask<DataChannelResult>(DataChannelResult.Good);
                }
                return new ValueTask<DataChannelResult>(m_usedStreams.Contains(streamId)
                    ? DataChannelResult.Good : DataChannelResult.UnknownChannel);
            }
        }

        /// <summary>
        /// Immediately rechecks affected configured or Session permissions after a policy change.
        /// This is distinct from losing the optional control connection.
        /// </summary>
        public ValueTask RecheckAuthorizationAsync(CancellationToken cancellationToken = default)
        {
            return RecheckCoreAsync(cancellationToken);
        }

        /// <summary>
        /// Faults this context after certificate/trust/security-association invalidation.
        /// </summary>
        public ValueTask InvalidateAsync(
            DataChannelResult result = DataChannelResult.SecurityFailure,
            CancellationToken cancellationToken = default)
        {
            return CloseCoreAsync(result, cancellationToken);
        }

        /// <summary>
        /// Probes connection management, subject to the same finite probe/rate limits as flows.
        /// </summary>
        public ValueTask<DataChannelResult> PingAsync(CancellationToken cancellationToken = default)
        {
            return PingAsync(0, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return DisposeCoreAsync();
        }

        private readonly IDataChannelCarrier m_carrier;
        private readonly IDataChannelAuthorizer m_authorizer;
        private readonly DataChannelOptions m_options;
        private readonly DataChannelApplicationBudget m_budget;
        private readonly TimeProvider m_clock;
        private readonly ILogger m_logger;
        private readonly FlowCarrier m_wireCarrier;
        private readonly FlowCodecLimits m_codecLimits;
    }

    /// <summary>
    /// A generated status snapshot captured at an actual flow state transition.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelStateChangedEventArgs
    {
        /// <summary>
        /// Captures the channel facade and its transition snapshot.
        /// </summary>
        internal DataChannelStateChangedEventArgs(DataChannel channel, DataChannelStatusDataType status)
        {
            Channel = channel;
            Status = status;
        }

        /// <summary>
        /// Gets the application facade, whose current state may have progressed.
        /// </summary>
        public DataChannel Channel { get; }

        /// <summary>
        /// Gets the generated snapshot at the reported transition.
        /// </summary>
        public DataChannelStatusDataType Status { get; }
    }
}
