/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.Server.Historian;

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A aggregate filter with additional state information.
    /// </summary>
    public class ServerAggregateFilter : AggregateFilter
    {
        /// <summary>
        /// Whether the variable requires stepped interpolation.
        /// </summary>
        public bool Stepped { get; set; }

        /// <summary>
        /// Whether the aggregate calculator requires historical initial-value input.
        /// </summary>
        internal bool PrimeInitialValue { get; set; }

        /// <summary>
        /// Historian used to supply initial values for the aggregate calculator.
        /// </summary>
        internal IHistorianProvider? HistorianProvider { get; set; }

        /// <summary>
        /// History capabilities of the provider supplying the aggregate's initial values.
        /// </summary>
        internal HistorianNodeCapabilities? HistorianCapabilities { get; set; }

        /// <summary>
        /// Structured-history key selector used when priming the aggregate calculator.
        /// </summary>
        internal IHistorianStructuredDataKeySelector? HistorianKeySelector { get; set; }

        /// <summary>
        /// The processing interval used when neither the request nor the server limits
        /// provide a usable positive interval.
        /// </summary>
        internal const double DefaultProcessingInterval = 1000;

        /// <summary>
        /// Revises the processing interval (Part 4 §7.22.4): a non-finite or non-positive
        /// request is replaced, and the result is at least twice the revised sampling interval,
        /// at least the server minimum and at least the historian interval.
        /// </summary>
        internal void ReviseProcessingInterval(
            double samplingInterval,
            double minimumProcessingInterval,
            double providerInterval = 0)
        {
            double requested = ProcessingInterval.IsFinite() && ProcessingInterval > 0
                ? ProcessingInterval
                : 0;
            double minimumFromSampling = samplingInterval.IsFinite() && samplingInterval > 0
                ? 2 * samplingInterval
                : 0;
            if (!minimumFromSampling.IsFinite())
            {
                minimumFromSampling = samplingInterval;
            }

            double revised = Math.Max(
                requested,
                Math.Max(
                    minimumFromSampling,
                    Math.Max(
                        ToPositiveFinite(minimumProcessingInterval),
                        ToPositiveFinite(providerInterval))));

            // the calculator needs an interval of at least one tick to advance its slices.
            if (revised * TimeSpan.TicksPerMillisecond < 1)
            {
                revised = DefaultProcessingInterval;
            }

            ProcessingInterval = revised;
        }

        /// <summary>
        /// Advances the start time to the earliest processing interval retained by the queue.
        /// </summary>
        internal void ReviseStartTime(DateTimeUtc currentTime, uint queueSize)
        {
            double retainedWindow = Math.Max((long)queueSize - 1, 0) * ProcessingInterval;
            DateTimeUtc earliestStartTime = retainedWindow.IsFinite() &&
                retainedWindow <= (currentTime - DateTimeUtc.MinValue).TotalMilliseconds
                    ? currentTime.SubtractMilliseconds(retainedWindow)
                    : DateTimeUtc.MinValue;
            if (earliestStartTime > StartTime)
            {
                StartTime = earliestStartTime;
            }
        }

        private static double ToPositiveFinite(double value)
        {
            return value.IsFinite() && value > 0 ? value : 0;
        }
    }
}
