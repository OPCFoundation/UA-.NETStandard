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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Pcap.DependencyInjection;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions that register the PubSub
    /// packet-capture diagnostics stack. Capture is wired as a transport
    /// decorator (<see cref="CapturingPubSubTransportFactory"/>) that wraps the
    /// registered PubSub transport factories, so the UDP / MQTT transports
    /// themselves carry no capture code; a capture session installed here taps
    /// the decorated send / receive paths at zero cost when inactive.
    /// </summary>
    public static class PubSubPcapServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the shared <see cref="IPubSubCaptureRegistry"/> and a
        /// <see cref="PubSubCaptureSessionManager"/>, and decorates every
        /// already-registered <see cref="Opc.Ua.PubSub.Transports.IPubSubTransportFactory"/>
        /// with a <see cref="CapturingPubSubTransportFactory"/> so capture is
        /// injected only when this method is called. Call it AFTER the
        /// transport registrations (<c>AddUdpTransport</c> / <c>AddMqttTransport</c>).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddPubSubPcap(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<IPubSubCaptureRegistry, PubSubCaptureRegistry>();
            services.TryAddSingleton<PubSubCaptureSessionManager>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<
                IPubSubTransportFactoryDecorator,
                CapturingPubSubTransportFactoryDecorator>());
            DecorateTransportFactories(services);
            return services;
        }

        /// <summary>
        /// Registers PubSub packet capture diagnostics on the fluent PubSub builder.
        /// </summary>
        /// <param name="builder">The PubSub builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        public static IPubSubBuilder AddPcapCapture(this IPubSubBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddPubSubPcap();
            return builder;
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

            var options = new PubSubPcapEnvironmentOptions(
                Normalize(pcapFile));
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

        private static void DecorateTransportFactories(IServiceCollection services)
        {
            for (int i = 0; i < services.Count; i++)
            {
                ServiceDescriptor descriptor = services[i];
                if (descriptor.ServiceType != typeof(IPubSubTransportFactory))
                {
                    continue;
                }
                if (descriptor.ImplementationType == typeof(CapturingPubSubTransportFactory))
                {
                    continue;
                }
                if (descriptor.ImplementationType is not null &&
                    typeof(IPubSubTransportFactoryRegistration).IsAssignableFrom(descriptor.ImplementationType))
                {
                    continue;
                }
                if (descriptor.ImplementationFactory?.Target is IPubSubTransportFactoryRegistration)
                {
                    continue;
                }
                ServiceDescriptor original = descriptor;
                services[i] = ServiceDescriptor.Describe(
                    typeof(IPubSubTransportFactory),
                    new CapturingPubSubTransportFactoryRegistration(original).Resolve,
                    descriptor.Lifetime);
            }
        }

        private static object ResolveInner(IServiceProvider provider, ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationInstance is not null)
            {
                return descriptor.ImplementationInstance;
            }
            if (descriptor.ImplementationFactory is not null)
            {
                return descriptor.ImplementationFactory(provider);
            }
            if (descriptor.ImplementationType is not null)
            {
                return ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType);
            }
            throw new InvalidOperationException(
                "Transport factory descriptor has no resolvable implementation.");
        }

        private sealed class CapturingPubSubTransportFactoryDecorator : IPubSubTransportFactoryDecorator
        {
            public IPubSubTransportFactory Decorate(
                IServiceProvider provider,
                IPubSubTransportFactory factory)
            {
                return new CapturingPubSubTransportFactory(
                    factory,
                    provider.GetRequiredService<IPubSubCaptureRegistry>(),
                    provider.GetService<ILoggerFactory>());
            }
        }

        private sealed class CapturingPubSubTransportFactoryRegistration : IPubSubTransportFactoryRegistration
        {
            private readonly ServiceDescriptor m_original;

            public CapturingPubSubTransportFactoryRegistration(ServiceDescriptor original)
            {
                m_original = original;
            }

            public bool AppliesDecorators => true;

            public CapturingPubSubTransportFactory Resolve(IServiceProvider provider)
            {
                return new CapturingPubSubTransportFactory(
                    (IPubSubTransportFactory)ResolveInner(provider, m_original),
                    provider.GetRequiredService<IPubSubCaptureRegistry>(),
                    provider.GetService<ILoggerFactory>());
            }
        }
    }
}
