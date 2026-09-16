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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using JsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using JsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Meaningful test/composition boundary: the document owns the returned application,
/// its explicit start/stop, and every acquired provider resource.
/// </summary>
internal interface IPubSubRuntimeFactory
{
    ArrayOf<PubSubPrerequisite> Inspect(PubSubConfiguration configuration, bool primarySessionAvailable);

    bool UsesPrimarySession(PubSubConfiguration configuration);

    ValueTask<PubSubRuntimeHandle> CreateAsync(
        PubSubConfiguration configuration,
        PubSubStartAuthorization authorization,
        ISession? primarySession,
        PubSubObservationStore observations,
        CancellationToken cancellationToken);
}

internal sealed class PubSubRuntimeFactory : IPubSubRuntimeFactory, IPubSubCommissioningCatalog
{
    public PubSubRuntimeFactory(
        ITelemetryContext telemetry,
        TimeProvider? clock = null,
        ArrayOf<IPubSubTransportProvider> transportProviders = default,
        ArrayOf<IPubSubKeyProviderResolver> keyProviders = default,
        ArrayOf<IPubSubAdapterProvider> adapterProviders = default)
    {
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        m_clock = clock ?? TimeProvider.System;
        m_transports.Add("builtin", new BuiltInPubSubTransportProvider());
        foreach (IPubSubTransportProvider provider in transportProviders)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.Profiles.IsNull || provider.Profiles.Count == 0 ||
                provider.Profiles.Contains(profile => !Enum.IsDefined(profile)) ||
                provider.Profiles.ToList().Distinct().Count() != provider.Profiles.Count)
            {
                throw new ArgumentException("A transport must declare distinct implemented profiles.",
                    nameof(transportProviders));
            }
            AddProvider(m_transports, provider.Id, provider);
        }
        foreach (IPubSubKeyProviderResolver provider in keyProviders)
        {
            ArgumentNullException.ThrowIfNull(provider);
            AddProvider(m_keys, provider.Id, provider);
        }
        foreach (IPubSubAdapterProvider provider in adapterProviders)
        {
            ArgumentNullException.ThrowIfNull(provider);
            AddProvider(m_adapters, provider.Id, provider);
        }
    }

    public PubSubProviderCatalog Catalog => new(
        [.. m_transports.Values.SelectMany(provider => provider.Profiles.ToList()
            .Select(profile => new PubSubTransportChoice(provider.Id, profile)))],
        [.. m_keys.Values.Select(provider =>
            new PubSubKeyProviderChoice(provider.Id, provider.KeySource, provider.SecurityKeyServiceEndpoint))],
        [.. m_adapters.Values.Select(provider => new PubSubAdapterChoice(provider.Id, provider.UsesPrimarySession))]);

    public ArrayOf<PubSubPrerequisite> Inspect(PubSubConfiguration configuration, bool primarySessionAvailable)
    {
        List<PubSubPrerequisite> issues = PubSubConfigurationValidation.Inspect(configuration).ToList();
        if (issues.Count == 0)
        {
            var validator = new PubSubConfigurationValidator([configuration.TransportProfileUri])
            {
                RegisteredSecurityGroupIds = configuration.KeySource == PubSubKeySource.ConfiguredProvider &&
                    m_keys.TryGetValue(configuration.SecurityProviderId, out IPubSubKeyProviderResolver? registered) &&
                    registered.KeySource == PubSubKeySource.ConfiguredProvider
                        ? [configuration.SecurityGroupId] : default
            };
            PubSubConfigurationValidationResult validation = validator.Validate(
                PubSubStackConfiguration.Build(configuration));
            foreach (PubSubConfigurationIssue issue in validation.Issues.ToList().Where(
                issue => issue.Severity == PubSubConfigurationIssueSeverity.Error).Take(8))
            {
                issues.Add(new PubSubPrerequisite("Stack configuration", PubSubReadiness.Unsupported,
                    configuration.Profile == PubSubProfile.DtlsUadp && issue.Code == "PSC0007"
                        ? "This stack validator rejects opc.dtls endpoints. Update its UDP/DTLS scheme support."
                        : issue.Code + ": The installed stack rejected this bounded configuration."));
            }
        }
        string transportId = TransportId(configuration);
        if (!m_transports.TryGetValue(transportId, out IPubSubTransportProvider? transport) ||
            !transport.Profiles.Contains(configuration.Profile))
        {
            issues.Add(new PubSubPrerequisite("Transport provider", PubSubReadiness.RequiresConfiguration,
                "The profile/provider is not installed. Configure its broker, DTLS context, or Ethernet channel."));
        }
        else
        {
            issues.Add(transport.Inspect(configuration));
        }
        if (configuration.SecurityMode != MessageSecurityMode.None)
        {
            if (configuration.SecurityProviderId is null ||
                !m_keys.TryGetValue(configuration.SecurityProviderId, out IPubSubKeyProviderResolver? keys))
            {
                issues.Add(new PubSubPrerequisite("Security", PubSubReadiness.RequiresConfiguration,
                    "The key/SKS provider is not registered. " +
                    "Configure authority trust, credentials and group access."));
            }
            else if (keys.KeySource != configuration.KeySource ||
                !string.Equals(keys.SecurityKeyServiceEndpoint, configuration.SecurityKeyServiceEndpoint,
                    StringComparison.Ordinal))
            {
                issues.Add(new PubSubPrerequisite("Security", PubSubReadiness.RequiresConfiguration,
                    "The key source and SKS endpoint must match the registered provider; " +
                    "changing text cannot retarget it."));
            }
        }
        if (configuration.UsesServerAdapter)
        {
            if (configuration.AdapterProviderId is null ||
                !m_adapters.TryGetValue(configuration.AdapterProviderId, out IPubSubAdapterProvider? adapter))
            {
                issues.Add(new PubSubPrerequisite("UA adapter", PubSubReadiness.RequiresConfiguration,
                    "The selected server-session adapter provider is not registered."));
            }
            else if (adapter.UsesPrimarySession && !primarySessionAvailable)
            {
                issues.Add(new PubSubPrerequisite("UA adapter", PubSubReadiness.RequiresConfiguration,
                    "Only this selected UA adapter requires the primary session; local reception does not."));
            }
        }
        return [.. issues];
    }

    public bool UsesPrimarySession(PubSubConfiguration configuration)
    {
        return configuration.UsesServerAdapter &&
            m_adapters.TryGetValue(configuration.AdapterProviderId, out IPubSubAdapterProvider? adapter) &&
            adapter.UsesPrimarySession;
    }

    public async ValueTask<PubSubRuntimeHandle> CreateAsync(
        PubSubConfiguration configuration,
        PubSubStartAuthorization authorization,
        ISession? primarySession,
        PubSubObservationStore observations,
        CancellationToken cancellationToken)
    {
        PubSubConfigurationValidation.RequireValid(configuration);
        PubSubConfigurationValidation.RequireAuthorization(configuration, authorization);
        PubSubPrerequisite? unavailable = Inspect(configuration, primarySession is not null).ToList()
            .FirstOrDefault(issue => issue.Readiness != PubSubReadiness.Ready);
        if (unavailable is not null)
        {
            throw new InvalidOperationException(unavailable.Area + ": " + unavailable.Detail);
        }
        var resources = new List<IAsyncDisposable>();
        IPubSubApplication? application = null;
        bool transferred = false;
        try
        {
            var context = new PubSubProviderContext(m_telemetry, m_clock, observations.TransportDiagnostics);
            PubSubTransportLease transport = await m_transports[TransportId(configuration)]
                .AcquireAsync(configuration, context, cancellationToken).ConfigureAwait(false);
            resources.Add(transport);
            cancellationToken.ThrowIfCancellationRequested();
            if (transport.Factory.TransportProfileUri != configuration.TransportProfileUri)
            {
                throw new InvalidOperationException("The configured provider returned a different transport profile.");
            }
            var builder = new PubSubApplicationBuilder(m_telemetry)
                .WithApplicationId("urn:ualens:pubsub:" + Guid.NewGuid().ToString("N"))
                .WithTimeProvider(m_clock)
                .WithDiagnosticsLevel(PubSubDiagnosticsLevel.High)
                .AddTransportFactory(transport.Factory)
                .AddEncoder(new UadpEncoder())
                .AddEncoder(new JsonEncoder())
                .AddDecoder(new PubSubObservedDecoder(
                    new UadpDecoder(), observations, configuration.MaxNetworkMessageBytes))
                .AddDecoder(new PubSubObservedDecoder(
                    new JsonDecoder(), observations, configuration.MaxNetworkMessageBytes));
            if (configuration.SecurityMode != MessageSecurityMode.None)
            {
                PubSubKeyProviderLease keys = await m_keys[configuration.SecurityProviderId]
                    .AcquireAsync(configuration.SecurityGroupId, cancellationToken).ConfigureAwait(false);
                resources.Add(keys);
                cancellationToken.ThrowIfCancellationRequested();
                if (keys.Provider.SecurityGroupId != configuration.SecurityGroupId)
                {
                    throw new InvalidOperationException(
                        "The selected key provider belongs to a different security group.");
                }
                PubSubSecurityKey key = await keys.Provider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (key.TokenId == 0 || key.IssuedAt > DateTimeUtc.From(m_clock.GetUtcNow()) ||
                    key.IsExpired(m_clock) ||
                    key.SigningKey.Length != 32 || key.EncryptingKey.Length is not (16 or 32) ||
                    key.KeyNonce.Length != 4)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                        "The configured key provider returned an inactive or invalid AES-CTR token.");
                }
                builder.AddSecurityKeyProvider(keys.Provider);
            }
            PubSubAdapterLease? adapter = null;
            PubSubGuardedServerSession? guardedSession = null;
            if (configuration.UsesServerAdapter)
            {
                adapter = await m_adapters[configuration.AdapterProviderId]
                    .AcquireAsync(primarySession, cancellationToken).ConfigureAwait(false);
                resources.Add(adapter);
                cancellationToken.ThrowIfCancellationRequested();
                await adapter.Session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                guardedSession = new PubSubGuardedServerSession(adapter.Session);
                resources.Add(guardedSession);
            }

            PubSubConfigurationDataType stackConfiguration = PubSubStackConfiguration.Build(configuration);
            PubSubBoundedSource? boundedSource = null;
            if (configuration.Publication != PubSubPublication.Disabled)
            {
                IPublishedDataSetSource source;
                if (configuration.Publication == PubSubPublication.ServerSource)
                {
                    PubSubAdapterLease sourceAdapter = adapter ??
                        throw new InvalidOperationException("The source adapter was not acquired.");
                    PublishedDataSetDataType dataSet = stackConfiguration.PublishedDataSets[0];
                    var variables = new PublishedVariableDataType[configuration.Fields.Count];
                    for (int i = 0; i < variables.Length; i++)
                    {
                        variables[i] = new PublishedVariableDataType
                        {
                            PublishedVariable = await sourceAdapter.ResolveAsync(
                                configuration.Fields[i].SourceNodeId, cancellationToken).ConfigureAwait(false),
                            AttributeId = Attributes.Value
                        };
                    }
                    dataSet.DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                    {
                        PublishedData = variables
                    });
                    IServerSession sourceSession = guardedSession ??
                        throw new InvalidOperationException("The guarded source session was not acquired.");
                    var metadata = new PubSubMetadataOwner(dataSet, sourceSession, m_telemetry);
                    resources.Add(metadata);
                    source = new ServerPublishedDataSetSource(
                        dataSet,
                        new CyclicReadStrategy(sourceSession, m_telemetry),
                        metadata.Metadata,
                        m_telemetry,
                        m_clock);
                }
                else
                {
                    source = new PubSubSyntheticSource(configuration, m_clock);
                }
                boundedSource = new PubSubBoundedSource(source, configuration.MaxPublishedMessages);
                builder.AddDataSetSource(PubSubStackConfiguration.DataSetName, boundedSource);
            }
            PubSubControlledWriteBack? controlledWriteBack = null;
            if (configuration.ReceiveEnabled)
            {
                ISubscribedDataSetSink sink = observations;
                if (configuration.WriteBackEnabled)
                {
                    PubSubAdapterLease targetAdapter = adapter ??
                        throw new InvalidOperationException("The write-back adapter was not acquired.");
                    var targets = new FieldTargetDataType[configuration.Fields.Count];
                    for (int i = 0; i < targets.Length; i++)
                    {
                        targets[i] = new FieldTargetDataType
                        {
                            TargetNodeId = await targetAdapter.ResolveAsync(
                                configuration.Fields[i].TargetNodeId, cancellationToken).ConfigureAwait(false),
                            AttributeId = Attributes.Value
                        };
                    }
                    var targetConfiguration = new TargetVariablesDataType { TargetVariables = targets };
                    IServerSession targetSession = guardedSession ??
                        throw new InvalidOperationException("The guarded target session was not acquired.");
                    var controlled = new PubSubControlledWriteBack(
                        new TargetVariablesSink(targetConfiguration, new PubSubVerifiedTargetWriter(
                            new ServerTargetVariableWriter(targetSession, m_telemetry), observations)),
                        observations,
                        configuration,
                        m_clock);
                    resources.Add(controlled);
                    controlledWriteBack = controlled;
                    sink = controlled;
                    stackConfiguration.Connections[0].ReaderGroups[0].DataSetReaders[0].SubscribedDataSet =
                        new ExtensionObject(targetConfiguration);
                }
                builder.AddSubscribedDataSetSink(PubSubStackConfiguration.ReaderName, sink);
            }
            if (configuration.ActionResponderEnabled)
            {
                PubSubAdapterLease actionAdapter = adapter ??
                    throw new InvalidOperationException("The Action adapter was not acquired.");
                NodeId objectId = await actionAdapter.ResolveAsync(configuration.ActionObjectNodeId, cancellationToken)
                    .ConfigureAwait(false);
                NodeId methodId = await actionAdapter.ResolveAsync(configuration.ActionMethodNodeId, cancellationToken)
                    .ConfigureAwait(false);
                var mappings = new ActionMethodMap().Add(
                    configuration.ActionWriterId, configuration.ActionTargetId, objectId, methodId);
                IServerSession actionSession = guardedSession ??
                    throw new InvalidOperationException("The guarded Action session was not acquired.");
                var handler = new PubSubControlledActionHandler(
                    new ServerActionHandler(actionSession, mappings, m_telemetry),
                    observations, configuration.MaxPublishedMessages, m_clock);
                resources.Add(handler);
                builder.AddActionResponder(
                    new PubSubActionTarget
                    {
                        ConnectionName = PubSubStackConfiguration.ConnectionName,
                        DataSetWriterId = configuration.ActionWriterId,
                        ActionTargetId = configuration.ActionTargetId,
                        ActionName = configuration.ActionName
                    },
                    handler,
                    allowUnsecured: authorization.AllowUnsecured,
                    responseAddressPolicy: string.IsNullOrEmpty(configuration.ActionResponseTopic)
                        ? PubSubResponseAddressPolicy.Default
                        : PubSubResponseAddressPolicy.Matching(configuration.ActionResponseTopic));
            }
            cancellationToken.ThrowIfCancellationRequested();
            application = builder.UseConfiguration(stackConfiguration).Build();
            if (application.Connections.Count != 1)
            {
                throw new InvalidOperationException("No matching runtime transport connection was constructed.");
            }
            if (configuration.ReceiveEnabled)
            {
                DataSetMetaDataType metadata = stackConfiguration.Connections[0]
                    .ReaderGroups[0].DataSetReaders[0].DataSetMetaData;
                application.MetaDataRegistry.Register(new DataSetMetaDataKey(
                    PublisherId.From(PubSubIdentity.Filter(configuration)),
                    configuration.IsJson ? (ushort)0 : configuration.WriterGroupId,
                    configuration.DataSetWriterId,
                    metadata.DataSetClassId,
                    metadata.ConfigurationVersion.MajorVersion), metadata);
            }
            Func<ValueTask>? releasePrimary = null;
            if (UsesPrimarySession(configuration) && adapter is not null && guardedSession is not null)
            {
                IPubSubApplication boundApplication = application;
                PubSubAdapterLease boundAdapter = adapter;
                PubSubGuardedServerSession boundSession = guardedSession;
                releasePrimary = async () =>
                {
                    controlledWriteBack?.Deactivate();
                    boundApplication.ClearActionHandlers();
                    foreach (Opc.Ua.PubSub.Groups.IWriterGroup group in boundApplication.Connections[0].WriterGroups)
                    {
                        group.State.TryDisable();
                    }
                    await boundSession.DisposeAsync().ConfigureAwait(false);
                    await boundAdapter.DisposeAsync().ConfigureAwait(false);
                };
            }
            if (guardedSession is not null)
            {
                resources.Remove(guardedSession);
                resources.Add(guardedSession);
            }
            PubSubPublicationDrain? publication = null;
            if (boundedSource is not null)
            {
                if (application.Connections[0].WriterGroups.Count != 1 ||
                    application.Connections[0].WriterGroups[0] is not Opc.Ua.PubSub.Groups.WriterGroup writer)
                {
                    throw new InvalidOperationException("The configured runtime must expose its single writer group.");
                }
                publication = new PubSubPublicationDrain(writer, boundedSource, configuration, observations, m_clock);
            }
            var runtime = new PubSubRuntimeHandle(
                application, [.. resources], boundedSource, releasePrimary, publication);
            transferred = true;
            return runtime;
        }
        finally
        {
            if (!transferred)
            {
                try
                {
                    if (application is not null)
                    {
                        await application.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await PubSubRuntimeHandle.DisposeResourcesAsync([.. resources], resources.Count - 1)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private static string TransportId(PubSubConfiguration configuration)
    {
        return string.IsNullOrEmpty(configuration.TransportProviderId) ? "builtin" : configuration.TransportProviderId;
    }

    private static void AddProvider<T>(Dictionary<string, T> providers, string id, T provider)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrEmpty(id) || !PubSubConfigurationValidation.IsProviderReference(id) ||
            !providers.TryAdd(id, provider))
        {
            throw new ArgumentException("Providers require unique opaque identifiers.", nameof(provider));
        }
    }

    private readonly ITelemetryContext m_telemetry;
    private readonly TimeProvider m_clock;
    private readonly Dictionary<string, IPubSubTransportProvider> m_transports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPubSubKeyProviderResolver> m_keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPubSubAdapterProvider> m_adapters = new(StringComparer.Ordinal);
}

internal sealed class PubSubRuntimeHandle : IAsyncDisposable
{
    public PubSubRuntimeHandle(
        IPubSubApplication application,
        ArrayOf<IAsyncDisposable> resources = default,
        PubSubBoundedSource? source = null,
        Func<ValueTask>? releasePrimaryBindings = null,
        PubSubPublicationDrain? publication = null)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
        Source = source;
        m_resources = resources;
        m_releasePrimaryBindings = releasePrimaryBindings;
        m_publication = publication;
    }

    public IPubSubApplication Application { get; }

    public PubSubBoundedSource? Source { get; }

    public Task CompletePublicationAsync(CancellationToken cancellationToken)
    {
        return m_publication?.CompleteAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public ValueTask ReleasePrimaryBindingsAsync()
    {
        lock (m_gate)
        {
            m_primaryRelease ??= m_releasePrimaryBindings?.Invoke().AsTask() ?? Task.CompletedTask;
            return new ValueTask(m_primaryRelease);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_disposal ??= DisposeCoreAsync();
            return new ValueTask(m_disposal);
        }
    }

    internal static async ValueTask DisposeResourcesAsync(ArrayOf<IAsyncDisposable> resources, int index)
    {
        if (index < 0)
        {
            return;
        }
        try
        {
            await resources[index].DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await DisposeResourcesAsync(resources, index - 1).ConfigureAwait(false);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (m_primaryRelease is not null)
            {
                await m_primaryRelease.ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await Application.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await DisposeResourcesAsync(m_resources, m_resources.Count - 1).ConfigureAwait(false);
            }
        }
    }

    private readonly Lock m_gate = new();
    private readonly ArrayOf<IAsyncDisposable> m_resources;
    private readonly Func<ValueTask>? m_releasePrimaryBindings;
    private readonly PubSubPublicationDrain? m_publication;
    private Task? m_disposal;
    private Task? m_primaryRelease;
}

internal sealed class PubSubMetadataOwner : IAsyncDisposable
{
    public PubSubMetadataOwner(
        PublishedDataSetDataType dataSet,
        IServerSession session,
        ITelemetryContext telemetry)
    {
        m_metadata = new DataSetMetaDataBuilder(dataSet, session, telemetry);
    }

    public DataSetMetaDataBuilder Metadata => m_metadata;

    public ValueTask DisposeAsync()
    {
        m_metadata.Dispose();
        return ValueTask.CompletedTask;
    }

    private readonly DataSetMetaDataBuilder m_metadata;
}
