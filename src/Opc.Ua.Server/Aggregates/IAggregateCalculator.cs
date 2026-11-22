// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// <returns>True if successful, false if the source timestamp has been superseded by values already in the stream.</returns>
        bool QueueRawValue(DataValue value);

        /// <summary>
        /// Tries to produce the next processed value.
        /// </summary>
        /// <param name="returnPartial">If true a partial interval should be processed.</param>
        /// <param name="value">The processed value when this method returns <c>true</c>.</param>
        /// <returns><c>true</c> if a processed value is available; otherwise <c>false</c>.</returns>
        bool TryGetProcessedValue(bool returnPartial, out DataValue value);

        /// <summary>
        /// Returns true if the specified time is later than the end of the current interval.
        /// </summary>
        /// <remarks>Return true if time flows forward and the time is later than the end time.</remarks>
        bool HasEndTimePassed(DateTimeUtc currentTime);
    }
}
