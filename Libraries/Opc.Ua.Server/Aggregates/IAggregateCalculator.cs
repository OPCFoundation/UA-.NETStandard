/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
