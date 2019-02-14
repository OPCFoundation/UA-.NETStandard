/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// Defines the possible states for the condition.
    /// </summary>
    [Flags]
    public enum UnderlyingSystemAlarmStates
    {
        /// <summary>
        /// The condition state is unknown.
        /// </summary>
        Undefined = 0x0,

        /// <summary>
        /// The condition is enabled and will produce events.
        /// </summary>
        Enabled = 0x1,

        /// <summary>
        /// The condition requires acknowledgement by the user. 
        /// </summary>
        Acknowledged = 0x2,

        /// <summary>
        /// The condition requires that the used confirm that action was taken. 
        /// </summary>
        Confirmed = 0x4,

        /// <summary>
        /// The condition is active. 
        /// </summary>
        Active = 0x8,

        /// <summary>
        /// The condition has been suppressed by the system.
        /// </summary>
        Suppressed = 0x10,

        /// <summary>
        /// The condition has been shelved by the user.
        /// </summary>
        Shelved = 0x20,

        /// <summary>
        /// The condition has exceeed the high-high limit.
        /// </summary>
        HighHigh = 0x40,

        /// <summary>
        /// The condition has exceeed the high limit.
        /// </summary>
        High = 0x80,

        /// <summary>
        /// The condition has exceeed the low limit.
        /// </summary>
        Low = 0x100,

        /// <summary>
        /// The condition has exceeed the low-low limit.
        /// </summary>
        LowLow = 0x200,

        /// <summary>
        /// A mask used to clear all limit bits.
        /// </summary>
        Limits = 0x3C0,

        /// <summary>
        /// The condition has deleted.
        /// </summary>
        Deleted = 0x400
    }
}
