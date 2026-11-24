// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The parameters, state, and generation ownership of a resumable Browse operation.
    /// </summary>
    /// <remarks>
    /// The mutable properties and their referenced data are not synchronized by this class. The owner must
    /// serialize access to them, including changes made while the point is being disposed.
    /// </remarks>
    public class ContinuationPoint : IDisposable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ContinuationPoint()
        {
        }

        /// <summary>
        /// Disposes the continuation point.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the Browse data and session ownership once. Overrides must call the base implementation.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Action? owner = Interlocked.Exchange(ref m_ownerRelease, s_disposedOwner);
                if (ReferenceEquals(owner, s_disposedOwner))
                {
                    return;
                }
                try
                {
                    (Data as IDisposable)?.Dispose();
                }
                finally
                {
                    Generation = null;
                    owner?.Invoke();
                }
            }
        }

        /// <summary>
        /// Registers the callback that releases the owning session slot when the continuation point is
        /// disposed.
        /// </summary>
        /// <param name="releaseOwner">The callback invoked once when the continuation point is disposed.</param>
        internal void SetOwnerRelease(Action releaseOwner)
        {
            if (releaseOwner is null)
            {
                throw new ArgumentNullException(nameof(releaseOwner));
            }
            Action? previous = Interlocked.CompareExchange(ref m_ownerRelease, releaseOwner, null);
            if (ReferenceEquals(previous, s_disposedOwner))
            {
                throw new ObjectDisposedException(nameof(ContinuationPoint));
            }
            if (previous is not null)
            {
                throw new InvalidOperationException("The continuation point already has a session owner.");
            }
        }

        /// <summary>
        /// A unique identifier for the continuation point.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets the manager associated with the recorded generation or assigns a manager to record its identity.
        /// </summary>
        /// <remarks>
        /// The getter can return <c>null</c> when the weak generation identity no longer resolves. Assigning a stack
        /// adapter can record a generation owned by its wrapped manager, so the getter can return a different manager
        /// from the one assigned. The base disposal implementation clears the identity, but a later assignment can
        /// set it again.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The assigned manager is <c>null</c>.</exception>
        public IAsyncNodeManager Manager
        {
            get => Generation?.TryGetGeneration(out NodeManagerGeneration? generation) == true &&
                generation is not null
                ? generation.NodeManager
                : null!;
            set
            {
                if (value is null)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                Generation = NodeManagerGenerationRegistry.GetOrCreate(value).Reference;
            }
        }

        /// <summary>
        /// Gets or sets the weak identity of the generation recorded for this continuation.
        /// It does not keep the generation or its manager alive.
        /// </summary>
        internal NodeManagerGenerationReference? Generation { get; set; }

        /// <summary>
        /// Captures override generation identities without retaining a routing view or its managers.
        /// </summary>
        internal IReadOnlyDictionary<NodeId, NodeManagerGenerationReference>? OverrideOwners { get; set; }

        /// <summary>
        /// Tracks local targets observed by the browser without retaining their owning generations.
        /// </summary>
        internal ConcurrentDictionary<NodeId, byte> OverrideTargets { get; set; } = new();

        /// <summary>
        /// Validates previously observed targets before resuming a saved browser.
        /// </summary>
        internal void ValidateOverrideTargets(NodeManagerRoutingTable.RoutingSnapshot snapshot)
        {
            foreach (NodeId nodeId in OverrideTargets.Keys)
            {
                ValidateOverrideOwner(snapshot, nodeId);
            }
        }

        /// <summary>
        /// Rejects a saved browser before it can evaluate an unseen or lazy override dependency.
        /// </summary>
        internal void ValidateOverrideOwners(NodeManagerRoutingTable.RoutingSnapshot snapshot)
        {
            if (OverrideOwners is null)
            {
                return;
            }

            foreach (NodeId nodeId in OverrideOwners.Keys)
            {
                ValidateOverrideOwner(snapshot, nodeId);
            }
            foreach (NodeId nodeId in snapshot.NodeOverrides.Keys)
            {
                ValidateOverrideOwner(snapshot, nodeId);
            }
        }

        /// <summary>
        /// Captures and validates the source's stored references for targets affected by an override.
        /// </summary>
        internal void ValidateOverrideReferences(
            NodeManagerRoutingTable.RoutingSnapshot snapshot,
            NodeState source,
            ISystemContext context)
        {
            ArrayOf<IReference> current = source.CaptureStoredReferences(context);
            m_referenceCheck.Validate(this, snapshot, current);
        }

        /// <summary>
        /// Shares dependency state when a manager replaces its continuation object.
        /// </summary>
        internal void ShareOverrideDependencies(ContinuationPoint previous)
        {
            OverrideTargets = previous.OverrideTargets;
            m_referenceCheck = previous.m_referenceCheck;
        }

        /// <summary>
        /// Rejects a target whose override owner changed since the browser was created.
        /// </summary>
        internal void ValidateOverrideTarget(
            NodeManagerRoutingTable.RoutingSnapshot snapshot,
            ExpandedNodeId targetId)
        {
            if (targetId.IsAbsolute)
            {
                return;
            }
            var nodeId = (NodeId)targetId;
            ValidateOverrideOwner(snapshot, nodeId);
            OverrideTargets.TryAdd(nodeId, 0);
        }

        private void ValidateOverrideOwner(NodeManagerRoutingTable.RoutingSnapshot snapshot, NodeId nodeId)
        {
            NodeManagerGenerationReference? previous = null;
            OverrideOwners?.TryGetValue(nodeId, out previous);
            NodeManagerGenerationReference? current = snapshot.NodeOverrides.TryGetValue(
                nodeId, out IAsyncNodeManager? owner)
                ? snapshot.GetGeneration(owner).Reference
                : null;
            if (!ReferenceEquals(previous, current))
            {
                throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
            }
        }

        private bool IsOverrideDependency(
            NodeManagerRoutingTable.RoutingSnapshot snapshot,
            ExpandedNodeId targetId)
        {
            if (targetId.IsAbsolute)
            {
                return false;
            }
            var nodeId = (NodeId)targetId;
            return OverrideOwners?.ContainsKey(nodeId) == true || snapshot.NodeOverrides.ContainsKey(nodeId);
        }

        private sealed class ReferenceCheck
        {
            /// <summary>
            /// Captures the initial references and rejects changes to override dependencies.
            /// </summary>
            internal void Validate(
                ContinuationPoint point,
                NodeManagerRoutingTable.RoutingSnapshot snapshot,
                ArrayOf<IReference> current)
            {
                lock (m_lock)
                {
                    if (!m_initialized)
                    {
                        m_references = current;
                        m_initialized = true;
                        return;
                    }
                    var previous = new ReferenceDictionary<bool>();
                    var next = new ReferenceDictionary<bool>();
                    foreach (IReference reference in m_references)
                    {
                        previous[reference] = true;
                    }
                    foreach (IReference reference in current)
                    {
                        next[reference] = true;
                    }
                    foreach (IReference reference in previous.Keys)
                    {
                        if (point.IsOverrideDependency(snapshot, reference.TargetId) &&
                            !next.ContainsKey(reference))
                        {
                            throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
                        }
                    }
                    foreach (IReference reference in next.Keys)
                    {
                        if (point.IsOverrideDependency(snapshot, reference.TargetId) &&
                            !previous.ContainsKey(reference))
                        {
                            throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid);
                        }
                    }
                }
            }

            private readonly Lock m_lock = new();
            private bool m_initialized;
            private ArrayOf<IReference> m_references;
        }

        private ReferenceCheck m_referenceCheck = new();

        /// <summary>
        /// The view being browsed.
        /// </summary>
        public ViewDescription? View { get; set; }

        /// <summary>
        /// The node being browsed.
        /// </summary>
        public object NodeToBrowse { get; set; } = null!;

        /// <summary>
        /// The NodeId from the original Browse request, when available.
        /// </summary>
        public NodeId RequestedNodeId { get; set; }

        /// <summary>
        /// The maximum number of results to return.
        /// </summary>
        public uint MaxResultsToReturn { get; set; }

        /// <summary>
        /// What direction to follow the references.
        /// </summary>
        public BrowseDirection BrowseDirection { get; set; }

        /// <summary>
        /// The reference type of the references to return.
        /// </summary>
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Whether subtypes of the reference type should be return as well.
        /// </summary>
        public bool IncludeSubtypes { get; set; }

        /// <summary>
        /// The node class of the target nodes for the references to return.
        /// </summary>
        public uint NodeClassMask { get; set; }

        /// <summary>
        /// The values to return.
        /// </summary>
        public BrowseResultMask ResultMask { get; set; }

        /// <summary>
        /// The index where browsing halted.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets node-manager-specific data used to continue the browse.
        /// </summary>
        /// <remarks>
        /// If the data owns resources that need disposal, it must implement <see cref="IDisposable"/>. The base
        /// disposal implementation calls <see cref="IDisposable.Dispose"/> at most once and does not clear this
        /// property. The data can therefore retain a manager or other objects after disposal. If disposal starts
        /// asynchronous cleanup, its completion must be coordinated separately.
        /// </remarks>
        public object? Data { get; set; }

        /// <summary>
        /// Whether the ReferenceTypeId should be returned in the result.
        /// </summary>
        public bool ReferenceTypeIdRequired
            => ((int)ResultMask & (int)BrowseResultMask.ReferenceTypeId) != 0;

        /// <summary>
        /// Whether the IsForward flag should be returned in the result.
        /// </summary>
        public bool IsForwardRequired => ((int)ResultMask & (int)BrowseResultMask.IsForward) != 0;

        /// <summary>
        /// Whether the NodeClass should be returned in the result.
        /// </summary>
        public bool NodeClassRequired => ((int)ResultMask & (int)BrowseResultMask.NodeClass) != 0;

        /// <summary>
        /// Whether the BrowseName should be returned in the result.
        /// </summary>
        public bool BrowseNameRequired => ((int)ResultMask & (int)BrowseResultMask.BrowseName) != 0;

        /// <summary>
        /// Whether the DisplayName should be returned in the result.
        /// </summary>
        public bool DisplayNameRequired
            => ((int)ResultMask & (int)BrowseResultMask.DisplayName) != 0;

        /// <summary>
        /// Whether the TypeDefinition should be returned in the result.
        /// </summary>
        public bool TypeDefinitionRequired
            => ((int)ResultMask & (int)BrowseResultMask.TypeDefinition) != 0;

        /// <summary>
        /// False if it is not necessary to read the attributes a target node.
        /// </summary>
        /// <remarks>
        /// This flag is true if the NodeClass filter is set or the target node attributes are returned in the result.
        /// </remarks>
        public bool TargetAttributesRequired
        {
            get
            {
                if (NodeClassMask != 0)
                {
                    return true;
                }

                return (
                        (int)ResultMask &
                        (
                            (int)BrowseResultMask.NodeClass |
                            (int)BrowseResultMask.BrowseName |
                            (int)BrowseResultMask.DisplayName |
                            (int)BrowseResultMask.TypeDefinition)
                    ) != 0;
            }
        }

        private static readonly Action s_disposedOwner = static () => { };
        private Action? m_ownerRelease;
    }
}
