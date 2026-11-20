/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua;
using Opc.Ua.Gpos;
using Opc.Ua.OpenUsd;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Rsl;
using Opc.Ua.XRegistry;

namespace Robotics
{
    /// <summary>
    /// Contributes the compiled OpenUSD binding model to stock Robotics hosting.
    /// </summary>
    public sealed class OpenUsdModelProvider : IRoboticsModelProvider
    {
        /// <inheritdoc/>
        public int Order => 0;

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

    /// <summary>
    /// Contributes the compiled RSL model to stock Robotics hosting.
    /// </summary>
    public sealed class RslModelProvider : IRoboticsModelProvider
    {
        /// <inheritdoc/>
        public int Order => 10;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => new string[]
        {
            Opc.Ua.Rsl.Namespaces.RSL
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

            nodes.AddOpcUaRsl(context);
        }
    }

    /// <summary>
    /// Contributes the compiled GPOS model to stock Robotics hosting.
    /// </summary>
    public sealed class GposModelProvider : IRoboticsModelProvider
    {
        /// <inheritdoc/>
        public int Order => 20;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => new string[]
        {
            Opc.Ua.Gpos.Namespaces.GPOS
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

            nodes.AddOpcUaGpos(context);
        }
    }
}
