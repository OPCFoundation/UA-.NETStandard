// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.ModelChange
{
    /// <summary>
    /// Describes a single address-space model change reported by the server.
    /// </summary>
    /// <param name="Verb">The change verb.</param>
    /// <param name="AffectedNode">NodeId of the node affected.</param>
    /// <param name="TypeDefinition">Type definition NodeId of the
    /// affected node. <see cref="NodeId.Null"/> when the server did not
    /// report one.</param>
    public readonly record struct ModelChange(
        ModelChangeVerb Verb,
        NodeId AffectedNode,
        NodeId TypeDefinition);

    /// <summary>
    /// The kind of address-space change reported.
    /// </summary>
    [Flags]
    public enum ModelChangeVerb : byte
    {
        /// <summary>
        /// No change.
        /// </summary>
        None = 0,

        /// <summary>
        /// A new node was added.
        /// </summary>
        NodeAdded = 1,

        /// <summary>
        /// An existing node was deleted.
        /// </summary>
        NodeDeleted = 2,

        /// <summary>
        /// A reference was added.
        /// </summary>
        ReferenceAdded = 4,

        /// <summary>
        /// A reference was deleted.
        /// </summary>
        ReferenceDeleted = 8,

        /// <summary>
        /// The DataType attribute changed.
        /// </summary>
        DataTypeChanged = 16
    }

    /// <summary>
    /// Event arguments raised when the server reports address-space model changes.
    /// </summary>
    public sealed class ModelChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The reported changes.
        /// </summary>
        public IReadOnlyList<ModelChange> Changes { get; }

        /// <summary>
        /// True when the change indicates that the entire cache
        /// should be invalidated.
        /// </summary>
        public bool RequiresFullCacheInvalidation { get; }

        /// <summary>
        /// True when the tracker re-read the server namespace table
        /// before raising this event because the change indicated that
        /// the server namespace array may have changed. NodeIds from
        /// namespaces added by the server resolve from this point on.
        /// </summary>
        public bool NamespaceTableRefreshed { get; }

        /// <summary>
        /// Constructs new args.
        /// </summary>
        public ModelChangedEventArgs(
            IReadOnlyList<ModelChange> changes,
            bool requiresFullCacheInvalidation,
            bool namespaceTableRefreshed = false)
        {
            Changes = changes ?? [];
            RequiresFullCacheInvalidation = requiresFullCacheInvalidation;
            NamespaceTableRefreshed = namespaceTableRefreshed;
        }
    }

    /// <summary>
    /// Tracks server-reported address-space model changes via an
    /// event subscription on the Server object's
    /// <c>GeneralModelChangeEventType</c> notifier.
    /// </summary>
    public interface IModelChangeTracker : IAsyncDisposable
    {
        /// <summary>
        /// Raised when model changes are observed.
        /// </summary>
        event EventHandler<ModelChangedEventArgs>? ModelChanged;

        /// <summary>
        /// True once tracking has been started.
        /// </summary>
        bool IsTracking { get; }

        /// <summary>
        /// Starts tracking model changes.
        /// </summary>
        ValueTask StartTrackingAsync(CancellationToken ct = default);

        /// <summary>
        /// Stops tracking model changes.
        /// </summary>
        ValueTask StopTrackingAsync(CancellationToken ct = default);
    }
}
