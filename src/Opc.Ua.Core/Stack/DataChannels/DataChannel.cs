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

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Identifies the UASC application-message identifier accepted by the data-channel API.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public enum DataChannelUascMessageKind
    {
        /// <summary>
        /// Legacy value retained for compatibility; current UASC DATA uses MSG.
        /// </summary>
        Flo,

        /// <summary>
        /// Legacy value retained for compatibility; current UASC CONTROL uses MSG.
        /// </summary>
        Flc,

        /// <summary>
        /// A baseline DATA/CONTROL MSG or a native WebTransport management MSG.
        /// </summary>
        Msg
    }

    /// <summary>
    /// A ready, independently authorized complete-message flow. The owning context handles
    /// association, readiness, revisions, resource accounting and carrier selection.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannel : IAsyncDisposable
    {
        /// <summary>
        /// Creates a facade for state owned exclusively by the context.
        /// </summary>
        internal DataChannel(DataChannelContext context, FlowState state)
        {
            Context = context;
            StateData = state;
        }

        /// <summary>
        /// Gets the context-generation-local identifier, which is never reused.
        /// </summary>
        public uint ChannelId => StateData.Id;

        /// <summary>
        /// Gets whether this endpoint opened the flow, independently of Publisher/transport roles.
        /// </summary>
        public bool IsLocalOpener => StateData.IsLocalOpener;

        /// <summary>
        /// Gets the actual authenticated carrier kind, not a caller-asserted transport label.
        /// </summary>
        public DataChannelCarrierKind CarrierKind => Context.CarrierKind;

        /// <summary>
        /// Gets the borrowed authenticated context for routing and identity evidence. Owning a
        /// flow does not confer ownership of this context or permission to dispose unrelated flows.
        /// </summary>
        public DataChannelContext Context { get; }

        /// <summary>
        /// Gets a defensive snapshot of the admitted descriptor.
        /// </summary>
        public DataChannelParametersDataType Parameters => GetStatus().Parameters;

        /// <summary>
        /// Gets the current committed revision.
        /// </summary>
        public uint Revision => GetStatus().Revision;

        /// <summary>
        /// Gets the observable lifecycle state.
        /// </summary>
        public DataChannelState State => GetStatus().State;

        /// <summary>
        /// Reports this facade's generated transition snapshots outside runtime synchronization.
        /// Send-only consumers can observe remote closure without reading a forbidden direction.
        /// </summary>
        public event Action<DataChannelStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Gets whether the owner needs to retain transition notifications for this facade.
        /// </summary>
        internal bool HasStateObservers => StateChanged != null;

        /// <summary>
        /// Gets context-owned mutable state, never synchronization primitives.
        /// </summary>
        internal FlowState StateData { get; }

        /// <summary>
        /// Returns a generated status snapshot suitable for optional model projection.
        /// </summary>
        public DataChannelStatusDataType GetStatus()
        {
            return Context.GetStatus(this);
        }

        /// <summary>
        /// Returns generated counters without interpreting application payload.
        /// </summary>
        public DataChannelDiagnosticsDataType GetDiagnostics()
        {
            return Context.GetDiagnostics(this);
        }

        /// <summary>
        /// Copies one complete message into bounded owned storage and awaits its send outcome.
        /// Queue expiry/cancellation applies only before the carrier's TryCommit cut-off.
        /// After commitment cancellation abandons the wait, not the numbered/secured write.
        /// </summary>
        public ValueTask<DataChannelResult> SendAsync(
            ByteString payload,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default)
        {
            return Context.SendAsync(this, payload, timeToLive, cancellationToken);
        }

        /// <summary>
        /// Returns one complete owned message, or null after the remote data direction ends.
        /// The caller must dispose each returned message to release bytes and message capacity.
        /// </summary>
        public ValueTask<DataChannelMessage?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Context.ReadAsync(this, cancellationToken);
        }

        /// <summary>
        /// Performs a complete opener-initiated revision transaction. A nonzero operationId
        /// permits idempotent retry; zero allocates a fresh identifier. WouldBlock preserves
        /// the accepted operation and datagram queue, allowing retry with the same identifier.
        /// </summary>
        public ValueTask<DataChannelResult> UpdateAsync(
            DataChannelParametersDataType parameters,
            uint operationId = 0,
            CancellationToken cancellationToken = default)
        {
            return Context.UpdateAsync(this, parameters, operationId, cancellationToken);
        }

        /// <summary>
        /// Cancels a pending uncommitted revision without rolling back a committed outcome.
        /// </summary>
        public ValueTask<DataChannelResult> AbortUpdateAsync(
            uint operationId,
            CancellationToken cancellationToken = default)
        {
            return Context.AbortUpdateAsync(this, operationId, cancellationToken);
        }

        /// <summary>
        /// Returns the bounded recorded outcome without reapplying a retired operation.
        /// </summary>
        public DataChannelStatusDataType GetOperationStatus(uint operationId)
        {
            return Context.GetOperationStatus(this, operationId);
        }

        /// <summary>
        /// Stops new local data, drains already queued messages and emits the directional END.
        /// The remote direction and required control remain active.
        /// </summary>
        public ValueTask<DataChannelResult> EndAsync(CancellationToken cancellationToken = default)
        {
            return Context.EndAsync(this, cancellationToken);
        }

        /// <summary>
        /// Discards unsecured queued data and closes this flow without affecting other flows.
        /// Good closes orderly; another result faults the flow.
        /// </summary>
        public ValueTask ResetAsync(
            DataChannelResult result = DataChannelResult.Good,
            CancellationToken cancellationToken = default)
        {
            return Context.ResetAsync(this, result, cancellationToken);
        }

        /// <summary>
        /// Sends a bounded probe and waits for its timestamp echo or monotonic timeout.
        /// </summary>
        public ValueTask<DataChannelResult> PingAsync(CancellationToken cancellationToken = default)
        {
            return Context.PingAsync(ChannelId, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return ResetAsync();
        }

        /// <summary>
        /// Borrows an ordered transport chunk until admission and completion, waiting for temporary
        /// authorization or queue capacity without retrying a message already admitted to the writer.
        /// </summary>
        internal ValueTask<DataChannelResult> SendWithAdmissionAsync(
            ByteString payload,
            CancellationToken cancellationToken = default)
        {
            return Context.SendWithAdmissionAsync(this, payload, cancellationToken);
        }

        /// <summary>
        /// Publishes a transition outside the context's state synchronization.
        /// </summary>
        internal void NotifyStateChanged(DataChannelStateChangedEventArgs notification)
        {
            StateChanged?.Invoke(notification);
        }
    }
}
