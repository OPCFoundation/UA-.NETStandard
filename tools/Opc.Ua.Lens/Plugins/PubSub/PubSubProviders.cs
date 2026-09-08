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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp;

namespace UaLens.Plugins.PubSub;

internal sealed record PubSubProviderContext(
    ITelemetryContext Telemetry,
    TimeProvider Clock,
    IPubSubDiagnostics Diagnostics);

/// <summary>
/// Installed transport composition. Acquisition must not start traffic, and the returned
/// lease owns any provider-local services until the application has stopped.
/// </summary>
internal interface IPubSubTransportProvider
{
    string Id { get; }

    ArrayOf<PubSubProfile> Profiles { get; }

    PubSubPrerequisite Inspect(PubSubConfiguration configuration);

    ValueTask<PubSubTransportLease> AcquireAsync(
        PubSubConfiguration configuration,
        PubSubProviderContext context,
        CancellationToken cancellationToken);
}

internal interface IPubSubKeyProviderResolver
{
    string Id { get; }

    ValueTask<PubSubKeyProviderLease> AcquireAsync(string securityGroupId, CancellationToken cancellationToken);
}

/// <summary>
/// Trusted session composition, outside workspace data. A primary-dependent provider
/// borrows the selected primary connection; other providers own their explicitly configured sessions.
/// </summary>
internal interface IPubSubAdapterProvider
{
    string Id { get; }

    bool UsesPrimarySession { get; }

    ValueTask<PubSubAdapterLease> AcquireAsync(ISession? primarySession, CancellationToken cancellationToken);
}

internal sealed class PubSubTransportLease : IAsyncDisposable
{
    public PubSubTransportLease(IPubSubTransportFactory factory, IAsyncDisposable? owner = null)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        m_owner = owner;
    }

    public IPubSubTransportFactory Factory { get; }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_disposal ??= m_owner?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            m_owner = null;
            return new ValueTask(m_disposal);
        }
    }

    private readonly Lock m_gate = new();
    private IAsyncDisposable? m_owner;
    private Task? m_disposal;
}

internal sealed class PubSubKeyProviderLease : IAsyncDisposable
{
    public PubSubKeyProviderLease(IPubSubSecurityKeyProvider provider, IAsyncDisposable? owner = null)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        m_owner = owner;
    }

    public IPubSubSecurityKeyProvider Provider { get; }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_disposal ??= m_owner?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            m_owner = null;
            return new ValueTask(m_disposal);
        }
    }

    private readonly Lock m_gate = new();
    private IAsyncDisposable? m_owner;
    private Task? m_disposal;
}

/// <summary>
/// Resolves portable workspace targets using the provider's actual session namespace table.
/// The session/resolver pair must refer to the same generation. No namespace-index guesses are made.
/// </summary>
internal sealed class PubSubAdapterLease : IAsyncDisposable
{
    public PubSubAdapterLease(
        IServerSession session,
        Func<ExpandedNodeId, CancellationToken, ValueTask<NodeId>> resolveTarget,
        IAsyncDisposable? owner = null,
        bool ownsSession = true)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        m_resolveTarget = resolveTarget ?? throw new ArgumentNullException(nameof(resolveTarget));
        m_owner = owner ?? (ownsSession ? session : null);
    }

    public IServerSession Session { get; }

    public async ValueTask<NodeId> ResolveAsync(string portableNodeId, CancellationToken cancellationToken)
    {
        if (!PubSubConfigurationValidation.IsPortableNodeId(portableNodeId))
        {
            throw new ArgumentException("A portable UA target is required.", nameof(portableNodeId));
        }
        NodeId target = await m_resolveTarget(ExpandedNodeId.Parse(portableNodeId), cancellationToken)
            .ConfigureAwait(false);
        if (target.IsNull)
        {
            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
        }
        return target;
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_disposal ??= m_owner?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            m_owner = null;
            return new ValueTask(m_disposal);
        }
    }

    private readonly Func<ExpandedNodeId, CancellationToken, ValueTask<NodeId>> m_resolveTarget;
    private readonly Lock m_gate = new();
    private IAsyncDisposable? m_owner;
    private Task? m_disposal;
}

/// <summary>
/// Direct/injectable seam for configured MQTT credentials, DTLS contexts, Ethernet channels,
/// and other installed AOT-compatible bindings. Workspace data cannot load a provider.
/// </summary>
internal sealed class ConfiguredPubSubTransportProvider : IPubSubTransportProvider
{
    public ConfiguredPubSubTransportProvider(
        string id,
        ArrayOf<PubSubProfile> profiles,
        Func<PubSubConfiguration, PubSubPrerequisite> inspect,
        Func<PubSubConfiguration, PubSubProviderContext, CancellationToken, ValueTask<PubSubTransportLease>> acquire)
    {
        if (string.IsNullOrEmpty(id) || !PubSubConfigurationValidation.IsProviderReference(id))
        {
            throw new ArgumentException("A registered provider identifier is required.", nameof(id));
        }
        if (profiles.IsNull || profiles.Count == 0 || profiles.Contains(profile => !Enum.IsDefined(profile)))
        {
            throw new ArgumentException("At least one supported profile is required.", nameof(profiles));
        }
        Id = id;
        Profiles = profiles;
        m_inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
        m_acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
    }

    public string Id { get; }

    public ArrayOf<PubSubProfile> Profiles { get; }

    public PubSubPrerequisite Inspect(PubSubConfiguration configuration)
    {
        return m_inspect(configuration);
    }

    public ValueTask<PubSubTransportLease> AcquireAsync(
        PubSubConfiguration configuration,
        PubSubProviderContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return m_acquire(configuration, context, cancellationToken);
    }

    private readonly Func<PubSubConfiguration, PubSubPrerequisite> m_inspect;
    private readonly Func<PubSubConfiguration, PubSubProviderContext, CancellationToken,
        ValueTask<PubSubTransportLease>> m_acquire;
}

internal sealed class ConfiguredPubSubKeyProvider : IPubSubKeyProviderResolver
{
    public ConfiguredPubSubKeyProvider(
        string id,
        Func<string, CancellationToken, ValueTask<PubSubKeyProviderLease>> acquire)
    {
        if (string.IsNullOrEmpty(id) || !PubSubConfigurationValidation.IsProviderReference(id))
        {
            throw new ArgumentException("A registered provider identifier is required.", nameof(id));
        }
        Id = id;
        m_acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
    }

    public string Id { get; }

    public ValueTask<PubSubKeyProviderLease> AcquireAsync(string securityGroupId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return m_acquire(securityGroupId, cancellationToken);
    }

    private readonly Func<string, CancellationToken, ValueTask<PubSubKeyProviderLease>> m_acquire;
}

internal sealed class ConfiguredPubSubAdapterProvider : IPubSubAdapterProvider
{
    public ConfiguredPubSubAdapterProvider(
        string id,
        bool usesPrimarySession,
        Func<ISession?, CancellationToken, ValueTask<PubSubAdapterLease>> acquire)
    {
        if (string.IsNullOrEmpty(id) || !PubSubConfigurationValidation.IsProviderReference(id))
        {
            throw new ArgumentException("A registered provider identifier is required.", nameof(id));
        }
        Id = id;
        UsesPrimarySession = usesPrimarySession;
        m_acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
    }

    public string Id { get; }

    public bool UsesPrimarySession { get; }

    public ValueTask<PubSubAdapterLease> AcquireAsync(ISession? primarySession, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (UsesPrimarySession && primarySession is null)
        {
            throw new InvalidOperationException("This configured UA adapter requires the primary session.");
        }
        return m_acquire(UsesPrimarySession ? primarySession : null, cancellationToken);
    }

    private readonly Func<ISession?, CancellationToken, ValueTask<PubSubAdapterLease>> m_acquire;
}

/// <summary>
/// Uses repository transports only. Broker client implementations are intentionally internal
/// to their libraries, so their supported DI registration is isolated to a non-hosted,
/// document-owned service provider. The actual application is always built separately.
/// </summary>
internal sealed class BuiltInPubSubTransportProvider : IPubSubTransportProvider
{
    public string Id => "builtin";

    public ArrayOf<PubSubProfile> Profiles { get; } =
    [
        PubSubProfile.UdpUadp,
        PubSubProfile.MqttJson,
        PubSubProfile.MqttUadp,
        PubSubProfile.KafkaJson,
        PubSubProfile.KafkaUadp
    ];

    public PubSubPrerequisite Inspect(PubSubConfiguration configuration)
    {
        if (!Profiles.Contains(configuration.Profile))
        {
            return new PubSubPrerequisite("Binding", PubSubReadiness.RequiresConfiguration,
                "DTLS requires its context/key configuration; Ethernet requires its installed channel and privileges.");
        }
        if (configuration.BrokerAuthentication == PubSubBrokerAuthentication.Provider)
        {
            return new PubSubPrerequisite("Credentials", PubSubReadiness.RequiresConfiguration,
                "Register a credential-aware transport provider. The built-in binding never falls back to Anonymous.");
        }
        return new PubSubPrerequisite("Binding", PubSubReadiness.Ready, configuration.IsBroker
            ? "Binding installed; broker reachability and authorization are checked only by explicit Start."
            : "UDP binding installed; interface/address availability is checked only by explicit Start.");
    }

    public async ValueTask<PubSubTransportLease> AcquireAsync(
        PubSubConfiguration configuration,
        PubSubProviderContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PubSubPrerequisite prerequisite = Inspect(configuration);
        if (prerequisite.Readiness != PubSubReadiness.Ready)
        {
            throw new InvalidOperationException(prerequisite.Detail);
        }
        if (configuration.Profile == PubSubProfile.UdpUadp)
        {
            return new PubSubTransportLease(new UdpPubSubTransportFactory(
                Options.Create(new UdpTransportOptions
                {
                    PreferredNetworkInterface = configuration.NetworkInterface,
                    MulticastLoopback = configuration.MulticastLoopback,
                    MaxFrameSize = configuration.MaxNetworkMessageBytes,
                    ReceiveQueueCapacity = 128,
                    Ttl = 1,
                    MessageRepeatCount = 0
                }),
                context.Diagnostics));
        }

        var services = new ServiceCollection();
        services.AddSingleton(context.Telemetry);
        services.AddSingleton(context.Clock);
        services.AddSingleton(context.Diagnostics);
        services.AddOpcUa().AddPubSub(builder =>
        {
            if (configuration.Profile is PubSubProfile.MqttJson or PubSubProfile.MqttUadp)
            {
                builder.AddMqttTransport(options =>
                {
                    options.Endpoint = configuration.Endpoint;
                    options.ClientId = "ualens-" + Guid.NewGuid().ToString("N");
                    options.MaxConcurrentSubscriptions = 8;
                    options.MaxNetworkMessageSize = configuration.MaxNetworkMessageBytes;
                    options.ConnectTimeout = TimeSpan.FromSeconds(10);
                    options.CleanSession = true;
                    options.Topics.RetainMetaDataMessages = false;
                    options.Topics.RetainDiscoveryMessages = false;
                });
            }
            else
            {
                // AddKafkaTransport selects managed Dekaf. Never opt into WithConfluentKafkaClient.
                builder.AddKafkaTransport(options =>
                {
                    options.Endpoint = configuration.Endpoint;
                    options.ClientId = "ualens-" + Guid.NewGuid().ToString("N");
                    options.GroupId = "ualens-" + Guid.NewGuid().ToString("N");
                    options.ConnectTimeout = TimeSpan.FromSeconds(10);
                    options.MaxMessageSize = configuration.MaxNetworkMessageBytes;
                });
            }
        });
        services.RemoveAll<IHostedService>();
        services.RemoveAll<IPubSubApplication>();
        ServiceProvider provider = services.BuildServiceProvider();
        bool transferred = false;
        try
        {
            IPubSubTransportFactory factory = provider.GetServices<IPubSubTransportFactory>()
                .Single(item => item.TransportProfileUri == configuration.TransportProfileUri);
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new PubSubTransportLease(factory, provider);
            transferred = true;
            return lease;
        }
        finally
        {
            if (!transferred)
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
