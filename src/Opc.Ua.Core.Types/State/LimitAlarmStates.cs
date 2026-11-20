// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The possible states for a limit alarm.
    /// </summary>
    [Flags]
    public enum LimitAlarmStates
    {
        /// <summary>
        /// The alarm ia inactive.
        /// </summary>
        Inactive = 0x0,

        /// <summary>
        /// The alarm is in the HighHigh state.
        /// </summary>
        HighHigh = 0x1,

        /// <summary>
        /// The alarm is in the High state.
        /// </summary>
        High = 0x2,

        /// <summary>
        /// The alarm is in the Low state.
        /// </summary>
        Low = 0x4,

        /// <summary>
        /// The alarm is in the LowLow state.
        /// </summary>
        LowLow = 0x8
    }
}
