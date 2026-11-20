// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Event identifiers used by the Robot Intent server surface.
    /// </summary>
    public static class RobotIntentServerEventIds
    {
        /// <summary>
        /// The standalone node manager completed startup.
        /// </summary>
        public const int NodeManagerReady = 15000;

        /// <summary>
        /// A Robot Intent method was called before its host was started.
        /// </summary>
        public const int HostNotStarted = 15001;

        /// <summary>
        /// Node-manager host or base cleanup failed.
        /// </summary>
        public const int NodeManagerDisposalFailed = 15002;
    }
}
