// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Opc.Ua.Di.Server;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Perception;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Contributes compiled predefined nodes to a Vision node manager.
    /// </summary>
    /// <remarks>
    /// The built-in <see cref="VisionModelProvider"/> loads the Vision
    /// companion NodeSet. Applications can add extra providers to inject
    /// vendor extensions or pre-materialised instances that are known at
    /// startup.
    /// </remarks>
    public interface IVisionModelProvider : IDiModelProvider
    {
    }

    /// <summary>
    /// Loads dependencies required by companion providers on a Vision owner.
    /// </summary>
    /// <remarks>
    /// Dependency providers run after the built-in Vision model and before other providers,
    /// regardless of their <see cref="IDiModelProvider.Order"/>. Dependencies are ordered
    /// among themselves by priority, type name and registration order.
    /// </remarks>
    public interface IVisionDependencyModelProvider : IVisionModelProvider
    {
    }

    /// <summary>
    /// Built-in Vision model provider.
    /// </summary>
    public sealed class VisionModelProvider : IVisionModelProvider
    {
        /// <summary>
        /// Creates the backwards-compatible draft-only model provider.
        /// </summary>
        public VisionModelProvider()
            : this(MachineVisionParts.None)
        {
        }

        /// <summary>
        /// Creates a provider including the selected released models and their dependencies.
        /// </summary>
        public VisionModelProvider(MachineVisionParts parts)
        {
            if ((parts & ~MachineVisionParts.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(parts));
            }
            Parts = parts;
        }

        /// <summary>
        /// Gets the released models contributed by this provider.
        /// </summary>
        public MachineVisionParts Parts { get; }

        /// <inheritdoc/>
        public int Order => int.MinValue;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris
        {
            get
            {
                var namespaces = new List<string> { Perception.Namespaces.Vision };
                if ((Parts & MachineVisionParts.Control) != 0)
                {
                    namespaces.Add(MachineVisionModels.ControlNamespaceUri);
                }
                return namespaces.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            nodes.AddOpcUaVisionPerception(context);
            if ((Parts & MachineVisionParts.Control) != 0)
            {
                nodes.AddOpcUaMachineVision(context);
            }
        }
    }
}
