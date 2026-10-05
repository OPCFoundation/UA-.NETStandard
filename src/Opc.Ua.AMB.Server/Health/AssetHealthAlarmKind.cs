/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
