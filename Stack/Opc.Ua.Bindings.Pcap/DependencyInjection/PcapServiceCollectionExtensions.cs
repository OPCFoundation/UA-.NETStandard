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
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Pcap.Bindings;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Capture.Sources;
using Opc.Ua.Bindings.Pcap.Formats;
using Opc.Ua.Bindings.Pcap.Replay;

namespace Opc.Ua.Bindings.Pcap.DependencyInjection
{
    /// <summary>
    /// Dependency injection registration helpers for the OPC UA
    /// Bindings.Pcap package.
    /// </summary>
    public static class PcapServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the default Pcap capture services AND installs the
        /// Pcap transport channel binding into
        /// <see cref="Opc.Ua.Bindings.TransportBindings.Channels"/>.
        /// After this call every OPC UA client channel created through
        /// <see cref="Opc.Ua.ClientChannelManager"/> uses a capture-aware
        /// socket; the actual recording is gated by the
        /// <see cref="IChannelCaptureRegistry"/> and turned on or off by
        /// a <see cref="CaptureSessionManager"/>.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcap(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.AddOpcUaBindingsPcap(static _ => { });
        }

        /// <summary>
        /// Registers the Pcap capture services with caller-supplied
        /// configuration.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcap(
            this IServiceCollection services,
            Action<PcapOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new PcapOptions();
            configure(options);

            // Shared registry: the Pcap binding installed below and every
            // CaptureSessionManager created via DI talk to the SAME
            // ChannelCaptureRegistry instance, so the
            // CapturingMessageSocket's single volatile read of
            // CurrentObserver picks up StartAsync / StopAsync writes
            // without any further coordination.
            var registry = new ChannelCaptureRegistry();
            Opc.Ua.Bindings.Pcap.Bindings.PcapBindings.Install(registry);

            services.AddSingleton(options);
            services.AddSingleton<IChannelCaptureRegistry>(registry);
            services.AddSingleton<ICaptureSourceFactory>(provider =>
                new DefaultCaptureSourceFactory(
                    provider.GetRequiredService<IChannelCaptureRegistry>()));
            services.AddSingleton<CaptureSessionManager>(provider =>
            {
                PcapOptions configuredOptions = provider.GetRequiredService<PcapOptions>();
                ICaptureSourceFactory sourceFactory = provider.GetRequiredService<ICaptureSourceFactory>();
                ILoggerFactory loggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
                return new CaptureSessionManager(
                    sourceFactory,
                    configuredOptions.BaseFolder,
                    loggerFactory,
                    configuredOptions.MaxActiveSessions);
            });

            return services;
        }

        /// <summary>
        /// Adds trace formatters to dependency injection as a singleton
        /// registry.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcapFormatters(
            this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton(TraceFormatterRegistry.CreateDefault());
            return services;
        }

        /// <summary>
        /// Registers pcap replay session services.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcapReplay(
            this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<ReplaySessionManager>();
            return services;
        }
    }

    /// <summary>
    /// Options for OPC UA Pcap binding service registration.
    /// </summary>
    public sealed class PcapOptions
    {
        /// <summary>
        /// Base folder used for capture session artifacts.
        /// </summary>
        public string BaseFolder { get; set; } = Path.Combine(Path.GetTempPath(), "opcua-pcap");

        /// <summary>
        /// Maximum intended number of concurrent active sessions. Passed
        /// to the <see cref="CaptureSessionManager"/> constructor and
        /// enforced by <see cref="CaptureSessionManager.StartAsync"/>.
        /// </summary>
        public int MaxActiveSessions { get; set; } = CaptureSessionManager.DefaultMaxActiveSessions;
    }
}
