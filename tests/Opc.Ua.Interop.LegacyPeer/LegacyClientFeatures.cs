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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Interop.LegacyPeer
{
    /// <summary>
    /// The event, subscription, identity and service checks of the 1.5.x
    /// client against a server with the Quickstarts reference server address
    /// space (TestData and Alarms node managers included).
    /// </summary>
    public static partial class LegacyClientChecks
    {
        public const string AlarmsNamespace = "http://test.org/UA/Alarms/";

        private static readonly TimeSpan s_eventWait = TimeSpan.FromSeconds(10);
        private static readonly int[] s_partialInt32Array = [20, 30];

        /// <summary>
        /// Subscribes to BaseEventType events of the Server object and writes the
        /// reference server's event trigger node, which reports one event.
        /// </summary>
        private static async Task EventSubscriptionAsync(ClientContext c)
        {
            var events = new List<EventFieldList>();
            Subscription subscription = await CreateEventSubscriptionAsync(c, ObjectIds.Server, events).ConfigureAwait(false);
            try
            {
                WriteResponse write = await c.Session
                    .WriteAsync(null, new WriteValueCollection { Write(c.Id("NodeIds_Events_TriggerNode01"), 1) }, c.Ct)
                    .ConfigureAwait(false);
                Require(StatusCode.IsGood(write.Results[0]), "writing the event trigger returned " + write.Results[0]);
                EventFieldList received = await WaitForEventAsync(events, e =>
                    (e.EventFields[4].Value as LocalizedText)?.Text?.Contains("Trigger event", StringComparison.Ordinal) == true).ConfigureAwait(false);
                Require(received != null, $"no trigger event in {s_eventWait.TotalSeconds} s; received {events.Count} events");
                Require(received.EventFields[0].Value is byte[] id && id.Length > 0, "the event has no EventId");
                Require(received.EventFields[5].Value is ushort severity && severity > 0, "the event has no Severity");
            }
            finally
            {
                await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Calls ConditionRefresh for an event subscription and expects the
        /// RefreshStartEvent and RefreshEndEvent pair.
        /// </summary>
        private static async Task ConditionRefreshAsync(ClientContext c)
        {
            var events = new List<EventFieldList>();
            Subscription subscription = await CreateEventSubscriptionAsync(c, ObjectIds.Server, events).ConfigureAwait(false);
            try
            {
                CallMethodResult result = await CallAsync(c, ObjectTypeIds.ConditionType,
                    MethodIds.ConditionType_ConditionRefresh, new Variant(subscription.Id)).ConfigureAwait(false);
                Require(StatusCode.IsGood(result.StatusCode), "ConditionRefresh returned " + result.StatusCode);
                EventFieldList end = await WaitForEventAsync(events,
                    e => Equals(e.EventFields[1].Value, ObjectTypeIds.RefreshEndEventType)).ConfigureAwait(false);
                Require(end != null, "no RefreshEndEvent received");
                Require(events.Any(e => Equals(e.EventFields[1].Value, ObjectTypeIds.RefreshStartEventType)),
                    "no RefreshStartEvent received");
            }
            finally
            {
                await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Starts the alarm simulation of the reference server, waits for an
        /// unacknowledged condition event and acknowledges it.
        /// </summary>
        private static async Task AlarmAcknowledgeAsync(ClientContext c)
        {
            ushort alarmsNs = (ushort)c.Session.NamespaceUris.GetIndex(AlarmsNamespace);
            Require(alarmsNs != ushort.MaxValue, "the Alarms namespace is missing");
            var folder = new NodeId("Alarms", alarmsNs);
            var events = new List<EventFieldList>();
            Subscription subscription = await CreateEventSubscriptionAsync(c, folder, events, condition: true)
                .ConfigureAwait(false);
            try
            {
                CallMethodResult started = await CallAsync(c, folder, new NodeId("Alarms.Start", alarmsNs),
                    new Variant(30u)).ConfigureAwait(false);
                Require(StatusCode.IsGood(started.StatusCode), "Alarms.Start returned " + started.StatusCode);
                // Fields: 0 EventId, 1 EventType, 2 SourceNode, 3 Time, 4 Message,
                // 5 Severity, 6 ConditionId, 7 AckedState/Id, 8 Retain.
                EventFieldList unacked = await WaitForEventAsync(events, e =>
                    e.EventFields[6].Value is NodeId && Equals(e.EventFields[7].Value, false) &&
                    Equals(e.EventFields[8].Value, true), TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                if (unacked == null)
                {
                    EventFieldList sample;
                    lock (events)
                    {
                        sample = events.LastOrDefault();
                    }
                    throw new InvalidOperationException(
                        $"no unacknowledged condition in 20 s; received {events.Count} events, last: " +
                        (sample == null ? "none" : string.Join(" | ", sample.EventFields.Select(f => $"{f.TypeInfo}:{f.Value}"))));
                }
                CallMethodResult ack = await CallAsync(c, (NodeId)unacked.EventFields[6].Value,
                    MethodIds.AcknowledgeableConditionType_Acknowledge,
                    new Variant((byte[])unacked.EventFields[0].Value),
                    new Variant(new LocalizedText("acknowledged by the interop test"))).ConfigureAwait(false);
                Require(StatusCode.IsGood(ack.StatusCode), "Acknowledge returned " + ack.StatusCode);
            }
            finally
            {
                await CallAsync(c, folder, new NodeId("Alarms.End", alarmsNs)).ConfigureAwait(false);
                await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
            }
        }

        private static async Task<Subscription> CreateEventSubscriptionAsync(
            ClientContext c,
            NodeId notifier,
            List<EventFieldList> events,
            bool condition = false)
        {
            var subscription = new Subscription(c.Session.DefaultSubscription)
            {
                PublishingInterval = 100,
                KeepAliveCount = 10,
                LifetimeCount = 100
            };
            c.Session.AddSubscription(subscription);
            await subscription.CreateAsync(c.Ct).ConfigureAwait(false);

            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventId);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventType);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.SourceNode);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Time);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Message);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Severity);
            if (condition)
            {
                filter.SelectClauses.Add(new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.ConditionType,
                    BrowsePath = [],
                    AttributeId = Attributes.NodeId
                });
                // AckedState/Id as a two element browse path.
                filter.SelectClauses.Add(new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.AcknowledgeableConditionType,
                    BrowsePath = [new QualifiedName(BrowseNames.AckedState), new QualifiedName(BrowseNames.Id)],
                    AttributeId = Attributes.Value
                });
                filter.AddSelectClause(ObjectTypeIds.ConditionType, BrowseNames.Retain);
            }
            filter.WhereClause.Push(FilterOperator.OfType, new LiteralOperand(
                new Variant(condition ? ObjectTypeIds.AcknowledgeableConditionType : ObjectTypeIds.BaseEventType)));

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = notifier,
                AttributeId = Attributes.EventNotifier,
                SamplingInterval = 0,
                QueueSize = 100,
                Filter = filter
            };
            item.Notification += (_, e) =>
            {
                if (e.NotificationValue is EventFieldList fields)
                {
                    lock (events)
                    {
                        events.Add(fields);
                    }
                }
            };
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(c.Ct).ConfigureAwait(false);
            Require(ServiceResult.IsGood(item.Status.Error), "event monitored item " + item.Status.Error);
            return subscription;
        }

        private static async Task<EventFieldList> WaitForEventAsync(
            List<EventFieldList> events,
            Func<EventFieldList, bool> match,
            TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? s_eventWait);
            while (DateTime.UtcNow < deadline)
            {
                lock (events)
                {
                    EventFieldList found = events.FirstOrDefault(match);
                    if (found != null)
                    {
                        return found;
                    }
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return null;
        }

        private static async Task DeleteSubscriptionAsync(ClientContext c, Subscription subscription)
        {
            try
            {
                await subscription.DeleteAsync(true, c.Ct).ConfigureAwait(false);
                await c.Session.RemoveSubscriptionAsync(subscription, c.Ct).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // The check already reported its outcome.
            }
        }

        private static async Task<CallMethodResult> CallAsync(ClientContext c, NodeId objectId, NodeId methodId,
            params Variant[] arguments)
        {
            CallResponse response = await c.Session.CallAsync(null, new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = objectId,
                    MethodId = methodId,
                    InputArguments = new VariantCollection(arguments)
                }
            }, c.Ct).ConfigureAwait(false);
            return response.Results[0];
        }

        /// <summary>
        /// Absolute and percent deadbands on the static AnalogItem
        /// DataAccess_AnalogType_Double (EURange 0..100): changes inside the
        /// deadband are not reported, larger ones are.
        /// </summary>
        private static async Task DeadbandFilterAsync(ClientContext c)
        {
            NodeId analog = c.Id("DataAccess_AnalogType_Double");
            foreach (DeadbandType type in new[] { DeadbandType.Absolute, DeadbandType.Percent })
            {
                await WriteValueAsync(c, analog, 0.0).ConfigureAwait(false);
                var values = new List<double>();
                Subscription subscription = await CreateDataSubscriptionAsync(c, analog, values, item =>
                {
                    item.Filter = new DataChangeFilter
                    {
                        Trigger = DataChangeTrigger.StatusValue,
                        DeadbandType = (uint)type,
                        DeadbandValue = 10 // 10 units, or 10 % of the EURange 0..100
                    };
                }).ConfigureAwait(false);
                try
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    foreach (double v in new[] { 5.0, 20.0, 25.0, 40.0 })
                    {
                        await WriteValueAsync(c, analog, v).ConfigureAwait(false);
                        await Task.Delay(400).ConfigureAwait(false);
                    }
                    await Task.Delay(800).ConfigureAwait(false);
                    double[] seen;
                    lock (values)
                    {
                        seen = [.. values];
                    }
                    string list = string.Join(", ", seen);
                    Require(seen.Contains(20.0) && seen.Contains(40.0), $"{type}: 20 and 40 not reported ({list})");
                    Require(!seen.Contains(5.0) && !seen.Contains(25.0), $"{type}: changes inside the deadband reported ({list})");
                }
                finally
                {
                    await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A queue of 2 with DiscardOldest and a slow publishing interval: five
        /// quick writes deliver the last two values, with the Overflow bit set.
        /// </summary>
        private static async Task QueueOverflowAsync(ClientContext c)
        {
            NodeId node = c.Id("Scalar_Static_Int32");
            await WriteValueAsync(c, node, 0).ConfigureAwait(false);
            var values = new List<DataValue>();
            Subscription subscription = await CreateDataSubscriptionAsync(c, node, null, item =>
            {
                item.SamplingInterval = 0;
                item.QueueSize = 2;
                item.DiscardOldest = true;
            }, publishingInterval: 2000, rawValues: values).ConfigureAwait(false);
            try
            {
                // Let the initial value go out first.
                await Task.Delay(2500).ConfigureAwait(false);
                lock (values)
                {
                    values.Clear();
                }
                for (int ii = 1; ii <= 5; ii++)
                {
                    await WriteValueAsync(c, node, ii).ConfigureAwait(false);
                }
                await Task.Delay(3000).ConfigureAwait(false);
                DataValue[] seen;
                lock (values)
                {
                    seen = [.. values];
                }
                string list = string.Join(", ", seen.Select(v => $"{v.Value}:{v.StatusCode}"));
                Require(seen.Length == 2, $"expected the last 2 of 5 values, got {seen.Length} ({list})");
                Require(Equals(seen[0].Value, 4) && Equals(seen[1].Value, 5), $"expected 4 and 5, got {list}");
                Require(seen.Any(v => v.StatusCode.Overflow), $"no value carries the Overflow bit ({list})");
            }
            finally
            {
                await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A sampling item linked to a reporting item by SetTriggering reports
        /// only when the triggering item reports.
        /// </summary>
        private static async Task TriggeringAsync(ClientContext c)
        {
            var subscription = new Subscription(c.Session.DefaultSubscription) { PublishingInterval = 100 };
            c.Session.AddSubscription(subscription);
            await subscription.CreateAsync(c.Ct).ConfigureAwait(false);
            try
            {
                int linkedReports = 0;
                var trigger = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = c.Id("Scalar_Static_Int32"),
                    SamplingInterval = 50,
                    MonitoringMode = MonitoringMode.Reporting
                };
                var linked = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = c.Id("Scalar_Static_String"),
                    SamplingInterval = 50,
                    MonitoringMode = MonitoringMode.Sampling
                };
                linked.Notification += (_, e) =>
                {
                    if (e.NotificationValue is MonitoredItemNotification)
                    {
                        Interlocked.Increment(ref linkedReports);
                    }
                };
                subscription.AddItem(trigger);
                subscription.AddItem(linked);
                await subscription.ApplyChangesAsync(c.Ct).ConfigureAwait(false);
                SetTriggeringResponse set = await c.Session.SetTriggeringAsync(null, subscription.Id,
                    trigger.Status.Id, new UInt32Collection { linked.Status.Id }, new UInt32Collection(), c.Ct).ConfigureAwait(false);
                Require(StatusCode.IsGood(set.AddResults[0]), "SetTriggering returned " + set.AddResults[0]);

                await Task.Delay(500).ConfigureAwait(false);
                int before = Volatile.Read(ref linkedReports);
                await WriteValueAsync(c, c.Id("Scalar_Static_String"), "linked " + Guid.NewGuid()).ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
                Require(Volatile.Read(ref linkedReports) == before, "the sampling item reported without its trigger");
                await WriteValueAsync(c, c.Id("Scalar_Static_Int32"), Environment.TickCount).ConfigureAwait(false);
                await Task.Delay(1000).ConfigureAwait(false);
                Require(Volatile.Read(ref linkedReports) > before, "the triggered item did not report");
            }
            finally
            {
                await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Republish of a sequence number the subscription never sent returns
        /// BadMessageNotAvailable, and of an unknown subscription
        /// BadSubscriptionIdInvalid. (Republishing a sent message is racy: the
        /// client acknowledges it with its next Publish and the server may drop
        /// acknowledged messages.)
        /// </summary>
        private static async Task RepublishAsync(ClientContext c)
        {
            NodeId node = c.Id("Scalar_Static_Int32");
            Subscription subscription = await CreateDataSubscriptionAsync(c, node, null, _ => { }).ConfigureAwait(false);
            try
            {
                StatusCode notSent = await RepublishStatusAsync(c, subscription.Id, subscription.SequenceNumber + 1000)
                    .ConfigureAwait(false);
                Require(notSent == StatusCodes.BadMessageNotAvailable,
                    "Republish of an unsent message returned " + StatusCodes.GetBrowseName(notSent.Code));
                StatusCode unknown = await RepublishStatusAsync(c, subscription.Id + 100_000, 1).ConfigureAwait(false);
                Require(unknown == StatusCodes.BadSubscriptionIdInvalid,
                    "Republish of an unknown subscription returned " + StatusCodes.GetBrowseName(unknown.Code));
            }
            finally
            {
                await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
            }
        }

        private static async Task<StatusCode> RepublishStatusAsync(ClientContext c, uint subscriptionId, uint sequence)
        {
            try
            {
                RepublishResponse response = await c.Session.RepublishAsync(null, subscriptionId, sequence, c.Ct)
                    .ConfigureAwait(false);
                return response.ResponseHeader.ServiceResult;
            }
            catch (ServiceResultException sre)
            {
                return sre.StatusCode;
            }
        }

        /// <summary>
        /// Transfers a subscription to a second session of the same user, which
        /// then receives its data changes.
        /// </summary>
        private static async Task TransferSubscriptionAsync(ClientContext c)
        {
            NodeId node = c.Id("Scalar_Static_Int32");
            Subscription subscription = await CreateDataSubscriptionAsync(c, node, null, _ => { }).ConfigureAwait(false);
            ISession target = await c.NewSessionAsync(c.Session.Identity).ConfigureAwait(false);
            try
            {
                SubscriptionCollection transferred = new SubscriptionCollection { subscription }.CloneSubscriptions(false);
                foreach (Subscription s in transferred)
                {
                    target.AddSubscription(s);
                }
                int notifications = 0;
                foreach (MonitoredItem item in transferred[0].MonitoredItems)
                {
                    item.Notification += (_, _) => Interlocked.Increment(ref notifications);
                }
                bool ok = await target.TransferSubscriptionsAsync(transferred, true, c.Ct).ConfigureAwait(false);
                Require(ok, "TransferSubscriptions failed");
                await Task.Delay(300).ConfigureAwait(false);
                int before = Volatile.Read(ref notifications);
                WriteResponse write = await target.WriteAsync(null,
                    new WriteValueCollection { Write(node, Environment.TickCount) }, c.Ct).ConfigureAwait(false);
                Require(StatusCode.IsGood(write.Results[0]), "write returned " + write.Results[0]);
                var deadline = DateTime.UtcNow + s_eventWait;
                while (Volatile.Read(ref notifications) == before && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
                Require(Volatile.Read(ref notifications) > before, "the transferred subscription reported nothing");
            }
            finally
            {
                await DeleteSubscriptionAsync(c, subscription).ConfigureAwait(false);
                target.DeleteSubscriptionsOnClose = true;
                await target.CloseAsync(c.Ct).ConfigureAwait(false);
                target.Dispose();
            }
        }

        private static async Task<Subscription> CreateDataSubscriptionAsync(
            ClientContext c,
            NodeId node,
            List<double> values,
            Action<MonitoredItem> configure,
            int publishingInterval = 100,
            List<DataValue> rawValues = null)
        {
            var subscription = new Subscription(c.Session.DefaultSubscription)
            {
                PublishingInterval = publishingInterval,
                KeepAliveCount = 10,
                LifetimeCount = 100
            };
            c.Session.AddSubscription(subscription);
            await subscription.CreateAsync(c.Ct).ConfigureAwait(false);
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = node,
                SamplingInterval = 50,
                QueueSize = 10
            };
            configure(item);
            item.Notification += (_, e) =>
            {
                if (e.NotificationValue is MonitoredItemNotification n)
                {
                    if (values != null && n.Value.Value is IConvertible)
                    {
                        lock (values)
                        {
                            values.Add(Convert.ToDouble(n.Value.Value, System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                    if (rawValues != null)
                    {
                        lock (rawValues)
                        {
                            rawValues.Add(n.Value);
                        }
                    }
                }
            };
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(c.Ct).ConfigureAwait(false);
            Require(ServiceResult.IsGood(item.Status.Error), "monitored item " + item.Status.Error);
            return subscription;
        }

        private static async Task WriteValueAsync(ClientContext c, NodeId node, object value)
        {
            WriteResponse response = await c.Session.WriteAsync(null, new WriteValueCollection { Write(node, value) }, c.Ct)
                .ConfigureAwait(false);
            Require(StatusCode.IsGood(response.Results[0]), $"write of {node} returned {response.Results[0]}");
        }

        /// <summary>
        /// A second session with a wrong password is rejected.
        /// </summary>
        private static async Task WrongPasswordRejectedAsync(ClientContext c)
        {
            try
            {
                ISession session = await c.NewSessionAsync(
                    new UserIdentity("user1", System.Text.Encoding.UTF8.GetBytes("wrong password"))).ConfigureAwait(false);
                await session.CloseAsync(c.Ct).ConfigureAwait(false);
                session.Dispose();
            }
            catch (ServiceResultException sre)
            {
                Require(sre.StatusCode == StatusCodes.BadUserAccessDenied || sre.StatusCode == StatusCodes.BadIdentityTokenRejected,
                    "the wrong password was rejected with " + StatusCodes.GetBrowseName(sre.StatusCode));
                return;
            }
            throw new InvalidOperationException("a session with a wrong password was activated");
        }

        /// <summary>
        /// A second session with an X509 user identity token (a self-signed
        /// user certificate) reads the server state.
        /// </summary>
        private static async Task X509UserTokenAsync(ClientContext c)
        {
            X509Certificate2 user = CertificateFactory
                .CreateCertificate("CN=InteropUser, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            ISession session = await c.NewSessionAsync(new UserIdentity(user)).ConfigureAwait(false);
            try
            {
                Require(session.Identity.TokenType == UserTokenType.Certificate,
                    "the session identity is " + session.Identity.TokenType);
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, c.Ct).ConfigureAwait(false);
                Require(StatusCode.IsGood(state.StatusCode), "reading with the X509 user returned " + state.StatusCode);
            }
            finally
            {
                await session.CloseAsync(c.Ct).ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// Registers two nodes, reads through the registered ids, unregisters.
        /// </summary>
        private static async Task RegisterNodesAsync(ClientContext c)
        {
            var nodes = new NodeIdCollection { c.Id("Scalar_Static_Int32"), c.Id("Scalar_Static_String") };
            RegisterNodesResponse registered = await c.Session.RegisterNodesAsync(null, nodes, c.Ct).ConfigureAwait(false);
            Require(registered.RegisteredNodeIds.Count == 2, "RegisterNodes returned " + registered.RegisteredNodeIds.Count + " ids");
            ReadResponse read = await c.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                new ReadValueIdCollection(registered.RegisteredNodeIds.Select(Value)), c.Ct).ConfigureAwait(false);
            Require(read.Results.All(r => StatusCode.IsGood(r.StatusCode)),
                "reading registered nodes returned " + string.Join(", ", read.Results.Select(r => r.StatusCode)));
            await c.Session.UnregisterNodesAsync(null, registered.RegisteredNodeIds, c.Ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the raw history of a historizing variable of the last 3 hours.
        /// </summary>
        private static async Task HistoryReadRawAsync(ClientContext c)
        {
            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.UtcNow.AddHours(-3),
                EndTime = DateTime.UtcNow,
                NumValuesPerNode = 10,
                ReturnBounds = false
            };
            HistoryReadResponse response = await c.Session.HistoryReadAsync(null, new ExtensionObject(details),
                TimestampsToReturn.Source, false,
                new HistoryReadValueIdCollection { new HistoryReadValueId { NodeId = c.Id("Scalar_Static_Double") } }, c.Ct)
                .ConfigureAwait(false);
            HistoryReadResult result = response.Results[0];
            Require(StatusCode.IsGood(result.StatusCode), "HistoryRead returned " + result.StatusCode);
            var data = ExtensionObject.ToEncodeable(result.HistoryData) as HistoryData;
            Require(data != null && data.DataValues.Count > 0, "HistoryRead returned no values");
            Require(data.DataValues.Count <= 10, $"{data.DataValues.Count} values despite NumValuesPerNode 10");
            if (result.ContinuationPoint?.Length > 0)
            {
                await c.Session.HistoryReadAsync(null, new ExtensionObject(details), TimestampsToReturn.Source, true,
                    new HistoryReadValueIdCollection
                    {
                        new HistoryReadValueId { NodeId = c.Id("Scalar_Static_Double"), ContinuationPoint = result.ContinuationPoint }
                    }, c.Ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adds an object below the Objects folder, finds it by browsing and
        /// deletes it again.
        /// </summary>
        private static async Task NodeManagementAsync(ClientContext c)
        {
            string name = "InteropAdded_" + Guid.NewGuid().ToString("N")[..8];
            var requested = new NodeId(name, c.Ns);
            AddNodesResponse added = await c.Session.AddNodesAsync(null, new AddNodesItemCollection
            {
                new AddNodesItem
                {
                    ParentNodeId = ObjectIds.ObjectsFolder,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    RequestedNewNodeId = requested,
                    BrowseName = new QualifiedName(name, c.Ns),
                    NodeClass = NodeClass.Object,
                    NodeAttributes = new ExtensionObject(new ObjectAttributes
                    {
                        DisplayName = new LocalizedText(name),
                        SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName
                    }),
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                }
            }, c.Ct).ConfigureAwait(false);
            Require(StatusCode.IsGood(added.Results[0].StatusCode), "AddNodes returned " + added.Results[0].StatusCode);
            NodeId nodeId = added.Results[0].AddedNodeId;
            ReferenceDescriptionCollection refs = await BrowseAsync(c, ObjectIds.ObjectsFolder, 0).ConfigureAwait(false);
            Require(refs.Any(r => r.BrowseName.Name == name), "the added node is not organized by the Objects folder");
            DeleteNodesResponse deleted = await c.Session.DeleteNodesAsync(null, new DeleteNodesItemCollection
            {
                new DeleteNodesItem { NodeId = nodeId, DeleteTargetReferences = true }
            }, c.Ct).ConfigureAwait(false);
            Require(StatusCode.IsGood(deleted.Results[0]), "DeleteNodes returned " + deleted.Results[0]);
        }

        /// <summary>
        /// Writes and reads parts of an array with an IndexRange.
        /// </summary>
        private static async Task IndexRangeAsync(ClientContext c)
        {
            NodeId node = c.Id("Scalar_Static_Arrays_Int32");
            await WriteValueAsync(c, node, Enumerable.Range(0, 10).ToArray()).ConfigureAwait(false);
            WriteResponse write = await c.Session.WriteAsync(null, new WriteValueCollection
            {
                new WriteValue
                {
                    NodeId = node,
                    AttributeId = Attributes.Value,
                    IndexRange = "2:3",
                    Value = new DataValue(new Variant(s_partialInt32Array))
                }
            }, c.Ct).ConfigureAwait(false);
            Require(StatusCode.IsGood(write.Results[0]), "writing the index range 2:3 returned " + write.Results[0]);
            ReadResponse read = await c.Session.ReadAsync(null, 0, TimestampsToReturn.Neither, new ReadValueIdCollection
            {
                new ReadValueId { NodeId = node, AttributeId = Attributes.Value, IndexRange = "1:4" }
            }, c.Ct).ConfigureAwait(false);
            Require(StatusCode.IsGood(read.Results[0].StatusCode), "reading the index range 1:4 returned " + read.Results[0].StatusCode);
            Require(read.Results[0].Value is int[] part && part.SequenceEqual(new[] { 1, 20, 30, 4 }),
                "index range 1:4 read " + string.Join(",", (read.Results[0].Value as int[]) ?? []));
        }

        /// <summary>
        /// FindServers lists the server and GetEndpoints offers the endpoint
        /// of this session.
        /// </summary>
        private static async Task FindServersAsync(ClientContext c)
        {
            using DiscoveryClient client = await DiscoveryClient
                .CreateAsync(c.Config, new Uri(c.Url), DiagnosticsMasks.None, c.Ct)
                .ConfigureAwait(false);
            ApplicationDescriptionCollection servers = await client.FindServersAsync(null, c.Ct).ConfigureAwait(false);
            string serverUri = c.Session.Endpoint.Server.ApplicationUri;
            Require(servers.Any(s => s.ApplicationUri == serverUri),
                $"FindServers does not list {serverUri}: {string.Join(", ", servers.Select(s => s.ApplicationUri))}");
            EndpointDescriptionCollection endpoints = await client.GetEndpointsAsync(null, c.Ct).ConfigureAwait(false);
            Require(endpoints.Any(e => e.SecurityPolicyUri == c.Session.Endpoint.SecurityPolicyUri &&
                e.SecurityMode == c.Session.Endpoint.SecurityMode),
                "GetEndpoints does not offer the session's endpoint");
        }

        /// <summary>
        /// Reconnects the session on a new secure channel (ActivateSession on
        /// the new channel) and keeps reading.
        /// </summary>
        private static async Task SessionReconnectAsync(ClientContext c)
        {
            NodeId before = c.Session.SessionId;
            await ((Session)c.Session).ReconnectAsync(c.Ct).ConfigureAwait(false);
            Require(c.Session.SessionId == before, $"the session id changed from {before} to {c.Session.SessionId}");
            DataValue state = await c.Session.ReadValueAsync(VariableIds.Server_ServerStatus_State, c.Ct).ConfigureAwait(false);
            Require(StatusCode.IsGood(state.StatusCode), "reading after the reconnect returned " + state.StatusCode);
        }
    }
}
