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

#if NET8_0_OR_GREATER

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using UaLens.Connection;
using UaLens.Subscriptions;

namespace UaLens.Tests.Connection;

[TestFixture]
[Category("WssOpenApiIntegration")]
[SetCulture("en-us")]
[SetUICulture("en-us")]
[NonParallelizable]
public sealed class OpenApiWebSocketConnectionTests
{
    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        m_telemetry = NUnitTelemetryContext.Create();
        m_pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        m_serverFixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
        {
            AutoAccept = true,
            SecurityNone = true,
            UriScheme = Utils.UriSchemeOpcWss,
            HttpsMutualTls = false,
            MaxChannelCount = 128,
            TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
        };
        await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
        m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
        m_clientFixture = new ClientFixture(telemetry: m_telemetry);
        await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
        m_endpointUrl = new Uri(Utils.ReplaceLocalhost(
            $"opc.wss://localhost:{m_serverFixture.Port.ToString(CultureInfo.InvariantCulture)}/" +
            nameof(ReferenceServer)));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        m_clientFixture?.Dispose();
        if (m_serverFixture is not null)
        {
            await m_serverFixture.StopAsync().ConfigureAwait(false);
        }
        if (m_pkiRoot is not null && Directory.Exists(m_pkiRoot))
        {
            Directory.Delete(m_pkiRoot, recursive: true);
        }
    }

    [Test]
    public async Task LensDiscoversConnectsBrowsesReadsAndMonitorsOverWssOpenApiAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var transports = new ConnectionTransportCatalog();
        var channels = new ClientChannelManager(m_clientFixture.Config, transports);
        await using (channels.ConfigureAwait(false))
        {
            using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                channels,
                m_endpointUrl,
                EndpointConfiguration.Create(m_clientFixture.Config),
                m_telemetry,
                ct: deadline.Token).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery
                .GetEndpointsAsync(default, deadline.Token)
                .ConfigureAwait(false);
            EndpointDescription? selectedEndpoint = null;
            foreach (EndpointDescription candidate in endpoints)
            {
                if (Profiles.IsWssOpenApi(candidate.TransportProfileUri))
                {
                    selectedEndpoint = candidate;
                    break;
                }
            }
            Assert.That(selectedEndpoint, Is.Not.Null);
            EndpointDescription endpoint = selectedEndpoint!;
            UserTokenPolicy? anonymousPolicy = null;
            foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
            {
                if (policy.TokenType == UserTokenType.Anonymous)
                {
                    anonymousPolicy = policy;
                    break;
                }
            }
            Assert.That(anonymousPolicy, Is.Not.Null);
            UserTokenPolicy anonymous = anonymousPolicy!;
            ConnectionProfile profile = ConnectionProfile.Create(
                endpoint,
                anonymous,
                SubscriptionEngineKind.Classic);
            var identity = new UserIdentity(new AnonymousIdentityToken())
            {
                PolicyId = anonymous.PolicyId ??
                    throw new InvalidOperationException("The anonymous policy has no policy id.")
            };
            await using ConnectionCredentials credentials = await ConnectionCredentials.FromIdentityAsync(
                profile,
                identity,
                deadline.Token).ConfigureAwait(false);
            var backend = new StackConnectionBackend(
                m_telemetry,
                _ => Task.FromResult(m_clientFixture.Config),
                transports);
            await using (backend.ConfigureAwait(false))
            await using (IConnectionSession connection = await backend.ConnectAsync(
                m_clientFixture.Config,
                endpoint,
                profile,
                credentials.Provider,
                deadline.Token).ConfigureAwait(false))
            {
                Opc.Ua.Client.ISession session = connection.Session;
                Assert.That(session.Connected, Is.True);
                Assert.That(
                    session.ConfiguredEndpoint.Description.TransportProfileUri,
                    Is.EqualTo(Profiles.WssOpenApiTransport));
                Assert.That(
                    () => profile.RequireMatch(session.ConfiguredEndpoint.Description),
                    Throws.Nothing);

                BrowseResponse browse = await session.BrowseAsync(
                    null,
                    null,
                    0,
                    [
                        new BrowseDescription
                        {
                            NodeId = Opc.Ua.ObjectIds.ObjectsFolder,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    deadline.Token).ConfigureAwait(false);
                Assert.That(browse.Results, Has.Count.EqualTo(1));
                Assert.That(StatusCode.IsGood(browse.Results[0].StatusCode), Is.True);
                Assert.That(browse.Results[0].References, Is.Not.Empty);

                DataValue currentTime = await session.ReadValueAsync(
                    Opc.Ua.VariableIds.Server_ServerStatus_CurrentTime,
                    deadline.Token).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(currentTime.StatusCode), Is.True);
                Assert.That(currentTime.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);

                await using var adapter = new ClassicEngineAdapter(session, m_telemetry);
                await adapter.ApplySubscriptionAsync(new SubscriptionConfig
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 5,
                    LifetimeCount = 100,
                    PublishingEnabled = true
                }, deadline.Token).ConfigureAwait(false);
                await adapter.AddItemAsync(new MonitoredItemConfig
                {
                    DisplayName = "Server current time",
                    NodeId = Opc.Ua.VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    SamplingInterval = TimeSpan.FromMilliseconds(100),
                    QueueSize = 1,
                    DiscardOldest = true,
                    MonitoringMode = MonitoringMode.Reporting
                }, deadline.Token).ConfigureAwait(false);
                await adapter.AddItemAsync(new MonitoredItemConfig
                {
                    DisplayName = "Server events",
                    NodeId = Opc.Ua.ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    SamplingInterval = TimeSpan.Zero,
                    QueueSize = 10,
                    DiscardOldest = true,
                    MonitoringMode = MonitoringMode.Reporting,
                    IsEvent = true
                }, deadline.Token).ConfigureAwait(false);

                IServerInternal server = m_server.CurrentInstance;
                var serverEvent = new BaseEventState(null);
                serverEvent.Initialize(
                    server.DefaultSystemContext,
                    server.ServerObject,
                    EventSeverity.Medium,
                    new LocalizedText("UaLens WSS OpenAPI integration event"));
                server.ReportEvent(server.DefaultSystemContext, serverEvent);

                bool receivedData = false;
                bool receivedEvent = false;
                while (!receivedData || !receivedEvent)
                {
                    NotificationEvent notification = await adapter.Events
                        .ReadAsync(deadline.Token)
                        .ConfigureAwait(false);
                    receivedData |= notification.Kind == NotificationKind.DataChange;
                    receivedEvent |= notification.Kind == NotificationKind.Event;
                }

                Assert.That(adapter.Counters.DataValues, Is.GreaterThan(0));
                Assert.That(adapter.Counters.EventValues, Is.GreaterThan(0));
            }
        }
    }

    private ITelemetryContext m_telemetry = null!;
    private ServerFixture<ReferenceServer> m_serverFixture = null!;
    private ClientFixture m_clientFixture = null!;
    private ReferenceServer m_server = null!;
    private string m_pkiRoot = null!;
    private Uri m_endpointUrl = null!;
}

#endif
