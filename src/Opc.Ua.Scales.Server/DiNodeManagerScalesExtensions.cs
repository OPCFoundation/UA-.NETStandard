// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Runtime.CompilerServices;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Direct Scales composition on an existing DI/Machinery owner.
    /// </summary>
    public static class DiNodeManagerScalesExtensions
    {
        /// <summary>
        /// Gets or creates the owner's shared Scales runtime after its models have loaded.
        /// </summary>
        /// <remarks>
        /// The first call selects the runtime options. Every DI owner releases the runtime
        /// automatically through <see cref="DiNodeManager.RegisterResource"/>.
        /// The runtime contributes capabilities to the owner without another manager or namespace.
        /// </remarks>
        /// <param name="manager">The owner that loaded DI, IA, Machinery, PackML and Scales.</param>
        /// <param name="options">Runtime options, or defaults for a new runtime.</param>
        public static ScalesRuntime CreateScalesRuntime(
            this DiNodeManager manager,
            ScalesServerOptions? options = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            options?.Validate();
            NodeId scaleType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.ScaleDeviceType, manager.SystemContext.NamespaceUris);
            if (manager.FindPredefinedNode(scaleType) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the Scales model on the owner with AddScalesToMachinery before creating scales.");
            }
            if (manager is ScalesNodeManager standalone)
            {
                return standalone.Runtime;
            }
            ScalesRuntime runtime = s_runtimes.GetValue(
                manager, owner => new Lazy<ScalesRuntime>(
                    () => new ScalesRuntime(owner, options ?? new ScalesServerOptions()))).Value;
            runtime.RegisterLifetime();
            return runtime;
        }

        /// <summary>The single runtime associated with each composed owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<ScalesRuntime>> s_runtimes = new();
    }
}
