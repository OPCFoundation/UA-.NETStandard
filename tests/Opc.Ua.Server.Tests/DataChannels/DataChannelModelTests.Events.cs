/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Server.Tests.DataChannels
{
    public sealed partial class DataChannelModelTests
    {
        /// <summary>
        /// The data-channel audit subtype adds no fields to standard Method auditing.
        /// </summary>
        [Test]
        public async Task AuditDataChannelTypeIsFieldlessMethodAudit()
        {
            BrowseResponse response = await m_session.BrowseAsync(null, null, 100,
            [
                new BrowseDescription
                {
                    NodeId = ObjectTypeIds.AuditDataChannelEventType,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IncludeSubtypes = false,
                    ResultMask = (uint)BrowseResultMask.All
                },
                new BrowseDescription
                {
                    NodeId = ObjectTypeIds.AuditDataChannelEventType,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.Aggregates,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].References.Count, Is.EqualTo(1));
                Assert.That(ExpandedNodeId.ToNodeId(
                    response.Results[0].References[0].NodeId, m_session.NamespaceUris),
                    Is.EqualTo(ObjectTypeIds.AuditUpdateMethodEventType));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[1].References, Is.Empty);
            });
        }

        /// <summary>
        /// Operation attribution is modelled separately from standard Method audit inheritance.
        /// </summary>
        [Test]
        public async Task OperationEventDeclaresExactMandatoryCorrelationFields()
        {
            string[] names =
            [
                BrowseNames.ChannelId, BrowseNames.ContextId, BrowseNames.Generation,
                BrowseNames.Operation, BrowseNames.Revision, BrowseNames.Result,
                BrowseNames.DataChannelSourceNodeId, BrowseNames.SessionId, BrowseNames.ClientUserId,
                BrowseNames.ActionSucceeded, BrowseNames.AuditEventId
            ];
            NodeId[] types =
            [
                DataTypeIds.UInt32, DataTypeIds.Guid, DataTypeIds.UInt64, DataTypeIds.String,
                DataTypeIds.UInt32, DataTypeIds.DataChannelResult, DataTypeIds.NodeId,
                DataTypeIds.NodeId, DataTypeIds.String, DataTypeIds.Boolean, DataTypeIds.ByteString
            ];
            BrowseResponse browse = await m_session.BrowseAsync(null, null, 100,
            [
                new BrowseDescription
                {
                    NodeId = ObjectTypeIds.DataChannelOperationEventType,
                    BrowseDirection = BrowseDirection.Inverse,
                    ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                    IncludeSubtypes = false,
                    ResultMask = (uint)BrowseResultMask.All
                },
                new BrowseDescription
                {
                    NodeId = ObjectTypeIds.DataChannelOperationEventType,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    IncludeSubtypes = false,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(browse.Results[0].References.Count, Is.EqualTo(1));
            Assert.That(ExpandedNodeId.ToNodeId(browse.Results[0].References[0].NodeId, m_session.NamespaceUris),
                Is.EqualTo(ObjectTypeIds.BaseEventType));
            Assert.That(browse.Results[1].References.ToList().Select(reference => reference.BrowseName.Name),
                Is.EquivalentTo(names));
            for (int ii = 0; ii < names.Length; ii++)
            {
                ReferenceDescription property = browse.Results[1].References.ToList()
                    .Single(reference => reference.BrowseName.Name == names[ii]);
                Assert.That(property.NodeClass, Is.EqualTo(NodeClass.Variable));
                var propertyId = ExpandedNodeId.ToNodeId(property.NodeId, m_session.NamespaceUris);
                ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = propertyId, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = propertyId, AttributeId = Attributes.ValueRank }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(read.Results[0].WrappedValue.TryGetValue(out NodeId type), Is.True);
                Assert.That(type, Is.EqualTo(types[ii]));
                Assert.That(read.Results[1].WrappedValue.TryGetValue(out int rank), Is.True);
                Assert.That(rank, Is.EqualTo(ValueRanks.Scalar));
                BrowseResponse rule = await m_session.BrowseAsync(null, null, 10,
                [
                    new BrowseDescription
                    {
                        NodeId = propertyId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasModellingRule,
                        IncludeSubtypes = false,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(rule.Results[0].References.Count, Is.EqualTo(1));
                Assert.That(ExpandedNodeId.ToNodeId(rule.Results[0].References[0].NodeId, m_session.NamespaceUris),
                    Is.EqualTo(ObjectIds.ModellingRule_Mandatory));
            }
        }

        /// <summary>
        /// Verifies audit placeholders preserve method argument positions and redact sensitive identifiers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MethodAuditRedactionPreservesArgumentPositions(bool unknownIdentifier)
        {
            StatusCode refusedStatus = unknownIdentifier
                ? StatusCodes.BadNodeIdUnknown : StatusCodes.BadUserAccessDenied;
            DataChannelBindingDataType binding = new()
            {
                ContextId = Guid.NewGuid(),
                Generation = 1,
                PeerApplicationUri = "urn:test:peer",
                PeerKeyThumbprint = ByteString.From([1, 2, 3]),
                Challenge = ByteString.From([4, 5, 6]),
                ExpiresAt = DateTime.MaxValue
            };
            DataChannelParametersDataType parameters = DataChannelControlTestContext.Parameters();
            ArrayOf<Variant> openArguments =
            [
                new Variant(7u),
                new Variant(new NodeId(8u)),
                Variant.FromStructure(binding),
                Variant.FromStructure(parameters)
            ];
            ArrayOf<Variant> redactedOpen = DataChannelNodeManager.RedactAuditArguments(
                MethodIds.DataChannelManager_OpenDataChannel, openArguments, StatusCodes.Good);
            ArrayOf<Variant> deniedModify = DataChannelNodeManager.RedactAuditArguments(
                MethodIds.DataChannelManager_ModifyDataChannel,
                [new Variant(9u), new Variant(10u), new Variant(2u), Variant.FromStructure(parameters)],
                refusedStatus);
            ArrayOf<Variant> deniedClose = DataChannelNodeManager.RedactAuditArguments(
                MethodIds.DataChannelManager_CloseDataChannel,
                [new Variant(11u), new Variant(true)],
                refusedStatus);
            ArrayOf<Variant> successfulClose = DataChannelNodeManager.RedactAuditArguments(
                MethodIds.DataChannelManager_CloseDataChannel,
                [new Variant(12u), new Variant(false)],
                StatusCodes.Good);

            Assert.Multiple(() =>
            {
                Assert.That(redactedOpen.Count, Is.EqualTo(openArguments.Count));
                Assert.That(redactedOpen[0], Is.EqualTo(openArguments[0]));
                Assert.That(redactedOpen[1], Is.EqualTo(openArguments[1]));
                Assert.That(redactedOpen[2].IsNull, Is.True);
                Assert.That(redactedOpen[3], Is.EqualTo(openArguments[3]));
                Assert.That(deniedModify.Count, Is.EqualTo(4));
                Assert.That(deniedModify[0].IsNull, Is.True);
                Assert.That(deniedModify[1].TryGetValue(out uint operationId), Is.True);
                Assert.That(operationId, Is.EqualTo(10u));
                Assert.That(deniedModify[2], Is.EqualTo(new Variant(2u)));
                Assert.That(deniedModify[3], Is.EqualTo(Variant.FromStructure(parameters)));
                Assert.That(deniedClose.Count, Is.EqualTo(2));
                Assert.That(deniedClose[0].IsNull, Is.True);
                Assert.That(deniedClose[1].TryGetValue(out bool discard), Is.True);
                Assert.That(discard, Is.True);
                Assert.That(successfulClose[0].TryGetValue(out uint channelId), Is.True);
                Assert.That(channelId, Is.EqualTo(12u));
            });
        }

        /// <summary>
        /// Configured data-path admission has neither a fabricated Method audit nor a Session credential.
        /// </summary>
        [Test]
        public async Task ConfiguredAdmissionHasOnlyUncorrelatedOperationalAttribution()
        {
            ClientSession auditSession = await m_client.ConnectAsync(
                new Uri(m_endpoint), SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            try
            {
                ISession administrator = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(session => session.Id == auditSession.SessionId);
                administrator.RefreshEffectiveIdentity(new RoleBasedIdentity(
                    administrator.Identity, [Role.SecurityAdmin], m_server.CurrentInstance.NamespaceUris));
                uint auditSubscription = await CreateEventSubscriptionAsync(
                    auditSession, ObjectTypeIds.AuditDataChannelEventType, true, BrowseNames.EventId)
                    .ConfigureAwait(false);
                uint operationSubscription = await CreateEventSubscriptionAsync(
                    auditSession, ObjectTypeIds.DataChannelOperationEventType, true,
                    BrowseNames.AuditEventId, BrowseNames.SessionId, BrowseNames.ChannelId,
                    BrowseNames.ActionSucceeded, BrowseNames.Result, BrowseNames.ClientUserId).ConfigureAwait(false);
                await using DataChannelControlTestPair pair = await CreatePairAsync().ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                DataChannel channel = await pair.Client.OpenAsync(
                    DataChannelControlTestContext.Parameters(), cancellationToken: deadline.Token).ConfigureAwait(false);
                bool sawOperation = false;
                bool sawAuditSubscription = false;
                for (int attempt = 0; attempt < 20 && (!sawOperation || !sawAuditSubscription); attempt++)
                {
                    PublishResponse response = await auditSession.PublishAsync(null, [], deadline.Token)
                        .ConfigureAwait(false);
                    if (response.SubscriptionId == auditSubscription)
                    {
                        Assert.That(response.NotificationMessage.NotificationData, Is.Empty);
                        sawAuditSubscription = true;
                    }
                    foreach (ExtensionObject body in response.NotificationMessage.NotificationData)
                    {
                        if (!body.TryGetValue(out EventNotificationList? events) || events == null)
                        {
                            continue;
                        }
                        foreach (EventFieldList fields in events.Events)
                        {
                            Assert.That(response.SubscriptionId, Is.EqualTo(operationSubscription));
                            Assert.That(fields.EventFields[1].TryGetValue(out ByteString auditId), Is.True);
                            Assert.That(auditId.IsNull, Is.True);
                            Assert.That(fields.EventFields[2].TryGetValue(out NodeId sessionId), Is.True);
                            Assert.That(sessionId.IsNull, Is.True);
                            Assert.That(fields.EventFields[3].TryGetValue(out uint channelId), Is.True);
                            Assert.That(channelId, Is.EqualTo(channel.ChannelId));
                            Assert.That(fields.EventFields[4].TryGetValue(out bool succeeded), Is.True);
                            Assert.That(succeeded, Is.True);
                            Assert.That(fields.EventFields[5].TryGetValue(out int result), Is.True);
                            Assert.That(result, Is.EqualTo((int)DataChannelResult.Good));
                            Assert.That(fields.EventFields[6].TryGetValue(out string? user), Is.True);
                            Assert.That(user, Is.Empty);
                            sawOperation = true;
                        }
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(sawOperation, Is.True);
                    Assert.That(sawAuditSubscription, Is.True);
                });
                await auditSession.DeleteSubscriptionsAsync(
                    null, [auditSubscription, operationSubscription], deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                await auditSession.CloseAsync(5000, true).ConfigureAwait(false);
                await auditSession.DisposeAsync().ConfigureAwait(false);
            }
        }

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

        /// <summary>
        /// Verifies live manager Calls emit redacted standard audit arguments and do not disclose foreign flows.
        /// </summary>
        [Test]
        public async Task ManagerCallsEmitStandardMethodAuditWithRedactedArguments()
        {
            ClientSession auditSession = await m_client.ConnectAsync(
                new Uri(m_endpoint), SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            try
            {
                ISession administrator = m_server.CurrentInstance.SessionManager.GetSessions()
                    .Single(session => session.Id == auditSession.SessionId);
                administrator.RefreshEffectiveIdentity(new RoleBasedIdentity(
                    administrator.Identity, [Role.SecurityAdmin], m_server.CurrentInstance.NamespaceUris));
                uint auditSubscription = await CreateEventSubscriptionAsync(
                    auditSession, ObjectTypeIds.AuditDataChannelEventType, true,
                    BrowseNames.InputArguments, BrowseNames.OutputArguments, BrowseNames.EventId,
                    BrowseNames.Status, BrowseNames.StatusCodeId, BrowseNames.MethodId, BrowseNames.ClientUserId,
                    BrowseNames.SourceNode, BrowseNames.SourceName)
                    .ConfigureAwait(false);
                uint operationSubscription = await CreateEventSubscriptionAsync(
                    auditSession, ObjectTypeIds.DataChannelOperationEventType, true,
                    BrowseNames.AuditEventId, BrowseNames.ActionSucceeded, BrowseNames.Result,
                    BrowseNames.ChannelId, BrowseNames.ContextId, BrowseNames.Generation, BrowseNames.Revision,
                    BrowseNames.DataChannelSourceNodeId, BrowseNames.SessionId, BrowseNames.ClientUserId,
                    BrowseNames.Operation)
                    .ConfigureAwait(false);
                await using DataChannelControlTestPair pair = await CreatePairAsync().ConfigureAwait(false);
                var proxy = new DataChannelManagerTypeClient(m_session, ObjectIds.DataChannelManager, m_telemetry);
                uint id = pair.Client.ReserveChannelId();
                DataChannelGrantDataType grant = await proxy.OpenDataChannelAsync(
                    id, SourceId, Binding(pair), DataChannelControlTestContext.Parameters()).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await pair.Client.OpenAsync(
                    DataChannelControlTestContext.Parameters(), DataChannelAuthorizationMode.Session,
                    grant.GrantId, id, deadline.Token).ConfigureAwait(false);
                DataChannelStatusDataType staged = await proxy.ModifyDataChannelAsync(
                    id, 77, 2, DataChannelControlTestContext.Parameters(), deadline.Token).ConfigureAwait(false);
                Assert.That(staged.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                await Assert.ThrowsAsync<ServiceResultException>(async () => await proxy.OpenDataChannelAsync(
                    pair.Client.ReserveChannelId(), new NodeId("missing-source", SourceId.NamespaceIndex),
                    Binding(pair), DataChannelControlTestContext.Parameters(), deadline.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                CallResponse invalid = await m_session.CallAsync(null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.DataChannelManager,
                        MethodId = MethodIds.DataChannelManager_CloseDataChannel,
                        InputArguments = [new Variant("invalid-channel-id"), new Variant(true)]
                    }
                ], deadline.Token).ConfigureAwait(false);
                StatusCode invalidStatus = invalid.Results[0].StatusCode;
                Assert.That(StatusCode.IsBad(invalidStatus), Is.True);

                DataChannelControlAudit? foreignCloseAudit = null;
                void captureAudit(DataChannelControlAudit audit)
                {
                    if (audit.Operation == "CloseDataChannel")
                    {
                        foreignCloseAudit = audit;
                    }
                }
                m_provider.Audited += captureAudit;
                ClientSession foreignSession = await m_client.ConnectAsync(
                    new Uri(m_endpoint), SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                NodeId foreignSessionId = foreignSession.SessionId;
                try
                {
                    var foreignProxy = new DataChannelManagerTypeClient(
                        foreignSession, ObjectIds.DataChannelManager, m_telemetry);
                    ServiceResultException foreignUpdate = (await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await foreignProxy.ModifyDataChannelAsync(
                            id, 88, 2, DataChannelControlTestContext.Parameters(), deadline.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false))!;
                    Assert.That(foreignUpdate.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                    ServiceResultException foreignFlow = (await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await foreignProxy.CloseDataChannelAsync(id, true, deadline.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false))!;
                    Assert.That(foreignFlow.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                }
                finally
                {
                    await foreignSession.CloseAsync(5000, true).ConfigureAwait(false);
                    await foreignSession.DisposeAsync().ConfigureAwait(false);
                    m_provider.Audited -= captureAudit;
                }
                DataChannelStatusDataType closed = await proxy.CloseDataChannelAsync(id, true, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(closed.Result, Is.EqualTo(DataChannelResult.Good));

                var audits = new Dictionary<ByteString, ArrayOf<Variant>>();
                var operations = new Dictionary<ByteString, ArrayOf<Variant>>();
                for (int attempt = 0; attempt < 20 &&
                    (audits.Count < 7 || operations.Count < 7); attempt++)
                {
                    PublishResponse response = await auditSession.PublishAsync(null, [], deadline.Token)
                        .ConfigureAwait(false);
                    foreach (ExtensionObject body in response.NotificationMessage.NotificationData)
                    {
                        if (!body.TryGetValue(out EventNotificationList? events) || events == null)
                        {
                            continue;
                        }
                        foreach (EventFieldList fields in events.Events)
                        {
                            if (!fields.EventFields[0].TryGetValue(out NodeId eventType))
                            {
                                continue;
                            }
                            if (eventType == ObjectTypeIds.AuditDataChannelEventType &&
                                fields.EventFields[3].TryGetValue(out ByteString eventId))
                            {
                                audits.Add(eventId, fields.EventFields);
                            }
                            else if (eventType == ObjectTypeIds.DataChannelOperationEventType &&
                                fields.EventFields[1].TryGetValue(out ByteString auditId) && !auditId.IsNull)
                            {
                                operations.Add(auditId, fields.EventFields);
                            }
                        }
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(audits, Has.Count.EqualTo(7));
                    Assert.That(operations.Keys, Is.EquivalentTo(audits.Keys));
                    Assert.That(foreignCloseAudit, Is.Not.Null);
                    Assert.That(foreignCloseAudit!.ChannelId, Is.Zero);
                });
                bool sawSuccessfulOpen = false;
                bool sawRefusedOpen = false;
                bool sawForeignClose = false;
                bool sawStagedUpdate = false;
                bool sawValidationRefusal = false;
                bool sawForeignUpdate = false;
                bool sawSuccessfulClose = false;
                foreach ((ByteString eventId, ArrayOf<Variant> audit) in audits)
                {
                    ArrayOf<Variant> operation = operations[eventId];
                    Assert.That(audit[1].TryGetValue(out ArrayOf<Variant> arguments), Is.True);
                    Assert.That(audit[2].TryGetValue(out ArrayOf<Variant> outputs), Is.True);
                    Assert.That(audit[4].TryGetValue(out bool succeeded), Is.True);
                    Assert.That(audit[5].TryGetValue(out StatusCode status), Is.True);
                    Assert.That(audit[6].TryGetValue(out NodeId method), Is.True);
                    Assert.That(operation[2].TryGetValue(out bool actionSucceeded), Is.True);
                    Assert.That(operation[4].TryGetValue(out uint channelId), Is.True);
                    Assert.That(operation[9].TryGetValue(out NodeId sessionId), Is.True);
                    Assert.That(audit[8].TryGetValue(out NodeId sourceNode), Is.True);
                    Assert.That(audit[9].TryGetValue(out string? sourceName), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(actionSucceeded, Is.EqualTo(succeeded));
                        Assert.That(succeeded, Is.EqualTo(StatusCode.IsGood(status)));
                        Assert.That(operation[10], Is.EqualTo(audit[7]));
                        Assert.That(eventId.IsNull, Is.False);
                        Assert.That(sourceNode, Is.EqualTo(ObjectIds.DataChannelManager));
                        Assert.That(sourceName, Is.EqualTo("Attribute/Call"));
                    });
                    if (!succeeded)
                    {
                        Assert.That(channelId, Is.Zero);
                        Assert.That(operation[5].TryGetValue(out Uuid contextId), Is.True);
                        Assert.That(contextId, Is.EqualTo(Uuid.Empty));
                        Assert.That(operation[6].TryGetValue(out ulong generation), Is.True);
                        Assert.That(generation, Is.Zero);
                        Assert.That(operation[7].TryGetValue(out uint revision), Is.True);
                        Assert.That(revision, Is.Zero);
                        Assert.That(operation[8].TryGetValue(out NodeId source), Is.True);
                        Assert.That(source.IsNull, Is.True);
                    }
                    if (method == MethodIds.DataChannelManager_OpenDataChannel)
                    {
                        Assert.That(arguments.Count, Is.EqualTo(4));
                        Assert.That(arguments[2].IsNull, Is.True);
                        Assert.That(sessionId, Is.EqualTo(m_session.SessionId));
                        if (succeeded)
                        {
                            Assert.That(outputs.Count, Is.EqualTo(1));
                            Assert.That(outputs[0].IsNull, Is.True, "Never export a redeemable grant.");
                            Assert.That(channelId, Is.EqualTo(id));
                            sawSuccessfulOpen = true;
                        }
                        else
                        {
                            Assert.That(outputs, Is.Empty);
                            sawRefusedOpen = true;
                        }
                    }
                    else if (method == MethodIds.DataChannelManager_ModifyDataChannel)
                    {
                        Assert.That(arguments.Count, Is.EqualTo(4));
                        Assert.That(arguments[1].TryGetValue(out uint operationId), Is.True);
                        Assert.That(operation[3].TryGetValue(out int result), Is.True);
                        if (succeeded)
                        {
                            Assert.That(operationId, Is.EqualTo(77u));
                            Assert.That(result, Is.EqualTo((int)DataChannelResult.WouldBlock));
                            Assert.That(operation[7].TryGetValue(out uint revision), Is.True);
                            Assert.That(revision, Is.EqualTo(2u));
                            Assert.That(channelId, Is.EqualTo(id));
                            Assert.That(sessionId, Is.EqualTo(m_session.SessionId));
                            sawStagedUpdate = true;
                        }
                        else
                        {
                            Assert.That(status, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                            Assert.That(arguments[0].IsNull, Is.True);
                            Assert.That(operationId, Is.EqualTo(88u));
                            Assert.That(channelId, Is.Zero);
                            Assert.That(sessionId, Is.EqualTo(foreignSessionId));
                            sawForeignUpdate = true;
                        }
                    }
                    else
                    {
                        Assert.That(method, Is.EqualTo(MethodIds.DataChannelManager_CloseDataChannel));
                        Assert.That(arguments.Count, Is.EqualTo(2));
                        if (succeeded)
                        {
                            Assert.That(channelId, Is.EqualTo(id));
                            Assert.That(sessionId, Is.EqualTo(m_session.SessionId));
                            Assert.That(arguments[0], Is.EqualTo(new Variant(id)));
                            sawSuccessfulClose = true;
                            continue;
                        }
                        if (status == StatusCodes.BadNodeIdUnknown)
                        {
                            Assert.That(arguments[0].IsNull, Is.True);
                            Assert.That(sessionId, Is.EqualTo(foreignSessionId));
                            sawForeignClose = true;
                        }
                        else
                        {
                            Assert.That(status, Is.EqualTo(invalidStatus));
                            Assert.That(sessionId, Is.EqualTo(m_session.SessionId));
                            Assert.That(operation[3].TryGetValue(out int result), Is.True);
                            Assert.That(result, Is.EqualTo((int)DataChannelResult.InvalidArgument));
                            sawValidationRefusal = true;
                        }
                        Assert.That(channelId, Is.Zero);
                    }
                }
                Assert.Multiple(() =>
                {
                    Assert.That(sawSuccessfulOpen, Is.True);
                    Assert.That(sawRefusedOpen, Is.True);
                    Assert.That(sawForeignClose, Is.True);
                    Assert.That(sawStagedUpdate, Is.True);
                    Assert.That(sawValidationRefusal, Is.True);
                    Assert.That(sawForeignUpdate, Is.True);
                    Assert.That(sawSuccessfulClose, Is.True);
                });
                await auditSession.DeleteSubscriptionsAsync(null, [auditSubscription, operationSubscription], deadline.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                await auditSession.CloseAsync(5000, true).ConfigureAwait(false);
                await auditSession.DisposeAsync().ConfigureAwait(false);
            }
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
                    auditSession, ObjectTypeIds.DataChannelOperationEventType, BrowseNames.Result).ConfigureAwait(false);
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
                for (int attempt = 0; attempt < 20 &&
                    (!sawState || !sawGoodAudit || !sawDeniedAudit); attempt++)
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
                                Assert.That(type, Is.Not.EqualTo(ObjectTypeIds.DataChannelOperationEventType));
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
                                    type == ObjectTypeIds.DataChannelOperationEventType)
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
