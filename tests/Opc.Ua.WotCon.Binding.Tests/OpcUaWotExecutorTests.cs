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
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Binding.OpcUa;
using Opc.Ua.WotCon.Binding.Planners;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>
    /// End-to-end tests proving OPC UA-to-OPC UA translation: a WoT Thing
    /// Description describing an OPC UA target is compiled and executed
    /// against a real in-process OPC UA server (a reference server). The
    /// fixture starts the server and connects a single session once, and
    /// each test exercises one operation: readproperty, writeproperty with
    /// readback, observeproperty (native data-change subscription),
    /// invokeaction (a real Method with ordered input/output arguments,
    /// resolved through <c>uav:componentOf</c>) and subscribeevent (a real
    /// EventNotifier and an emitted event, asserting selected fields). The
    /// counter, action and event forms address their NodeIds with the
    /// portable <c>nsu=</c> form to prove namespace-table resolution.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotExecutorTests
    {
        // Server_ServerStatus_CurrentTime (readable UtcTime) and
        // Server_ServerStatus_State (a read-only Int32) are standard nodes
        // every OPC UA server exposes; they need no namespace resolution.
        private const string CurrentTimeNodeId = "i=2258";
        private const string StateNodeId = "i=2259";

        // The ReferenceServer's own namespace; nodes below are addressed with
        // the portable nsu= form so resolution goes through the session's
        // namespace table rather than a guessed namespace index.
        private const string ReferenceServerNamespace = "http://opcfoundation.org/Quickstarts/ReferenceServer";
        private const string CounterNodeId = "nsu=" + ReferenceServerNamespace + ";s=Scalar_Static_Int32";
        private const string AddMethodNodeId = "nsu=" + ReferenceServerNamespace + ";s=Methods_Add";
        private const string MethodsObjectNodeId = "nsu=" + ReferenceServerNamespace + ";s=Methods";
        private const string TriggerNode01Id = "nsu=" + ReferenceServerNamespace + ";s=NodeIds_Events_TriggerNode01";

        // The standard Server object (i=2253, ns=0) addressed portably too
        // (nsu= for the base namespace); every ReportEvent call in this
        // stack reports starting at the Server object, so it always receives
        // events regardless of where the event's SourceNode lives.
        private const string ServerObjectNodeId = "nsu=http://opcfoundation.org/UA/;i=2253";

        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ISession m_session = null!;
        private WotProtocolBinderRegistry m_registry = null!;
        private WotBindingPlan m_plan = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = "opc.tcp",
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true
            };
            await m_serverFixture.StartAsync(pkiRoot).ConfigureAwait(false);

            var clientFixture = new ClientFixture(telemetry);
            await clientFixture.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
            var url = new Uri("opc.tcp://localhost:" + m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));
            m_session = await clientFixture.ConnectAsync(url, SecurityPolicies.None).ConfigureAwait(false);

            m_registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new OpcUaBindingPlanner() },
                new IWotBindingExecutor[]
                {
                    new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                    {
                        SessionFactory = (endpoint, ct) => new ValueTask<ISession>(m_session),
                        DisposeSession = false,
                        ObserveInterval = TimeSpan.FromMilliseconds(100)
                    })
                });

            string ep = url.ToString();
            string td = BuildThingDescription(ep);
            m_plan = m_registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, System.Text.Encoding.UTF8.GetBytes(td)));

            Assert.That(m_plan.Diagnostics.Any(d => d.IsError), Is.False,
                "The Thing Description must compile without diagnostic errors: " +
                string.Join("; ", m_plan.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session is not null)
            {
                await m_session.CloseAsync().ConfigureAwait(false);
                m_session.Dispose();
            }
            if (m_serverFixture is not null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReadProperty_ReturnsRealServerValueAsync()
        {
            WotCompiledForm read = m_plan.CompiledForms.First(
                f => f.AffordanceName == "time" && f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True, "Reading a real OPC UA node must succeed.");
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.Not.Null);
            }
        }

        [Test]
        public async Task WriteProperty_ReadOnlyNode_MapsBadStatusAsync()
        {
            WotCompiledForm write = m_plan.CompiledForms.First(
                f => f.AffordanceName == "state" && f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(new DataValue(new Variant(1))).ConfigureAwait(false);
                Assert.That(result.Success, Is.False,
                    "Writing a read-only OPC UA node must be translated and its bad status mapped.");
            }
        }

        [Test]
        public async Task WriteProperty_WithReadback_RoundTripsThroughPortableNodeIdAsync()
        {
            const int expected = 4242;
            WotCompiledForm write = m_plan.CompiledForms.First(
                f => f.AffordanceName == "counter" && f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm read = m_plan.CompiledForms.First(
                f => f.AffordanceName == "counter" && f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel writeChannel = await m_registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult writeResult = await writeChannel
                    .WriteAsync(new DataValue(new Variant(expected))).ConfigureAwait(false);
                Assert.That(writeResult.Success, Is.True,
                    $"Writing the counter property (portable nsu= NodeId) must succeed: {writeResult.Error}");
            }

            IWotBindingChannel readChannel = await m_registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult readResult = await readChannel.ReadAsync().ConfigureAwait(false);
                Assert.That(readResult.Success, Is.True,
                    $"Reading back the counter property must succeed: {readResult.Error}");
                Assert.That(readResult.Value.WrappedValue.TryGetValue(out int actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected),
                    "The read-back value must match the value written through the binding.");
            }
        }

        [Test]
        public async Task ObserveProperty_DeliversNotificationViaNativeSubscriptionAsync()
        {
            WotCompiledForm observe = m_plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ObserveProperty);

            var received = new ConcurrentQueue<object?>();
            IWotBindingChannel channel = await m_registry.OpenChannelAsync(observe).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                IWotSubscription subscription = await channel
                    .ObserveAsync(n => received.Enqueue(n.Value.WrappedValue.AsBoxedObject())).ConfigureAwait(false);
                await using (subscription.ConfigureAwait(false))
                {
                    bool got = false;
                    for (int i = 0; i < 100 && !got; i++)
                    {
                        got = !received.IsEmpty;
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                    Assert.That(got, Is.True,
                        "The observe channel must deliver a value from the server via a native MonitoredItem.");
                }
            }
        }

        [Test]
        public async Task InvokeAction_RealMethod_ReturnsOrderedOutputArgumentsAsync()
        {
            WotCompiledForm invoke = m_plan.CompiledForms.First(
                f => f.AffordanceName == "add" && f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel channel = await m_registry.OpenChannelAsync(invoke).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotInvokeResult result = await channel
                    .InvokeAsync(new Variant[] { new Variant(2.5f), new Variant(3u) }).ConfigureAwait(false);
                Assert.That(result.Success, Is.True,
                    $"Invoking the real 'Methods_Add' method (resolved via uav:componentOf) must succeed: {result.Error}");
                Assert.That(result.Outputs.Count, Is.EqualTo(1));
                Assert.That(result.Outputs[0].WrappedValue.TryGetValue(out float sum), Is.True);
                Assert.That(sum, Is.EqualTo(5.5f).Within(0.0001f),
                    "The Add method sums its Float and UInt32 arguments in order.");
            }
        }

        [Test]
        public async Task SubscribeEvent_RealEventNotifier_DeliversSelectedFieldsAsync()
        {
            WotCompiledForm subscribe = m_plan.CompiledForms.First(
                f => f.AffordanceName == "trigger" && f.Operation == WoTBindingCapabilityEnum.SubscribeEvent);

            var received = new ConcurrentQueue<WotNotification>();
            IWotBindingChannel channel = await m_registry.OpenChannelAsync(subscribe).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                IWotSubscription subscription = await channel
                    .SubscribeEventAsync(n => received.Enqueue(n)).ConfigureAwait(false);
                await using (subscription.ConfigureAwait(false))
                {
                    NodeId triggerNodeId = ResolvePortableNodeId(TriggerNode01Id);
                    var write = new WriteValue
                    {
                        NodeId = triggerNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(42))
                    };
                    WriteResponse writeResponse = await m_session
                        .WriteAsync(null, new WriteValue[] { write }, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True,
                        "Writing the trigger node must succeed and fire a BaseEvent.");

                    WotNotification? notification = null;
                    for (int i = 0; i < 100 && notification is null; i++)
                    {
                        if (!received.TryDequeue(out notification))
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                        }
                    }
                    Assert.That(notification, Is.Not.Null,
                        "The subscribeevent channel must deliver the event triggered by the write.");

                    Assert.That(notification!.EventFields.TryGetValue("EventId", out DataValue eventIdValue), Is.True);
                    Assert.That(eventIdValue.WrappedValue.TryGetValue(out ByteString eventId), Is.True);
                    Assert.That(eventId.Length, Is.GreaterThan(0), "EventId must be a non-empty identifier.");

                    Assert.That(notification.EventFields.TryGetValue("EventType", out DataValue eventTypeValue), Is.True);
                    Assert.That(eventTypeValue.WrappedValue.TryGetValue(out NodeId eventType), Is.True);
                    Assert.That(eventType, Is.EqualTo(Opc.Ua.Types.ObjectTypeIds.BaseEventType));

                    Assert.That(notification.EventFields.TryGetValue("SourceNode", out DataValue sourceNodeValue), Is.True);
                    Assert.That(sourceNodeValue.WrappedValue.TryGetValue(out NodeId sourceNode), Is.True);
                    Assert.That(sourceNode, Is.EqualTo(triggerNodeId),
                        "SourceNode must be the trigger variable that raised the event.");

                    Assert.That(notification.EventFields.TryGetValue("Severity", out DataValue severityValue), Is.True);
                    Assert.That(severityValue.WrappedValue.TryGetValue(out ushort severity), Is.True);
                    Assert.That(severity, Is.EqualTo((ushort)EventSeverity.Medium));

                    Assert.That(notification.EventFields.TryGetValue("Message", out DataValue messageValue), Is.True);
                    Assert.That(messageValue.WrappedValue.TryGetValue(out LocalizedText message), Is.True);
                    Assert.That(message.Text, Does.Contain("Trigger event"));

                    // The primary DataValue carries the same Message text with a
                    // Good status and the event's own Time / ReceiveTime.
                    Assert.That(StatusCode.IsGood(notification.Value.StatusCode), Is.True);
                    Assert.That(notification.Value.WrappedValue.TryGetValue(out LocalizedText primaryMessage), Is.True);
                    Assert.That(primaryMessage.Text, Does.Contain("Trigger event"));
                }
            }
        }

        private static string BuildThingDescription(string endpoint)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"@type\":\"uav:object\"," +
                "\"title\":\"t\",\"properties\":{" +
                "\"time\":{\"type\":\"string\",\"forms\":[{\"href\":\"" + endpoint + "\",\"uav:id\":\"" +
                CurrentTimeNodeId + "\"}]}," +
                "\"watch\":{\"type\":\"string\",\"observable\":true,\"forms\":[{\"href\":\"" + endpoint +
                "\",\"uav:id\":\"" + CurrentTimeNodeId + "\",\"op\":[\"observeproperty\"]}]}," +
                "\"state\":{\"type\":\"integer\",\"forms\":[{\"href\":\"" + endpoint + "\",\"uav:id\":\"" +
                StateNodeId + "\"}]}," +
                "\"counter\":{\"type\":\"integer\",\"forms\":[" +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + CounterNodeId + "\",\"op\":[\"writeproperty\"]}," +
                "{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + CounterNodeId + "\",\"op\":[\"readproperty\"]}" +
                "]}}," +
                "\"actions\":{\"add\":{\"forms\":[{\"href\":\"" + endpoint + "\",\"uav:id\":\"" + AddMethodNodeId +
                "\",\"uav:componentOf\":\"" + MethodsObjectNodeId + "\",\"op\":[\"invokeaction\"]}]}}," +
                "\"events\":{\"trigger\":{\"forms\":[{\"href\":\"" + endpoint + "\",\"uav:id\":\"" +
                ServerObjectNodeId + "\",\"op\":[\"subscribeevent\"]}]}}}";
        }

        /// <summary>
        /// Resolves a portable <c>nsu=</c> NodeId string directly against the
        /// connected session's namespace table (mirroring the fallback the
        /// executor itself uses), so the test can address the trigger
        /// variable without hard-coding a namespace index.
        /// </summary>
        private NodeId ResolvePortableNodeId(string value)
        {
            ExpandedNodeId expanded = ExpandedNodeId.Parse(value);
            return ExpandedNodeId.ToNodeId(expanded, m_session.NamespaceUris);
        }
    }
}
