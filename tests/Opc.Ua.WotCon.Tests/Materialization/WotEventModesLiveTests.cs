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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Export;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Server.Materialization;
using Quickstarts.ReferenceServer;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    [NonParallelizable]
    [Category("Integration")]
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission", false)]
        [TestCase("transparent-forwarding", false)]
        [TestCase("local-re-emission", true)]
        [TestCase("transparent-forwarding", true)]
        public async Task NativeEventModesPreserveOccurrenceFactsAsync(string mode, bool managed)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(managed, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            bool transparent = mode == "transparent-forwarding";
            ExpandedNodeId projectedType = new("EventType", transparent ? SourceNamespace : LocalNamespace);
            WotCompiledForm form = EventForm(source.EndpointUrl);
            WotProjectedAffordance declaration = EventDeclaration(mode, projectedType);
            var channels = new NativeChannels(source.Session);
            var factory = new WotProjectionBindingRuntimeFactory(channels);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle, factory);
            WotProjectionHandle handle = await host.AddAsync(
                Projection(projectedType, form, declaration), ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeEvents events = await NativeEvents.OpenAsync(destination.Session, ct)
                    .ConfigureAwait(false);
                ByteString originalId = ByteString.From(new byte[] { 0xD3, 0x10, 0x20, 0x30 });
                DateTimeUtc boundary = DateTimeUtc.Now;
                await source.ReportAsync(originalId, ct).ConfigureAwait(false);
                ArrayOf<Variant> fields = await events.ReadAsync(ct).ConfigureAwait(false);

                Assert.That(fields.Count, Is.EqualTo(8));
                Assert.That(fields[0].TryGetValue(out ByteString eventId), Is.True);
                Assert.That(eventId.IsEmpty, Is.False);
                Assert.That(eventId == originalId, Is.EqualTo(transparent));
                Assert.That(fields[1].TryGetValue(out NodeId eventType), Is.True);
                Assert.That(NodeId.ToExpandedNodeId(eventType, destination.Session.NamespaceUris),
                    Is.EqualTo(projectedType));
                Assert.That(fields[2].TryGetValue(out NodeId sourceNode), Is.True);
                Assert.That(NodeId.ToExpandedNodeId(sourceNode, destination.Session.NamespaceUris),
                    Is.EqualTo(transparent
                        ? new ExpandedNodeId("Pump", SourceNamespace)
                        : new ExpandedNodeId("Owner", LocalNamespace)));
                Assert.That(fields[4].TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.EqualTo(SourceTime));
                Assert.That(fields[5].TryGetValue(out DateTimeUtc received), Is.True);
                Assert.That(received, Is.GreaterThanOrEqualTo(boundary));
                Assert.That(received, Is.Not.EqualTo(SourceReceiveTime));
                Assert.That(fields[6].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo("Source occurrence"));
                Assert.That(fields[7].TryGetValue(out ushort severity), Is.True);
                Assert.That(severity, Is.EqualTo((ushort)612));
                Assert.That(source.Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(destination.Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                TestContext.Out.WriteLine(
                    $"D3 native mode={mode}; managed={managed}; runtime={Environment.Version}; event={eventId}");
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static WotCompiledForm EventForm(string endpoint)
        {
            return new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "10101", "urn:opcfoundation:wot:binding:opcua"),
                WotAffordanceKind.Event, "event", "/events/event/forms/0",
                WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                new WotEndpointDescriptor("opc.tcp", null, -1, endpoint),
                new WotAddressingDescriptor("i=2253"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                null, WotEventSelection.Default, null);
        }

        private static WotProjectedAffordance EventDeclaration(string mode, ExpandedNodeId type)
        {
            using JsonDocument declaration = JsonDocument.Parse(
                $$"""{"uav:eventIdentityMode":"{{mode}}"}""");
            return WotProjectedAffordance.FromConverted(new WotConvertedAffordance(
                Wot.WotAffordanceKind.Event, "event", "/events/event", type,
                new ExpandedNodeId("Owner", LocalNamespace), declaration.RootElement));
        }

        private static WotProjectionDocument Projection(
            ExpandedNodeId eventType, WotCompiledForm form, WotProjectedAffordance declaration)
        {
            if (declaration.IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding)
            {
                return TransparentProjection(form, declaration);
            }
            var nodes = new UANodeSet
            {
                NamespaceUris = [LocalNamespace, SourceNamespace],
                Models = [new ModelTableEntry { ModelUri = LocalNamespace, Version = "1.0.0" }],
                Items =
                [
                    Object("ns=1;s=Owner", "1:Owner"),
                    EventType(eventType.NamespaceUri == SourceNamespace ? 2 : 1)
                ]
            };
            WotBindingPlan plan = new WotBindingPlan("urn:wot:d3:document", [], [form], [], [])
                .WithProjectedAffordances([declaration]);
            return new WotProjectionDocument("d3-events",
                [new WotProjectionSource("d3-events", [LocalNamespace], Serialize(nodes))], [plan]);
        }

        private static UANodeSet SourceNodes()
        {
            return new UANodeSet
            {
                NamespaceUris = [SourceNamespace],
                Models = [new ModelTableEntry { ModelUri = SourceNamespace, Version = "1.0.0" }],
                Items = [Object("ns=1;s=Pump", "1:Pump"), EventType(1)]
            };
        }

        private static UAObject Object(string id, string name)
        {
            return new UAObject
            {
                NodeId = id,
                BrowseName = name,
                References =
                [
                    new Reference { ReferenceType = "i=35", IsForward = false, Value = "i=85" },
                    new Reference { ReferenceType = "i=40", Value = "i=58" }
                ]
            };
        }

        private static UAObjectType EventType(int index)
        {
            return new UAObjectType
            {
                NodeId = $"ns={index};s=EventType",
                BrowseName = $"{index}:EventType",
                References = [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=2041" }]
            };
        }

        private static byte[] Serialize(UANodeSet nodes)
        {
            using var output = new MemoryStream();
            nodes.Write(output);
            return output.ToArray();
        }

        private sealed class NativeChannels(ISession session) : IWotBindingChannelFactory
        {
            public ValueTask<IWotBindingChannel> OpenChannelAsync(
                WotCompiledForm form, CancellationToken cancellationToken = default)
            {
                var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (_, _) => new ValueTask<ISession>(session),
                    DisposeSession = false,
                    ObserveInterval = TimeSpan.FromMilliseconds(20)
                });
                return executor.ActivateAsync(form,
                    new WotExecutorContext(telemetry: NUnitTelemetryContext.Create()), cancellationToken);
            }
        }

        private sealed class NativeEndpoint : IAsyncDisposable
        {
            public ReferenceServer Server { get; private set; } = null!;

            public ISession Session { get; private set; } = null!;

            public string EndpointUrl => $"opc.tcp://localhost:{m_fixture.Port}";

            public async Task StartAsync(bool managed, CancellationToken ct)
            {
                Server = await m_fixture.StartAsync(m_root).ConfigureAwait(false);
                await m_client.LoadClientConfigurationAsync(m_root, "EventModesClient").ConfigureAwait(false);
                m_session = await m_client.ConnectAsync(new Uri(EndpointUrl), SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                Session = m_session;
                if (managed)
                {
                    m_managed = await ManagedSession.CreateAsync(
                        m_client.Config, m_client.Endpoint, m_client.SessionFactory,
                        telemetry: NUnitTelemetryContext.Create(), ct: ct).ConfigureAwait(false);
                    Session = m_managed;
                }
                Session.KeepAliveInterval = 60000;
            }

            public async Task InstallAsync(UANodeSet nodes, CancellationToken ct)
            {
                byte[] bytes = Serialize(nodes);
                _ = await Server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(new RuntimeNodeSetOptions
                {
                    Sources =
                    [
                        RuntimeNodeSetSource.FromStream("source",
                            _ => new ValueTask<Stream>(new MemoryStream(bytes, writable: false)),
                            nodes.NamespaceUris!.ToArrayOf())
                    ]
                }, null, ct).ConfigureAwait(false);
            }

            public async Task RefreshNamespaceTableAsync(CancellationToken ct)
            {
                await Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
            }

            public async Task ReportAsync(ByteString eventId, CancellationToken ct)
            {
                var context = Server.CurrentInstance.DefaultSystemContext;
                var state = new BaseEventState(null)
                {
                    TypeDefinitionId = ExpandedNodeId.Parse(
                        "nsu=" + SourceNamespace + ";s=EventType", context.NamespaceUris)
                };
                state.EventId = PropertyState<ByteString>.With<VariantBuilder>(state, eventId);
                state.EventType = PropertyState<NodeId>.With<VariantBuilder>(state, state.TypeDefinitionId);
                state.SourceNode = PropertyState<NodeId>.With<VariantBuilder>(state,
                    ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=Pump", context.NamespaceUris));
                state.SourceName = PropertyState<string>.With<VariantBuilder>(state, "Pump");
                state.Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, SourceTime);
                state.ReceiveTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, SourceReceiveTime);
                state.Message = PropertyState<LocalizedText>.With<VariantBuilder>(
                    state, new LocalizedText("Source occurrence"));
                state.Severity = PropertyState<ushort>.With<VariantBuilder>(state, 612);
                await Server.CurrentInstance.ReportEventAsync(context, state, ct).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (m_managed is not null)
                {
                    await m_managed.DisposeAsync().ConfigureAwait(false);
                }
                if (m_session is not null)
                {
                    await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    m_session.Dispose();
                }
                await m_client.DisposeAsync().ConfigureAwait(false);
                await m_fixture.StopAsync().ConfigureAwait(false);
                Server?.Dispose();
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }

            private readonly ServerFixture<ReferenceServer> m_fixture = new(t => new ReferenceServer(t))
            {
                AutoAccept = true
            };
            private readonly ClientFixture m_client = new(NUnitTelemetryContext.Create());
            private readonly string m_root = Path.Combine(
                Environment.GetEnvironmentVariable("WOT_EVENT_TEST_ROOT") ?? Path.GetTempPath(),
                "wem-" + Guid.NewGuid().ToString("N")[..8]);
            private ISession? m_session;
            private ManagedSession? m_managed;
        }

        private sealed class NativeEvents : IAsyncDisposable
        {
            private NativeEvents(ISession session, Subscription subscription)
            {
                m_session = session;
                m_subscription = subscription;
            }

            public static async Task<NativeEvents> OpenAsync(ISession session, CancellationToken ct)
            {
                var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = 20,
                    PublishingEnabled = true
                };
                var result = new NativeEvents(session, subscription);
                session.AddSubscription(subscription);
                try
                {
                    await subscription.CreateAsync(ct).ConfigureAwait(false);
                    var filter = new EventFilter
                    {
                        SelectClauses = WotEventSelection.Default.Clauses.ConvertAll(clause =>
                            new SimpleAttributeOperand
                            {
                                TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                                AttributeId = Attributes.Value,
                                BrowsePath = [QualifiedName.From(clause.BrowsePath)]
                            })
                    };
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = Ua.ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier,
                        Filter = filter,
                        QueueSize = 64,
                        DiscardOldest = false
                    };
                    item.Notification += (_, notification) =>
                    {
                        if (notification.NotificationValue is EventFieldList fields &&
                            fields.EventFields.Count == 8 &&
                            fields.EventFields[6].TryGetValue(out LocalizedText message) &&
                            message.Text == "Source occurrence" &&
                            !result.m_events.Writer.TryWrite(fields.EventFields))
                        {
                            result.m_events.Writer.TryComplete(
                                new InvalidOperationException("The native test event queue is full."));
                        }
                    };
                    subscription.AddItem(item);
                    await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
                    Assert.That(item.Created, Is.True, item.Status.Error?.ToString());
                    return result;
                }
                catch
                {
                    await result.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public ValueTask<ArrayOf<Variant>> ReadAsync(CancellationToken ct)
            {
                return m_events.Reader.ReadAsync(ct);
            }

            public async ValueTask DisposeAsync()
            {
                m_events.Writer.TryComplete();
                await m_session.RemoveSubscriptionAsync(m_subscription, CancellationToken.None).ConfigureAwait(false);
                m_subscription.Dispose();
            }

            private readonly ISession m_session;
            private readonly Subscription m_subscription;
            private readonly Channel<ArrayOf<Variant>> m_events = Channel.CreateBounded<ArrayOf<Variant>>(64);
        }

        private const string SourceNamespace = "urn:wot:d3:source";
        private const string LocalNamespace = "urn:wot:d3:local";
        private static readonly DateTimeUtc SourceTime = new(2026, 8, 1, 1, 2, 3);
        private static readonly DateTimeUtc SourceReceiveTime = new(2026, 8, 1, 1, 2, 4);
    }
}
#endif
