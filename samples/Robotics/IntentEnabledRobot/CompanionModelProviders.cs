// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;
using Opc.Ua.OpenUsd;
using Opc.Ua.Robotics.Server;
using Opc.Ua.XRegistry;

namespace Robotics.IntentEnabledRobot
{
    /// <summary>
    /// Contributes the compiled OpenUSD companion model to the standalone Robot Intent manager.
    /// </summary>
    public sealed class OpenUsdIntentModelProvider : IRobotIntentModelProvider
    {
        /// <inheritdoc/>
        public int Order => 10;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => new string[]
        {
            Opc.Ua.OpenUsd.Namespaces.OpenUSD,
            Opc.Ua.XRegistry.Namespaces.xRegistry
        };

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

            nodes.AddOpcUaXRegistry(context);
            nodes.AddOpcUaOpenUsd(context);
        }
    }
}
