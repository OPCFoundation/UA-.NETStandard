/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Options;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Augments the selected Machinery owner with MachineTool's independently selected namespace closure.
    /// </summary>
    public sealed class MachineToolModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a directly constructible provider selecting MachineTool alone.
        /// </summary>
        public MachineToolModelProvider()
            : this(MachineToolParts.MachineTool)
        {
        }

        /// <summary>
        /// Creates a directly constructible provider for an explicit independent model selection.
        /// </summary>
        /// <param name="parts">The models to load.</param>
        public MachineToolModelProvider(MachineToolParts parts)
        {
            var options = new MachineToolServerOptions { Parts = parts };
            options.Validate();
            m_parts = parts;
        }

        /// <summary>
        /// Creates an injectable provider and validates the selected owner's dependency parts.
        /// </summary>
        /// <param name="options">The selected family models.</param>
        /// <param name="ownerOptions">The existing Machinery owner's configuration.</param>
        public MachineToolModelProvider(
            IOptions<MachineToolServerOptions> options,
            IOptions<MachineryServerOptions> ownerOptions)
        {
            MachineToolServerOptions selected = options?.Value ?? throw new ArgumentNullException(nameof(options));
            MachineryServerOptions owner = ownerOptions?.Value ?? throw new ArgumentNullException(nameof(ownerOptions));
            selected.Validate();
            if ((owner.Parts & selected.RequiredMachineryParts) != selected.RequiredMachineryParts)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The Machinery owner must select the building-block, job or process-value parts required by the models.");
            }
            m_parts = selected.Parts;
        }

        /// <inheritdoc/>
        public int Order => 100;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => MachineToolModels.GetNamespaceUris(m_parts);

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            nodes.AddMachineToolTypeSystem(context, m_parts);
            context.EncodeableFactory.Builder.AddMachineToolTypeSystem(m_parts).Commit();
        }

        /// <summary>The validated, independently selected model closure.</summary>
        private readonly MachineToolParts m_parts;
    }
}
