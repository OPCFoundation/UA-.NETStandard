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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Cutter;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// A completed physical cut and absolute product counters, supplied by the real cutter authority.
    /// </summary>
    /// <param name="Revision">Monotonic cut commit sequence, including resets/reconfiguration.</param>
    /// <param name="ProductId">The product that was cut.</param>
    /// <param name="ActiveProductId">The product now active after the cut; sequencing remains hardware-owned.</param>
    /// <param name="ActualOutput">Absolute output for this production of the product.</param>
    /// <param name="TotalOutput">Absolute lifetime/resettable output reported by the cutter.</param>
    /// <param name="SourceTimestamp">The actual completion timestamp.</param>
    public sealed record ExtrusionCutCommit(
        ulong Revision,
        string ProductId,
        string ActiveProductId,
        ulong ActualOutput,
        ulong TotalOutput,
        DateTimeUtc SourceTimestamp);

    /// <summary>
    /// Publishes OPC 40084-9 cutting-product counters and one event per committed cut, without simulating cutting.
    /// </summary>
    public sealed class ExtrusionCutController : IAsyncDisposable
    {
        /// <summary>
        /// Creates the controller over real mandatory cutting-product instances.
        /// </summary>
        internal ExtrusionCutController(
            ExtrusionRuntime runtime,
            Cutter_InterfaceState node,
            ExtrusionDeviceBinding binding)
        {
            m_runtime = runtime;
            Node = node;
            m_binding = binding;
            m_events = new ExtrusionEventEmitter(runtime, node);
        }

        /// <summary>
        /// Gets the generated cutter interface.
        /// </summary>
        public Cutter_InterfaceState Node { get; }

        /// <summary>
        /// Applies a completed physical cut once, preserving all externally committed absolute counts.
        /// </summary>
        public async ValueTask ApplyAsync(ExtrusionCutCommit commit, CancellationToken cancellationToken = default)
        {
            if (commit == null)
            {
                throw new ArgumentNullException(nameof(commit));
            }
            if (string.IsNullOrWhiteSpace(commit.ProductId) || string.IsNullOrWhiteSpace(commit.ActiveProductId) ||
                commit.SourceTimestamp == DateTimeUtc.MinValue || commit.ActualOutput > commit.TotalOutput)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Invalid committed cutting state.");
            }
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                m_lifetime.ThrowIfStopped();
                if (m_last != null && commit.Revision <= m_last.Revision)
                {
                    if (commit == m_last)
                    {
                        return;
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState, "The cut commit is stale or conflicts with an applied revision.");
                }
                CuttingProductState product = RequireProduct(commit.ProductId);
                RequireProduct(commit.ActiveProductId);
                m_binding.SetProtocolValue(product.ActualOutput!, new DataValue(
                    Variant.From(commit.ActualOutput), StatusCodes.Good, commit.SourceTimestamp));
                m_binding.SetProtocolValue(product.TotalOutput!, new DataValue(
                    Variant.From(commit.TotalOutput), StatusCodes.Good, commit.SourceTimestamp));
                m_binding.SetProtocolValue(Node.CuttingProducts!.ActualCuttingProductId!, new DataValue(
                    Variant.From(commit.ActiveProductId), StatusCodes.Good, commit.SourceTimestamp));
                m_last = commit;
                Node.ClearChangeMasks(m_runtime.SystemContext, true);
                CutEventState notification = m_runtime.SystemContext.CreateInstanceOfCutEventType();
                notification.CuttingProductId!.Value = commit.ProductId;
                notification.ActualOutput!.Value = commit.ActualOutput;
                notification.TotalOutput!.Value = commit.TotalOutput;
                await m_events.ReportAsync(notification, m_eventType, Node,
                    commit.SourceTimestamp, "Extrusion cut completed.").ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref m_disposalStarted, 1, 0) == 0)
            {
                try
                {
                    try
                    {
                        await m_lifetime.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_gate.Dispose();
                    }
                    m_disposed.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    m_disposed.TrySetException(failure);
                }
            }
            await m_disposed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a concrete event type without publishing an instance of abstract CutEventType.
        /// </summary>
        internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            m_eventType = await m_events.EnsureTypeAsync(
                Cutter.ObjectTypeIds.CutEventType, "CutCompleted", pending.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a product by its real Id, not its position in a simulated queue.
        /// </summary>
        private CuttingProductState RequireProduct(string id)
        {
            CuttingProductState[] products = ExtrusionNodeAccess.Children(
                m_runtime.SystemContext, Node.CuttingProducts!)
                .OfType<CuttingProductState>().Where(product => product.Id?.Value == id).ToArray();
            if (products.Length != 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound, "The cutting product identifier is missing or ambiguous.");
            }
            return products[0];
        }

        /// <summary>The existing owner.</summary>
        private readonly ExtrusionRuntime m_runtime;

        /// <summary>The parent's authoritative variable publisher.</summary>
        private readonly ExtrusionDeviceBinding m_binding;

        /// <summary>The generated event publisher.</summary>
        private readonly ExtrusionEventEmitter m_events;

        /// <summary>Serializes committed cuts and their event order.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Cancels and drains all active and queued cut operations.</summary>
        private readonly ExtrusionOperationLifetime m_lifetime = new(nameof(ExtrusionCutController));

        /// <summary>The concrete event type in the application namespace.</summary>
        private NodeId m_eventType;

        /// <summary>The last published cut receipt.</summary>
        private ExtrusionCutCommit? m_last;

        /// <summary>Claims the single terminal cleanup.</summary>
        private int m_disposalStarted;

        /// <summary>Shares completion after the drained gate has been released.</summary>
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
