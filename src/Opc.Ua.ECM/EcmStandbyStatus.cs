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
