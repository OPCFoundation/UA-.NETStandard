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
using System.Collections.Generic;
using System.Globalization;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Shared environment-variable presets used to launch the redundant sample
    /// applications from the integration tests.
    /// </summary>
    internal static class SampleTestEnvironment
    {
        /// <summary>
        /// Environment for the single-process PubSub demo that shortens the two demo
        /// phases so the failover narrative completes quickly during tests.
        /// </summary>
        public static IReadOnlyDictionary<string, string> FastDemo { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DEMO_FIRST_SECONDS"] = "1",
                ["DEMO_SECOND_SECONDS"] = "1"
            };

        /// <summary>
        /// Environment for a plain, independent managed RedundantClient (one that fails
        /// over on its own rather than joining a coordinated client replica set).
        /// </summary>
        public static IReadOnlyDictionary<string, string> IndependentClient { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CLIENT_MODE"] = "independent"
            };

        /// <summary>
        /// Reads the configured long-haul duration in minutes from the
        /// <c>SAMPLE_HA_DURATION_MINUTES</c> environment variable, falling back to the
        /// supplied default when it is unset or invalid.
        /// </summary>
        /// <param name="defaultMinutes">The default duration in minutes.</param>
        /// <returns>The configured long-haul duration.</returns>
        public static TimeSpan LongHaulDuration(double defaultMinutes)
        {
            string? configured = Environment.GetEnvironmentVariable("SAMPLE_HA_DURATION_MINUTES");
            if (!string.IsNullOrWhiteSpace(configured) &&
                double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) &&
                minutes > 0)
            {
                return TimeSpan.FromMinutes(minutes);
            }

            return TimeSpan.FromMinutes(defaultMinutes);
        }
    }
}
