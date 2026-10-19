/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.DataChannels;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Server.Tests.DataChannels
{
    public sealed partial class DataChannelModelTests
    {
        [Test]
        public async Task SessionGrantDoesNotOverrideOppositePeerApplicationPolicy()
        {
            var localPolicy = new DataChannelControlTestPair.LocalPolicy { Allowed = false };
            await using var authorizer = new SessionDataChannelAuthorizer(
                m_session, localPolicy, TimeProvider.System, m_telemetry);
            await using DataChannelControlTestPair pair = await CreatePairAsync(authorizer).ConfigureAwait(false);
            var control = new DataChannelSessionControl(m_session, authorizer, TimeProvider.System);
            uint id = pair.Client.ReserveChannelId();
            DataChannelGrantDataType grant = await control.GetGrantAsync(
                pair.Client, id, SourceId, DataChannelControlTestContext.Parameters()).ConfigureAwait(false);
            Assert.That(grant.GrantId, Is.Not.EqualTo(Uuid.Empty));
            DataChannelException denied = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await pair.Client.OpenAsync(
                    DataChannelControlTestContext.Parameters(), DataChannelAuthorizationMode.Session,
                    grant.GrantId, id).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(denied.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(pair.Server.Channels, Is.Empty);
        }

        [Test]
        public async Task LiveStateAndAuditEventsAreScopedAndReportAcceptedAndRefusedOutcomes()
        {
            ClientSession auditSession = await m_client.ConnectAsync(
                new Uri(m_endpoint), SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            try
            {
                ISession administrator = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(session => session.Id == auditSession.SessionId);
                administrator.RefreshEffectiveIdentity(new RoleBasedIdentity(
                    administrator.Identity, [Role.SecurityAdmin], m_server.CurrentInstance.NamespaceUris));
                uint ownSubscription = await CreateEventSubscriptionAsync(
                    m_session, ObjectTypeIds.DataChannelStateChangeEventType, BrowseNames.ChannelId)
                    .ConfigureAwait(false);
                uint auditSubscription = await CreateEventSubscriptionAsync(
                    auditSession, ObjectTypeIds.AuditDataChannelEventType, BrowseNames.Result).ConfigureAwait(false);
                await using DataChannelControlTestPair pair = await CreatePairAsync().ConfigureAwait(false);
                var proxy = new DataChannelManagerTypeClient(m_session, ObjectIds.DataChannelManager, m_telemetry);
                uint id = pair.Client.ReserveChannelId();
                DataChannelGrantDataType grant = await proxy.OpenDataChannelAsync(
                    id, SourceId, Binding(pair), DataChannelControlTestContext.Parameters()).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await pair.Client.OpenAsync(
                    DataChannelControlTestContext.Parameters(), DataChannelAuthorizationMode.Session,
                    grant.GrantId, id, deadline.Token).ConfigureAwait(false);
                await Assert.ThrowsAsync<ServiceResultException>(async () => await proxy.OpenDataChannelAsync(
                    pair.Client.ReserveChannelId(), new NodeId("missing-source", SourceId.NamespaceIndex),
                    Binding(pair), DataChannelControlTestContext.Parameters(), deadline.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                bool sawState = false;
                bool sawGoodAudit = false;
                bool sawDeniedAudit = false;
                for (int attempt = 0; attempt < 20 && (!sawState || !sawGoodAudit || !sawDeniedAudit); attempt++)
                {
                    PublishResponse own = await m_session.PublishAsync(null, [], deadline.Token).ConfigureAwait(false);
                    foreach (ExtensionObject body in own.NotificationMessage.NotificationData)
                    {
                        if (body.TryGetValue(out EventNotificationList? events) && events != null)
                        {
                            foreach (EventFieldList fields in events.Events)
                            {
                                Assert.That(fields.EventFields[0].TryGetValue(out NodeId type), Is.True);
                                Assert.That(type, Is.Not.EqualTo(ObjectTypeIds.AuditDataChannelEventType));
                                if (type == ObjectTypeIds.DataChannelStateChangeEventType)
                                {
                                    Assert.That(fields.EventFields[1].TryGetValue(out uint channelId), Is.True);
                                    Assert.That(channelId, Is.EqualTo(id));
                                    sawState = true;
                                }
                            }
                        }
                    }
                    PublishResponse audited = await auditSession.PublishAsync(null, [], deadline.Token)
                        .ConfigureAwait(false);
                    foreach (ExtensionObject body in audited.NotificationMessage.NotificationData)
                    {
                        if (body.TryGetValue(out EventNotificationList? events) && events != null)
                        {
                            foreach (EventFieldList fields in events.Events)
                            {
                                if (fields.EventFields[0].TryGetValue(out NodeId type) &&
                                    type == ObjectTypeIds.AuditDataChannelEventType)
                                {
                                    Assert.That(fields.EventFields[1].TryGetValue(out int result), Is.True);
                                    sawGoodAudit |= result == (int)DataChannelResult.Good;
                                    sawDeniedAudit |= result == (int)DataChannelResult.UnknownChannel;
                                }
                            }
                        }
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(sawState, Is.True);
                    Assert.That(sawGoodAudit, Is.True);
                    Assert.That(sawDeniedAudit, Is.True);
                });
                await m_session.DeleteSubscriptionsAsync(null, [ownSubscription], deadline.Token).ConfigureAwait(false);
                await auditSession.DeleteSubscriptionsAsync(null, [auditSubscription], deadline.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                await auditSession.CloseAsync(5000, true).ConfigureAwait(false);
                await auditSession.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
