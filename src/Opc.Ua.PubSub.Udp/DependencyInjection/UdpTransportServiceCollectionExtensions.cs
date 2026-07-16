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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IPubSubBuilder"/> extensions that register the
    /// <see cref="UdpPubSubTransportFactory"/> with the OPC UA
    /// PubSub DI surface.
    /// </summary>
    /// <remarks>
    /// A UDP transport only makes sense together with the PubSub
    /// feature, so the supported surface hangs off
    /// <see cref="IPubSubBuilder"/> (returned by
    /// <c>AddPubSub(pubsub =&gt; ...)</c>). Every <c>Add*Transport</c>
    /// method returns the builder so the call chain remains composable.
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2">
    /// Part 14 §7.3.2 UDP datagram transport</see>.
    /// </remarks>
    public static class UdpTransportServiceCollectionExtensions
    {
        /// <summary>
        /// Default configuration section name read by
        /// <see cref="AddUdpTransport(IPubSubBuilder, IConfiguration)"/>.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:PubSub:Udp";

        /// <summary>
        /// Registers the
        /// <see cref="UdpPubSubTransportFactory"/> as a singleton
        /// <see cref="IPubSubTransportFactory"/> and binds
        /// <see cref="UdpTransportOptions"/> via the optional
        /// <paramref name="configure"/> callback.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configure">Optional options callback.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IUdpTransportBuilder AddUdpTransport(
            this IPubSubBuilder builder,
            Action<UdpTransportOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                builder.Services.AddOptions<UdpTransportOptions>();
            }
            else
            {
                builder.Services.AddOptions<UdpTransportOptions>().Configure(configure);
            }
            RegisterFactory(builder.Services);
            return CreateUdpTransportBuilder(builder);
        }

        /// <summary>
        /// Registers the
        /// <see cref="UdpPubSubTransportFactory"/> and binds
        /// <see cref="UdpTransportOptions"/> from <paramref name="configuration"/>.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configuration">Root configuration.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IUdpTransportBuilder AddUdpTransport(
            this IPubSubBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddUdpTransport(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the
        /// <see cref="UdpPubSubTransportFactory"/> and binds
        /// <see cref="UdpTransportOptions"/> from the supplied
        /// section.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="section">Configuration section.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IUdpTransportBuilder AddUdpTransport(
            this IPubSubBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }
            builder.Services.AddOptions<UdpTransportOptions>().Bind(section);
            RegisterFactory(builder.Services);
            return CreateUdpTransportBuilder(builder);
        }

        /// <summary>
        /// Registers DTLS 1.3 support for <c>opc.dtls://</c> unicast PubSub endpoints.
        /// </summary>
        /// <remarks>
        /// The <paramref name="configure"/> callback configures <see cref="DtlsTransportOptions"/>,
        /// including one or more <see cref="DtlsTransportOptions.LocalCertificates"/> (selected per
        /// negotiated profile certificate curve), an optional
        /// <see cref="DtlsTransportOptions.PreferredProfileName"/>, configuration-time
        /// <see cref="DtlsTransportOptions.DisabledProfiles"/>, and a
        /// <see cref="DtlsTransportOptions.PeerCertificateValidator"/>. The cipher suite/profile is
        /// selected at runtime from the enabled and runtime-supported set; profiles are never pinned
        /// by configuration. Chains on the <see cref="IUdpTransportBuilder"/> returned by
        /// <c>AddUdpTransport()</c>.
        /// </remarks>
        /// <param name="builder">UDP transport builder.</param>
        /// <param name="configure">Optional DTLS options callback.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IUdpTransportBuilder WithDtls(
            this IUdpTransportBuilder builder,
            Action<DtlsTransportOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure is null)
            {
                builder.Services.AddOptions<DtlsTransportOptions>();
            }
            else
            {
                builder.Services.AddOptions<DtlsTransportOptions>().Configure(configure);
            }

            RegisterDtls(builder.Services);
            RegisterFactory(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers DTLS support and binds options from the default UDP configuration section.
        /// </summary>
        /// <param name="builder">UDP transport builder.</param>
        /// <param name="configuration">Root configuration.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IUdpTransportBuilder WithDtls(
            this IUdpTransportBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return builder.WithDtls(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers DTLS support and binds options from the supplied configuration section.
        /// </summary>
        /// <param name="builder">UDP transport builder.</param>
        /// <param name="section">Configuration section.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IUdpTransportBuilder WithDtls(
            this IUdpTransportBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            builder.Services.AddOptions<DtlsTransportOptions>().Configure(options =>
                BindDtlsOptions(options, section));
            RegisterDtls(builder.Services);
            RegisterFactory(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers a PubSub publisher and subscriber with the UDP transport.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configure">Optional UDP transport builder callback.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddUdpPubSub(
            this IServiceCollection services,
            Action<IUdpTransportBuilder>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOpcUa().AddUdpPubSub(configure);
            return services;
        }

        /// <summary>
        /// Registers a PubSub publisher and subscriber with the UDP transport.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configure">Optional UDP transport builder callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddUdpPubSub(
            this IOpcUaBuilder builder,
            Action<IUdpTransportBuilder>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddPubSub(pubsub =>
            {
                IUdpTransportBuilder udpBuilder = pubsub
                    .AddPublisher()
                    .AddSubscriber()
                    .AddUdpTransport();
                configure?.Invoke(udpBuilder);
            });
            return builder;
        }

        /// <summary>
        /// Registers a secured UDP PubSub runtime with an SKS pull client and a default secured writer group.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="securityPolicyUri">PubSub security policy URI.</param>
        /// <param name="securityKeyServiceFactory">Security Key Service factory.</param>
        /// <param name="configureTransport">Optional UDP transport builder callback.</param>
        /// <param name="configureKeyProvider">Optional SKS pull provider options callback.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddSecureUdpPubSub(
            this IServiceCollection services,
            string securityGroupId,
            string securityPolicyUri,
            Func<IServiceProvider, ISecurityKeyService> securityKeyServiceFactory,
            Action<IUdpTransportBuilder>? configureTransport = null,
            Action<PullSecurityKeyProviderOptions>? configureKeyProvider = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOpcUa().AddSecureUdpPubSub(
                securityGroupId,
                securityPolicyUri,
                securityKeyServiceFactory,
                configureTransport,
                configureKeyProvider);
            return services;
        }

        /// <summary>
        /// Registers a secured UDP PubSub runtime with an SKS pull client and a default secured writer group.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="securityPolicyUri">PubSub security policy URI.</param>
        /// <param name="securityKeyServiceFactory">Security Key Service factory.</param>
        /// <param name="configureTransport">Optional UDP transport builder callback.</param>
        /// <param name="configureKeyProvider">Optional SKS pull provider options callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddSecureUdpPubSub(
            this IOpcUaBuilder builder,
            string securityGroupId,
            string securityPolicyUri,
            Func<IServiceProvider, ISecurityKeyService> securityKeyServiceFactory,
            Action<IUdpTransportBuilder>? configureTransport = null,
            Action<PullSecurityKeyProviderOptions>? configureKeyProvider = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddPubSub(pubsub =>
            {
                IUdpTransportBuilder udpBuilder = pubsub
                    .AddPublisher()
                    .AddSubscriber()
                    .AddUdpTransport();
                configureTransport?.Invoke(udpBuilder);
                pubsub.AddSecurityKeyServiceClient(
                    securityGroupId,
                    securityPolicyUri,
                    securityKeyServiceFactory,
                    configureKeyProvider);
                pubsub.ConfigureConfiguration(configuration => configuration
                    .AddConnection("secure-udp", connection => connection
                        .WithPublisherId(new Variant((ushort)1))
                        .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                        .WithAddress("opc.udp://224.0.0.22:4840")
                        .AddWriterGroup("secured-writer-group", writerGroup => writerGroup
                            .WithWriterGroupId(1)
                            .WithPublishingInterval(1000)
                            .WithSecurity(
                                MessageSecurityMode.SignAndEncrypt,
                                securityGroupId,
                                "opc.tcp://localhost:4840"))));
            });
            return builder;
        }

        private static void RegisterFactory(IServiceCollection services)
        {
            services.TryAddPubSubTransportFactory<UdpPubSubTransportFactory>();
        }

        private static void RegisterDtls(IServiceCollection services)
        {
            services.TryAddSingleton<DtlsProfileRegistry>();
            services.TryAddSingleton<IDtlsContextFactory, DefaultDtlsContextFactory>();
        }

        private static void BindDtlsOptions(
            DtlsTransportOptions options,
            IConfiguration section)
        {
            string? preferredProfileName = section[nameof(DtlsTransportOptions.PreferredProfileName)];
            if (!string.IsNullOrEmpty(preferredProfileName))
            {
                options.PreferredProfileName = preferredProfileName;
            }

            if (int.TryParse(
                section[nameof(DtlsTransportOptions.MaxHandshakeDatagramSize)],
                out int maxHandshakeDatagramSize))
            {
                options.MaxHandshakeDatagramSize = maxHandshakeDatagramSize;
            }

            if (TimeSpan.TryParse(
                section[nameof(DtlsTransportOptions.InitialRetransmissionTimeout)],
                out TimeSpan initialRetransmissionTimeout))
            {
                options.InitialRetransmissionTimeout = initialRetransmissionTimeout;
            }

            if (TimeSpan.TryParse(
                section[nameof(DtlsTransportOptions.MaxRetransmissionTimeout)],
                out TimeSpan maxRetransmissionTimeout))
            {
                options.MaxRetransmissionTimeout = maxRetransmissionTimeout;
            }

            if (bool.TryParse(
                section[nameof(DtlsTransportOptions.RequireHelloRetryRequestCookie)],
                out bool requireHelloRetryRequestCookie))
            {
                options.RequireHelloRetryRequestCookie = requireHelloRetryRequestCookie;
            }

            if (bool.TryParse(
                section[nameof(DtlsTransportOptions.RequireClientCertificate)],
                out bool requireClientCertificate))
            {
                options.RequireClientCertificate = requireClientCertificate;
            }

            foreach (IConfigurationSection profile in
                section.GetSection(nameof(DtlsTransportOptions.DisabledProfiles)).GetChildren())
            {
                if (!string.IsNullOrEmpty(profile.Value))
                {
                    options.DisabledProfiles.Add(profile.Value!);
                }
            }
        }

        private static IUdpTransportBuilder CreateUdpTransportBuilder(IPubSubBuilder builder)
        {
            return builder as IUdpTransportBuilder ?? new UdpTransportBuilder(builder);
        }
    }
}
