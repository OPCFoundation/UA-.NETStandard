// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Vision.MachineVision.AMCM
{
    /// <summary>
    /// Registers the OPC 40100-2 asset-management model and its generated factories.
    /// </summary>
    public static class OpcUaMachineVisionAMCMExtensions
    {
        /// <summary>
        /// Adds the generated asset-management model nodes to a collection.
        /// </summary>
        public static NodeStateCollection AddOpcUaMachineVisionAMCM(
            this NodeStateCollection nodes,
            ISystemContext context)
        {
            return OpcUaVisionMachineVisionAMCMExtensions.AddOpcUaVisionMachineVisionAMCM(
                nodes ?? throw new ArgumentNullException(nameof(nodes)),
                context ?? throw new ArgumentNullException(nameof(context)));
        }

        /// <summary>
        /// Registers the generated asset-management encodeable factories.
        /// </summary>
        public static IEncodeableFactoryBuilder AddOpcUaMachineVisionAMCM(this IEncodeableFactoryBuilder builder)
        {
            return OpcUaVisionMachineVisionAMCMExtensions.AddOpcUaVisionMachineVisionAMCM(
                builder ?? throw new ArgumentNullException(nameof(builder)));
        }

        /// <summary>
        /// Registers the generated asset-management node state factories.
        /// </summary>
        public static INodeStateFactoryBuilder AddOpcUaMachineVisionAMCM(this INodeStateFactoryBuilder builder)
        {
            return OpcUaVisionMachineVisionAMCMExtensions.AddOpcUaVisionMachineVisionAMCM(
                builder ?? throw new ArgumentNullException(nameof(builder)));
        }
    }
}
