/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.ECM
{
    /// <summary>
    /// Standard StandbyManagementStatus slots from OPC 34100 Table 31.
    /// </summary>
    public enum EcmStandbyStatus : byte
    {
        /// <summary>
        /// Energy saving is disabled.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// The device is powered off.
        /// </summary>
        PowerOff = 1,

        /// <summary>
        /// The device is ready to operate.
        /// </summary>
        ReadyToOperate = 2,

        /// <summary>
        /// The device is entering an energy-saving mode.
        /// </summary>
        MovingToEnergySavingMode = 3,

        /// <summary>
        /// The device has reached an energy-saving mode.
        /// </summary>
        EnergySavingMode = 4,

        /// <summary>
        /// The device is returning to operation.
        /// </summary>
        MovingToReadyToOperate = 5,

        /// <summary>
        /// The device is entering WOL sleep.
        /// </summary>
        MovingToSleepWol = 6,

        /// <summary>
        /// The device is in WOL sleep.
        /// </summary>
        SleepWol = 7,

        /// <summary>
        /// The device is recovering from WOL sleep.
        /// </summary>
        WakeUpWol = 8
    }
}
