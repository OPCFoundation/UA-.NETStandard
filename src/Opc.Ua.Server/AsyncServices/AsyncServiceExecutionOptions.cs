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

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Selects the default-off 0.2.0 experiment and supported exact lifetimes, in milliseconds.
    /// Message and resource limits remain the existing transport and resource-isolation limits.
    /// </summary>
    public sealed class AsyncServiceExecutionOptions
    {
        /// <summary>
        /// Gets or sets whether the Server installs the experimental execution provider and model at startup.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the minimum positive execution allowance.
        /// </summary>
        public double MinExecutionAllowance { get; set; } = 1;

        /// <summary>
        /// Gets or sets the maximum finite execution allowance.
        /// </summary>
        public double MaxExecutionAllowance { get; set; } = 600_000;

        /// <summary>
        /// Gets or sets the advertised execution allowance a Client may explicitly request.
        /// </summary>
        public double DefaultExecutionAllowance { get; set; } = 30_000;

        /// <summary>
        /// Gets or sets the minimum positive result-retention and subsequent history interval.
        /// </summary>
        public double MinResultRetentionTime { get; set; } = 1;

        /// <summary>
        /// Gets or sets the maximum finite result-retention and subsequent history interval.
        /// </summary>
        public double MaxResultRetentionTime { get; set; } = 600_000;

        /// <summary>
        /// Gets or sets the advertised result-retention interval a Client may explicitly request.
        /// </summary>
        public double DefaultResultRetentionTime { get; set; } = 30_000;

        /// <summary>
        /// Captures validated startup options independently of later host configuration mutations.
        /// </summary>
        internal AsyncServiceExecutionOptions Snapshot()
        {
            ValidateRange(MinExecutionAllowance, DefaultExecutionAllowance, MaxExecutionAllowance);
            ValidateRange(MinResultRetentionTime, DefaultResultRetentionTime, MaxResultRetentionTime);
            return new AsyncServiceExecutionOptions
            {
                Enabled = Enabled,
                MinExecutionAllowance = MinExecutionAllowance,
                MaxExecutionAllowance = MaxExecutionAllowance,
                DefaultExecutionAllowance = DefaultExecutionAllowance,
                MinResultRetentionTime = MinResultRetentionTime,
                MaxResultRetentionTime = MaxResultRetentionTime,
                DefaultResultRetentionTime = DefaultResultRetentionTime
            };
        }

        /// <summary>
        /// Validates a timer-representable range without revising a Client's requested duration.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateRange(double minimum, double defaultValue, double maximum)
        {
            if (double.IsNaN(minimum) ||
                double.IsNaN(defaultValue) ||
                double.IsNaN(maximum) ||
                minimum <= 0 ||
                minimum > defaultValue ||
                defaultValue > maximum ||
                maximum > int.MaxValue)
            {
                throw new ArgumentException("Async Service lifetimes require finite, positive, ordered timer ranges.");
            }
        }
    }
}
