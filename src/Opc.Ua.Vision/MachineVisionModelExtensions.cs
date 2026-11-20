/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Vision.MachineVision
{
    /// <summary>
    /// Registers the OPC 40100-1 Machine Vision model and its generated factories.
    /// </summary>
    public static class OpcUaMachineVisionExtensions
    {
        /// <summary>
        /// Adds the generated Machine Vision model nodes to a collection.
        /// </summary>
        public static NodeStateCollection AddOpcUaMachineVision(
            this NodeStateCollection nodes,
            ISystemContext context)
        {
            return OpcUaVisionMachineVisionExtensions.AddOpcUaVisionMachineVision(
                nodes ?? throw new ArgumentNullException(nameof(nodes)),
                context ?? throw new ArgumentNullException(nameof(context)));
        }

        /// <summary>
        /// Registers the generated Machine Vision encodeable factories.
        /// </summary>
        public static IEncodeableFactoryBuilder AddOpcUaMachineVision(this IEncodeableFactoryBuilder builder)
        {
            return OpcUaVisionMachineVisionExtensions.AddOpcUaVisionMachineVision(
                builder ?? throw new ArgumentNullException(nameof(builder)));
        }

        /// <summary>
        /// Registers the generated Machine Vision node state factories.
        /// </summary>
        public static INodeStateFactoryBuilder AddOpcUaMachineVision(this INodeStateFactoryBuilder builder)
        {
            return OpcUaVisionMachineVisionExtensions.AddOpcUaVisionMachineVision(
                builder ?? throw new ArgumentNullException(nameof(builder)));
        }
    }
}
