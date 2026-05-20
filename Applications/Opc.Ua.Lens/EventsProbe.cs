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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.Telemetry;

namespace UaLens;

/// <summary>
/// Headless validator for the EventView event-flow pipeline: connects,
/// creates a classic event subscription rooted at Server (i=2253) with the
/// same BaseEventType SelectClauses as EventViewPlugin uses, writes to
/// TriggerNode01 to fire a server-side event, and reports how many events
/// were actually delivered to the FastEventCallback. Run with
/// <c>--probe-events</c>.
/// </summary>
internal static class EventsProbe
{
    public static async Task<int> RunAsync(string endpoint)
    {
        Console.WriteLine("== EventsProbe ==");
        Console.WriteLine($"   endpoint: {endpoint}");

        var logBuf = new LogRingBuffer(1024);
        var telemetry = new AppTelemetryContext(logBuf);
        var connection = new ConnectionService(telemetry);
        await using (connection.ConfigureAwait(false))
        {
            try
            {
                await connection.ConnectAsync(
                    new ConnectionOptions
                    {
                        EndpointUrl = endpoint,
                        UseSecurity = false,
                        Engine = SubscriptionEngineKind.Classic
                    },
                    CancellationToken.None).ConfigureAwait(false);

                if (connection.Session is not { } session)
                {
                    Console.WriteLine("FAIL: no session after Connect.");
                    return 2;
                }

                // Build SelectClauses identical to EventViewPlugin defaults.
                string[] fields = ["EventId", "EventType", "SourceName", "Time", "Message", "Severity"];
                var filter = new EventFilter();
                foreach (string f in fields)
                {
                    var op = new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        AttributeId = Attributes.Value
                    };
                    op.BrowsePath = op.BrowsePath.AddItem(QualifiedName.From(f));
                    filter.SelectClauses = filter.SelectClauses.AddItem(op);
                }
                // Match CommonTestWorkers.cs pattern: WhereClause with
                // EventType == BaseEventType (accepts every event subtype).
                var whereClause = new ContentFilter();
                whereClause.Push(
                    FilterOperator.OfType,
                    new Variant[] { new(ObjectTypeIds.BaseEventType) });
                filter.WhereClause = whereClause;

                var sub = new Subscription(session.MessageContext.Telemetry, new SubscriptionOptions
                {
                    DisplayName = "EventsProbeSub",
                    PublishingInterval = 500,
                    KeepAliveCount = 10,
                    LifetimeCount = 1000,
                    PublishingEnabled = true
                });
#pragma warning disable CA2000
                using (sub)
                {
                int evCount = 0;
                sub.FastEventCallback = (s, notif, st) =>
                {
                    if (notif?.Events is null)
                    {
                        return;
                    }
                    foreach (EventFieldList ev in notif.Events)
                    {
                        Interlocked.Increment(ref evCount);
                        Console.WriteLine($"[event] handle={ev.ClientHandle} fields={ev.EventFields.Count}");
                    }
                };
                session.AddSubscription(sub);
                await sub.CreateAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"  subscription created: Id={sub.Id}");

                var mi = new MonitoredItem(session.MessageContext.Telemetry, new MonitoredItemOptions
                {
                    DisplayName = "event:Server",
                    StartNodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = 0,
                    QueueSize = 100,
                    DiscardOldest = true,
                    Filter = filter
                });
                sub.AddItem(mi);

                // Also subscribe DataChange to CurrentTime as a sanity check —
                // if DataChange ticks but EventNotifier never fires, the
                // pipeline routes data but not events.
                int dcCount = 0;
                sub.FastDataChangeCallback = (s, dc, st) => Interlocked.Add(ref dcCount, dc.MonitoredItems.Count);
                var miTime = new MonitoredItem(session.MessageContext.Telemetry, new MonitoredItemOptions
                {
                    DisplayName = "data:CurrentTime",
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = 1000,
                    QueueSize = 10,
                    DiscardOldest = true
                });
                sub.AddItem(miTime);
                await sub.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

                Console.WriteLine($"  monitored item handle:                       {mi.ClientHandle}");
                Console.WriteLine($"  monitored item status error:                 {mi.Status.Error}");
                Console.WriteLine($"  monitored item filter result:                {mi.Status.FilterResult}");
                if (mi.Status.FilterResult is EventFilterResult efr)
                {
                    Console.WriteLine($"    select clause count:                       {efr.SelectClauseResults.Count}");
                    for (int i = 0; i < efr.SelectClauseResults.Count; i++)
                    {
                        Console.WriteLine($"    select[{i}] {fields[i]}:                  {efr.SelectClauseResults[i]}");
                    }
                }

                // Wait a bit for any auto-fired audit events.
                await Task.Delay(2500).ConfigureAwait(false);
                int auditCount = Interlocked.CompareExchange(ref evCount, 0, 0);
                Console.WriteLine($"  after 2.5s wait, events received:            {auditCount}");

                // Try every known way to trigger an event-emitting action:
                // (a) Call Server.GetMonitoredItems — fires AuditCallEventType.
                // (b) Write to Server.Auditing — fires AuditUpdateMethodEventType on audit-enabled servers.
                try
                {
                    CallMethodRequest req = new()
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems
                    };
                    req.InputArguments = req.InputArguments.AddItem(new Variant((uint)123));
                    ArrayOf<CallMethodRequest> calls = new CallMethodRequest[] { req };
                    CallResponse cr = await session.CallAsync(null, calls, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"  method call (audit trigger) status:          {cr.Results[0].StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  method call threw: {ex.Message}");
                }

                // Try writing to Server.Auditing (toggle then restore).
                try
                {
                    WriteValue wv = new()
                    {
                        NodeId = VariableIds.Server_Auditing,
                        AttributeId = Attributes.Value,
                        Value = new DataValue { WrappedValue = new Variant(true) }
                    };
                    ArrayOf<WriteValue> writes = new WriteValue[] { wv };
                    WriteResponse wresp = await session.WriteAsync(null, writes, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"  Server.Auditing write status:                {wresp.Results[0]}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Server.Auditing write threw: {ex.Message}");
                }

                // Fire a trigger event by writing to TriggerNode01.
                NodeId triggerId = await ResolveTriggerNodeAsync(session).ConfigureAwait(false);
                Console.WriteLine($"  TriggerNode01 NodeId:                        {triggerId}");
                if (!triggerId.IsNull)
                {
                    var wv = new WriteValue
                    {
                        NodeId = triggerId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue { WrappedValue = new Variant(42) }
                    };
                    ArrayOf<WriteValue> nodesToWrite = new WriteValue[] { wv };
                    WriteResponse wr = await session.WriteAsync(null, nodesToWrite, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"  trigger write status:                        {wr.Results[0]}");
                }

                await Task.Delay(3500).ConfigureAwait(false);
                int finalCount = Interlocked.CompareExchange(ref evCount, 0, 0);
                int finalDc = Interlocked.CompareExchange(ref dcCount, 0, 0);
                Console.WriteLine($"  total events received:                       {finalCount}");
                Console.WriteLine($"  total data-changes received:                 {finalDc}");

                // Dump any warnings the SDK logged during the probe.
                Console.WriteLine();
                Console.WriteLine("---- SDK log buffer ----");
                foreach (var entry in logBuf.SnapshotList())
                {
                    Console.WriteLine($"  [{entry.Level}] {entry.Category}: {entry.Message}");
                }

                await session.RemoveSubscriptionAsync(sub).ConfigureAwait(false);
                await connection.DisconnectAsync().ConfigureAwait(false);

                if (finalCount > 0)
                {
                    Console.WriteLine("EVENTS PROBE PASS");
                    return 0;
                }
                Console.WriteLine("FAIL: no events received.");
                return 3;
                }
#pragma warning restore CA2000
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: exception: {ex}");
                return 4;
            }
        }
    }

    private static async Task<NodeId> ResolveTriggerNodeAsync(ManagedSession session)
    {
        // Resolve via the reference-server namespace URI so we don't depend
        // on the namespace index ordering.  Path: CTT > NodeIds >
        // NodeIds_Events > NodeIds_Events_TriggerNode01 (browse names).
        int nsIndex = session.NamespaceUris.GetIndex("http://opcfoundation.org/Quickstarts/ReferenceServer");
        if (nsIndex < 0)
        {
            // Fall back to scanning all known indexes — write-and-discover.
            for (int i = 1; i < session.NamespaceUris.Count; i++)
            {
                NodeId id = await TryResolveTriggerInNamespaceAsync(session, (ushort)i)
                    .ConfigureAwait(false);
                if (!id.IsNull)
                {
                    return id;
                }
            }
            return NodeId.Null;
        }
        return await TryResolveTriggerInNamespaceAsync(session, (ushort)nsIndex)
            .ConfigureAwait(false);
    }

    private static async Task<NodeId> TryResolveTriggerInNamespaceAsync(
        ManagedSession session, ushort ns)
    {
        var browsePath = new BrowsePath { StartingNode = ObjectIds.ObjectsFolder };
        string[] segments = ["CTT", "NodeIds", "NodeIds_Events", "NodeIds_Events_TriggerNode01"];
        foreach (string seg in segments)
        {
            browsePath.RelativePath.Elements = browsePath.RelativePath.Elements.AddItem(new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName(seg, ns)
            });
        }
        ArrayOf<BrowsePath> paths = new BrowsePath[] { browsePath };
        TranslateBrowsePathsToNodeIdsResponse resp = await session
            .TranslateBrowsePathsToNodeIdsAsync(null, paths, CancellationToken.None).ConfigureAwait(false);
        if (resp.Results.Count == 0
            || resp.Results[0].StatusCode != StatusCodes.Good
            || resp.Results[0].Targets.Count == 0)
        {
            return NodeId.Null;
        }
        return ExpandedNodeId.ToNodeId(resp.Results[0].Targets[0].TargetId, session.NamespaceUris);
    }
}
