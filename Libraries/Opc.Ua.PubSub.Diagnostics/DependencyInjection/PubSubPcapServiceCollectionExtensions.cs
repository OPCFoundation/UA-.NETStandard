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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.PubSub.Pcap.DependencyInjection;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions that register the PubSub
    /// packet-capture diagnostics stack. The capture registry is shared with
    /// the PubSub transports, so a capture session installed here taps the
    /// live UDP / MQTT send and receive paths at zero cost when inactive.
    /// </summary>
    public static class PubSubPcapServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the shared <see cref="IPubSubCaptureRegistry"/> and a
        /// <see cref="PubSubCaptureSessionManager"/> so a PubSub capture
        /// session can be started on demand.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddPubSubPcap(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<IPubSubCaptureRegistry, PubSubCaptureRegistry>();
            services.TryAddSingleton<PubSubCaptureSessionManager>();
            return services;
        }

        /// <summary>
        /// Registers the PubSub capture stack and, when the
        /// <c>OPCUA_PUBSUB_PCAP_FILE</c> environment variable is set, an
        /// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> that
        /// auto-starts an in-process capture on host start and flushes it to
        /// the configured pcap file on host stop.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddPubSubPcapFromEnvironment(
            this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddPubSubPcap();

            string? pcapFile = Environment.GetEnvironmentVariable(
                PubSubPcapEnvironmentVariableNames.OpcuaPubSubPcapFile);
            string? keyLogFile = Environment.GetEnvironmentVariable(
                PubSubPcapEnvironmentVariableNames.OpcuaPubSubKeyLogFile);

            var options = new PubSubPcapEnvironmentOptions(
                Normalize(pcapFile),
                Normalize(keyLogFile));
            if (!options.IsEnabled)
            {
                return services;
            }

            services.TryAddSingleton(options);
            services.AddHostedService<PubSubPcapEnvironmentAutoStartHostedService>();
            return services;
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
