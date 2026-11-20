/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Loads the OPC 30010 AutoID model into a Device Integration based node manager, next to the
    /// Device Integration model its core provider loads.
    /// </summary>
    /// <remarks>
    /// Registered with <c>AddAutoIdModel</c> or the generic <c>AddDiModel</c> helper. Only the AutoID nodes
    /// are added: the DI nodes the owner loaded already are kept, so DI is loaded once however
    /// many companions are composed.
    /// </remarks>
    public sealed class AutoIdModelProvider : Opc.Ua.Di.Server.IDiModelProvider
    {
        /// <summary>
        /// Creates a provider with optional additional namespace registrations.
        /// </summary>
        /// <param name="options">The AutoID options; defaults when <see langword="null"/>.</param>
        public AutoIdModelProvider(IOptions<AutoIdServerOptions>? options = null)
            : this(options?.Value)
        {
        }

        /// <summary>
        /// Creates a provider for explicit options, as the stand-alone owner does.
        /// </summary>
        internal AutoIdModelProvider(AutoIdServerOptions? options)
        {
            m_options = (options ?? new AutoIdServerOptions()).Validate();
        }

        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            ((ArrayOf<string>)[AutoIdModel.NamespaceUri]).AddItems(m_options.AdditionalNamespaceUris);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException">
        /// The owner did not load the Device Integration model.
        /// </exception>
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
            var deviceSet = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == deviceSet))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "AutoID composition requires an owner that loads the Device Integration model first.");
            }
            var deviceType = ExpandedNodeId.ToNodeId(ObjectTypeIds.AutoIdDeviceType, context.NamespaceUris);
            if (nodes.Any(node => node.NodeId == deviceType))
            {
                return;
            }

            // The scan settings and results travel as structures: the server has to decode and
            // encode them for the method calls and events.
            if (!context.EncodeableFactory.ContainsEncodeableType(DataTypeIds.ScanSettings))
            {
                context.EncodeableFactory.Builder.AddAutoIdTypeSystem().Commit();
            }
            nodes.AddAutoIdTypeSystem(context);
        }

        /// <summary>The validated model and namespace options.</summary>
        private readonly AutoIdServerOptions m_options;
    }
}
