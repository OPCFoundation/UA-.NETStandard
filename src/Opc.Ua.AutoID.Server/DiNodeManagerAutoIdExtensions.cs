// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using Opc.Ua.Di.Server;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Composes AutoID readers into an existing Device Integration owner.
    /// </summary>
    public static class DiNodeManagerAutoIdExtensions
    {
        /// <summary>
        /// Gets or creates the owner's shared AutoID runtime after its models have loaded.
        /// </summary>
        /// <remarks>
        /// The first call selects the runtime options; later calls return the same runtime. A
        /// stand-alone <see cref="AutoIdNodeManager"/> returns its own <see cref="AutoIdNodeManager.Runtime"/>.
        /// The runtime registers itself with <see cref="DiNodeManager.RegisterResource"/>, so the
        /// owner disposes it on shutdown. No additional node manager is created: the readers are
        /// registered in the owner, below its <c>DeviceSet</c>.
        /// </remarks>
        /// <param name="manager">The owner that loaded the Device Integration and AutoID models.</param>
        /// <param name="options">Runtime options, or defaults for a new runtime.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException">The owner did not load the AutoID model.</exception>
        public static AutoIdRuntime CreateAutoIdRuntime(
            this DiNodeManager manager,
            AutoIdServerOptions? options = null)
        {
            return CreateAutoIdRuntime(manager, options, null);
        }

        /// <summary>
        /// Creates or retrieves the owner-scoped runtime with an optional image file binder.
        /// </summary>
        /// <remarks>
        /// The first call selects options and binder; subsequent calls share that runtime.
        /// </remarks>
        public static AutoIdRuntime CreateAutoIdRuntime(
            this DiNodeManager manager,
            AutoIdServerOptions? options,
            Opc.Ua.Server.FileSystem.IFileTypeBinder? fileBinder)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            options?.Validate();
            if (manager is AutoIdNodeManager standalone)
            {
                return standalone.Runtime;
            }
            var deviceType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.AutoIdDeviceType,
                manager.SystemContext.NamespaceUris);
            if (deviceType.IsNull || manager.FindPredefinedNode(deviceType) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the AutoID model on the owner with AddAutoIdModel or AddDiModel<AutoIdModelProvider> " +
                    "before creating readers.");
            }
            AutoIdRuntime runtime = s_runtimes.GetValue(
                manager,
                owner => new Lazy<AutoIdRuntime>(
                    () => new AutoIdRuntime(owner, options ?? new AutoIdServerOptions(), fileBinder))).Value;
            runtime.RegisterLifetime();
            return runtime;
        }

        /// <summary>The runtime associated with each composed owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<AutoIdRuntime>> s_runtimes = new();
    }
}
