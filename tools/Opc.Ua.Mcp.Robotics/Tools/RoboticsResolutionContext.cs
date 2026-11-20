// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// A per-call resolution context. It resolves the controller selector once
    /// and reads the controller info exactly once, then resolves every scoped
    /// name reference in the request against that single snapshot. Resolution
    /// is read-only: it never submits work and never requests or releases
    /// command authority.
    /// </summary>
    internal sealed class RoboticsResolutionContext
    {
        private RoboticsResolutionContext(
            RobotIntentControllerClient client,
            RobotIntentControllerInfo info)
        {
            Client = client;
            Info = info;
            Scope = new RoboticsScopeResolver(info.Lookups);
        }

        /// <summary>
        /// Gets the resolved controller client.
        /// </summary>
        public RobotIntentControllerClient Client { get; }

        /// <summary>
        /// Gets the controller info snapshot, read exactly once per call.
        /// </summary>
        public RobotIntentControllerInfo Info { get; }

        /// <summary>
        /// Gets the scoped name resolver over the snapshot lookups.
        /// </summary>
        public RoboticsScopeResolver Scope { get; }

        /// <summary>
        /// Resolves the controller selector and reads its info snapshot once.
        /// </summary>
        public static async ValueTask<RoboticsResolutionContext> CreateAsync(
            RoboticsIntentManager manager,
            string controller,
            string? sessionName,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(manager);

            RobotIntentControllerClient client = await manager
                .ResolveControllerAsync(controller, sessionName, ct)
                .ConfigureAwait(false);
            return await CreateAsync(client, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the info snapshot of an already resolved controller once.
        /// </summary>
        public static async ValueTask<RoboticsResolutionContext> CreateAsync(
            RobotIntentControllerClient client,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(client);

            RobotIntentControllerInfo info = await client.ReadAsync(ct).ConfigureAwait(false);
            return new RoboticsResolutionContext(client, info);
        }
    }
}
