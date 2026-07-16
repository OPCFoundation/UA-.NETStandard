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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// Collects condition events from Server.EventNotifier for CTT-style
    /// Alarms &amp; Conditions tests.
    /// </summary>
    internal sealed class AlarmEventCollector : IDisposable, IAsyncDisposable
    {
        private AlarmEventCollector(ISession session, EventFilter eventFilter)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_eventFilter = eventFilter ?? throw new ArgumentNullException(nameof(eventFilter));
            m_shutdown = new CancellationTokenSource();
        }

        public enum FieldIndex
        {
            EventId,
            EventType,
            Time,
            Severity,
            ConditionId,
            Comment,
            Retain,
            ActiveStateId,
            AckedStateId,
            ConfirmedStateId,
            EnabledStateId,
            AckedStateTransitionTime,
            ConfirmedStateTransitionTime,
            EnabledStateTransitionTime,
            Quality
        }

        public Dictionary<NodeId, List<EventFieldList>> EventLog
        {
            get
            {
                var snapshot = new Dictionary<NodeId, List<EventFieldList>>();
                foreach (KeyValuePair<NodeId, ConcurrentQueue<EventFieldList>> kvp in m_eventLog)
                {
                    snapshot[kvp.Key] = [.. kvp.Value];
                }
                return snapshot;
            }
        }

        public static async Task<AlarmEventCollector> CreateAsync(ISession session)
        {
            return await CreateAsync(session, CreateCttEventFilter()).ConfigureAwait(false);
        }

        public static async Task<AlarmEventCollector> CreateAsync(
            ISession session,
            EventFilter eventFilter)
        {
            var collector = new AlarmEventCollector(session, eventFilter);
            try
            {
                await collector.InitializeAsync().ConfigureAwait(false);
            }
            catch
            {
                await collector.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            return collector;
        }

        public static EventFilter CreateCttEventFilter()
        {
            return new EventFilter
            {
                SelectClauses =
                [
                    Select(ObjectTypeIds.BaseEventType, BrowseNames.EventId),
                    Select(ObjectTypeIds.BaseEventType, BrowseNames.EventType),
                    Select(ObjectTypeIds.BaseEventType, BrowseNames.Time),
                    Select(ObjectTypeIds.BaseEventType, BrowseNames.Severity),
                    SelectNodeId(ObjectTypeIds.ConditionType),
                    Select(ObjectTypeIds.ConditionType, BrowseNames.Comment),
                    Select(ObjectTypeIds.ConditionType, BrowseNames.Retain),
                    Select(ObjectTypeIds.AlarmConditionType, BrowseNames.ActiveState, BrowseNames.Id),
                    Select(ObjectTypeIds.AcknowledgeableConditionType, BrowseNames.AckedState, BrowseNames.Id),
                    Select(ObjectTypeIds.AcknowledgeableConditionType, BrowseNames.ConfirmedState, BrowseNames.Id),
                    Select(ObjectTypeIds.ConditionType, BrowseNames.EnabledState, BrowseNames.Id),
                    Select(
                        ObjectTypeIds.AcknowledgeableConditionType,
                        BrowseNames.AckedState,
                        BrowseNames.TransitionTime),
                    Select(
                        ObjectTypeIds.AcknowledgeableConditionType,
                        BrowseNames.ConfirmedState,
                        BrowseNames.TransitionTime),
                    Select(
                        ObjectTypeIds.ConditionType,
                        BrowseNames.EnabledState,
                        BrowseNames.TransitionTime),
                    Select(ObjectTypeIds.ConditionType, BrowseNames.Quality)
                ],
                WhereClause = new ContentFilter()
            };
        }

        public void Reset()
        {
            m_eventLog.Clear();
        }

        /// <summary>
        /// Calls <c>ConditionType_ConditionRefresh</c> on the server, asking it
        /// to push the current state of all retained conditions to this collector's
        /// subscription queue.
        /// Use this immediately after writing a value that should trigger an alarm
        /// so that slow CI runners (notably macOS-hosted agents under load) do not
        /// have to wait for the server's natural publish cycle to deliver the event.
        /// </summary>
        public async Task ConditionRefreshAsync()
        {
            if (m_subscriptionId == 0)
            {
                return;
            }

            try
            {
                await m_session.CallAsync(
                    null,
                    new CallMethodRequest[]
                    {
                        new()
                        {
                            ObjectId = ObjectTypeIds.ConditionType,
                            MethodId = MethodIds.ConditionType_ConditionRefresh,
                            InputArguments = new Variant[] { new(m_subscriptionId) }.ToArrayOf()
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // Best effort — ignore if the server does not support ConditionRefresh.
            }
        }

        public async Task<EventFieldList> WaitForEventAsync(
            NodeId conditionId,
            Func<EventFieldList, bool> predicate,
            TimeSpan timeout)
        {
            if (conditionId.IsNull)
            {
                throw new ArgumentException("ConditionId must not be null.", nameof(conditionId));
            }
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow <= deadline)
            {
                if (TryFindEvent(conditionId, predicate, out EventFieldList eventFields))
                {
                    return eventFields;
                }

                await Task.Delay(50, m_shutdown.Token).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"No matching event for condition {conditionId} arrived within {timeout}.");
        }

        public bool HasEvents(NodeId conditionId)
        {
            return m_eventLog.TryGetValue(conditionId, out ConcurrentQueue<EventFieldList> queue) && !queue.IsEmpty;
        }

        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_shutdown.Cancel();

            if (m_publishLoop != null)
            {
                try
                {
                    await m_publishLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ServiceResultException ex) when (IsShutdownStatus(ex.StatusCode))
                {
                }
            }

            if (m_subscriptionId != 0)
            {
                try
                {
                    await m_session.DeleteSubscriptionsAsync(
                        null,
                        new uint[] { m_subscriptionId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Best effort cleanup; the session may already be closed.
                }
                m_subscriptionId = 0;
            }

            m_shutdown.Dispose();
        }

        public void Dispose()
        {
            m_shutdown.Cancel();
            m_shutdown.Dispose();
            m_disposed = true;
        }

        public static bool TryGetConditionId(EventFieldList eventFields, out NodeId conditionId)
        {
            conditionId = NodeId.Null;
            return TryGetFieldVariant(eventFields, FieldIndex.ConditionId, out Variant field) &&
                field.TryGetValue(out conditionId) &&
                !conditionId.IsNull;
        }

        public static bool TryGetBoolean(
            EventFieldList eventFields,
            FieldIndex fieldIndex,
            out bool value)
        {
            value = false;
            return TryGetFieldVariant(eventFields, fieldIndex, out Variant field) &&
                field.TryGetValue(out value);
        }

        public static bool TryGetLocalizedText(
            EventFieldList eventFields,
            FieldIndex fieldIndex,
            out LocalizedText value)
        {
            value = LocalizedText.Null;
            return TryGetFieldVariant(eventFields, fieldIndex, out Variant field) &&
                field.TryGetValue(out value);
        }

        public static bool TryGetDateTime(
            EventFieldList eventFields,
            FieldIndex fieldIndex,
            out DateTime value)
        {
            value = default;
            if (!TryGetFieldVariant(eventFields, fieldIndex, out Variant field) ||
                !field.TryGetValue(out DateTimeUtc dateTimeUtc))
            {
                return false;
            }

            value = dateTimeUtc.ToDateTime();
            return true;
        }

        public static bool TryGetByteString(
            EventFieldList eventFields,
            FieldIndex fieldIndex,
            out ByteString value)
        {
            value = default;
            return TryGetFieldVariant(eventFields, fieldIndex, out Variant field) &&
                field.TryGetValue(out value);
        }

        private async Task InitializeAsync()
        {
            CreateSubscriptionResponse subscriptionResponse = await m_session.CreateSubscriptionAsync(
                null,
                100,
                1000,
                100,
                0,
                true,
                0,
                CancellationToken.None).ConfigureAwait(false);
            m_subscriptionId = subscriptionResponse.SubscriptionId;

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
                    Filter = new ExtensionObject(m_eventFilter),
                    QueueSize = 100,
                    DiscardOldest = true
                }
            };

            CreateMonitoredItemsResponse monitoredItemsResponse = await m_session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (monitoredItemsResponse.Results.Count == 0 ||
                StatusCode.IsBad(monitoredItemsResponse.Results[0].StatusCode))
            {
                StatusCode statusCode = monitoredItemsResponse.Results.Count == 0
                    ? StatusCodes.BadUnexpectedError
                    : monitoredItemsResponse.Results[0].StatusCode;
                throw new ServiceResultException(
                    statusCode,
                    $"Could not subscribe to alarm events on Server: {statusCode}");
            }

            m_publishLoop = Task.Run(PublishLoopAsync);
        }

        private async Task PublishLoopAsync()
        {
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = Array.Empty<SubscriptionAcknowledgement>().ToArrayOf();
            while (!m_shutdown.IsCancellationRequested)
            {
                PublishResponse publishResponse;
                try
                {
                    publishResponse = acknowledgements.Count == 0
                        ? await m_session.PublishWithTimeoutAsync(1000).ConfigureAwait(false)
                        : await m_session.PublishWithTimeoutAsync(acknowledgements, 1000).ConfigureAwait(false);
                    acknowledgements = Array.Empty<SubscriptionAcknowledgement>().ToArrayOf();
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadRequestTimeout)
                {
                    continue;
                }

                ProcessNotificationMessage(publishResponse.NotificationMessage);

                if (publishResponse.SubscriptionId == m_subscriptionId &&
                    publishResponse.NotificationMessage.SequenceNumber != 0)
                {
                    acknowledgements = new SubscriptionAcknowledgement[]
                    {
                        new() {
                            SubscriptionId = publishResponse.SubscriptionId,
                            SequenceNumber = publishResponse.NotificationMessage.SequenceNumber
                        }
                    }.ToArrayOf();
                }
            }
        }

        private void ProcessNotificationMessage(NotificationMessage notificationMessage)
        {
            foreach (ExtensionObject notification in notificationMessage.NotificationData)
            {
                if (!notification.TryGetValue(out EventNotificationList eventNotificationList))
                {
                    continue;
                }

                foreach (EventFieldList eventFields in eventNotificationList.Events)
                {
                    if (!TryGetConditionId(eventFields, out NodeId conditionId))
                    {
                        continue;
                    }

                    ConcurrentQueue<EventFieldList> queue = m_eventLog.GetOrAdd(
                        conditionId,
                        static _ => new ConcurrentQueue<EventFieldList>());
                    queue.Enqueue(eventFields);
                }
            }
        }

        private bool TryFindEvent(
            NodeId conditionId,
            Func<EventFieldList, bool> predicate,
            out EventFieldList eventFields)
        {
            eventFields = null;
            if (!m_eventLog.TryGetValue(conditionId, out ConcurrentQueue<EventFieldList> queue))
            {
                return false;
            }

            foreach (EventFieldList candidate in queue)
            {
                if (predicate(candidate))
                {
                    eventFields = candidate;
                    return true;
                }
            }
            return false;
        }

        private static SimpleAttributeOperand Select(NodeId typeDefinitionId, params string[] browseNames)
        {
            var browsePath = new QualifiedName[browseNames.Length];
            for (int i = 0; i < browseNames.Length; i++)
            {
                browsePath[i] = new QualifiedName(browseNames[i]);
            }

            return new SimpleAttributeOperand
            {
                TypeDefinitionId = typeDefinitionId,
                BrowsePath = browsePath.ToArrayOf(),
                AttributeId = Attributes.Value
            };
        }

        private static SimpleAttributeOperand SelectNodeId(NodeId typeDefinitionId)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = typeDefinitionId,
                BrowsePath = Array.Empty<QualifiedName>().ToArrayOf(),
                AttributeId = Attributes.NodeId
            };
        }

        private static bool TryGetFieldVariant(
            EventFieldList eventFields,
            FieldIndex fieldIndex,
            out Variant value)
        {
            value = default;
            int index = (int)fieldIndex;
            if (eventFields.EventFields.Count <= index)
            {
                return false;
            }

            value = eventFields.EventFields[index];
            return true;
        }

        private static bool IsShutdownStatus(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadRequestTimeout ||
                statusCode == StatusCodes.BadRequestInterrupted ||
                statusCode == StatusCodes.BadSessionClosed ||
                statusCode == StatusCodes.BadSessionIdInvalid ||
                statusCode == StatusCodes.BadSubscriptionIdInvalid;
        }

        private readonly ISession m_session;
        private readonly EventFilter m_eventFilter;
        private readonly ConcurrentDictionary<NodeId, ConcurrentQueue<EventFieldList>> m_eventLog = [];
        private readonly CancellationTokenSource m_shutdown;
        private Task m_publishLoop;
        private uint m_subscriptionId;
        private bool m_disposed;
    }
}
