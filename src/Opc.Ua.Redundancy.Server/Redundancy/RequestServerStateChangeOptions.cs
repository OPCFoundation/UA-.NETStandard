// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Options for wiring the standard OPC 10000-4 §6.6.5 <c>Server.RequestServerStateChange</c> method.
    /// </summary>
    public sealed class RequestServerStateChangeOptions
    {
        /// <summary>
        /// Gets or sets an optional administrator access validator.
        /// </summary>
        /// <remarks>
        /// When unset, the startup task uses
        /// <see cref="IConfigurationNodeManager.HasApplicationSecureAdminAccess(ISystemContext)"/>.
        /// The current implementation publishes the requested Maintenance or
        /// NoData <c>ServiceLevel</c> so Clients back off. It does not install
        /// a transport-level hook to reject newly created sessions.
        /// </remarks>
        public Action<ISystemContext>? AdminAccessValidator { get; set; }

        /// <summary>
        /// Gets or sets an optional mapping from requested server state to
        /// <c>ServiceLevel</c>.
        /// </summary>
        public Func<ServerState, byte>? ServiceLevelSelector { get; set; }

        /// <summary>
        /// Gets or sets the time provider that drives the <c>SecondsTillShutdown</c>
        /// countdown published after a <see cref="ServerState.Shutdown"/> request.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="TimeProvider.System"/>.
        /// </remarks>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    }
}
