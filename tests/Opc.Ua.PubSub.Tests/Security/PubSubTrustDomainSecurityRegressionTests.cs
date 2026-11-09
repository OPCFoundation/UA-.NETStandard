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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;
using PubSubJsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using PubSubJsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using PubSubJsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Exercises connection trust boundaries through real receive loops, codecs and application sinks.
    /// </summary>
    [TestFixture]
    [CancelAfter(30000)]
    public sealed class PubSubTrustDomainSecurityRegressionTests
    {
        [TestCase("same")]
        [TestCase("changed")]
        [TestCase("mutation")]
        [Category("SecurityRegression")]
        public async Task SecurityF14RebuildPreservesPrivateActionScopeAsync(string rebuild)
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            StaticSecurityKeyProvider privateKeys = CreateKeys("private-group", 1);
            StaticSecurityKeyProvider publicKeys = CreateKeys("public-group", 2);
            PubSubConfigurationDataType configuration = Configuration(
                Connection("private", "private-group"),
                Connection("public", "public-group"));
            await using IPubSubApplication application = Builder(configuration, factory, privateKeys, publicKeys)
                .Build();
            int privateCalls = 0;
            int globalCalls = 0;
            var target = new PubSubActionTarget
            {
                ConnectionName = "private",
                DataSetWriterId = 77,
                ActionTargetId = 12
            };
            var handler = new DelegatePubSubActionHandler((invocation, _) =>
            {
                Interlocked.Increment(ref privateCalls);
                Assert.That(invocation.Target.ConnectionName, Is.EqualTo("private"));
                Assert.That(invocation.InputFields, Has.Count.EqualTo(1));
                Assert.That(invocation.InputFields[0].Value.TryGetValue(out int input), Is.True);
                Assert.That(input, Is.EqualTo(21));
                return new ValueTask<PubSubActionHandlerResult>(new PubSubActionHandlerResult
                {
                    OutputFields = [new DataSetField { Value = new Variant(input * 2) }]
                });
            });
            application.RegisterActionHandler(target, handler);
            application.RegisterActionHandler(
                target with { ConnectionName = string.Empty, ActionTargetId = 13 },
                new DelegatePubSubActionHandler((_, _) =>
                {
                    Interlocked.Increment(ref globalCalls);
                    return new ValueTask<PubSubActionHandlerResult>(new PubSubActionHandlerResult());
                }));

            await application.StartAsync().ConfigureAwait(false);
            UadpSecurityWrapper privatePeer = CreatePeer(privateKeys);
            UadpSecurityWrapper publicPeer = CreatePeer(publicKeys);
            for (int round = 0; round < 2; round++)
            {
                IngressTransport publicTransport = factory.Get("public");
                IngressTransport privateTransport = factory.Get("private");
                int responsesBeforeAttack = publicTransport.Sent.Count;
                await publicTransport.DeliverAsync(await EncodeSecuredAsync(
                    ActionRequest(12), publicPeer).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(privateCalls, Is.EqualTo(round));
                Assert.That(publicTransport.Sent, Has.Count.EqualTo(responsesBeforeAttack));

                int privateResponsesBefore = privateTransport.Sent.Count;
                await privateTransport.DeliverAsync(await EncodeSecuredAsync(
                    ActionRequest(12), privatePeer).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(privateCalls, Is.EqualTo(round + 1));
                Assert.That(privateTransport.Sent, Has.Count.EqualTo(privateResponsesBefore + 1),
                    "An authorized Action must return a secured response through the real send pipeline.");
                UadpActionResponseMessage response = await DecodeActionResponseAsync(
                    privateTransport.Sent[^1], privatePeer).ConfigureAwait(false);
                Assert.That(response.Status.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Payload, Has.Count.EqualTo(1));
                Assert.That(response.Payload[0].Value.TryGetValue(out int answer), Is.True);
                Assert.That(answer, Is.EqualTo(42));

                await publicTransport.DeliverAsync(await EncodeSecuredAsync(
                    ActionRequest(13), publicPeer).ConfigureAwait(false)).ConfigureAwait(false);
                await privateTransport.DeliverAsync(await EncodeSecuredAsync(
                    ActionRequest(13), privatePeer).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(globalCalls, Is.EqualTo((round + 1) * 2),
                    "Unscoped responders remain available on both authenticated connections.");

                if (round == 0)
                {
                    if (rebuild == "mutation")
                    {
                        await application.AddConnectionAsync(Connection("added", "public-group"))
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        PubSubConfigurationDataType replacement = application.GetConfiguration();
                        if (rebuild == "changed")
                        {
                            replacement.Connections =
                                [replacement.Connections[1], replacement.Connections[0]];
                            replacement.Connections[0].ReaderGroups[0].Name = "changed-readers";
                        }
                        await application.ReplaceConfigurationAsync(replacement).ConfigureAwait(false);
                    }
                    Assert.That(factory.CreateCount, Is.GreaterThan(2));
                    Assert.That(factory.Get("public"), Is.Not.SameAs(publicTransport));
                }
            }
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF14ConnectionRejectsMismatchedActionScopeAsync()
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            await using IPubSubApplication application = Builder(
                Configuration(Connection("public", "public-group")),
                factory,
                CreateKeys("public-group", 2)).Build();
            var handler = new DelegatePubSubActionHandler((_, _) =>
                new ValueTask<PubSubActionHandlerResult>(new PubSubActionHandlerResult()));
            Assert.That(
                () => ((PubSubConnection)application.Connections[0]).RegisterActionHandler(
                    new PubSubActionTarget
                    {
                        ConnectionName = "private",
                        DataSetWriterId = 77,
                        ActionTargetId = 12
                    },
                    handler),
                Throws.ArgumentException);
            Assert.That(factory.CreateCount, Is.Zero);
        }

        [TestCase(MessageSecurityMode.Invalid, MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.Invalid, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(MessageSecurityMode.None, MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.None, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, MessageSecurityMode.Sign)]
        [Category("SecurityRegression")]
        public async Task SecurityF15LeafReaderRejectsUnsignedAndAcceptsAuthorizedDataAsync(
            MessageSecurityMode parentMode,
            MessageSecurityMode readerMode)
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            StaticSecurityKeyProvider keys = CreateKeys("reader-group", 1);
            PubSubConnectionDataType connection = Connection(
                "subscriber", "reader-group", parentMode);
            DataSetReaderDataType reader = Reader("reader", 1);
            reader.SecurityMode = readerMode;
            reader.SecurityGroupId = keys.SecurityGroupId;
            reader.SecurityKeyServices = [KeyService()];
            connection.ReaderGroups[0].DataSetReaders = [reader];
            var writer = new TargetWriter();
            await using IPubSubApplication application = Builder(Configuration(connection), factory, keys)
                .AddSubscribedDataSetSink("reader", TargetSink(writer))
                .Build();

            await application.StartAsync().ConfigureAwait(false);
            IngressTransport transport = factory.Get("subscriber");
            UadpNetworkMessage message = DataMessage(1, 42);
            await transport.DeliverAsync(await EncodeAsync(message).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(writer.Writes, Is.Empty, "Unsigned data must not reach the target-variable writer.");

            UadpSecurityWrapOptions options =
                parentMode == MessageSecurityMode.SignAndEncrypt ||
                readerMode == MessageSecurityMode.SignAndEncrypt
                    ? UadpSecurityWrapOptions.SignAndEncrypt
                    : UadpSecurityWrapOptions.SignOnly;
            UadpSecurityWrapper peer = CreatePeer(keys);
            if (options == UadpSecurityWrapOptions.SignAndEncrypt)
            {
                await transport.DeliverAsync(await EncodeSecuredAsync(
                    message, peer, UadpSecurityWrapOptions.SignOnly).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(writer.Writes, Is.Empty, "A valid signature must not waive requested encryption.");
            }
            await transport.DeliverAsync(await EncodeSecuredAsync(
                message, peer, options).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(writer.Writes, Has.Count.EqualTo(1));
            Assert.That(writer.Writes[0].Value.WrappedValue, Is.EqualTo(new Variant(42)));
        }

        [TestCase(MessageSecurityMode.Invalid)]
        [TestCase(MessageSecurityMode.None)]
        [Category("SecurityRegression")]
        public void SecurityF15MissingLeafKeyProviderRefusesBeforeTransportActivation(MessageSecurityMode parentMode)
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            PubSubConnectionDataType connection = Connection("subscriber", string.Empty, parentMode);
            DataSetReaderDataType reader = Reader("reader", 1);
            reader.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            reader.SecurityGroupId = "missing-group";
            reader.SecurityKeyServices = [KeyService()];
            connection.ReaderGroups[0].DataSetReaders = [reader];

            Assert.That(
                () => Builder(Configuration(connection), factory).Build(),
                Throws.TypeOf<PubSubConfigurationException>()
                    .With.Message.Contains("PSC1401"));
            Assert.That(factory.CreateCount, Is.Zero);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF15ExplicitlyUnsecuredReaderStillReceivesDataAsync()
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            PubSubConnectionDataType connection = Connection(
                "subscriber", string.Empty, MessageSecurityMode.None);
            DataSetReaderDataType reader = Reader("reader", 1);
            connection.ReaderGroups[0].DataSetReaders = [reader];
            var writer = new TargetWriter();
            await using IPubSubApplication application = Builder(Configuration(connection), factory)
                .AddSubscribedDataSetSink("reader", TargetSink(writer))
                .Build();

            await application.StartAsync().ConfigureAwait(false);
            await factory.Get("subscriber").DeliverAsync(
                await EncodeAsync(DataMessage(1, 42)).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(writer.Writes, Has.Count.EqualTo(1));
            Assert.That(writer.Writes[0].Value.WrappedValue, Is.EqualTo(new Variant(42)));
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF16MixedSecurityGroupsRefuseBeforeTransportActivationAsync(
            [Values("readers", "writers", "writer-reader", "reader-leaf", "leaves")] string layout,
            [Values] bool reverse,
            [Values(MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)] MessageSecurityMode secondMode)
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            PubSubConnectionDataType connection = Connection("mixed", "group-a");
            ReaderGroupDataType first = connection.ReaderGroups[0];
            ReaderGroupDataType second = Connection("unused", "group-b", secondMode).ReaderGroups[0];
            first.Name = "first";
            second.Name = "second";
            if (layout == "readers")
            {
                connection.ReaderGroups = reverse ? [second, first] : [first, second];
            }
            else if (layout == "writers")
            {
                WriterGroupDataType a = WriterGroup("first", 1, "group-a");
                WriterGroupDataType b = WriterGroup("second", 2, "group-b", secondMode);
                connection.WriterGroups = reverse ? [b, a] : [a, b];
                connection.ReaderGroups = [];
            }
            else if (layout == "writer-reader")
            {
                connection.WriterGroups =
                    [WriterGroup("writer", 1, reverse ? "group-b" : "group-a", secondMode)];
                first.SecurityGroupId = reverse ? "group-a" : "group-b";
            }
            else
            {
                DataSetReaderDataType leaf = Reader("leaf", 1);
                leaf.SecurityMode = secondMode;
                leaf.SecurityGroupId = reverse ? "group-a" : "group-b";
                leaf.SecurityKeyServices = [KeyService()];
                if (layout == "reader-leaf")
                {
                    first.SecurityGroupId = reverse ? "group-b" : "group-a";
                    first.DataSetReaders = [leaf];
                }
                else
                {
                    first.SecurityMode = MessageSecurityMode.None;
                    first.SecurityGroupId = string.Empty;
                    leaf.SecurityGroupId = "group-b";
                    DataSetReaderDataType other = Reader("other", 2);
                    other.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    other.SecurityGroupId = "group-a";
                    other.SecurityKeyServices = [KeyService()];
                    first.DataSetReaders = reverse ? [leaf, other] : [other, leaf];
                }
            }
            PubSubConfigurationDataType configuration = Configuration(Connection("safe", "group-a"), connection);
            var validator = new PubSubConfigurationValidator([Profiles.PubSubUdpUadpTransport])
            {
                RegisteredSecurityGroupIds = ["group-a", "group-b"]
            };
            PubSubConfigurationValidationResult validation = validator.Validate(configuration);
            Assert.That(validation.IsValid, Is.False);
            Assert.That(HasIssueCode(validation.Issues, "PSC1402"), Is.True);
            PubSubConfigurationException? rejection = null;
            try
            {
                await using IPubSubApplication application = Builder(
                    configuration,
                    factory,
                    CreateKeys("group-a", 1),
                    CreateKeys("group-b", 2)).Build();
            }
            catch (PubSubConfigurationException exception)
            {
                rejection = exception;
            }

            Assert.That(rejection, Is.Not.Null, "Distinct SecurityGroups require separate connections.");
            Assert.That(HasIssueCode(rejection!.Issues, "PSC1402"), Is.True);
            Assert.That(factory.CreateCount, Is.Zero,
                "Neither the mixed connection nor its otherwise valid sibling may activate.");
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF16HomogeneousGroupsDeliverToMatchingReaderAsync()
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            StaticSecurityKeyProvider keys = CreateKeys("shared-group", 1);
            PubSubConnectionDataType connection = Connection("subscriber", keys.SecurityGroupId);
            DataSetReaderDataType first = Reader("first", 1);
            DataSetReaderDataType second = Reader("second", 2);
            connection.ReaderGroups[0].DataSetReaders = [first];
            ReaderGroupDataType other = Connection("unused", keys.SecurityGroupId).ReaderGroups[0];
            other.Name = "other";
            other.DataSetReaders = [second];
            connection.ReaderGroups = [connection.ReaderGroups[0], other];
            var firstWriter = new TargetWriter();
            var secondWriter = new TargetWriter();
            await using IPubSubApplication application = Builder(Configuration(connection), factory, keys)
                .AddSubscribedDataSetSink("first", TargetSink(firstWriter))
                .AddSubscribedDataSetSink("second", TargetSink(secondWriter))
                .Build();

            await application.StartAsync().ConfigureAwait(false);
            await factory.Get("subscriber").DeliverAsync(await EncodeSecuredAsync(
                DataMessage(2, 42), CreatePeer(keys)).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(firstWriter.Writes, Is.Empty);
            Assert.That(secondWriter.Writes, Has.Count.EqualTo(1));
            Assert.That(secondWriter.Writes[0].Value.WrappedValue, Is.EqualTo(new Variant(42)));
        }

        [TestCase(false, 1U, false, false)]
        [TestCase(false, uint.MaxValue, false, false)]
        [TestCase(true, 1U, false, false)]
        [TestCase(true, uint.MaxValue, false, false)]
        [TestCase(false, 1U, true, true)]
        [TestCase(true, uint.MaxValue, true, true)]
        [Category("SecurityRegression")]
        [TestSpec("6.2.10.2.3")]
        public async Task SecurityF42PublicMetadataCannotSwapProtectedTargetsAsync(
            bool includeWriterGroupId,
            uint forgedVersion,
            bool subsetTargets,
            bool swapFieldIds)
        {
            var uadp = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            var json = new IngressTransportFactory(Profiles.PubSubMqttJsonTransport);
            StaticSecurityKeyProvider keys = CreateKeys("protected-group", 1);
            PubSubConnectionDataType protectedConnection = Connection("protected", keys.SecurityGroupId);
            DataSetReaderDataType protectedReader = Reader("protected-reader", 1);
            protectedReader.WriterGroupId = includeWriterGroupId ? (ushort)1 : (ushort)0;
            protectedReader.DataSetMetaData = MetaData();
            protectedConnection.ReaderGroups[0].DataSetReaders = [protectedReader];
            PubSubConnectionDataType publicConnection = Connection(
                "public", string.Empty, MessageSecurityMode.None);
            publicConnection.TransportProfileUri = Profiles.PubSubMqttJsonTransport;
            publicConnection.Address = new ExtensionObject(new NetworkAddressUrlDataType
            {
                Url = "mqtt://localhost:1883"
            });
            DataSetReaderDataType publicReader = Reader("public-reader", 1);
            publicReader.PublisherId = default;
            publicConnection.ReaderGroups[0].DataSetReaders = [publicReader];
            var targetWriter = new TargetWriter();
            var protectedSink = new CapturingSink(TargetSink(
                targetWriter, subsetTargets ? [SecondFieldId] : [SecondFieldId, FirstFieldId]));
            var publicSink = new CapturingSink();
            await using IPubSubApplication application = Builder(
                Configuration(publicConnection, protectedConnection), uadp, keys)
                .AddTransportFactory(json)
                .AddSubscribedDataSetSink("protected-reader", protectedSink)
                .AddSubscribedDataSetSink("public-reader", publicSink)
                .Build();

            await application.StartAsync().ConfigureAwait(false);
            IngressTransport protectedTransport = uadp.Get("protected");
            IngressTransport publicTransport = json.Get("public");
            UadpSecurityWrapper peer = CreatePeer(keys);
            var announcement = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromString("victim"),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterId = 1,
                DataSetMetaData = MetaData()
            };
            await protectedTransport.DeliverAsync(
                await EncodeSecuredAsync(announcement, peer).ConfigureAwait(false)).ConfigureAwait(false);

            DataSetMetaDataType forged = MetaData(forgedVersion);
            forged.Fields[0].Name = SecondFieldId.ToString();
            forged.Fields[1].Name = FirstFieldId.ToString();
            if (swapFieldIds)
            {
                forged.Fields[0].DataSetFieldId = SecondFieldId;
                forged.Fields[1].DataSetFieldId = FirstFieldId;
            }
            var maliciousAnnouncement = new JsonMetaDataMessage
            {
                PublisherId = PublisherId.FromString("victim"),
                DataSetWriterId = 1,
                WriterGroupName = "forged-group",
                DataSetWriterName = "forged-writer",
                MetaDataPayload = forged
            };
            await publicTransport.DeliverAsync(
                await new PubSubJsonEncoder().EncodeAsync(maliciousAnnouncement, Context()).ConfigureAwait(false))
                .ConfigureAwait(false);
            var publicKey = new DataSetMetaDataKey(PublisherId.FromString("victim"), 0, 1, Uuid.Empty, forgedVersion);
            IDataSetMetaDataRegistry publicRegistry = ((PubSubConnection)application.Connections[0])
                .InboundMetaDataRegistry;
            Assert.That(publicRegistry.TryGet(in publicKey, out DataSetMetaDataType? publicMetaData),
                Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(publicMetaData!.Fields[0].Name, Is.EqualTo(SecondFieldId.ToString()),
                "Unsecured metadata remains usable in its own connection, not in the protected connection.");
            Assert.That(publicMetaData.Fields[0].DataSetFieldId,
                Is.EqualTo(swapFieldIds ? SecondFieldId : FirstFieldId));

            UadpNetworkMessage authenticData = DataMessage(1, 1001, 2002) with
            {
                WriterGroupId = includeWriterGroupId ? 1 : null,
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    (includeWriterGroupId
                        ? UadpNetworkMessageContentMask.GroupHeader | UadpNetworkMessageContentMask.WriterGroupId
                        : 0)
            };
            await protectedTransport.DeliverAsync(
                await EncodeSecuredAsync(authenticData, peer).ConfigureAwait(false)).ConfigureAwait(false);
            AssertProtectedTargets(targetWriter, protectedSink, updates: 1, subsetTargets);
            bool metadataPromoted = false;
            foreach (DataSetMetaDataKey key in application.MetaDataRegistry.Keys)
            {
                if (key.WriterGroupId == 0 && key.PublisherId.Equals(PublisherId.FromString("victim")))
                {
                    metadataPromoted = true;
                    break;
                }
            }
            Assert.That(metadataPromoted, Is.False,
                "Network metadata must not be promoted to the host-trusted application registry.");

            var publicData = new PubSubJsonNetworkMessage
            {
                PublisherId = PublisherId.FromString("victim"),
                ContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader |
                    JsonNetworkMessageContentMask.PublisherId |
                    JsonNetworkMessageContentMask.DataSetMessageHeader,
                DataSetMessages =
                [
                    new PubSubJsonDataSetMessage
                    {
                        DataSetWriterId = 1,
                        ContentMask = JsonDataSetMessageContentMask.DataSetWriterId,
                        Fields =
                        [
                            new DataSetField { Name = SecondFieldId.ToString(), Value = new Variant(3) },
                            new DataSetField { Name = FirstFieldId.ToString(), Value = new Variant(4) }
                        ]
                    }
                ]
            };
            await publicTransport.DeliverAsync(
                await new PubSubJsonEncoder().EncodeAsync(publicData, Context()).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(publicSink.Fields, Has.Count.EqualTo(2),
                "The unsecured connection still accepts its own data.");
            Assert.That(publicSink.Fields[0].DataSetFieldId,
                Is.EqualTo(swapFieldIds ? SecondFieldId : FirstFieldId));
            Assert.That(publicSink.Fields[1].DataSetFieldId,
                Is.EqualTo(swapFieldIds ? FirstFieldId : SecondFieldId));

            await protectedTransport.DeliverAsync(
                await EncodeAsync(announcement with { DataSetMetaData = forged }).ConfigureAwait(false))
                .ConfigureAwait(false);
            UadpNetworkMessage rawData = authenticData with
            {
                DataSetMessages =
                [
                    (UadpDataSetMessage)authenticData.DataSetMessages[0] with
                    {
                        FieldEncoding = PubSubFieldEncoding.RawData
                    }
                ]
            };
            var encodingRegistry = new DataSetMetaDataRegistry();
            var encodingKey = new DataSetMetaDataKey(
                PublisherId.FromString("victim"), includeWriterGroupId ? (ushort)1 : (ushort)0, 1, Uuid.Empty, 1);
            encodingRegistry.Register(encodingKey, MetaData());
            await protectedTransport.DeliverAsync(await EncodeSecuredAsync(
                rawData, peer, context: Context(encodingRegistry)).ConfigureAwait(false)).ConfigureAwait(false);
            AssertProtectedTargets(targetWriter, protectedSink, updates: 2, subsetTargets);

            UadpNetworkMessage deltaFrame = authenticData with
            {
                DataSetMessages =
                [
                    (UadpDataSetMessage)authenticData.DataSetMessages[0] with
                    {
                        MessageType = PubSubDataSetMessageType.DeltaFrame,
                        Fields = [new DataSetField { FieldIndex = 1, Value = new Variant(2002) }]
                    }
                ]
            };
            await protectedTransport.DeliverAsync(
                await EncodeSecuredAsync(deltaFrame, peer).ConfigureAwait(false)).ConfigureAwait(false);
            int expectedWrites = subsetTargets ? 3 : 5;
            Assert.That(targetWriter.Writes, Has.Count.EqualTo(expectedWrites));
            Assert.That(targetWriter.Writes[^1].NodeId, Is.EqualTo(new NodeId("B", 1)));
            Assert.That(targetWriter.Writes[^1].Value.WrappedValue, Is.EqualTo(new Variant(2002)));
            Assert.That(protectedSink.Fields, Has.Count.EqualTo(1));
            Assert.That(protectedSink.Fields[0].FieldIndex, Is.EqualTo(1));
            Assert.That(protectedSink.Fields[0].Name, Is.EqualTo("B"));
            Assert.That(protectedSink.Fields[0].DataSetFieldId, Is.EqualTo(SecondFieldId));

            UadpNetworkMessage unmatchedVersion = authenticData with
            {
                DataSetMessages =
                [
                    (UadpDataSetMessage)authenticData.DataSetMessages[0] with
                    {
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 2 },
                        Fields =
                        [
                            new DataSetField { Value = new Variant(9999) },
                            new DataSetField { Value = new Variant(8888) }
                        ]
                    }
                ]
            };
            await protectedTransport.DeliverAsync(
                await EncodeSecuredAsync(unmatchedVersion, peer).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(targetWriter.Writes, Has.Count.EqualTo(expectedWrites),
                "Unresolved field IDs must not fall back to target positions under stale configured metadata.");
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        [TestSpec("6.2.10.2.3")]
        [TestSpec("7.2.4.5.6")]
        public async Task SecurityF42AuthenticatedMetadataReorderingKeepsFieldIdTargetsAsync(bool subsetTargets)
        {
            var factory = new IngressTransportFactory(Profiles.PubSubUdpUadpTransport);
            StaticSecurityKeyProvider keys = CreateKeys("protected-group", 1);
            PubSubConnectionDataType connection = Connection("protected", keys.SecurityGroupId);
            DataSetReaderDataType reader = Reader("reader", 1);
            connection.ReaderGroups[0].DataSetReaders = [reader];
            var writer = new TargetWriter();
            var sink = new CapturingSink(TargetSink(
                writer, subsetTargets ? [SecondFieldId] : [FirstFieldId, SecondFieldId]));
            await using IPubSubApplication application = Builder(Configuration(connection), factory, keys)
                .AddSubscribedDataSetSink("reader", sink)
                .Build();
            await application.StartAsync().ConfigureAwait(false);
            IngressTransport transport = factory.Get("protected");
            UadpSecurityWrapper peer = CreatePeer(keys);
            DataSetMetaDataType updated = MetaData(major: 2);
            updated.Fields = [updated.Fields[1], updated.Fields[0]];
            var announcement = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromString("victim"),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterId = 1,
                DataSetMetaData = updated
            };
            await transport.DeliverAsync(
                await EncodeSecuredAsync(announcement, peer).ConfigureAwait(false)).ConfigureAwait(false);
            UadpNetworkMessage message = DataMessage(1, 2002, 1001);
            message = message with
            {
                DataSetMessages =
                [
                    (UadpDataSetMessage)message.DataSetMessages[0] with
                    {
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 2 }
                    }
                ]
            };
            await transport.DeliverAsync(
                await EncodeSecuredAsync(message, peer).ConfigureAwait(false)).ConfigureAwait(false);

            int keyFrameWrites = subsetTargets ? 1 : 2;
            Assert.That(writer.Writes, Has.Count.EqualTo(keyFrameWrites));
            Assert.That(writer.Writes[0].NodeId, Is.EqualTo(new NodeId(subsetTargets ? "B" : "A", 1)));
            Assert.That(writer.Writes[0].Value.WrappedValue, Is.EqualTo(new Variant(subsetTargets ? 2002 : 1001)));
            Assert.That(writer.Writes[^1].NodeId, Is.EqualTo(new NodeId("B", 1)));
            Assert.That(writer.Writes[^1].Value.WrappedValue, Is.EqualTo(new Variant(2002)));
            Assert.That(sink.Fields[0].DataSetFieldId, Is.EqualTo(SecondFieldId));
            Assert.That(sink.Fields[1].DataSetFieldId, Is.EqualTo(FirstFieldId));

            message = message with
            {
                DataSetMessages =
                [
                    (UadpDataSetMessage)message.DataSetMessages[0] with
                    {
                        MessageType = PubSubDataSetMessageType.DeltaFrame,
                        Fields = [new DataSetField { FieldIndex = 0, Value = new Variant(2002) }]
                    }
                ]
            };
            await transport.DeliverAsync(
                await EncodeSecuredAsync(message, peer).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(writer.Writes, Has.Count.EqualTo(keyFrameWrites + 1));
            Assert.That(writer.Writes[^1].NodeId, Is.EqualTo(new NodeId("B", 1)));
            Assert.That(writer.Writes[^1].Value.WrappedValue, Is.EqualTo(new Variant(2002)));
            Assert.That(sink.Fields[0].FieldIndex, Is.Zero);
            Assert.That(sink.Fields[0].DataSetFieldId, Is.EqualTo(SecondFieldId));
        }

        private static void AssertProtectedTargets(
            TargetWriter writer,
            CapturingSink sink,
            int updates,
            bool subsetTargets)
        {
            Assert.That(writer.Writes, Has.Count.EqualTo(updates * (subsetTargets ? 1 : 2)));
            (NodeId NodeId, DataValue Value)[] first = writer.Writes.Where(write => write.NodeId.Equals(new NodeId("A", 1))).ToArray();
            (NodeId NodeId, DataValue Value)[] second = writer.Writes.Where(write => write.NodeId.Equals(new NodeId("B", 1))).ToArray();
            Assert.That(first, Has.Length.EqualTo(subsetTargets ? 0 : updates));
            Assert.That(second, Has.Length.EqualTo(updates));
            Assert.That(first.Select(write => write.Value.WrappedValue), Is.All.EqualTo(new Variant(1001)));
            Assert.That(second.Select(write => write.Value.WrappedValue), Is.All.EqualTo(new Variant(2002)));
            Assert.That(sink.Fields, Has.Count.EqualTo(2));
            Assert.That(sink.Fields[0].Name, Is.EqualTo("A"),
                "Authenticated decoding must not consult another connection's mutable metadata.");
            Assert.That(sink.Fields[1].Name, Is.EqualTo("B"));
            Assert.That(sink.Fields[0].DataSetFieldId, Is.EqualTo(FirstFieldId));
            Assert.That(sink.Fields[1].DataSetFieldId, Is.EqualTo(SecondFieldId));
        }

        /// <summary>
        /// Finds a concrete configuration issue without boxing the ArrayOf collection.
        /// </summary>
        private static bool HasIssueCode(ArrayOf<PubSubConfigurationIssue> issues, string code)
        {
            foreach (PubSubConfigurationIssue issue in issues)
            {
                if (string.Equals(issue.Code, code, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static PubSubApplicationBuilder Builder(
            PubSubConfigurationDataType configuration,
            IngressTransportFactory factory,
            params StaticSecurityKeyProvider[] providers)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new PubSubApplicationBuilder(telemetry)
                .UseConfiguration(configuration)
                .UseAllStandardEncoders()
                .AddTransportFactory(factory)
                .WithSecurityWrapperResolver(
                    new PubSubSecurityWrapperResolver(
                        providers, telemetry, nonceProvider: new SequenceNonceProvider()));
        }

        private static PubSubConfigurationDataType Configuration(params PubSubConnectionDataType[] connections)
        {
            return new PubSubConfigurationDataType { Connections = connections, PublishedDataSets = [] };
        }

        private static PubSubConnectionDataType Connection(
            string name,
            string securityGroupId,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt)
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                PublisherId = new Variant(name),
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://239.0.0.1:4840" }),
                ReaderGroups =
                [
                    new ReaderGroupDataType
                    {
                        Name = "readers",
                        SecurityMode = mode,
                        SecurityGroupId = mode is MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt
                            ? securityGroupId : string.Empty
                    }
                ]
            };
        }

        private static WriterGroupDataType WriterGroup(
            string name,
            ushort id,
            string securityGroupId,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt)
        {
            return new WriterGroupDataType
            {
                Name = name,
                WriterGroupId = id,
                PublishingInterval = 1000,
                SecurityMode = mode,
                SecurityGroupId = securityGroupId
            };
        }

        private static DataSetReaderDataType Reader(string name, ushort writerId)
        {
            return new DataSetReaderDataType
            {
                Name = name,
                PublisherId = new Variant("victim"),
                DataSetWriterId = writerId,
                MessageReceiveTimeout = 60000,
                SecurityMode = MessageSecurityMode.None,
                DataSetMetaData = MetaData(),
                SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
            };
        }

        private static EndpointDescription KeyService()
        {
            return new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" };
        }

        private static TargetVariablesSink TargetSink(TargetWriter writer, params Uuid[] fieldIds)
        {
            if (fieldIds.Length == 0)
            {
                fieldIds = [FirstFieldId];
            }
            ArrayOf<FieldTargetDataType> targets =
            [.. fieldIds.Select(fieldId => new FieldTargetDataType
                {
                    DataSetFieldId = fieldId,
                    TargetNodeId = new NodeId(fieldId == FirstFieldId ? "A" : "B", 1),
                    AttributeId = Attributes.Value
                })];
            return new TargetVariablesSink(
                new TargetVariablesDataType { TargetVariables = targets }, writer);
        }

        private static DataSetMetaDataType MetaData(uint major = 1)
        {
            return new DataSetMetaDataType
            {
                Name = "values",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = major },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "A",
                        DataSetFieldId = FirstFieldId,
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "B",
                        DataSetFieldId = SecondFieldId,
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static StaticSecurityKeyProvider CreateKeys(string groupId, uint tokenId)
        {
            var ring = new PubSubSecurityKeyRing(groupId);
            ring.SetCurrent(TestSecurityKeyFactory.Create(tokenId, encryptingKeyLength: 32));
            return new StaticSecurityKeyProvider(groupId, ring);
        }

        private static UadpSecurityWrapper CreatePeer(StaticSecurityKeyProvider keys)
        {
            var resolver = new PubSubSecurityWrapperResolver(
                [keys], NUnitTelemetryContext.Create(), nonceProvider: new SequenceNonceProvider(5678));
            return resolver.Resolve(Connection("peer", keys.SecurityGroupId))!.Wrapper;
        }

        private static UadpActionRequestMessage ActionRequest(ushort targetId)
        {
            return new UadpActionRequestMessage
            {
                PublisherId = PublisherId.FromString("requestor"),
                DataSetWriterId = 77,
                ActionTargetId = targetId,
                RequestId = 1,
                CorrelationData = ByteString.Create([1, 2, 3]),
                RequestorId = new Variant("requestor"),
                Payload = [new DataSetField { Value = new Variant(21) }]
            };
        }

        private static UadpNetworkMessage DataMessage(ushort writerId, params int[] values)
        {
            return new UadpNetworkMessage
            {
                PublisherId = PublisherId.FromString("victim"),
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = writerId,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                        Fields = [.. values.Select(value => new DataSetField { Value = new Variant(value) })]
                    }
                ]
            };
        }

        private static ValueTask<ReadOnlyMemory<byte>> EncodeAsync(PubSubNetworkMessage message)
        {
            return new UadpEncoder().EncodeAsync(message, Context());
        }

        private static ValueTask<ReadOnlyMemory<byte>> EncodeSecuredAsync(
            PubSubNetworkMessage message,
            UadpSecurityWrapper peer,
            UadpSecurityWrapOptions options = UadpSecurityWrapOptions.SignAndEncrypt,
            PubSubNetworkMessageContext? context = null)
        {
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                message, context ?? Context(), out int offset);
            return peer.WrapAsync(encoded[..offset], encoded[offset..], options);
        }

        private static async Task<UadpActionResponseMessage> DecodeActionResponseAsync(
            ReadOnlyMemory<byte> frame,
            UadpSecurityWrapper peer)
        {
            Assert.That(UadpDecoder.TryReadPrefix(frame, out UadpPrefixInfo prefix), Is.True);
            UadpSecurityWrapper.UnwrapResult result = await peer.TryUnwrapAsync(
                frame[..prefix.PrefixLength], frame[prefix.PrefixLength..]).ConfigureAwait(false);
            Assert.That(result.IsSuccess, Is.True, result.Reason);
            byte[] clear = new byte[prefix.PrefixLength + result.InnerPayload!.Value.Length];
            frame.Span[..prefix.PrefixLength].CopyTo(clear);
            result.InnerPayload.Value.Span.CopyTo(clear.AsSpan(prefix.PrefixLength));
            PubSubNetworkMessage? response = UadpDecoder.Decode(clear, Context());
            Assert.That(response, Is.InstanceOf<UadpActionResponseMessage>());
            return (UadpActionResponseMessage)response!;
        }

        private static PubSubNetworkMessageContext Context(IDataSetMetaDataRegistry? registry = null)
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                registry ?? new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        /// <summary>
        /// Records the real target-variable writes performed by a subscription.
        /// </summary>
        private sealed class TargetWriter : ITargetVariableWriter
        {
            public List<(NodeId NodeId, DataValue Value)> Writes { get; } = [];

            public ValueTask<StatusCode> WriteAsync(
                NodeId nodeId,
                uint attributeId,
                string? writeIndexRange,
                DataValue value,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Writes.Add((nodeId, value));
                return new ValueTask<StatusCode>(StatusCodes.Good);
            }
        }

        /// <summary>
        /// Observes decoded metadata names while retaining the actual target sink.
        /// </summary>
        private sealed class CapturingSink(TargetVariablesSink? target = null) : ISubscribedDataSetSink
        {
            public ArrayOf<DataSetField> Fields { get; private set; }

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                Fields = [.. fields];
                return target?.WriteAsync(fields, cancellationToken) ?? default;
            }
        }

        /// <summary>
        /// Produces unique test nonces without allocating cryptographic resources.
        /// </summary>
        private sealed class SequenceNonceProvider(uint random = 1234) : INonceProvider
        {
            public void GetNext(uint keyId, ReadOnlySpan<byte> keyNonce, Span<byte> buffer)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, random);
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], (uint)Interlocked.Increment(ref m_sequence));
            }

            private int m_sequence;
        }

        /// <summary>
        /// Creates independently addressable, socket-free connection transports.
        /// </summary>
        private sealed class IngressTransportFactory(string profile) : IPubSubTransportFactory
        {
            public string TransportProfileUri => profile;

            public int CreateCount { get; private set; }

            public IngressTransport Get(string name)
            {
                return m_transports[name];
            }

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                string name = connection.Name
                    ?? throw new ArgumentException("A connection name is required.", nameof(connection));
                CreateCount++;
                var transport = new IngressTransport(profile);
                m_transports[name] = transport;
                return transport;
            }

            private readonly Dictionary<string, IngressTransport> m_transports = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Acknowledges injected frames only after the real connection has finished processing each frame.
        /// </summary>
        private sealed class IngressTransport(string profile) : IPubSubTransport
        {
            public string TransportProfileUri => profile;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected { get; private set; }

            public List<ReadOnlyMemory<byte>> Sent { get; } = [];

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public async Task DeliverAsync(ReadOnlyMemory<byte> payload)
            {
                var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await m_inbound.Writer.WriteAsync(new Injection(payload, completed)).ConfigureAwait(false);
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                IsConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                IsConnected = false;
                m_inbound.Writer.TryComplete();
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                Sent.Add(payload.ToArray());
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (await m_inbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (m_inbound.Reader.TryRead(out Injection? injection))
                    {
                        try
                        {
                            yield return new PubSubTransportFrame(
                                injection.Payload, null, DateTimeUtc.From(DateTime.UtcNow));
                        }
                        finally
                        {
                            injection.Completed.TrySetResult(true);
                        }
                    }
                }
            }

            public ValueTask DisposeAsync()
            {
                return CloseAsync();
            }

            /// <summary>
            /// Pairs a wire frame with its post-dispatch acknowledgment.
            /// </summary>
            private sealed record Injection(ReadOnlyMemory<byte> Payload, TaskCompletionSource<bool> Completed);

            private readonly Channel<Injection> m_inbound = Channel.CreateUnbounded<Injection>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        }

        private static readonly Uuid FirstFieldId = new(new Guid("11111111-1111-1111-1111-111111111111"));
        private static readonly Uuid SecondFieldId = new(new Guid("22222222-2222-2222-2222-222222222222"));
    }
}
