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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Transports;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// DI extensions for hosting an OPC UA Part 14 PubSub
    /// <see cref="IPubSubApplication"/> in a .NET Generic Host. Hangs
    /// off the central <see cref="IOpcUaBuilder"/> returned by
    /// <c>services.AddOpcUa()</c> so callers compose the PubSub feature
    /// the same way they add the server, identity or transports.
    /// </summary>
    /// <remarks>
    /// Mirrors the conventions documented in
    /// <c>docs/DependencyInjection.md</c>. The extensions register
    /// every PubSub primitive (encoders, decoders, scheduler, metadata
    /// registry, diagnostics, security policies) as singletons and
    /// finally bind an <see cref="IPubSubApplication"/> built from the
    /// resolved services. A <see cref="IHostedService"/> drives the
    /// application's lifecycle through
    /// <see cref="PubSubApplicationHostedService"/>.
    /// Implements the application bootstrap surface implied by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2">
    /// Part 14 §9.1.2</see>.
    /// </remarks>
    public static class OpcUaPubSubBuilderExtensions
    {
        /// <summary>
        /// Default configuration section name (<c>OpcUa:PubSub</c>) for
        /// the <see cref="PubSubApplicationOptions"/> bound by
        /// <see cref="AddPubSub(IOpcUaBuilder, IConfiguration)"/>.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:PubSub";

        /// <summary>
        /// Registers the OPC UA PubSub application using the supplied
        /// <paramref name="configure"/> options callback.
        /// </summary>
        /// <param name="builder">OPC UA root builder.</param>
        /// <param name="configure">Optional options callback.</param>
        /// <returns>The original <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddPubSub(
            this IOpcUaBuilder builder,
            Action<PubSubApplicationOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            OptionsBuilder<PubSubApplicationOptions> opt =
                builder.Services.AddOptions<PubSubApplicationOptions>();
            if (configure is not null)
            {
                opt.Configure(configure);
            }
            RegisterCoreServices(builder);
            return builder;
        }

        /// <summary>
        /// Registers the PubSub application with options bound from
        /// the <c>OpcUa:PubSub</c> section of <paramref name="configuration"/>.
        /// </summary>
        /// <param name="builder">OPC UA root builder.</param>
        /// <param name="configuration">Configuration root.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddPubSub(
            this IOpcUaBuilder builder,
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
            return builder.AddPubSub(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the PubSub application with options bound from
        /// the supplied <paramref name="section"/>.
        /// </summary>
        /// <param name="builder">OPC UA root builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddPubSub(
            this IOpcUaBuilder builder,
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
            builder.Services.AddOptions<PubSubApplicationOptions>().Bind(section);
            RegisterCoreServices(builder);
            return builder;
        }

        /// <summary>
        /// Registers the OPC UA PubSub application and exposes a fluent
        /// <see cref="IPubSubBuilder"/> for composing publishers,
        /// subscribers, transports, security key providers, DataSet
        /// sources / sinks, Action responders and inline configuration. Replaces the need to
        /// pre-register a hand-rolled <see cref="IPubSubApplication"/>
        /// factory before adding the feature.
        /// </summary>
        /// <param name="builder">OPC UA root builder.</param>
        /// <param name="configure">PubSub composition callback.</param>
        /// <returns>The original <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddPubSub(
            this IOpcUaBuilder builder,
            Action<IPubSubBuilder> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddOptions<PubSubApplicationOptions>();
            RegisterCoreServices(builder);
            var pubSubBuilder = new PubSubBuilder(builder);
            configure(pubSubBuilder);
            pubSubBuilder.Build();
            return builder;
        }

        /// <summary>
        /// Registers the PubSub application as a publisher only.
        /// Convenience alias for <see cref="AddPubSub(IOpcUaBuilder, Action{PubSubApplicationOptions}?)"/>.
        /// </summary>
        /// <param name="builder">OPC UA root builder.</param>
        /// <param name="configure">Optional options callback.</param>
        public static IOpcUaBuilder AddPubSubPublisher(
            this IOpcUaBuilder builder,
            Action<PubSubApplicationOptions>? configure = null)
        {
            return builder.AddPubSub(configure);
        }

        /// <summary>
        /// Registers the PubSub application as a subscriber only.
        /// Convenience alias for <see cref="AddPubSub(IOpcUaBuilder, Action{PubSubApplicationOptions}?)"/>.
        /// </summary>
        /// <param name="builder">OPC UA root builder.</param>
        /// <param name="configure">Optional options callback.</param>
        public static IOpcUaBuilder AddPubSubSubscriber(
            this IOpcUaBuilder builder,
            Action<PubSubApplicationOptions>? configure = null)
        {
            return builder.AddPubSub(configure);
        }

        /// <summary>
        /// Registers and enables the experimental JSON schema-exchange provider used by JSON NetworkMessage encoders.
        /// </summary>
        /// <param name="builder">OPC UA root builder.</param>
        /// <param name="configure">Optional JSON schema-exchange options callback.</param>
        /// <returns>The original <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddJsonSchemaExchange(
            this IOpcUaBuilder builder,
            Action<JsonSchemaExchangeOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddSchemaGeneration();
            RegisterJsonSchemaExchangeServices(builder.Services);
            if (configure is not null)
            {
                builder.Services.Configure(configure);
            }
            builder.Services.Configure<PubSubApplicationOptions>(options =>
            {
                options.JsonSchemaExchange = JsonSchemaExchangeMode.Compact;
            });
            return builder;
        }

        private static void RegisterCoreServices(IOpcUaBuilder builder)
        {
            builder.AddSchemaGeneration();
            IServiceCollection services = builder.Services;
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            services.TryAddSingleton<IDataSetMetaDataRegistry>(
                sp => new DataSetMetaDataRegistry(
                    sp.GetService<ILogger<DataSetMetaDataRegistry>>()));
            services.TryAddSingleton<IPubSubDiagnostics>(sp =>
            {
                PubSubApplicationOptions opts =
                    sp.GetRequiredService<IOptions<PubSubApplicationOptions>>().Value;
                return new PubSubDiagnostics(
                    opts.DiagnosticsLevel,
                    sp.GetService<TimeProvider>());
            });
            services.TryAddSingleton<IPubSubScheduler>(sp => new PubSubScheduler(
                sp.GetService<ITelemetryContext>(),
                sp.GetService<TimeProvider>()));

            // Standard encoders / decoders — opt-in via options.
            RegisterJsonSchemaExchangeServices(services);
            services.AddSingleton<INetworkMessageEncoder>(_ => new Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder());
            services.AddSingleton<INetworkMessageEncoder>(CreateJsonEncoder);
            services.AddSingleton<INetworkMessageDecoder>(_ => new Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder());
            services.AddSingleton<INetworkMessageDecoder>(_ => new Opc.Ua.PubSub.Encoding.Json.JsonDecoder());

            // Experimental Avro NetworkMessage encoder/decoder (Part 14 draft) so routes can
            // transcode to and from the Avro mapping alongside UADP and JSON. Registered transient so
            // each transcoding bridge gets its own progressive-schema state, which resets naturally
            // when a route is reloaded (the bridge is recreated); SchemaCache.Reset provides an
            // explicit reset, and a DataSet MetaData version change re-announces automatically.
            services.AddTransient<INetworkMessageEncoder>(_ => new Opc.Ua.PubSub.Encoding.AvroNetworkMessageEncoder());
            services.AddTransient<INetworkMessageDecoder>(_ => new Opc.Ua.PubSub.Encoding.AvroNetworkMessageDecoder());

            // Security policies.
            foreach (IPubSubSecurityPolicy policy in PubSubSecurityPolicyRegistry.All)
            {
                services.AddSingleton(policy);
            }

            // Fail-closed security wrapper resolver. Sources key providers
            // registered in DI (none by default → secured connections fail
            // to resolve and the application refuses to start in the clear).
            services.TryAddSingleton<IPubSubSecurityWrapperResolver>(sp =>
                new PubSubSecurityWrapperResolver(
                    sp.GetServices<IPubSubSecurityKeyProvider>(),
                    sp.GetRequiredService<ITelemetryContext>(),
                    sp.GetService<TimeProvider>()));

            // Configuration store: file-based if a path is supplied, otherwise inline.
            services.TryAddSingleton<IPubSubConfigurationStore>(sp =>
            {
                PubSubApplicationOptions opts =
                    sp.GetRequiredService<IOptions<PubSubApplicationOptions>>().Value;
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                TimeProvider clock = sp.GetRequiredService<TimeProvider>();
                if (!string.IsNullOrEmpty(opts.ConfigurationFilePath))
                {
                    return new XmlPubSubConfigurationStore(
                        opts.ConfigurationFilePath!, telemetry, clock);
                }
                return new InlinePubSubConfigurationStore(
                    opts.InlineConfiguration ?? new PubSubConfigurationDataType());
            });
            services.TryAddSingleton<IPubSubIdAllocator, InMemoryPubSubIdAllocator>();
            services.TryAddSingleton<IPubSubRuntimeStateStore, InMemoryPubSubRuntimeStateStore>();
            services.TryAddSingleton<IPubSubSecurityKeyStore, InMemoryPubSubSecurityKeyStore>();
            services.TryAddSingleton<IPubSubActivationCoordinator>(AlwaysActiveCoordinator.Instance);
            services.TryAddSingleton<IPubSubWriterCheckpointStore>(NullPubSubWriterCheckpointStore.Instance);
            services.TryAddSingleton<IDataSetSourceProvider, MutableDataSetSourceProvider>();
            services.TryAddSingleton<IDataSetSinkProvider, MutableDataSetSinkProvider>();

            services.TryAddSingleton<IPubSubApplication>(sp =>
            {
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                TimeProvider clock = sp.GetRequiredService<TimeProvider>();
                IPubSubConfigurationStore store =
                    sp.GetRequiredService<IPubSubConfigurationStore>();
                PubSubConfigurationDataType config =
                    store.LoadAsync(CancellationToken.None)
                        .AsTask().GetAwaiter().GetResult();
                var snapshot =
                    PubSubConfigurationSnapshot.Create(config, clock);
                return new PubSubApplication(
                    snapshot,
                    sp.GetServices<IPubSubTransportFactory>(),
                    sp.GetServices<INetworkMessageEncoder>(),
                    sp.GetServices<INetworkMessageDecoder>(),
                    sp.GetServices<IPubSubSecurityPolicy>(),
                    sp.GetRequiredService<IPubSubScheduler>(),
                    sp.GetRequiredService<IDataSetMetaDataRegistry>(),
                    sp.GetRequiredService<IPubSubDiagnostics>(),
                    telemetry,
                    clock,
                    publishedDataSetSources: null,
                    subscribedDataSetSinks: null,
                    dataSetSourceProvider: sp.GetService<IDataSetSourceProvider>(),
                    dataSetSinkProvider: sp.GetService<IDataSetSinkProvider>(),
                    securityWrapperResolver:
                        sp.GetRequiredService<IPubSubSecurityWrapperResolver>(),
                    configurationStore: store,
                    runtimeStateStore: sp.GetRequiredService<IPubSubRuntimeStateStore>(),
                    activationCoordinator:
                        sp.GetRequiredService<IPubSubActivationCoordinator>(),
                    writerCheckpointStore:
                        sp.GetRequiredService<IPubSubWriterCheckpointStore>());
            });

            services.AddSingleton<IHostedService, PubSubApplicationHostedService>();
        }

        private static Opc.Ua.PubSub.Encoding.Json.JsonEncoder CreateJsonEncoder(IServiceProvider sp)
        {
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder();
            PubSubApplicationOptions options =
                sp.GetRequiredService<IOptions<PubSubApplicationOptions>>().Value;
            if (options.JsonSchemaExchange == JsonSchemaExchangeMode.Disabled)
            {
                return encoder;
            }

            JsonSchemaExchangeOptions jsonOptions =
                sp.GetService<IOptions<JsonSchemaExchangeOptions>>()?.Value ?? new JsonSchemaExchangeOptions();
            encoder.EnableSchemaExchange = true;
            encoder.SchemaProvider = sp.GetRequiredService<IDataSetJsonSchemaProvider>();
            encoder.SchemaVerbose = options.JsonSchemaExchange == JsonSchemaExchangeMode.Verbose || jsonOptions.Verbose;
            encoder.DestinationId = jsonOptions.DestinationId ?? options.ApplicationId ?? "pubsub-json-schema-exchange";
            return encoder;
        }

        private static void RegisterJsonSchemaExchangeServices(IServiceCollection services)
        {
            services.TryAddSingleton<IDataSetJsonSchemaProvider, DataSetJsonSchemaProvider>();
        }
    }

    /// <summary>
    /// In-memory <see cref="IPubSubConfigurationStore"/> used by the DI
    /// extensions when no XML configuration file is provided. Serves a
    /// static snapshot and never raises <see cref="IPubSubConfigurationStore.Changed"/>.
    /// </summary>
    internal sealed class InlinePubSubConfigurationStore : IPubSubConfigurationStore
    {
        private readonly PubSubConfigurationDataType m_configuration;
        private ConfigurationVersionDataType? m_configurationVersion;

        public InlinePubSubConfigurationStore(PubSubConfigurationDataType configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            m_configuration = configuration;
        }

#pragma warning disable CS0067
        public event EventHandler<PubSubConfigurationChangedEventArgs>? Changed;
#pragma warning restore CS0067

        public ValueTask<PubSubConfigurationDataType> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<PubSubConfigurationDataType>(m_configuration);
        }

        public ValueTask SaveAsync(
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<ConfigurationVersionDataType?> GetConfigurationVersionAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return new ValueTask<ConfigurationVersionDataType?>(
                m_configurationVersion is null
                    ? null
                    : (ConfigurationVersionDataType)m_configurationVersion.Clone());
        }

        public ValueTask SetConfigurationVersionAsync(
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (configurationVersion is null)
            {
                throw new ArgumentNullException(nameof(configurationVersion));
            }

            m_configurationVersion = (ConfigurationVersionDataType)configurationVersion.Clone();
            return default;
        }

        public ValueTask<ConfigurationVersionDataType?> GetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            CancellationToken cancellationToken = default)
        {
            if (m_configuration.PublishedDataSets.IsNull)
            {
                return new ValueTask<ConfigurationVersionDataType?>((ConfigurationVersionDataType?)null);
            }

            foreach (PublishedDataSetDataType dataSet in m_configuration.PublishedDataSets)
            {
                if (StringComparer.Ordinal.Equals(dataSet.Name, publishedDataSetName))
                {
                    return new ValueTask<ConfigurationVersionDataType?>(
                        dataSet.DataSetMetaData?.ConfigurationVersion);
                }
            }

            return new ValueTask<ConfigurationVersionDataType?>((ConfigurationVersionDataType?)null);
        }

        public ValueTask SetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default)
        {
            if (configurationVersion is null)
            {
                throw new ArgumentNullException(nameof(configurationVersion));
            }
            if (m_configuration.PublishedDataSets.IsNull)
            {
                return default;
            }

            foreach (PublishedDataSetDataType dataSet in m_configuration.PublishedDataSets)
            {
                if (StringComparer.Ordinal.Equals(dataSet.Name, publishedDataSetName) &&
                    dataSet.DataSetMetaData is not null)
                {
                    dataSet.DataSetMetaData.ConfigurationVersion = configurationVersion;
                    break;
                }
            }

            return default;
        }
    }
}
