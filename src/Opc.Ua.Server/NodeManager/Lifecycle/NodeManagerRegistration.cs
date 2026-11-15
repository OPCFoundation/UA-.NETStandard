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
    /// Describes one NodeManager generation by its slot and generation number.
    /// </summary>
    /// <remarks>
    /// The snapshot holds its slot, which retains its current generation and unresolved subscription items.
    /// </remarks>
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
    /// Describes a prepared or committed registration by its slot and generation metadata.
    /// </summary>
    /// <remarks>
    /// References to the registration, its generation and NodeManager are weak. The snapshot holds its
    /// slot strongly, and the slot retains its current generation and unresolved subscription items.
    /// </remarks>
    public readonly record struct NodeManagerRegistrationSnapshot
    {
        /// <summary>
        /// Creates a snapshot of a registration.
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
            m_references = new RegistrationReferences(registration);
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
        /// Attempts to obtain the NodeManager through its weak reference.
        /// </summary>
        /// <param name="nodeManager">The NodeManager if the reference resolves; otherwise, null.</param>
        /// <returns>True if the reference resolves; otherwise, false.</returns>
        public bool TryGetNodeManager(out IAsyncNodeManager? nodeManager)
        {
            if (m_references is not null && m_references.NodeManager.TryGetTarget(out nodeManager))
            {
                return true;
            }
            nodeManager = null;
            return false;
        }

        /// <summary>
        /// Attempts to obtain a strong registration handle.
        /// </summary>
        /// <remarks>
        /// Returns the original registration if its weak reference resolves. Otherwise, a handle is reconstructed
        /// only while the original generation and NodeManager are available. Reassociation of the NodeManager
        /// with another generation does not change the returned handle.
        /// </remarks>
        /// <param name="registration">The strong registration handle if available; otherwise, null.</param>
        /// <returns>True if a registration is returned; otherwise, false.</returns>
        public bool TryGetRegistration(out NodeManagerRegistration? registration)
        {
            if (m_registration is not null &&
                m_registration.TryGetTarget(out NodeManagerRegistration? retainedRegistration))
            {
                registration = retainedRegistration;
                return true;
            }
            if (m_references is not null &&
                m_references.Generation.TryGetGeneration(out NodeManagerGeneration? identity) &&
                identity is not null &&
                ReferenceEquals(identity.Slot, Slot) &&
                identity.Number == Generation &&
                identity.Slot.Id == Id &&
                TryGetNodeManager(out IAsyncNodeManager? nodeManager) &&
                ReferenceEquals(identity.NodeManager, nodeManager))
            {
                registration = new NodeManagerRegistration(identity, NamespaceUris);
                return true;
            }
            registration = null;
            return false;
        }

        /// <summary>
        /// Determines whether two snapshots have the same slot, identifier and generation number.
        /// </summary>
        /// <remarks>
        /// Equality does not distinguish separate prepared generations with the same metadata.
        /// </remarks>
        /// <param name="other">The snapshot to compare.</param>
        /// <returns>Whether both snapshots have the same registration metadata.</returns>
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
        private readonly RegistrationReferences? m_references;

        /// <summary>
        /// Keeps weak references to the original manager and generation without extending their lifetimes.
        /// </summary>
        private sealed class RegistrationReferences
        {
            /// <summary>
            /// Captures the original registration's manager and generation.
            /// </summary>
            /// <param name="registration">The registration being described.</param>
            internal RegistrationReferences(NodeManagerRegistration registration)
            {
                NodeManager = new WeakReference<IAsyncNodeManager>(registration.NodeManager);
                Generation = registration.Identity.Reference;
            }

            /// <summary>
            /// Gets the weak reference to the original manager.
            /// </summary>
            internal WeakReference<IAsyncNodeManager> NodeManager { get; }

            /// <summary>
            /// Gets the weak reference to the original generation.
            /// </summary>
            internal NodeManagerGenerationReference Generation { get; }
        }
    }

    /// <summary>
    /// Identifies one generation of a live NodeManager registration.
    /// </summary>
    public sealed class NodeManagerRegistration
    {
        /// <summary>
        /// Creates a registration for the manager's associated generation.
        /// </summary>
        /// <param name="id">The identifier to assign to the generation's slot.</param>
        /// <param name="generation">The expected generation number.</param>
        /// <param name="nodeManager">The manager associated with the generation.</param>
        internal NodeManagerRegistration(
            Guid id,
            long generation,
            IAsyncNodeManager nodeManager)
        {
            NodeManagerGeneration identity = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
            if (identity.Number != generation)
            {
                throw new InvalidOperationException(
                    $"Expected node-manager generation {generation}, but the published identity is {identity.Number}.");
            }
            identity.Slot.SetRegistrationId(id);
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
        /// Reconstructs a handle from the original generation and its captured namespace metadata.
        /// </summary>
        /// <param name="identity">The exact generation represented by the handle.</param>
        /// <param name="namespaceUris">The captured namespace metadata.</param>
        internal NodeManagerRegistration(NodeManagerGeneration identity, ArrayOf<string> namespaceUris)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Slot = identity.Slot;
            Id = Slot.Id;
            Generation = identity.Number;
            NodeManager = identity.NodeManager;
            NamespaceUris = namespaceUris;
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
