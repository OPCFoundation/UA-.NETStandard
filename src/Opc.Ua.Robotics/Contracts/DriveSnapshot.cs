// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a DriveType instance.
    /// </summary>
    public sealed record DriveSnapshot
    {
        /// <summary>
        /// The drive identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();
    }
}
