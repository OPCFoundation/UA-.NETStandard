/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server.NodeManager
{
    /// <summary>
    /// Aggregates model change events that occur during a service call
    /// so they can be reported in a single <c>GeneralModelChangeEventType</c>
    /// notification at the end of the call.
    /// </summary>
    /// <remarks>
    /// Per Part 5 §6.4.32, servers should batch model changes per
    /// transaction or publish cycle rather than emitting one event per
    /// change. This aggregator collects entries safely from concurrent
    /// callers and can be drained on the publish cycle boundary. It
    /// is used by every <c>CustomNodeManager</c> / <c>AsyncCustomNodeManager</c>
    /// (not just alarm node managers) and lives in
    /// <c>Opc.Ua.Server.NodeManager</c> to reflect its general scope.
    /// </remarks>
    public sealed class ModelChangeAggregator
    {
        private readonly Lock m_lock = new();
        private List<ModelChangeStructureDataType> m_pending = [];

        /// <summary>
        /// Adds a model change to the pending batch.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="change"/> is <c>null</c>.</exception>
        public void Add(ModelChangeStructureDataType change)
        {
            if (change == null)
            {
                throw new ArgumentNullException(nameof(change));
            }
            lock (m_lock)
            {
                m_pending.Add(change);
            }
        }

        /// <summary>
        /// Records the addition of a node.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void RecordNodeAdded(NodeId affected, NodeId? typeDefinition)
        {
            if (affected.IsNull)
            {
                throw new ArgumentNullException(nameof(affected));
            }
            Add(new ModelChangeStructureDataType
            {
                Affected = affected,
                AffectedType = typeDefinition ?? NodeId.Null,
                Verb = (byte)ModelChangeVerbs.NodeAdded
            });
        }

        /// <summary>
        /// Records the deletion of a node.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void RecordNodeDeleted(NodeId affected, NodeId? typeDefinition)
        {
            if (affected.IsNull)
            {
                throw new ArgumentNullException(nameof(affected));
            }
            Add(new ModelChangeStructureDataType
            {
                Affected = affected,
                AffectedType = typeDefinition ?? NodeId.Null,
                Verb = (byte)ModelChangeVerbs.NodeDeleted
            });
        }

        /// <summary>
        /// Records the addition of a reference.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void RecordReferenceAdded(NodeId affected, NodeId? typeDefinition = null)
        {
            if (affected.IsNull)
            {
                throw new ArgumentNullException(nameof(affected));
            }
            Add(new ModelChangeStructureDataType
            {
                Affected = affected,
                AffectedType = typeDefinition ?? NodeId.Null,
                Verb = (byte)ModelChangeVerbs.ReferenceAdded
            });
        }

        /// <summary>
        /// Records the deletion of a reference.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void RecordReferenceDeleted(NodeId affected, NodeId? typeDefinition = null)
        {
            if (affected.IsNull)
            {
                throw new ArgumentNullException(nameof(affected));
            }
            Add(new ModelChangeStructureDataType
            {
                Affected = affected,
                AffectedType = typeDefinition ?? NodeId.Null,
                Verb = (byte)ModelChangeVerbs.ReferenceDeleted
            });
        }

        /// <summary>
        /// Records a DataType change on a variable.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void RecordDataTypeChanged(NodeId affected, NodeId? typeDefinition = null)
        {
            if (affected.IsNull)
            {
                throw new ArgumentNullException(nameof(affected));
            }
            Add(new ModelChangeStructureDataType
            {
                Affected = affected,
                AffectedType = typeDefinition ?? NodeId.Null,
                Verb = (byte)ModelChangeVerbs.DataTypeChanged
            });
        }

        /// <summary>
        /// True when there are pending changes to drain.
        /// </summary>
        public bool HasPending
        {
            get
            {
                lock (m_lock)
                {
                    return m_pending.Count > 0;
                }
            }
        }

        /// <summary>
        /// Drains all pending changes, returning a snapshot suitable
        /// for use as the <c>Changes</c> array of a
        /// <c>GeneralModelChangeEventType</c>.
        /// </summary>
        public ArrayOf<ModelChangeStructureDataType> Drain()
        {
            lock (m_lock)
            {
                if (m_pending.Count == 0)
                {
                    return [];
                }

                List<ModelChangeStructureDataType> drained = m_pending;
                m_pending = [];
                return new ArrayOf<ModelChangeStructureDataType>(drained.ToArray());
            }
        }
    }

    /// <summary>
    /// Bit values for the <c>Verb</c> field of
    /// <c>ModelChangeStructureDataType</c>.
    /// </summary>
    [Flags]
    public enum ModelChangeVerbs : byte
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
}
