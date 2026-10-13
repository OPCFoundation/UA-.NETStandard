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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Contains tests for WoT small generation.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WotSmallGenerationTests
    {
        /// <summary>
        /// Verifies that native transport reconnect retains namespace mapping holder.
        /// </summary>
        [Test]
        public async Task NativeTransportReconnectRetainsNamespaceMappingHolderAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            CancellationToken ct = timeout.Token;
            await using WotSampleEnvironment environment = await WotSampleEnvironment.StartAsync(ct, forceLeasedContent: true)
                .ConfigureAwait(false);
            IManagedSessionPool pool = environment.AggregationHost.Services.GetRequiredService<IManagedSessionPool>();
            ManagedSession upstream = await pool.GetOrConnectAsync(environment.ClientOptions.SourceAEndpoint,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    EndpointUrl = environment.ClientOptions.SourceAEndpoint,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }, null) { UpdateBeforeConnect = true }, ct).ConfigureAwait(false);
            Assert.That(upstream.TransportChannel.MessageContext.NamespaceUris, Is.SameAs(upstream.NamespaceUris));
            using ISessionClient before = await ((ISessionBindingProvider)upstream).CreateBindingAsync(ct)
                .ConfigureAwait(false);
            Assert.That(((ISessionBinding)before).IsCurrent, Is.True);

            await upstream.TransportChannel.ReconnectAsync(null, ct).ConfigureAwait(false);

            Assert.That(upstream.TransportChannel.MessageContext.NamespaceUris, Is.SameAs(upstream.NamespaceUris),
                "A new channel context must remain paired with the owning session's namespace holder.");
            using ISessionClient after = await ((ISessionBindingProvider)upstream).CreateBindingAsync(ct)
                .ConfigureAwait(false);
            Assert.That(((ISessionBinding)after).IsCurrent, Is.True);
        }

        /// <summary>
        /// Verifies that small native condition composition can refresh two generations.
        /// </summary>
        /// <param name="change">The number of changes applied in each generation.</param>
        /// <param name="transparent">The transparent flag for this test case.</param>
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public async Task SmallNativeConditionCompositionCanRefreshTwoGenerationsAsync(int change, bool transparent)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            await using WotSampleEnvironment environment = await WotSampleEnvironment.StartAsync(ct, forceLeasedContent: true)
                .ConfigureAwait(false);
            await using WotClientConnection connection = await environment.ConnectAsync(ct).ConfigureAwait(false);
            var nodes = new UANodeSet
            {
                NamespaceUris = ["urn:test:small-generation"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:small-generation", Version = "1.0.0" }],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;s=Owner",
                        BrowseName = "1:Owner",
                        EventNotifier = 1,
                        References =
                        [
                            new Reference { ReferenceType = "i=35", IsForward = false, Value = "i=85" },
                            new Reference { ReferenceType = "i=40", Value = "i=58" },
                            new Reference { ReferenceType = "i=41", Value = "ns=1;s=AlarmType" }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;s=AlarmType",
                        BrowseName = "1:AlarmType",
                        References =
                        [
                            new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=2915" }
                        ]
                    }
                ]
            };
            using WotDocument generated = WotNodeSetConverter.FromNodeSet(nodes);
            using var proof = new WotDocumentSet("owner",
                new WotDocumentSetEntry[]
                {
                    new("owner", WotDocument.Parse(generated.Utf8Json))
                }.ToArrayOf());
            WotConversionResult<UANodeSet> preserved = await WotNodeSetConverter.ToNodeSetAsync(
                proof, cancellationToken: ct).ConfigureAwait(false);
            Assert.That(preserved.Success, Is.True, string.Join("; ", preserved.Diagnostics));
            Assert.That(preserved.Value!.Items!.Select(node => node.NodeId),
                Is.EquivalentTo(s_ownedNodes));
            JsonNode owner = JsonNode.Parse(generated.Utf8Json.Span)!;
            owner["id"] = "urn:test:small-generation";
            JsonObject events = owner["events"]!.AsObject();
            Assert.That(events, Has.Count.EqualTo(1));
            JsonNode alarm = events.Single().Value!;
            if (!transparent)
            {
                alarm["uav:eventIdentityMode"] = "local-re-emission";
            }
            alarm["uav:conditionType"] = "ua:AlarmConditionType";
            alarm["uav:conditionTypeId"] = "i=2915";
            alarm["forms"] = new JsonArray(new JsonObject
            {
                ["href"] = environment.ClientOptions.SourceAEndpoint,
                ["op"] = new JsonArray("subscribeevent", "unsubscribeevent"),
                ["uav:id"] = "nsu=urn:opcfoundation.org:UA:WotAggregation:SourceA;s=Pump1"
            });
            ByteString content = ByteString.From(Encoding.UTF8.GetBytes(owner.ToJsonString()));
            (WotRegistryGroupClient group, _) = await connection.Registry.GetOrCreateDocumentGroupAsync(
                WoTDocumentKindEnum.ThingDescription, "urn:test:small-generation:group", ct).ConfigureAwait(false);
            (WotRegistryResourceAllocation allocation, _, _) = await group.GetOrCreateDocumentResourceAsync(
                "urn:test:small-generation", ct: ct).ConfigureAwait(false);
            var document = new WotRegistryDocument(WoTDocumentKindEnum.ThingDescription, group.GroupId,
                allocation.LogicalResource.ResourceId, content);
            WotRegistryBulkLoadResult loaded = await connection.Registry.LoadDocumentsAsync(
                new WotRegistryDocument[] { document }.ToArrayOf(), refresh: true,
                refreshOptions: new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry },
                requestId: "small-first-generation", ct: ct).ConfigureAwait(false);
            WotRegistryRefreshResult first = loaded.Refresh!;
            TestContext.Out.WriteLine("First: {0}", Describe(first));
            Assert.That(first.HasFailures, Is.False, Describe(first));
            await connection.Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
            Assert.That(connection.Session.NamespaceUris.GetIndex("urn:test:small-generation"), Is.GreaterThan(0));
            using var subscription = new Subscription(connection.Session.DefaultSubscription)
            {
                PublishingInterval = 50
            };
            connection.Session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            var received = Channel.CreateBounded<ArrayOf<Variant>>(32);
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = Ua.ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                Filter = new EventFilter
                {
                    SelectClauses =
                    [
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                            AttributeId = Attributes.Value,
                            BrowsePath = [QualifiedName.From(Ua.BrowseNames.EventId)]
                        },
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                            AttributeId = Attributes.Value,
                            BrowsePath = [QualifiedName.From(Ua.BrowseNames.EventType)]
                        },
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType,
                            AttributeId = Attributes.Value,
                            BrowsePath = [QualifiedName.From(Ua.BrowseNames.Time)]
                        }
                    ]
                },
                QueueSize = 32
            };
            item.Notification += (_, notification) =>
            {
                if (notification.NotificationValue is EventFieldList fields)
                {
                    received.Writer.TryWrite(fields.EventFields);
                }
            };
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            Assert.That(item.Created, Is.True, item.Status.Error?.ToString());
            IManagedSessionPool pool = environment.AggregationHost.Services.GetRequiredService<IManagedSessionPool>();
            ManagedSession upstream = await pool.GetOrConnectAsync(environment.ClientOptions.SourceAEndpoint,
                new ConfiguredEndpoint(null, new EndpointDescription
                {
                    EndpointUrl = environment.ClientOptions.SourceAEndpoint,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }, null), ct).ConfigureAwait(false);
            Assert.That(upstream.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            DateTimeUtc firstBoundary = DateTimeUtc.Now;
            await TripAsync(upstream, ct).ConfigureAwait(false);
            ArrayOf<Variant> firstEvent = await ReadOccurrenceAsync(received.Reader, firstBoundary, ct)
                .ConfigureAwait(false);
            Assert.That(firstEvent[0].TryGetValue(out ByteString firstId) && !firstId.IsEmpty, Is.True);
            Assert.That(firstEvent[1].TryGetValue(out NodeId firstType), Is.True);
            var expectedType = new ExpandedNodeId("AlarmType", "urn:test:small-generation");
            Assert.That(NodeId.ToExpandedNodeId(firstType, connection.Session.NamespaceUris),
                Is.EqualTo(expectedType));
            using ISessionClient pinned = await ((ISessionBindingProvider)upstream).CreateBindingAsync(ct)
                .ConfigureAwait(false);
            Assert.That(((ISessionBinding)pinned).IsCurrent, Is.True);
            TestContext.Out.WriteLine("Source mapping versions before: {0}/{1}",
                upstream.NamespaceUris.Version, upstream.ServerUris.Version);
            if (change == 1)
            {
                await upstream.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
            }
            else if (change == 2)
            {
                TestContext.Out.WriteLine("Reconnecting the authenticated upstream between generations.");
                await upstream.ReconnectAsync(null, null, ct).ConfigureAwait(false);
            }
            TestContext.Out.WriteLine("Source mapping versions after: {0}/{1}; previous binding current={2}",
                upstream.NamespaceUris.Version, upstream.ServerUris.Version, ((ISessionBinding)pinned).IsCurrent);
            if (change != 0)
            {
                Assert.That(((ISessionBinding)pinned).IsCurrent, Is.False);
                ServiceResultException? rejected = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await pinned.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        new ReadValueId[]
                        {
                            new()
                            {
                                NodeId = Ua.VariableIds.Server_ServerStatus_CurrentTime,
                                AttributeId = Attributes.Value
                            }
                        }.ToArrayOf(), ct).ConfigureAwait(false));
                Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            }
            WotRegistryResourceClient resource = await group.OpenResourceAsync(document.ResourceId, ct)
                .ConfigureAwait(false);
            owner["title"] = "Small replacement owner";
            content = ByteString.From(Encoding.UTF8.GetBytes(owner.ToJsonString()));
            WotRegistryUploadResult uploaded = await resource.UploadNewVersionAndGetResultAsync(content, ct: ct)
                .ConfigureAwait(false);
            await resource.SetDefaultVersionAsync(uploaded.VersionId, expectedEpoch: 0, ct).ConfigureAwait(false);
            WotRegistryRefreshResult second = await connection.Registry.RefreshAllAsync(
                new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry },
                requestId: "small-second-generation", ct: ct).ConfigureAwait(false);
            TestContext.Out.WriteLine("Second: {0}", Describe(second));
            Assert.That(second.HasFailures, Is.False, Describe(second));
            Assert.That(second.NewGeneration, Is.GreaterThan(first.NewGeneration));
            WotRegistryUploadResult thirdVersion = await resource.UploadNewVersionAndGetResultAsync(content, ct: ct)
                .ConfigureAwait(false);
            Assert.That(thirdVersion.VersionId, Is.Not.EqualTo(uploaded.VersionId));
            DateTimeUtc secondBoundary = DateTimeUtc.Now;
            await TripAsync(upstream, ct).ConfigureAwait(false);
            ArrayOf<Variant> secondEvent = await ReadOccurrenceAsync(received.Reader, secondBoundary, ct)
                .ConfigureAwait(false);
            Assert.That(secondEvent[0].TryGetValue(out ByteString secondId) && !secondId.IsEmpty, Is.True);
            Assert.That(secondId, Is.Not.EqualTo(firstId));
            Assert.That(secondEvent[1].TryGetValue(out NodeId secondType), Is.True);
            Assert.That(NodeId.ToExpandedNodeId(secondType, connection.Session.NamespaceUris),
                Is.EqualTo(expectedType));
            await connection.Session.RemoveSubscriptionAsync(subscription, ct).ConfigureAwait(false);
        }

        private static async Task<ArrayOf<Variant>> ReadOccurrenceAsync(
            ChannelReader<ArrayOf<Variant>> reader,
            DateTimeUtc notBefore,
            CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                ArrayOf<Variant> fields = await reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                if (fields.Count == 3 && fields[2].TryGetValue(out DateTimeUtc time) && time >= notBefore)
                {
                    return fields;
                }
            }
        }

        private static async Task TripAsync(ManagedSession session, CancellationToken ct)
        {
            ushort ns = checked((ushort)session.NamespaceUris.GetIndex(
                "urn:opcfoundation.org:UA:WotAggregation:SourceA"));
            for (int index = 0; index < 2; index++)
            {
                WriteResponse response = await session.WriteAsync(null,
                    new WriteValue[]
                    {
                        new()
                        {
                            NodeId = new NodeId("Pump1.Events.SupervisionProcessFluid.Cavitation", ns),
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(index == 1))
                        }
                    }.ToArrayOf(), ct).ConfigureAwait(false);
                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
            }
        }

        private static string Describe(WotRegistryRefreshResult result)
        {
            return $"Generation={result.NewGeneration}; " + string.Join("; ", result.Results.ToList().Select(load =>
                $"{load.ResourceId}: {load.Phase}/{load.Outcome}: {load.Message}"));
        }

        private static readonly string[] s_ownedNodes = ["ns=1;s=Owner", "ns=1;s=AlarmType"];
    }
}
