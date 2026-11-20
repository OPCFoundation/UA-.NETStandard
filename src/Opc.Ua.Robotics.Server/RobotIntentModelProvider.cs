// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Di.Server;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Provides compiled Robot Intent model nodes.
    /// </summary>
    public interface IRobotIntentModelProvider : IDiModelProvider
    {
    }

    /// <summary>
    /// Built-in Robot Intent model provider.
    /// </summary>
    public sealed class RobotIntentModelProvider : IRobotIntentModelProvider
    {
        /// <inheritdoc/>
        public int Order => int.MinValue;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => new string[]
        {
            global::Opc.Ua.RobotIntent.Namespaces.RobotIntent
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
            nodes.AddOpcUaRobotIntent(context);
        }
    }
}
