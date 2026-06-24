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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Manual non-DI fluent builder for an
    /// <see cref="IPubSubApplication"/>. Mirrors the
    /// <c>ManagedSessionBuilder</c> pattern from
    /// <c>Opc.Ua.Client</c>: accumulate state via
    /// <c>With*</c> / <c>Use*</c> / <c>Configure*</c>; call
    /// <see cref="Build"/> or <see cref="BuildAndStartAsync"/> to
    /// materialise the application. Use this builder for samples,
    /// tests, or any caller that does not run inside a
    /// generic host.
    /// </summary>
    /// <remarks>
    /// Provides the same composition surface as
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaPubSubBuilderExtensions.AddPubSub(Opc.Ua.IOpcUaBuilder, Action{PubSubApplicationOptions}?)"/>
    /// but without the
    /// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
    /// dependency. Implements the application bootstrap surface
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2">
    /// Part 14 §9.1.2</see>.
    /// </remarks>
    public sealed class PubSubApplicationBuilder
    {
        private readonly ITelemetryContext m_telemetry;
        private readonly List<IPubSubTransportFactory> m_transportFactories = [];
        private readonly List<INetworkMessageEncoder> m_encoders = [];
        private readonly List<INetworkMessageDecoder> m_decoders = [];
        private readonly List<IPubSubSecurityPolicy> m_policies = [];
        private readonly List<EndpointDescription> m_sksEndpoints = [];
        private readonly List<IPubSubSecurityKeyProvider> m_keyProviders = [];
        private readonly Dictionary<string, IPublishedDataSetSource> m_dataSetSources
            = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ISubscribedDataSetSink> m_dataSetSinks
            = new(StringComparer.Ordinal);
        private readonly List<(PubSubActionTarget Target, IPubSubActionHandler Handler,
            bool AllowUnsecured, PubSubResponseAddressPolicy? ResponseAddressPolicy)>
            m_actionResponders = [];
        private readonly PubSubApplicationOptions m_options = new();
        private IUaPubSubDataStore? m_dataStore;
        private IDataSetSourceProvider? m_dataSetSourceProvider;
        private IDataSetSinkProvider? m_dataSetSinkProvider;
        private TimeProvider m_timeProvider = TimeProvider.System;
        private InMemoryPubSubKeyServiceServer? m_sksServer;
        private PubSubConfigurationDataType? m_configuration;
        private string? m_configurationFilePath;
        private IPubSubSecurityWrapperResolver? m_securityWrapperResolver;
        private Func<PubSubConnectionDataType, string, IPubSubSecurityPolicy?>?
            m_securityPolicySelector;

        /// <summary>
        /// Initializes a new <see cref="PubSubApplicationBuilder"/>.
        /// </summary>
        /// <param name="telemetry">
        /// Required telemetry context. Use
        /// <c>ServiceProviderTelemetryContext</c> when bridging with
        /// DI, or a custom <c>TelemetryContextBase</c> implementation
        /// for tests.
        /// </param>
        public PubSubApplicationBuilder(ITelemetryContext telemetry)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_telemetry = telemetry;
            foreach (IPubSubSecurityPolicy policy in PubSubSecurityPolicyRegistry.All)
            {
                m_policies.Add(policy);
            }
        }

        /// <summary>
        /// Sets the application identifier.
        /// </summary>
        /// <param name="id">Application identifier.</param>
        public PubSubApplicationBuilder WithApplicationId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("id must not be empty.", nameof(id));
            }
            m_options.ApplicationId = id;
            return this;
        }

        /// <summary>
        /// Uses the supplied inline <see cref="PubSubConfigurationDataType"/>.
        /// </summary>
        /// <param name="config">Configuration.</param>
        public PubSubApplicationBuilder UseConfiguration(PubSubConfigurationDataType config)
        {
            if (config is null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            m_configuration = config;
            return this;
        }

        /// <summary>
        /// Loads the configuration from an XML file at
        /// <see cref="Build"/> time via
        /// <see cref="XmlPubSubConfigurationStore"/>.
        /// </summary>
        /// <param name="path">Path to the XML configuration file.</param>
        public PubSubApplicationBuilder UseConfigurationFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("path must not be empty.", nameof(path));
            }
            m_configurationFilePath = path;
            return this;
        }

        /// <summary>
        /// Mutates the accumulated
        /// <see cref="PubSubApplicationOptions"/> via <paramref name="configure"/>.
        /// </summary>
        /// <param name="configure">Options callback.</param>
        public PubSubApplicationBuilder Configure(Action<PubSubApplicationOptions> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(m_options);
            return this;
        }

        /// <summary>
        /// Sets the diagnostics verbosity.
        /// </summary>
        /// <param name="level">Diagnostics level.</param>
        public PubSubApplicationBuilder WithDiagnosticsLevel(PubSubDiagnosticsLevel level)
        {
            m_options.DiagnosticsLevel = level;
            return this;
        }

        /// <summary>
        /// Overrides the wall clock used by the runtime. Tests pass a
        /// <c>FakeTimeProvider</c> here.
        /// </summary>
        /// <param name="clock">Clock.</param>
        public PubSubApplicationBuilder WithTimeProvider(TimeProvider clock)
        {
            if (clock is null)
            {
                throw new ArgumentNullException(nameof(clock));
            }
            m_timeProvider = clock;
            return this;
        }

        /// <summary>
        /// Sets the runtime provider queried for PublishedDataSet sources that are not
        /// registered through <see cref="AddDataSetSource(string, IPublishedDataSetSource)"/>.
        /// </summary>
        /// <param name="provider">Runtime source provider.</param>
        public PubSubApplicationBuilder WithDataSetSourceProvider(IDataSetSourceProvider provider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            m_dataSetSourceProvider = provider;
            return this;
        }

        /// <summary>
        /// Sets the runtime provider queried for DataSetReader sinks that are not
        /// registered through <see cref="AddSubscribedDataSetSink(string, ISubscribedDataSetSink)"/>.
        /// </summary>
        /// <param name="provider">Runtime sink provider.</param>
        public PubSubApplicationBuilder WithDataSetSinkProvider(IDataSetSinkProvider provider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            m_dataSetSinkProvider = provider;
            return this;
        }

        /// <summary>
        /// Registers a legacy <see cref="IUaPubSubDataStore"/> as the
        /// data source for every <c>PublishedDataSet</c> that does not
        /// have an explicit <see cref="IPublishedDataSetSource"/>
        /// registered via
        /// <see cref="AddDataSetSource(string, IPublishedDataSetSource)"/>.
        /// </summary>
        /// <param name="dataStore">Legacy data store.</param>
        public PubSubApplicationBuilder WithDataStore(IUaPubSubDataStore dataStore)
        {
            if (dataStore is null)
            {
                throw new ArgumentNullException(nameof(dataStore));
            }
            m_dataStore = dataStore;
            return this;
        }

        /// <summary>
        /// Adds an <see cref="IPubSubTransportFactory"/> to the
        /// builder. Convenience overloads
        /// <c>AddUdpTransport</c> / <c>AddMqttTransport</c> are
        /// provided by the per-transport assemblies.
        /// </summary>
        /// <param name="factory">Factory instance.</param>
        public PubSubApplicationBuilder AddTransportFactory(IPubSubTransportFactory factory)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            m_transportFactories.Add(factory);
            return this;
        }

        /// <summary>
        /// Adds an <see cref="INetworkMessageEncoder"/>.
        /// </summary>
        /// <param name="encoder">Encoder instance.</param>
        public PubSubApplicationBuilder AddEncoder(INetworkMessageEncoder encoder)
        {
            if (encoder is null)
            {
                throw new ArgumentNullException(nameof(encoder));
            }
            m_encoders.Add(encoder);
            return this;
        }

        /// <summary>
        /// Adds an <see cref="INetworkMessageDecoder"/>.
        /// </summary>
        /// <param name="decoder">Decoder instance.</param>
        public PubSubApplicationBuilder AddDecoder(INetworkMessageDecoder decoder)
        {
            if (decoder is null)
            {
                throw new ArgumentNullException(nameof(decoder));
            }
            m_decoders.Add(decoder);
            return this;
        }

        /// <summary>
        /// Adds an SKS endpoint the runtime may pull keys from.
        /// </summary>
        /// <param name="endpoint">Endpoint description.</param>
        public PubSubApplicationBuilder AddSecurityKeyServiceClient(EndpointDescription endpoint)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            m_sksEndpoints.Add(endpoint);
            m_options.SecurityKeyServiceEndpoints.Add(endpoint);
            return this;
        }

        /// <summary>
        /// Adds an in-memory <see cref="InMemoryPubSubKeyServiceServer"/>
        /// to the application. The server is built on-demand inside
        /// <see cref="Build"/> using <paramref name="configure"/>.
        /// </summary>
        /// <param name="configure">Optional configuration callback.</param>
        public PubSubApplicationBuilder AddSecurityKeyServiceServer(
            Action<InMemoryPubSubKeyServiceServer>? configure = null)
        {
            m_sksServer = new InMemoryPubSubKeyServiceServer(m_timeProvider, m_telemetry);
            configure?.Invoke(m_sksServer);
            return this;
        }

        /// <summary>
        /// Registers an <see cref="IPubSubSecurityKeyProvider"/> that
        /// supplies key material for its
        /// <see cref="IPubSubSecurityKeyProvider.SecurityGroupId"/>. The
        /// builder feeds every registered provider into the default
        /// <see cref="PubSubSecurityWrapperResolver"/> unless an explicit
        /// resolver is supplied via
        /// <see cref="WithSecurityWrapperResolver"/>.
        /// </summary>
        /// <param name="keyProvider">Key provider instance.</param>
        public PubSubApplicationBuilder AddSecurityKeyProvider(
            IPubSubSecurityKeyProvider keyProvider)
        {
            if (keyProvider is null)
            {
                throw new ArgumentNullException(nameof(keyProvider));
            }
            m_keyProviders.Add(keyProvider);
            return this;
        }

        /// <summary>
        /// Overrides the policy selection used by the default
        /// <see cref="PubSubSecurityWrapperResolver"/>. The callback maps
        /// a connection plus SecurityGroupId to the
        /// <see cref="IPubSubSecurityPolicy"/> to apply.
        /// </summary>
        /// <param name="selector">Policy selection callback.</param>
        public PubSubApplicationBuilder WithSecurityPolicySelector(
            Func<PubSubConnectionDataType, string, IPubSubSecurityPolicy?> selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            m_securityPolicySelector = selector;
            return this;
        }

        /// <summary>
        /// Supplies an explicit
        /// <see cref="IPubSubSecurityWrapperResolver"/>, bypassing the
        /// default resolver built from the registered key providers.
        /// </summary>
        /// <param name="resolver">Resolver instance.</param>
        public PubSubApplicationBuilder WithSecurityWrapperResolver(
            IPubSubSecurityWrapperResolver resolver)
        {
            if (resolver is null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }
            m_securityWrapperResolver = resolver;
            return this;
        }

        /// <summary>
        /// Wires an <see cref="IPublishedDataSetSource"/> for the
        /// PublishedDataSet named <paramref name="publishedDataSetName"/>.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <param name="source">Source implementation.</param>
        public PubSubApplicationBuilder AddDataSetSource(
            string publishedDataSetName,
            IPublishedDataSetSource source)
        {
            if (string.IsNullOrEmpty(publishedDataSetName))
            {
                throw new ArgumentException(
                    "publishedDataSetName must not be empty.",
                    nameof(publishedDataSetName));
            }
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            m_dataSetSources[publishedDataSetName] = source;
            return this;
        }

        /// <summary>
        /// Registers a PublishedAction source for the named PublishedDataSet.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <param name="action">Published action configuration.</param>
        public PubSubApplicationBuilder AddPublishedAction(
            string publishedDataSetName,
            PublishedActionDataType action)
        {
            if (string.IsNullOrEmpty(publishedDataSetName))
            {
                throw new ArgumentException(
                    "publishedDataSetName must not be empty.",
                    nameof(publishedDataSetName));
            }

            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            m_dataSetSources[publishedDataSetName] = new PublishedActionSource(action);
            return this;
        }

        /// <summary>
        /// Registers a PublishedActionMethod source for the named PublishedDataSet.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <param name="action">Published method-action configuration.</param>
        public PubSubApplicationBuilder AddPublishedAction(
            string publishedDataSetName,
            PublishedActionMethodDataType action)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            return AddPublishedAction(publishedDataSetName, (PublishedActionDataType)action);
        }

        /// <summary>
        /// Registers a responder-side PubSub Action handler for the target.
        /// </summary>
        /// <param name="target">Action target handled by <paramref name="handler"/>.</param>
        /// <param name="handler">Action handler.</param>
        /// <param name="allowUnsecured">Allow serving the Action on an unsecured connection.</param>
        /// <param name="responseAddressPolicy">
        /// Optional policy validating the requestor-supplied response address (SA-ACT-03).
        /// Defaults to <see cref="PubSubResponseAddressPolicy.Default"/>.
        /// </param>
        public PubSubApplicationBuilder AddActionResponder(
            PubSubActionTarget target,
            IPubSubActionHandler handler,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            m_actionResponders.Add((target, handler, allowUnsecured, responseAddressPolicy));
            return this;
        }

        /// <summary>
        /// Registers a delegate-backed responder-side PubSub Action handler for the target.
        /// </summary>
        /// <param name="target">Action target handled by <paramref name="handler"/>.</param>
        /// <param name="handler">Delegate action handler.</param>
        /// <param name="allowUnsecured">Allow serving the Action on an unsecured connection.</param>
        /// <param name="responseAddressPolicy">
        /// Optional policy validating the requestor-supplied response address (SA-ACT-03).
        /// Defaults to <see cref="PubSubResponseAddressPolicy.Default"/>.
        /// </param>
        public PubSubApplicationBuilder AddActionResponder(
            PubSubActionTarget target,
            Func<PubSubActionInvocation, CancellationToken, ValueTask<PubSubActionHandlerResult>> handler,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return AddActionResponder(
                target, new DelegatePubSubActionHandler(handler), allowUnsecured, responseAddressPolicy);
        }

        /// <summary>
        /// Wires an <see cref="ISubscribedDataSetSink"/> for the
        /// DataSetReader named <paramref name="dataSetReaderName"/>.
        /// </summary>
        /// <param name="dataSetReaderName">DataSetReader name.</param>
        /// <param name="sink">Sink implementation.</param>
        public PubSubApplicationBuilder AddSubscribedDataSetSink(
            string dataSetReaderName,
            ISubscribedDataSetSink sink)
        {
            if (string.IsNullOrEmpty(dataSetReaderName))
            {
                throw new ArgumentException(
                    "dataSetReaderName must not be empty.",
                    nameof(dataSetReaderName));
            }
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }
            m_dataSetSinks[dataSetReaderName] = sink;
            return this;
        }

        /// <summary>
        /// In-memory SKS server attached via
        /// <see cref="AddSecurityKeyServiceServer"/>, exposed for the
        /// caller to wire into its OPC UA Server's NodeManager.
        /// </summary>
        public InMemoryPubSubKeyServiceServer? SecurityKeyServiceServer => m_sksServer;

        /// <summary>
        /// Resolves the PubSub configuration currently assigned to the builder,
        /// loading it from the configured XML file when a file path was supplied
        /// and returning an empty configuration when none was set. Intended for
        /// composition steps that must enumerate the configured datasets and
        /// readers before <see cref="Build"/> runs.
        /// </summary>
        /// <returns>
        /// The resolved configuration, or an empty configuration when none was
        /// supplied.
        /// </returns>
        /// <exception cref="PubSubApplicationBuildException">
        /// Both an inline configuration and a configuration file path were
        /// supplied.
        /// </exception>
        public PubSubConfigurationDataType GetConfigurationOrDefault()
        {
            return LoadConfiguration();
        }

        /// <summary>
        /// Validates the accumulated state and constructs the
        /// runtime <see cref="IPubSubApplication"/>.
        /// </summary>
        /// <exception cref="PubSubApplicationBuildException">
        /// Configuration is missing, both inline configuration and a
        /// file path are supplied, or validation fails.
        /// </exception>
        public IPubSubApplication Build()
        {
            PubSubConfigurationDataType configuration = LoadConfiguration();
            try
            {
                PubSubConfigurationSnapshot snapshot =
                    PubSubConfigurationSnapshot.Create(configuration, m_timeProvider);
                Dictionary<string, IPublishedDataSetSource> sources = ResolveSources(configuration);
                var diagnostics = new PubSubDiagnostics(m_options.DiagnosticsLevel, m_timeProvider);
                var metaDataRegistry = new DataSetMetaDataRegistry();
                var scheduler = new PubSubScheduler(m_telemetry, m_timeProvider);
                IPubSubSecurityWrapperResolver? resolver = ResolveSecurityWrapperResolver();

                var application = new PubSubApplication(
                    snapshot,
                    m_transportFactories,
                    m_encoders,
                    m_decoders,
                    m_policies,
                    scheduler,
                    metaDataRegistry,
                    diagnostics,
                    m_telemetry,
                    m_timeProvider,
                    sources,
                    m_dataSetSinks,
                    m_dataSetSourceProvider,
                    m_dataSetSinkProvider,
                    securityWrapperResolver: resolver);
                for (int i = 0; i < m_actionResponders.Count; i++)
                {
                    application.RegisterActionHandler(
                        m_actionResponders[i].Target,
                        m_actionResponders[i].Handler,
                        m_actionResponders[i].AllowUnsecured,
                        m_actionResponders[i].ResponseAddressPolicy);
                }

                return application;
            }
            catch (PubSubApplicationBuildException)
            {
                throw;
            }
            catch (Opc.Ua.PubSub.Configuration.PubSubConfigurationException)
            {
                // Surface fail-closed security/configuration errors verbatim.
                throw;
            }
            catch (Exception ex)
            {
                throw new PubSubApplicationBuildException(
                    "Failed to build PubSub application: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Builds the application and starts it in one step.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<IPubSubApplication> BuildAndStartAsync(
            CancellationToken cancellationToken = default)
        {
            IPubSubApplication app = Build();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            return app;
        }

        private PubSubConfigurationDataType LoadConfiguration()
        {
            if (m_configuration is not null && m_configurationFilePath is not null)
            {
                throw new PubSubApplicationBuildException(
                    "Both an inline configuration and a configuration file path "
                    + "were supplied. Choose one.");
            }
            if (m_configuration is not null)
            {
                return m_configuration;
            }
            if (m_configurationFilePath is not null)
            {
                var store = new XmlPubSubConfigurationStore(
                    m_configurationFilePath, m_telemetry, m_timeProvider);
                return store.LoadAsync(CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
            }
            return new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
        }

        private Dictionary<string, IPublishedDataSetSource> ResolveSources(
            PubSubConfigurationDataType configuration)
        {
            var sources = new Dictionary<string, IPublishedDataSetSource>(
                m_dataSetSources, StringComparer.Ordinal);
            if (configuration.PublishedDataSets.IsNull)
            {
                return sources;
            }

            foreach (PublishedDataSetDataType pds in configuration.PublishedDataSets)
            {
                string name = pds.Name ?? string.Empty;
                if (string.IsNullOrEmpty(name) || sources.ContainsKey(name))
                {
                    continue;
                }
                if (TryCreatePublishedActionSource(pds, out IPublishedDataSetSource? actionSource)
                    && actionSource is not null)
                {
                    sources[name] = actionSource;
                    continue;
                }
                if (m_dataStore is not null)
                {
                    sources[name] = new DataStoreBackedPublishedDataSetSource(m_dataStore, pds);
                }
            }

            return sources;
        }

        private static bool TryCreatePublishedActionSource(
            PublishedDataSetDataType publishedDataSet,
            out IPublishedDataSetSource? source)
        {
            source = null;
            ExtensionObject dataSetSource = publishedDataSet.DataSetSource;
            if (dataSetSource.IsNull)
            {
                return false;
            }

            if (dataSetSource.TryGetValue(out PublishedActionMethodDataType? methodAction)
                && methodAction is not null)
            {
                source = new PublishedActionSource(methodAction);
                return true;
            }

            if (dataSetSource.TryGetValue(out PublishedActionDataType? action)
                && action is not null)
            {
                source = new PublishedActionSource(action);
                return true;
            }

            return false;
        }

        private IPubSubSecurityWrapperResolver? ResolveSecurityWrapperResolver()
        {
            if (m_securityWrapperResolver is not null)
            {
                return m_securityWrapperResolver;
            }
            if (m_keyProviders.Count == 0)
            {
                return null;
            }
            return new PubSubSecurityWrapperResolver(
                m_keyProviders,
                m_telemetry,
                m_timeProvider,
                nonceProvider: null,
                m_securityPolicySelector);
        }

        internal IReadOnlyList<EndpointDescription> SecurityKeyServiceEndpoints => m_sksEndpoints;
    }
}
