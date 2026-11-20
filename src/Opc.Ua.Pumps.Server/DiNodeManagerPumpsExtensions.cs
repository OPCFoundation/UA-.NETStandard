/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Runtime.CompilerServices;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Direct Pumps composition on an existing DI/Machinery address-space owner.
    /// </summary>
    public static class DiNodeManagerPumpsExtensions
    {
        /// <summary>
        /// Gets or creates the owner's shared Pumps runtime after its models have loaded.
        /// </summary>
        /// <remarks>
        /// The first call selects the runtime options. Every DI owner releases the runtime
        /// automatically through <see cref="DiNodeManager.RegisterResource"/>.
        /// No additional node manager or instance namespace is created.
        /// </remarks>
        /// <param name="manager">The owner that loaded DI, IA, Machinery and Pumps.</param>
        /// <param name="options">Runtime options, or defaults for a new runtime.</param>
        public static PumpsRuntime CreatePumpsRuntime(
            this DiNodeManager manager,
            PumpsServerOptions? options = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            options?.Validate();
            NodeId pumpType = ExpandedNodeId.ToNodeId(ObjectTypeIds.PumpType, manager.SystemContext.NamespaceUris);
            if (manager.FindPredefinedNode(pumpType) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the Pumps model on the owner with AddPumpsToMachinery before creating pumps.");
            }
            if (manager is PumpsNodeManager standalone)
            {
                return standalone.Runtime;
            }
            PumpsRuntime runtime = s_runtimes.GetValue(
                manager, owner => new Lazy<PumpsRuntime>(
                    () => new PumpsRuntime(owner, options ?? new PumpsServerOptions()))).Value;
            runtime.RegisterLifetime();
            return runtime;
        }

        /// <summary>The runtime associated with each composed owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<PumpsRuntime>> s_runtimes = new();
    }
}
