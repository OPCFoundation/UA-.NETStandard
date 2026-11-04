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
using System.Collections.Generic;
using System.Linq;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Describes one NodeManager generation without retaining the NodeManager.
    /// </summary>
    public readonly record struct NodeManagerGenerationSnapshot
    {
        /// <summary>
        /// Creates a snapshot of a registration generation.
        /// </summary>
        /// <param name="slot">The stable registration slot.</param>
        /// <param name="generation">The generation represented by the snapshot.</param>
        internal NodeManagerGenerationSnapshot(NodeManagerSlot slot, long generation)
        {
            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            Generation = generation;
        }

        /// <summary>
        /// Gets the stable registration slot.
        /// </summary>
        public NodeManagerSlot Slot { get; }

        /// <summary>
        /// Gets the generation represented by this snapshot.
        /// </summary>
        public long Generation { get; }
    }

    /// <summary>
    /// Describes a committed registration without retaining the NodeManager.
    /// </summary>
    public readonly record struct NodeManagerRegistrationSnapshot
    {
        /// <summary>
        /// Creates a snapshot of a committed registration.
        /// </summary>
        /// <param name="registration">The registration to describe.</param>
        internal NodeManagerRegistrationSnapshot(NodeManagerRegistration registration)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            Slot = registration.Slot;
            Id = registration.Id;
            Generation = registration.Generation;
            NamespaceUris = registration.NamespaceUris;
            m_registration = new WeakReference<NodeManagerRegistration>(registration);
            m_nodeManager = new WeakReference<IAsyncNodeManager>(registration.NodeManager);
        }

        /// <summary>
        /// Gets the stable registration slot.
        /// </summary>
        public NodeManagerSlot Slot { get; }

        /// <summary>
        /// Gets the stable identifier shared by all generations of this registration.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets the generation represented by this snapshot.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets the namespaces owned by this generation. The array is null for a NodeManager that uses custom
        /// routing instead of namespace ownership.
        /// </summary>
        public ArrayOf<string> NamespaceUris { get; }

        /// <summary>
        /// Gets the NodeManager while another owner still retains it.
        /// </summary>
        /// <param name="nodeManager">The NodeManager when it is still available.</param>
        /// <returns>Whether the NodeManager is still available.</returns>
        public bool TryGetNodeManager(out IAsyncNodeManager? nodeManager)
        {
            if (m_nodeManager is not null && m_nodeManager.TryGetTarget(out nodeManager))
            {
                return true;
            }
            nodeManager = null;
            return false;
        }

        /// <summary>
        /// Creates a strong registration handle while another owner still retains the NodeManager.
        /// </summary>
        /// <param name="registration">The strong registration handle when the NodeManager is still available.</param>
        /// <returns>Whether a strong registration handle was created.</returns>
        public bool TryGetRegistration(out NodeManagerRegistration? registration)
        {
            if (m_registration is not null &&
                m_registration.TryGetTarget(out NodeManagerRegistration? retainedRegistration))
            {
                registration = retainedRegistration;
                return true;
            }
            if (TryGetNodeManager(out IAsyncNodeManager? nodeManager) && nodeManager is not null)
            {
                registration = new NodeManagerRegistration(Id, Generation, nodeManager);
                return true;
            }
            registration = null;
            return false;
        }

        /// <summary>
        /// Determines whether two snapshots identify the same registration generation.
        /// </summary>
        /// <param name="other">The snapshot to compare.</param>
        /// <returns>Whether both snapshots identify the same generation.</returns>
        public bool Equals(NodeManagerRegistrationSnapshot other)
        {
            return ReferenceEquals(Slot, other.Slot) && Id == other.Id && Generation == other.Generation;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Slot, Id, Generation);
        }

        private readonly WeakReference<NodeManagerRegistration>? m_registration;
        private readonly WeakReference<IAsyncNodeManager>? m_nodeManager;
    }

    /// <summary>
    /// Identifies one generation of a live NodeManager registration.
    /// </summary>
    public sealed class NodeManagerRegistration
    {
        internal NodeManagerRegistration(
            Guid id,
            long generation,
            IAsyncNodeManager nodeManager)
        {
            NodeManagerGeneration identity = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
            identity.Slot.SetRegistrationId(id);
            if (identity.Number != generation)
            {
                throw new InvalidOperationException(
                    $"Expected node-manager generation {generation}, but the published identity is {identity.Number}.");
            }
            Identity = identity;
            Slot = identity.Slot;
            Id = Slot.Id;
            Generation = identity.Number;
            NodeManager = identity.NodeManager;
            IEnumerable<string>? namespaceUris = nodeManager.NamespaceUris;
            NamespaceUris = namespaceUris is null
                ? default
                : new ArrayOf<string>(namespaceUris.ToArray());
        }

        /// <summary>
        /// Gets the stable slot shared by every generation of this registration.
        /// </summary>
        public NodeManagerSlot Slot { get; }

        /// <summary>
        /// Gets the stable identifier shared by all generations of this registration.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets the generation represented by this handle.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets the live NodeManager represented by this generation.
        /// </summary>
        public IAsyncNodeManager NodeManager { get; }

        /// <summary>
        /// Gets the namespaces owned by this generation. The array is null for
        /// a NodeManager that uses custom routing instead of namespace ownership.
        /// </summary>
        public ArrayOf<string> NamespaceUris { get; }

        /// <summary>
        /// Gets the immutable generation identity associated with this public registration.
        /// </summary>
        internal NodeManagerGeneration Identity { get; }
    }
}
