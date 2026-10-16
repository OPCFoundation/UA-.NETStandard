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
using System.IO;
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Exercises classic persistence through the supported session entry points.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    public sealed class ClassicSnapshotPersistenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SavedSubscriptionsRetainOriginalTargetsAndRemapNamespaces(bool forwardOnly)
        {
            using Session source = CreateSession();
            source.NamespaceUris.Append("urn:classic:root");
            source.NamespaceUris.Append("urn:classic:target");
            using var first = new Subscription(
                NUnitTelemetryContext.Create(), new SubscriptionOptions { TransferId = 101 });
            first.Restore(new SubscriptionState
            {
                TransferId = 101,
                MonitoredItems =
                [
                    new MonitoredItemState
                    {
                        StartNodeId = new NodeId(10, 1),
                        RelativePath = "/Target",
                        ServerId = 201,
                        ClientId = 301,
                        Filter = new DataChangeFilter { DeadbandValue = 1.5 },
                        ResolvedNodeId = new NodeId(20, 2)
                    },
                    new MonitoredItemState
                    {
                        StartNodeId = new NodeId(11, 1),
                        RelativePath = "/Other",
                        ServerId = 202,
                        ClientId = 302,
                        ResolvedNodeId = new NodeId(21, 2)
                    }
                ]
            });
            using var second = new Subscription(
                NUnitTelemetryContext.Create(), new SubscriptionOptions { TransferId = 102 });
            second.Restore(new SubscriptionState { TransferId = 102 });
            using var output = new MemoryStream();
            source.Save(output, [first, second]);
            Assert.That(output.CanWrite, Is.True);
            using Session target = CreateSession();
            target.NamespaceUris.Append("urn:classic:target");
            target.NamespaceUris.Append("urn:classic:root");
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);

            Subscription[] loaded = target.Load(input, transferSubscriptions: true).ToArray();

            Assert.That(loaded, Has.Length.EqualTo(2));
            Assert.That(loaded[0].TransferId, Is.EqualTo(101u));
            Assert.That(loaded[1].TransferId, Is.EqualTo(102u));
            MonitoredItem[] items = loaded[0].MonitoredItems.ToArray();
            Assert.That(items, Has.Length.EqualTo(2));
            Assert.That(items[0].StartNodeId, Is.EqualTo(new NodeId(10, 2)));
            Assert.That(items[0].ResolvedNodeId, Is.EqualTo(new NodeId(20, 1)));
            Assert.That(items[0].Status.NodeId, Is.EqualTo(new NodeId(20, 1)));
            Assert.That(items[0].ServerId, Is.EqualTo(201u));
            Assert.That(items[0].ClientHandle, Is.EqualTo(301u));
            Assert.That(items[0].Filter, Is.InstanceOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)items[0].Filter!).DeadbandValue, Is.EqualTo(1.5));
            Assert.That(items[1].ResolvedNodeId, Is.EqualTo(new NodeId(21, 1)));
            Assert.That(input.CanRead, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacySubscriptionsRetainBoundariesWithoutInventingTargets(bool forwardOnly)
        {
            using Session target = CreateSession();
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, target.MessageContext, true))
            {
                WriteTables(encoder);
                encoder.WriteInt32(null, 2);
                WriteLegacySubscription(encoder, 101);
                WriteLegacySubscription(encoder, 102);
            }
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);

            Subscription[] loaded = target.Load(input, transferSubscriptions: true).ToArray();

            Assert.That(loaded.Select(s => s.TransferId), Is.EqualTo(new uint[] { 101, 102 }));
            foreach (Subscription subscription in loaded)
            {
                MonitoredItem[] items = subscription.MonitoredItems.ToArray();
                Assert.That(items, Has.Length.EqualTo(2));
                Assert.That(items[0].ServerId, Is.EqualTo(201u));
                Assert.That(items[0].ClientHandle, Is.EqualTo(301u));
                Assert.That(items[0].Filter, Is.InstanceOf<DataChangeFilter>());
                Assert.That(((DataChangeFilter)items[0].Filter!).DeadbandValue, Is.EqualTo(1.5));
                Assert.That(items[0].ResolvedNodeId.IsNull, Is.True);
                Assert.That(items[0].Status.NodeId.IsNull, Is.True);
                Assert.That(items[1].ServerId, Is.EqualTo(202u));
                Assert.That(items[1].StartNodeId, Is.EqualTo(new NodeId(11)));
            }
            Assert.That(input.CanRead, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SessionConfigurationSaveAndCreateLeaveStreamsOpen(bool forwardOnly)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            SessionConfiguration expected = session.SaveSessionConfiguration(output);
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);

            SessionConfiguration? restored = SessionConfiguration.Create(input, NUnitTelemetryContext.Create());

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.SessionName, Is.EqualTo(expected.SessionName));
            Assert.That(restored.ConfiguredEndpoint!.Description.EndpointUrl,
                Is.EqualTo(expected.ConfiguredEndpoint!.Description.EndpointUrl));
            Assert.That(restored.ConfiguredEndpoint.Description.SecurityPolicyUri,
                Is.EqualTo(expected.ConfiguredEndpoint.Description.SecurityPolicyUri));
            Assert.That(restored.ConfiguredEndpoint.Configuration, Is.Not.Null);
            Assert.That(restored.ConfiguredEndpoint.Configuration!.OperationTimeout,
                Is.EqualTo(expected.ConfiguredEndpoint.Configuration!.OperationTimeout));
            Assert.That(input.CanRead, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacySessionConfigurationReadsNestedSubscriptions(bool forwardOnly)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, session.MessageContext, true))
            {
                WriteTables(encoder);
                encoder.WriteString(null, "legacy");
                encoder.WriteEncodeableAsExtensionObject(null, new AnonymousIdentityToken { PolicyId = "anonymous" });
                encoder.WriteEncodeable(null, new ConfiguredEndpoint());
                encoder.WriteBoolean(null, true);
                encoder.WriteDateTime(null, new DateTimeUtc(2025, 1, 1));
                encoder.WriteNodeId(null, new NodeId(51));
                encoder.WriteNodeId(null, new NodeId(52));
                encoder.WriteByteString(null, default(ByteString));
                encoder.WriteByteString(null, default(ByteString));
                encoder.WriteString(null, "anonymous");
                encoder.WriteByteString(null, default(ByteString));
                encoder.WriteInt32(null, 2);
                WriteLegacySubscription(encoder, 101);
                WriteLegacySubscription(encoder, 102);
            }
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);

            SessionConfiguration? restored = SessionConfiguration.Create(input, NUnitTelemetryContext.Create());

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.SessionId, Is.EqualTo(new NodeId(51)));
            Assert.That(restored.AuthenticationToken, Is.EqualTo(new NodeId(52)));
            Assert.That(restored.Subscriptions.Count, Is.EqualTo(2));
            Assert.That(restored.Subscriptions[1].TransferId, Is.EqualTo(102u));
            Assert.That(restored.Subscriptions[1].MonitoredItems[1].ServerId, Is.EqualTo(202u));
            Assert.That(restored.Subscriptions[0].MonitoredItems[0].ResolvedNodeId.IsNull, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptySubscriptionCollectionIsValid(bool forwardOnly)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            session.Save(output, []);
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);
            Assert.That(session.Load(input), Is.Empty);
            Assert.That(input.CanRead, Is.True);
        }

        [TestCase(-1)]
        [TestCase(-2)]
        [TestCase(int.MaxValue)]
        public void InvalidLegacyCountFailsWithoutAttachingSubscriptions(int count)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, session.MessageContext, true))
            {
                WriteTables(encoder);
                encoder.WriteInt32(null, count);
            }
            output.Position = 0;
            Assert.Throws<ServiceResultException>(() => session.Load(output).ToArray());
            Assert.That(session.Subscriptions, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TruncatedLaterSubscriptionDoesNotAttachEarlierSubscription(bool forwardOnly)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, session.MessageContext, true))
            {
                WriteTables(encoder);
                encoder.WriteInt32(null, 2);
                WriteLegacySubscription(encoder, 101);
                encoder.WriteInt32(null, 42);
            }
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);
            Assert.Throws<ServiceResultException>(() => session.Load(input).ToArray());
            Assert.That(session.Subscriptions, Is.Empty);
        }

        [TestCase(99, 1)]
        [TestCase(1, 2)]
        [TestCase(1, 99)]
        public void UnknownVersionOrWrongPayloadKindFails(int version, int kind)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, session.MessageContext, true))
            {
                encoder.WriteInt32(null, -2);
                encoder.WriteInt32(null, version);
                encoder.WriteInt32(null, kind);
            }
            output.Position = 0;
            Assert.Throws<ServiceResultException>(() => session.Load(output).ToArray());
            Assert.That(session.Subscriptions, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VersionedConfigurationPreservesNestedTargets(bool forwardOnly)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            var configuration = new SessionConfiguration
            {
                SessionName = "versioned",
                Subscriptions =
                [
                    new SubscriptionState
                    {
                        TransferId = 101,
                        MonitoredItems =
                        [
                            new MonitoredItemState
                            {
                                StartNodeId = new NodeId(10),
                                RelativePath = "/Target",
                                ServerId = 201,
                                ClientId = 301,
                                ResolvedNodeId = new NodeId(20)
                            },
                            new MonitoredItemState { ServerId = 202, ResolvedNodeId = new NodeId(21) }
                        ]
                    },
                    new SubscriptionState { TransferId = 102 }
                ]
            };
            using (var encoder = new BinaryEncoder(output, session.MessageContext, true))
            {
                encoder.WriteInt32(null, -2);
                encoder.WriteInt32(null, 1);
                encoder.WriteInt32(null, 2);
                WriteTables(encoder);
                configuration.Encode(encoder);
            }
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);

            SessionConfiguration? restored = SessionConfiguration.Create(input, NUnitTelemetryContext.Create());

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Subscriptions.Count, Is.EqualTo(2));
            Assert.That(restored.Subscriptions[1].TransferId, Is.EqualTo(102u));
            Assert.That(restored.Subscriptions[0].MonitoredItems[0].ResolvedNodeId, Is.EqualTo(new NodeId(20)));
            Assert.That(restored.Subscriptions[0].MonitoredItems[1].ResolvedNodeId, Is.EqualTo(new NodeId(21)));
        }

        [Test]
        public void LoadingForRecreationClearsSavedTarget()
        {
            using Session session = CreateSession();
            using var original = new Subscription(NUnitTelemetryContext.Create());
            original.Restore(new SubscriptionState
            {
                TransferId = 101,
                MonitoredItems =
                [
                    new MonitoredItemState
                    {
                        StartNodeId = new NodeId(10),
                        RelativePath = "/Target",
                        ServerId = 201,
                        ClientId = 301,
                        ResolvedNodeId = new NodeId(20)
                    }
                ]
            });
            using var output = new MemoryStream();
            session.Save(output, [original]);
            output.Position = 0;

            Subscription restored = session.Load(output, transferSubscriptions: false).Single();

            MonitoredItem item = restored.MonitoredItems.Single();
            Assert.That(item.ServerId, Is.Zero);
            Assert.That(item.ResolvedNodeId.IsNull, Is.True);
            Assert.That(item.Status.NodeId.IsNull, Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void TruncatedHeaderFails(int length)
        {
            using Session session = CreateSession();
            using var input = new MemoryStream(new byte[length]);
            Assert.Throws<ServiceResultException>(() => session.Load(input).ToArray());
            Assert.That(session.Subscriptions, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OversizedNamespaceArrayFailsBeforeDecodingPayload(bool forwardOnly)
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, session.MessageContext, true))
            {
                encoder.WriteInt32(null, int.MaxValue);
            }
            output.Position = 0;
            using var input = new InputStream(output, forwardOnly);
            Assert.Throws<ServiceResultException>(() => session.Load(input).ToArray());
            Assert.That(session.Subscriptions, Is.Empty);
        }

        [Test]
        public void InvalidTargetNamespaceFailsWithoutAttachingSubscriptions()
        {
            using Session session = CreateSession();
            using var output = new MemoryStream();
            using (var encoder = new BinaryEncoder(output, session.MessageContext, true))
            {
                encoder.WriteInt32(null, -2);
                encoder.WriteInt32(null, 1);
                encoder.WriteInt32(null, 1);
                WriteTables(encoder);
                encoder.WriteInt32(null, 2);
                new SubscriptionState { TransferId = 101 }.Encode(encoder);
                new SubscriptionState
                {
                    TransferId = 102,
                    MonitoredItems =
                    [
                        new MonitoredItemState { ResolvedNodeId = new NodeId(20, ushort.MaxValue) }
                    ]
                }.Encode(encoder);
            }
            output.Position = 0;
            Assert.Throws<ServiceResultException>(() => session.Load(output).ToArray());
            Assert.That(session.Subscriptions, Is.Empty);
        }

        private static Session CreateSession()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "ClassicSnapshotPersistenceTests",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:classic:snapshot:test",
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration(),
                TransportQuotas = new TransportQuotas()
            };
            ServiceMessageContext context = configuration.CreateMessageContext();
            context.Factory.Builder.AddOpcUaClientDataTypes();
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(c => c.MessageContext).Returns(context);
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            }, EndpointConfiguration.Create());
            return new Session(channel.Object, configuration, endpoint);
        }

        private static void WriteTables(BinaryEncoder encoder)
        {
            encoder.WriteStringArray(null, [Namespaces.OpcUa]);
            encoder.WriteStringArray(null, ArrayOf<string>.Empty);
        }

        private static void WriteLegacySubscription(BinaryEncoder encoder, uint transferId)
        {
            // Frozen pre-change binary contract: options precede derived state,
            // even though the MonitoredItems field's order is numerically lower.
            encoder.WriteString(null, "LegacySubscription");
            encoder.WriteInt32(null, 1000);
            encoder.WriteUInt32(null, 10);
            encoder.WriteUInt32(null, 100);
            encoder.WriteUInt32(null, 0);
            encoder.WriteBoolean(null, true);
            encoder.WriteByte(null, 1);
            encoder.WriteInt32(null, (int)TimestampsToReturn.Both);
            encoder.WriteInt32(null, 10);
            encoder.WriteUInt32(null, 1000);
            encoder.WriteBoolean(null, false);
            encoder.WriteBoolean(null, true);
            encoder.WriteBoolean(null, true);
            encoder.WriteUInt32(null, transferId);
            encoder.WriteInt32(null, 2);
            WriteLegacyItem(encoder, 10, 201, 301);
            WriteLegacyItem(encoder, 11, 202, 302);
            encoder.WriteDouble(null, 1000);
            encoder.WriteUInt32(null, 10);
            encoder.WriteUInt32(null, 100);
            encoder.WriteDateTime(null, new DateTimeUtc(2025, 1, 1));
        }

        private static void WriteLegacyItem(BinaryEncoder encoder, uint nodeId, uint serverId, uint clientId)
        {
            encoder.WriteString(null, "LegacyItem");
            encoder.WriteNodeId(null, new NodeId(nodeId));
            encoder.WriteString(null, "/Target");
            encoder.WriteInt32(null, (int)NodeClass.Variable);
            encoder.WriteUInt32(null, Attributes.Value);
            encoder.WriteString(null, null);
            encoder.WriteQualifiedName(null, QualifiedName.Null);
            encoder.WriteInt32(null, (int)MonitoringMode.Reporting);
            encoder.WriteInt32(null, 100);
            encoder.WriteEncodeableAsExtensionObject(null, new DataChangeFilter { DeadbandValue = 1.5 });
            encoder.WriteUInt32(null, 2);
            encoder.WriteBoolean(null, true);
            encoder.WriteUInt32(null, serverId);
            encoder.WriteUInt32(null, clientId);
            encoder.WriteDateTime(null, new DateTimeUtc(2025, 1, 1));
            encoder.WriteUInt32(null, 0);
            encoder.WriteUInt32Array(null, ArrayOf<uint>.Empty);
            encoder.WriteUInt32(null, 1);
        }

        /// <summary>
        /// Caller-owned test input which can deliberately forbid seeking and short-read.
        /// </summary>
        private sealed class InputStream(Stream inner, bool forwardOnly) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => !forwardOnly && inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => CanSeek ? inner.Length : throw new NotSupportedException();
            public override long Position
            {
                get => CanSeek ? inner.Position : throw new NotSupportedException();
                set
                {
                    if (!CanSeek)
                    {
                        throw new NotSupportedException();
                    }
                    inner.Position = value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return inner.Read(buffer, offset, Math.Min(count, 3));
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return CanSeek ? inner.Seek(offset, origin) : throw new NotSupportedException();
            }

            public override void Flush() => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
