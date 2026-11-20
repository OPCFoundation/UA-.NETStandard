/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AMB.Server.Health
{
    /// <summary>
    /// The OPC 10000-100 alarm type of an asset health alarm, one per NAMUR
    /// NE 107 status signal (OPC 10000-110 §9.3).
    /// </summary>
    /// <remarks>
    /// The members are ordered by precedence: when <c>DeviceHealth</c> is
    /// derived from the active alarms, the first kind among them decides.
    /// </remarks>
    public enum AssetHealthAlarmKind
    {
        /// <summary>
        /// <c>2:FailureAlarmType</c>; the asset health is <c>FAILURE</c>.
        /// </summary>
        Failure,

        /// <summary>
        /// <c>2:CheckFunctionAlarmType</c>; the asset health is
        /// <c>CHECK_FUNCTION</c>.
        /// </summary>
        CheckFunction,

        /// <summary>
        /// <c>2:OffSpecAlarmType</c>; the asset health is <c>OFF_SPEC</c>.
        /// </summary>
        OffSpec,

        /// <summary>
        /// <c>2:MaintenanceRequiredAlarmType</c>; the asset health is
        /// <c>MAINTENANCE_REQUIRED</c>.
        /// </summary>
        MaintenanceRequired
    }
}
