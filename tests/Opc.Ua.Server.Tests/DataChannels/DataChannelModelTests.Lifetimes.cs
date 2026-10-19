/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Server.Tests.DataChannels
{
    public sealed partial class DataChannelModelTests
    {
        [Test]
        public async Task LateSessionAttachmentFailsClosedUntilGeneratedControlBindsTheSession()
        {
            await using var authorizer = new SessionDataChannelAuthorizer(
                new DataChannelControlTestPair.LocalPolicy(), TimeProvider.System, m_telemetry);
            await using DataChannelControlTestPair pair = await CreatePairAsync(authorizer).ConfigureAwait(false);
            var proxy = new DataChannelManagerTypeClient(m_session, ObjectIds.DataChannelManager, m_telemetry);
            uint first = pair.Client.ReserveChannelId();
            DataChannelGrantDataType grant = await proxy.OpenDataChannelAsync(
                first, SourceId, Binding(pair), DataChannelControlTestContext.Parameters()).ConfigureAwait(false);
            DataChannelException unbound = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Client.OpenAsync(DataChannelControlTestContext.Parameters(),
                    DataChannelAuthorizationMode.Session, grant.GrantId, first).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
            Assert.That(unbound.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            var control = new DataChannelSessionControl(m_session, authorizer, TimeProvider.System);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DataChannel opened = await control.OpenAsync(
                pair.Client, SourceId, DataChannelControlTestContext.Parameters(), deadline.Token)
                .ConfigureAwait(false);
            Assert.That(opened.State, Is.EqualTo(DataChannelState.Open));
            ClientSession other = await m_client.ConnectAsync(new Uri(m_endpoint), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            try
            {
                Assert.That(() => authorizer.AttachSession(other), Throws.InvalidOperationException);
                Assert.That(opened.State, Is.EqualTo(DataChannelState.Open));
            }
            finally
            {
                await other.CloseAsync(5000, true).ConfigureAwait(false);
                await other.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task LiveSessionClosureAndSubscriptionDeletionDoNotCloseConfiguredNeighbour()
        {
            await using var authorizer = new SessionDataChannelAuthorizer(
                m_session, new DataChannelControlTestPair.LocalPolicy(), TimeProvider.System, m_telemetry);
            await using DataChannelControlTestPair pair = await CreatePairAsync(authorizer).ConfigureAwait(false);
            var control = new DataChannelSessionControl(m_session, authorizer, TimeProvider.System);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            DataChannel authorized = await control.OpenAsync(
                pair.Client, SourceId, DataChannelControlTestContext.Parameters(), deadline.Token)
                .ConfigureAwait(false);
            DataChannel dependent = (await m_provider.AcceptAsync(
                DataChannelControlTestContext.ResourceUri, DataChannelProfiles.OpaquePayload,
                DataChannelCarrierKind.SharedUasc, deadline.Token).ConfigureAwait(false))!;
            DataChannel configured = await pair.Client.OpenAsync(
                DataChannelControlTestContext.Parameters(), cancellationToken: deadline.Token).ConfigureAwait(false);
            DataChannel independent = (await m_provider.AcceptAsync(
                DataChannelControlTestContext.ResourceUri, DataChannelProfiles.OpaquePayload,
                DataChannelCarrierKind.SharedUasc, deadline.Token).ConfigureAwait(false))!;
            uint subscription = await CreateDataSubscriptionAsync(m_session).ConfigureAwait(false);
            await m_session.DeleteSubscriptionsAsync(null, [subscription], deadline.Token).ConfigureAwait(false);
            Assert.That(authorized.State, Is.EqualTo(DataChannelState.Open));
            await m_session.CloseAsync(5000, true, deadline.Token).ConfigureAwait(false);
            await m_provider.RecheckAuthorizationAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(authorized.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(dependent.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(configured.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(independent.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(pair.Server.IsReady, Is.True);
            });
            ByteString payload = ByteString.From([71, 72, 73]);
            Assert.That(await configured.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await independent.ReadAsync(deadline.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
        }

        [Test]
        public async Task ModelRequiresExplicitOptInAndBorrowedProviderSurvivesServerShutdown()
        {
            await using var provider = new ServerDataChannelProvider(
                new DataChannelSourceRegistry(), m_options, new DataChannelApplicationBudget(),
                TimeProvider.System, m_telemetry);
            var fixture = new ServerFixture<StandardServer>(telemetry =>
                new StandardServer(telemetry).UseDataChannels(provider))
            {
                AutoAccept = true
            };
            StandardServer server = await fixture.StartAsync(Path.Combine(m_pkiRoot, "model-disabled"))
                .ConfigureAwait(false);
            try
            {
                string endpoint = server.GetEndpoints().ToList().First(description =>
                    description.TransportProfileUri == Profiles.UaTcpTransport &&
                    description.SecurityMode == MessageSecurityMode.SignAndEncrypt).EndpointUrl!;
                ClientSession session = await m_client.ConnectAsync(new Uri(endpoint), SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                try
                {
                    ReadResponse values = await session.ReadAsync(
                        null, 0, TimestampsToReturn.Neither,
                        [
                            new ReadValueId
                            {
                                NodeId = ObjectIds.DataChannelManager,
                                AttributeId = Attributes.NodeClass
                            },
                            new ReadValueId
                            {
                                NodeId = ObjectIds.DataChannelCapabilities,
                                AttributeId = Attributes.NodeClass
                            }
                        ], CancellationToken.None).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(values.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                        Assert.That(values.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                        Assert.That(session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/DataChannel/"),
                            Is.EqualTo(-1));
                    });
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
            Assert.That(provider.GetEndpoints(), Is.Empty);
            Assert.DoesNotThrow(() => provider.AttachServer(m_server.CurrentInstance),
                "Shutdown must detach the original publishers and the borrowed provider's old host.");
            provider.DetachServer(m_server.CurrentInstance);
        }
    }
}
