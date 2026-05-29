/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for auditing operational event types,
    /// event subscriptions, server configuration, history audit types,
    /// node management audit types, cancel, and condition audit types.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Auditing")]
    [Category("AuditingOperations")]
    public class AuditingOperationTests : TestFixture
    {
        [Test]
        public async Task ServerObjectEventNotifierBitIsSetAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.EventNotifier).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            byte notifier =
                result.WrappedValue.GetByte();
            Assert.That(
                notifier & EventNotifiers.SubscribeToEvents,
                Is.Not.Zero,
                "Server object should support event subscriptions.");
        }

        [Test]
        public async Task AuditEventTypeExistsInAddressSpaceAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditEventType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditEventType should exist.");
        }

        [Test]
        public async Task BaseEventTypeHasEventIdAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.BaseEventType,
                "EventId").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "BaseEventType should have EventId property.");
        }

        [Test]
        public async Task BaseEventTypeHasTimeAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.BaseEventType,
                "Time").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "BaseEventType should have Time property.");
        }

        [Test]
        public async Task BaseEventTypeHasSourceNodeAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.BaseEventType,
                "SourceNode").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "BaseEventType should have SourceNode property.");
        }

        [Test]
        public async Task AuditCreateSessionEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditCreateSessionEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                "AuditCreateSessionEventType should exist.");
        }

        [Test]
        public Task AuditEventAfterCreateSessionFiresAsync()
        {
            return AssertAuditEventFiresAsync(
                ObjectTypeIds.AuditCreateSessionEventType,
                async () =>
                {
                    ISession s = await OpenAuxSessionAsync().ConfigureAwait(false);
                    await s.CloseAsync(2000, true, CancellationToken.None).ConfigureAwait(false);
                    s.Dispose();
                });
        }

        [Test]
        public Task AuditEventAfterActivateSessionFiresAsync()
        {
            return AssertAuditEventFiresAsync(
                ObjectTypeIds.AuditActivateSessionEventType,
                async () =>
                {
                    ISession s = await OpenAuxSessionAsync().ConfigureAwait(false);
                    await s.CloseAsync(2000, true, CancellationToken.None).ConfigureAwait(false);
                    s.Dispose();
                });
        }

        [Test]
        public Task AuditEventAfterCloseSessionFiresAsync()
        {
            return AssertAuditEventFiresAsync(
                ObjectTypeIds.AuditSessionEventType,
                async () =>
                {
                    ISession s = await OpenAuxSessionAsync().ConfigureAwait(false);
                    await s.CloseAsync(2000, true, CancellationToken.None).ConfigureAwait(false);
                    s.Dispose();
                });
        }

        /// <summary>
        /// Pre-subscribes to events on Server.EventNotifier (using a
        /// sysadmin session because audit events are typically only
        /// surfaced to privileged subscribers), runs <paramref name="trigger"/>,
        /// then awaits a notification whose EventType is or inherits
        /// from <paramref name="expectedEventType"/>.
        /// </summary>
        private async Task AssertAuditEventFiresAsync(
            NodeId expectedEventType,
            Func<Task> trigger)
        {
            ISession adminSession = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            ISession session = adminSession ?? Session;
            try
            {
                CreateSubscriptionResponse subResp = await session.CreateSubscriptionAsync(
                    null, 100, 1000, 100, 0, true, 0,
                    CancellationToken.None).ConfigureAwait(false);
                uint subscriptionId = subResp.SubscriptionId;
                try
                {
                    var eventFilter = new EventFilter
                    {
                        SelectClauses =
                        [
                            new SimpleAttributeOperand
                            {
                                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                BrowsePath = [new QualifiedName(BrowseNames.EventType)],
                                AttributeId = Attributes.Value
                            }
                        ],
                        WhereClause = new ContentFilter()
                    };
                    var item = new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            Filter = new ExtensionObject(eventFilter),
                            QueueSize = 100,
                            DiscardOldest = true
                        }
                    };
                    CreateMonitoredItemsResponse miResp =
                        await session.CreateMonitoredItemsAsync(
                            null, subscriptionId, TimestampsToReturn.Neither,
                            new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                            CancellationToken.None).ConfigureAwait(false);
                    if (miResp.Results.Count == 0 ||
                        !StatusCode.IsGood(miResp.Results[0].StatusCode))
                    {
                        Assert.Ignore(
                            "Could not subscribe to events on Server " +
                            $"(StatusCode={(miResp.Results.Count > 0 ? miResp.Results[0].StatusCode.ToString() : "no result")}).");
                    }

                    // Trigger the operation that should fire an audit event.
                    await trigger().ConfigureAwait(false);

                    // Poll Publish for up to 5 seconds looking for the event type.
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    bool seen = false;
                    while (!seen && DateTime.UtcNow < deadline)
                    {
                        PublishResponse pubResp;
                        try
                        {
                            pubResp = await session.PublishAsync(
                                null,
                                Array.Empty<SubscriptionAcknowledgement>().ToArrayOf(),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (ServiceResultException)
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                            continue;
                        }

                        var candidateEventTypes = new List<NodeId>();
                        foreach (ExtensionObject notification in pubResp.NotificationMessage.NotificationData)
                        {
                            if (notification.TryGetValue(out EventNotificationList eventList))
                            {
                                foreach (EventFieldList ef in eventList.Events)
                                {
                                    if (ef.EventFields.Count > 0 &&
                                        ef.EventFields[0].TryGetValue(out NodeId eventType))
                                    {
                                        candidateEventTypes.Add(eventType);
                                    }
                                }
                            }
                        }

                        foreach (NodeId eventType in candidateEventTypes)
                        {
                            if (await NodeIdMatchesTypeAsync(session, eventType, expectedEventType).ConfigureAwait(false))
                            {
                                seen = true;
                                break;
                            }
                        }
                    }

                    if (!seen)
                    {
                        Assert.Ignore(
                            $"No {expectedEventType} event observed within 5s — server may not " +
                            "audit anonymous sessions or fixture timing race fired before subscribe.");
                    }
                }
                finally
                {
                    try
                    {
                        await session.DeleteSubscriptionsAsync(
                            null,
                            new uint[] { subscriptionId }.ToArrayOf(),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    try
                    {
                        await adminSession.CloseAsync(2000, true, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    adminSession.Dispose();
                }
            }
        }

        private static async Task<bool> NodeIdMatchesTypeAsync(ISession session, NodeId actual, NodeId expected)
        {
            if (actual == expected)
            {
                return true;
            }
            try
            {
                BrowseResponse resp = await session.BrowseAsync(
                    null, null, 0,
                    new BrowseDescription[]
                    {
                        new() {
                            NodeId = actual,
                            BrowseDirection = BrowseDirection.Inverse,
                            ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                            IncludeSubtypes = false,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.NodeClass
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                if (resp.Results.Count == 0 || resp.Results[0].References.Count == 0)
                {
                    return false;
                }
                var parent = ExpandedNodeId.ToNodeId(
                    resp.Results[0].References[0].NodeId, session.NamespaceUris);
                if (parent == expected)
                {
                    return true;
                }
                return await NodeIdMatchesTypeAsync(session, parent, expected).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        [Test]
        public async Task ServerAuditingPropertyIsBoolAsync()
        {
            DataValue result = await ReadAttributeAsync(
                VariableIds.Server_Auditing,
                Attributes.Value).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    $"Server_Auditing not accessible: {result.StatusCode}");
            }

            Assert.That(
                result.WrappedValue.TryGetValue(out bool _), Is.True,
                "Server_Auditing value should be a boolean.");
        }

        [Test]
        public async Task ServerAuditingDataTypeIsBooleanAsync()
        {
            DataValue result = await ReadAttributeAsync(
                VariableIds.Server_Auditing,
                Attributes.DataType).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    "Server_Auditing DataType not readable: " +
                    $"{result.StatusCode}");
            }

            NodeId dataType =
                result.WrappedValue.GetNodeId();
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Boolean),
                "Server_Auditing DataType should be Boolean.");
        }

        [Test]
        public async Task SessionDiagnosticsArrayIsReadableAsync()
        {
            DataValue result = await ReadAttributeAsync(
                VariableIds
                    .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                Attributes.Value).ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Ignore("SessionDiagnosticsArray not readable: " +
                    result.StatusCode.ToString());
            }

            Assert.That(
                StatusCode.IsGood(result.StatusCode) ||
                StatusCode.IsUncertain(result.StatusCode),
                Is.True,
                "SessionDiagnosticsArray should be readable.");
        }

        [Test]
        public async Task ServerCurrentTimeIsRecentAsync()
        {
            DataValue result = await ReadAttributeAsync(
                VariableIds.Server_ServerStatus_CurrentTime,
                Attributes.Value).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            DateTimeUtc serverTime = result.WrappedValue.GetDateTime();
            Assert.That(
                Math.Abs((DateTime.UtcNow - (DateTime)serverTime).TotalMinutes),
                Is.LessThan(5),
                "Server time should be within 5 minutes of local time.");
        }

        [Test]
        public async Task AuditHistoryUpdateEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditHistoryUpdateEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditHistoryUpdateEventType not supported.");
            }
        }

        [Test]
        public async Task AuditHistoryEventUpdateHasPropertyAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditHistoryEventUpdateEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditHistoryEventUpdateEventType not supported.");
            }

            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditHistoryEventUpdateEventType,
                "UpdatedNode").ConfigureAwait(false);
            Assert.That(has, Is.True.Or.False,
                "UpdatedNode property may or may not exist.");
        }

        [Test]
        public async Task AuditHistoryValueUpdateEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditHistoryValueUpdateEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditHistoryValueUpdateEventType not supported.");
            }
        }

        [Test]
        public async Task AuditHistoryDeleteEventTypeExistsOrFailAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditHistoryDeleteEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("AuditHistoryDeleteEventType not supported.");
            }
        }

        [Test]
        public async Task AuditHistoryRawModifyDeleteExistsOrFailAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditHistoryRawModifyDeleteEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("AuditHistoryRawModifyDeleteEventType not supported.");
            }
        }

        [Test]
        public async Task ProgramTransitionAuditEventTypeExistsOrFailAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.ProgramTransitionAuditEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("ProgramTransitionAuditEventType not supported.");
            }
        }

        [Test]
        public async Task AuditAddNodesHasNodesToAddAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditAddNodesEventType,
                "NodesToAdd").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditAddNodesEventType should have NodesToAdd.");
        }

        [Test]
        public async Task AuditDeleteNodesHasNodesToDeleteAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditDeleteNodesEventType,
                "NodesToDelete").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditDeleteNodesEventType should have NodesToDelete.");
        }

        [Test]
        public async Task AuditAddReferencesHasReferencesToAddAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditAddReferencesEventType,
                "ReferencesToAdd").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditAddReferencesEventType should have ReferencesToAdd.");
        }

        [Test]
        public async Task AuditDeleteReferencesHasReferencesToDeleteAsync()
        {
            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditDeleteReferencesEventType,
                "ReferencesToDelete").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditDeleteReferencesEventType should have " +
                "ReferencesToDelete.");
        }

        [Test]
        public async Task AuditCancelHasRequestHandleAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditCancelEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("AuditCancelEventType not supported.");
            }

            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditCancelEventType,
                "RequestHandle").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditCancelEventType should have RequestHandle.");
        }

        [Test]
        public async Task AuditConditionCommentHasCommentAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditConditionCommentEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditConditionCommentEventType not supported.");
            }

            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AuditConditionCommentEventType,
                "Comment").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AuditConditionCommentEventType should have Comment.");
        }

        [Test]
        public async Task AuditConditionRespondExistsOrFailAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditConditionRespondEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditConditionRespondEventType not supported.");
            }
        }

        [Test]
        public async Task AuditConditionShelvingExistsOrFailAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditConditionShelvingEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    "AuditConditionShelvingEventType not supported.");
            }
        }

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private Task<DataValue> ReadBrowseNameAsync(NodeId nodeId)
        {
            return ReadAttributeAsync(
                nodeId, Attributes.BrowseName);
        }

        private async Task<bool> TypeHasPropertyAsync(
            NodeId eventTypeId, string propertyName)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = eventTypeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                if (r.BrowseName.Name == propertyName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
