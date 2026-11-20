/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Selects independently hosted MachineTool and wireless peripheral models.
    /// </summary>
    public sealed class MachineToolServerOptions
    {
        /// <summary>
        /// Gets or sets the model selection; MachineTool alone is the default.
        /// </summary>
        public MachineToolParts Parts { get; set; } = MachineToolParts.MachineTool;

        /// <summary>
        /// Gets or sets the standalone owner's application namespace.
        /// Shared-owner composition uses the existing owner's namespace instead.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = "urn:opcua-netstandard:machinetool:instances";

        /// <summary>
        /// Gets the Machinery parts required by the selected model closure.
        /// </summary>
        internal MachineryParts RequiredMachineryParts => MachineryParts.BuildingBlocks |
            ((Parts & MachineToolParts.MachineTool) != 0 ? MachineryParts.Jobs : MachineryParts.None) |
            ((Parts & MachineToolParts.Wmtp) != 0 ? MachineryParts.ProcessValues : MachineryParts.None);

        /// <summary>
        /// Rejects empty or unknown model selections and invalid application namespaces.
        /// </summary>
        public void Validate()
        {
            if (Parts == MachineToolParts.None || (Parts & ~MachineToolParts.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Parts));
            }
            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute application namespace URI is required.",
                    nameof(InstanceNamespaceUri));
            }
            foreach (string model in MachineToolModels.GetNamespaceUris(Parts))
            {
                if (string.Equals(model, InstanceNamespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException("The application namespace must not be a model namespace.",
                        nameof(InstanceNamespaceUri));
                }
            }
            if (string.Equals(InstanceNamespaceUri, Opc.Ua.Namespaces.OpcUa, StringComparison.Ordinal))
            {
                throw new ArgumentException("The application namespace must not be the OPC UA namespace.",
                    nameof(InstanceNamespaceUri));
            }
        }
    }
}
