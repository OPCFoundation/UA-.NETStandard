/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Configures one bounded equipment inventory, including actual generated companion tool factories.
    /// </summary>
    public sealed record MachineToolEquipmentOptions
    {
        /// <summary>
        /// Gets the explicitly selected equipment features.
        /// </summary>
        public MachineToolFeatures Features { get; init; }

        /// <summary>
        /// Gets the maximum total number of tools and multitools in this machine, between 1 and 65536.
        /// </summary>
        public int MaximumTools { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum number of life measurements on each tool, between 1 and 256.
        /// </summary>
        public int MaximumLifeEntriesPerTool { get; init; } = 16;

        /// <summary>
        /// Gets the maximum combined number of tool and life entries, between 1 and 65536.
        /// </summary>
        public int MaximumElements { get; init; } = 4096;

        /// <summary>
        /// Gets the maximum length of each provider name, identifier and unit text.
        /// </summary>
        public int MaximumTextLength { get; init; } = 1024;

        /// <summary>
        /// Gets the optional factory for a ToolType or a genuine generated subtype such as GMS SensorType.
        /// </summary>
        public Func<ISystemContext, NodeState, QualifiedName, MachineToolTool, CancellationToken,
            ValueTask<MachineToolEquipmentNode<ToolState>>>? CreateToolAsync { get; init; }

        /// <summary>
        /// Gets the optional factory for a MultiToolType or a genuine generated subtype such as MultiSensorType.
        /// </summary>
        public Func<ISystemContext, NodeState, QualifiedName, MachineToolTool, CancellationToken,
            ValueTask<MachineToolEquipmentNode<MultiToolState>>>? CreateMultiToolAsync { get; init; }

        /// <summary>
        /// Gets the required factory for explicitly selected concrete Companion entries of BaseToolType.
        /// </summary>
        public Func<ISystemContext, NodeState, QualifiedName, MachineToolTool, CancellationToken,
            ValueTask<MachineToolEquipmentNode<BaseToolState>>>? CreateCompanionToolAsync { get; init; }

        /// <summary>
        /// Gets the final-value server-publication barrier required before dynamic removal.
        /// The public builder supplies the namespace owner's real PublishResponse barrier when omitted.
        /// </summary>
        public Func<ArrayOf<NodeState>, CancellationToken, ValueTask>? PublishFinalObservationsAsync { get; init; }
    }

    /// <summary>
    /// Transfers an actual generated equipment or prognosis node and an optional asynchronously drained resource.
    /// </summary>
    /// <typeparam name="TState">The narrowly accepted generated state type.</typeparam>
    public sealed class MachineToolEquipmentNode<TState> where TState : BaseObjectState
    {
        /// <summary>
        /// Creates an owned result; a throwing factory remains responsible for resources it has not returned.
        /// </summary>
        public MachineToolEquipmentNode(TState state, IAsyncDisposable? resource = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Resource = resource;
        }

        /// <summary>
        /// Gets the actual generated state, preserving its parent, QName, NodeId and derived children.
        /// </summary>
        public TState State { get; }

        /// <summary>
        /// Gets the resource to drain on rollback, node removal or runtime disposal.
        /// </summary>
        public IAsyncDisposable? Resource { get; }
    }
}
