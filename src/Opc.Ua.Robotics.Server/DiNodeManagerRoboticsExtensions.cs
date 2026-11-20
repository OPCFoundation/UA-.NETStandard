// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Direct Robotics configuration helpers for custom DI node managers.
    /// </summary>
    public static class DiNodeManagerRoboticsExtensions
    {
        /// <summary>
        /// Creates a Robotics build context after validating that the custom
        /// manager exposes the required DI and Robotics model nodes.
        /// </summary>
        public static IRoboticsBuildContext CreateRoboticsBuildContext(
            this DiNodeManager manager,
            RoboticsServerOptions options,
            CancellationToken cancellationToken = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            return new RoboticsBuildContext(manager, options, cancellationToken);
        }
    }
}
