// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Shared environment-variable helpers used to launch the redundant sample
    /// applications from the integration tests. Factory methods rather than
    /// static properties are used where the environment depends on a freshly
    /// allocated port so that parallel test children do not share the same
    /// network resource.
    /// </summary>
    internal static class SampleTestEnvironment
    {
        /// <summary>
        /// Builds an environment for the single-process PubSub demo that shortens
        /// the two demo phases so the failover narrative completes quickly during
        /// tests. Each call allocates a fresh loopback UDP port so that parallel
        /// test processes do not contend on the same endpoint.
        /// </summary>
        /// <returns>
        /// A new environment dictionary with a unique <c>PUBSUB_ENDPOINT</c> for
        /// this child process.
        /// </returns>
        public static IReadOnlyDictionary<string, string?> BuildFastDemo()
        {
            int port = TestPorts.GetFreeUdpPort();
            return new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DEMO_FIRST_SECONDS"] = "1",
                ["DEMO_SECOND_SECONDS"] = "1",
                ["PUBSUB_ENDPOINT"] = "opc.udp://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture),
                ["HA_INSECURE"] = "true"
            };
        }

        /// <summary>
        /// Environment for an independent client in the explicit loopback reconnect demo.
        /// </summary>
        public static IReadOnlyDictionary<string, string?> IndependentClient { get; } =
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["CLIENT_MODE"] = "independent",
                ["HA_INSECURE"] = "true"
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
