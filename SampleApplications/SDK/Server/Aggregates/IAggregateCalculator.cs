/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An interface that captures the original active API of the AggregateCalculator class
    /// required to integrate with the subscription code.
    /// </summary>
    public interface IAggregateCalculator
    {
        /// <summary>
        /// The aggregate function applied by the calculator.
        /// </summary>
        NodeId AggregateId { get; }

        /// <summary>
        /// Pushes the next raw value into the stream.
        /// </summary>
        /// <param name="value">The data value to append to the stream.</param>
        /// <returns>True if successful, false if the source timestamp has been superceeded by values already in the stream.</returns>
        bool QueueRawValue(DataValue value);

        /// <summary>
        /// Returns the next processed value.
        /// </summary>
        /// <param name="returnPartial">If true a partial interval should be processed.</param>
        /// <returns>The processed value. Null if nothing available and returnPartial is false.</returns>
        DataValue GetProcessedValue(bool returnPartial);

        /// <summary>
        /// Returns true if the specified time is later than the end of the current interval.
        /// </summary>
        /// <remarks>Return true if time flows forward and the time is later than the end time.</remarks>
        bool HasEndTimePassed(DateTime currentTime);
    }
}
