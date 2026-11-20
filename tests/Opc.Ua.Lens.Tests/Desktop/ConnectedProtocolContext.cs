// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Desktop;

internal sealed class ConnectedProtocolContext : IAsyncDisposable
{
    public ConnectedProtocolContext()
    {
        SessionBindingTestHelper.Attach(Session);
        Messages = ServiceMessageContext.Create(Desktop.Telemetry);
        Messages.NamespaceUris.GetIndexOrAppend("urn:fixture:protocol");
        Messages.NamespaceUris.GetIndexOrAppend("urn:fixture:application");
        Endpoint = new EndpointDescription("opc.tcp://protocol.example.test:4840/Fixture")
        {
            SecurityMode = MessageSecurityMode.None,
            SecurityPolicyUri = SecurityPolicies.None,
            TransportProfileUri = Profiles.UaTcpTransport,
            Server = new ApplicationDescription { ApplicationUri = "urn:fixture:protocol" },
            UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
        };
        Session.SetupGet(session => session.MessageContext).Returns(Messages);
        Session.SetupGet(session => session.NamespaceUris).Returns(Messages.NamespaceUris);
        Session.SetupGet(session => session.Factory).Returns(Messages.Factory);
        Session.SetupGet(session => session.Connected).Returns(() => Connected);
        Session.SetupGet(session => session.SessionId).Returns(new NodeId(23001u));
        Session.SetupGet(session => session.ConfiguredEndpoint)
            .Returns(new ConfiguredEndpoint(null, Endpoint, new EndpointConfiguration()));
        Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
        Session.SetupGet(session => session.TypeTree).Returns(TypeTree.Object);
        TypeTree.Setup(tree => tree.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>()))
            .Returns((NodeId type, NodeId parent) => type == parent);
        Session.Setup(session => session.ReadAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
            It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> ids,
                CancellationToken token) =>
            {
                Reads.Add(ids);
                return Read(ids, token);
            });
        Session.Setup(session => session.BrowseAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> ids,
                CancellationToken token) => Browse(ids, token));
        Session.Setup(session => session.BrowseNextAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
            It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, bool release, ArrayOf<ByteString> points, CancellationToken token) =>
                BrowseNext(release, points, token));
        Session.Setup(session => session.CallAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(
                (RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken token) => Call(calls, token));
        Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken token) => Translate(paths, token));
        Session.Setup(session => session.WriteAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ArrayOf<WriteValue> values, CancellationToken token) => Write(values, token));
        var live = new Mock<IConnectionSession>();
        live.SetupGet(connection => connection.Session).Returns(() => m_connectedSession ?? Session.Object);
        live.SetupGet(connection => connection.State).Returns(new ConnectionSessionState(ConnectionPhase.Connected));
        live.Setup(connection => connection.DisposeAsync()).Returns(() =>
        {
            Connected = false;
            return ValueTask.CompletedTask;
        });
        Desktop.DiscoverAsync = (_, _) => Task.FromResult<ArrayOf<EndpointDescription>>([Endpoint]);
        Desktop.Backend.Setup(backend => backend.ConnectAsync(
            It.IsAny<ApplicationConfiguration>(), It.IsAny<EndpointDescription>(), It.IsAny<ConnectionProfile>(),
            It.IsAny<IClientIdentityProvider>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Connected = true;
                return Task.FromResult(live.Object);
            });
        Workspace.SetupProperty(workspace => workspace.EndpointUrl, Endpoint.EndpointUrl);
        Host = new PluginHost(Workspace.Object, Desktop.Connection, Desktop.Browser, Desktop.Telemetry);
    }

    public DesktopConnectionContext Desktop { get; } = new();
    public ServiceMessageContext Messages { get; }
    public EndpointDescription Endpoint { get; }
    public Mock<ISession> Session { get; } = new();
    public Mock<ITypeTable> TypeTree { get; } = new();
    public Mock<IPluginWorkspace> Workspace { get; } = new();
    public PluginHost Host { get; }
    public bool Connected { get; private set; }
    public List<ArrayOf<ReadValueId>> Reads { get; } = [];
    public Func<ArrayOf<ReadValueId>, CancellationToken, ValueTask<ReadResponse>> Read { get; set; } =
        (_, _) => throw new AssertionException("Unexpected protocol read.");
    public Func<ArrayOf<BrowseDescription>, CancellationToken, ValueTask<BrowseResponse>> Browse { get; set; } =
        (_, _) => ValueTask.FromResult(new BrowseResponse { Results = [new BrowseResult()] });
    public Func<bool, ArrayOf<ByteString>, CancellationToken, ValueTask<BrowseNextResponse>> BrowseNext { get; set; } =
        (_, _, _) => throw new AssertionException("Unexpected continuation point.");
    public Func<ArrayOf<CallMethodRequest>, CancellationToken, ValueTask<CallResponse>> Call { get; set; } =
        (_, _) => throw new AssertionException("Unexpected protocol mutation.");
    public Func<ArrayOf<BrowsePath>, CancellationToken, ValueTask<TranslateBrowsePathsToNodeIdsResponse>>
        Translate
    { get; set; } = (_, _) => throw new AssertionException("Unexpected browse-path translation.");
    public Func<ArrayOf<WriteValue>, CancellationToken, ValueTask<WriteResponse>> Write { get; set; } =
        (_, _) => throw new AssertionException("Unexpected protocol write.");

    /// <summary>
    /// Connects the controlled host with its mock session or a caller-owned stack session.
    /// </summary>
    public async Task ConnectAsync(ISession? session = null)
    {
        m_connectedSession = session;
        await Desktop.Connection.ConnectAsync(ConnectionProfile.Create(
            Endpoint, Endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2)).ConfigureAwait(true);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync().ConfigureAwait(true);
        await Desktop.DisposeAsync().ConfigureAwait(true);
    }

    /// <summary>The optional caller-owned session exposed by the controlled connection.</summary>
    private ISession? m_connectedSession;
}
