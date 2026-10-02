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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Mints the concrete event types the OPC 40001 series needs at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two of the event types the series declares — OPC 40001-101's
    /// <c>ResultReadyEventType</c> and OPC 40001-2's
    /// <c>ZeroPointAdjustmentEventType</c> — are abstract. OPC 10000-3 forbids
    /// instances of an abstract type, and a client that filters on a concrete
    /// <c>EventType</c> never sees an event reported with an abstract one, so a
    /// server that means to publish them has to derive a concrete subtype of
    /// its own.
    /// </para>
    /// <para>
    /// The subtype is minted once per node manager into the application-owned
    /// instance namespace and shared by every object that reports the event;
    /// deriving one per instance would multiply types a client has to know
    /// about for no gain. <c>Isa95NodeManager.CreateV2StatusEventTypeAsync</c>
    /// does the same for the ISA-95 job-order status event.
    /// </para>
    /// </remarks>
    internal static class MachineryConcreteEventTypes
    {
        /// <summary>
        /// The browse name of the concrete subtype of
        /// <c>ResultReadyEventType</c>.
        /// </summary>
        public const string ResultReadyEventInstanceType =
            "ResultReadyEventInstanceType";

        /// <summary>
        /// The browse name of the concrete subtype of
        /// <c>ZeroPointAdjustmentEventType</c>.
        /// </summary>
        public const string ZeroPointAdjustmentEventInstanceType =
            "ZeroPointAdjustmentEventInstanceType";

        /// <summary>
        /// Returns the NodeId of the concrete subtype of
        /// <paramref name="abstractTypeId"/>, registering it with the manager
        /// on first use.
        /// </summary>
        /// <param name="manager">The manager that owns the address space.</param>
        /// <param name="instanceNamespaceIndex">
        /// The application-owned namespace the subtype is minted into. It must
        /// not be a model namespace: the subtype is this server's, not the
        /// companion specification's.
        /// </param>
        /// <param name="abstractTypeId">The abstract event type to derive from.</param>
        /// <param name="name">The browse name of the concrete subtype.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static async ValueTask<NodeId> EnsureAsync(
            AsyncCustomNodeManager manager,
            ushort instanceNamespaceIndex,
            NodeId abstractTypeId,
            string name,
            CancellationToken cancellationToken)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            var nodeId = new NodeId(name, instanceNamespaceIndex);
            SemaphoreSlim gate = s_gates.GetValue(manager, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (manager.FindPredefinedNode<BaseObjectTypeState>(nodeId) != null)
                {
                    return nodeId;
                }

                var eventType = new BaseObjectTypeState
                {
                    NodeId = nodeId,
                    SymbolicName = name,
                    BrowseName = new QualifiedName(name, instanceNamespaceIndex),
                    DisplayName = new LocalizedText(name),
                    SuperTypeId = abstractTypeId,
                    IsAbstract = false,
                    IsPartOfTypeHierarchy = true
                };
                await manager
                    .AddPredefinedNodeAsync(eventType, cancellationToken)
                    .ConfigureAwait(false);
                return nodeId;
            }
            finally
            {
                gate.Release();
            }
        }

        private static readonly ConditionalWeakTable<
            AsyncCustomNodeManager,
            SemaphoreSlim> s_gates = new();
    }
}
