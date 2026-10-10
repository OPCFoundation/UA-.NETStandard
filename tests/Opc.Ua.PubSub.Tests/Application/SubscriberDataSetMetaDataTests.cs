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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Verifies that a Subscriber uses the DataSetMetaData configured on its
    /// DataSetReader to decode RawData DataSetMessages when it runs separately
    /// from the Publisher and shares no metadata registry with it, per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.4">
    /// Part 14 §6.2.9.4</see>.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.9.4")]
    [CancelAfter(30000)]
    public sealed class SubscriberDataSetMetaDataTests
    {
        private const string Address = "opc.udp://239.0.0.1:4840";
        private const string SecurityGroupId = "SubscriberMetaDataGroup";
        private const string DataSetName = "Simple";
        private const string ReaderName = "Reader 1";
        private const ushort PublisherIdValue = 1;
        private const ushort WriterGroupId = 100;
        private const ushort DataSetWriterId = 1;

        [TestCase(MessageSecurityMode.SignAndEncrypt)]
        [TestCase(MessageSecurityMode.None)]
        public async Task SeparateSubscriberDecodesRawDataWithConfiguredMetaDataAsync(
            MessageSecurityMode securityMode)
        {
            var bus = new InMemoryBus();
            var sink = new FirstDataSetSink();

            await using IPubSubApplication subscriber = CreateBuilder(bus, securityMode)
                .WithApplicationId("urn:test:subscriber")
                .UseConfiguration(CreateSubscriberConfiguration(
                    securityMode, PublisherIdValue, WriterGroupId))
                .AddSubscribedDataSetSink(ReaderName, sink)
                .Build();
            await using IPubSubApplication publisher = CreateBuilder(bus, securityMode)
                .WithApplicationId("urn:test:publisher")
                .UseConfiguration(CreatePublisherConfiguration(securityMode))
                .AddDataSetSource(DataSetName, new CounterSource())
                .Build();

            await subscriber.StartAsync().ConfigureAwait(false);
            await publisher.StartAsync().ConfigureAwait(false);

            IReadOnlyList<DataSetField> fields = await sink.FirstDataSet
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Assert.That(fields, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(fields[0].Name, Is.EqualTo("Counter"));
                Assert.That(fields[0].Value.TryGetValue(out int counter), Is.True);
                Assert.That(counter, Is.GreaterThan(0));
                Assert.That(fields[1].Name, Is.EqualTo("Toggle"));
            });
        }

        [Test]
        public async Task BuildRegistersMetaDataOfReaderWithExactFilterAsync()
        {
            await using IPubSubApplication subscriber = CreateBuilder(
                    new InMemoryBus(), MessageSecurityMode.None)
                .WithApplicationId("urn:test:subscriber")
                .UseConfiguration(CreateSubscriberConfiguration(
                    MessageSecurityMode.None, PublisherIdValue, WriterGroupId))
                .Build();

            MetaDataMatchResult result = subscriber.MetaDataRegistry.TryGet(
                ReaderKey(WriterGroupId),
                out DataSetMetaDataType? metaData);

            Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(metaData!.Fields.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task BuildDoesNotRegisterMetaDataOfReaderWithWildcardFilterAsync()
        {
            await using IPubSubApplication subscriber = CreateBuilder(
                    new InMemoryBus(), MessageSecurityMode.None)
                .WithApplicationId("urn:test:subscriber")
                .UseConfiguration(CreateSubscriberConfiguration(
                    MessageSecurityMode.None, PublisherIdValue, writerGroupId: 0))
                .Build();

            Assert.That(subscriber.MetaDataRegistry.Keys.Count, Is.Zero);
        }

        [Test]
        public async Task ReaderMetaDataDoesNotReplacePublishedDataSetMetaDataAsync()
        {
            PubSubConfigurationDataType configuration =
                CreatePublisherConfiguration(MessageSecurityMode.None);
            PubSubConfigurationDataType readers = CreateSubscriberConfiguration(
                MessageSecurityMode.None, PublisherIdValue, WriterGroupId);
            configuration.Connections[0].ReaderGroups = readers.Connections[0].ReaderGroups;
            configuration.Connections[0].ReaderGroups[0].DataSetReaders[0]
                .DataSetMetaData.Name = "ReaderView";

            await using IPubSubApplication application = CreateBuilder(
                    new InMemoryBus(), MessageSecurityMode.None)
                .WithApplicationId("urn:test:loopback")
                .UseConfiguration(configuration)
                .AddDataSetSource(DataSetName, new CounterSource())
                .Build();

            application.MetaDataRegistry.TryGet(
                ReaderKey(WriterGroupId),
                out DataSetMetaDataType? metaData);

            Assert.That(metaData!.Name, Is.EqualTo(DataSetName));
        }

        private static DataSetMetaDataKey ReaderKey(ushort writerGroupId)
        {
            return new DataSetMetaDataKey(
                PublisherId.FromUInt16(PublisherIdValue),
                writerGroupId,
                DataSetWriterId,
                Uuid.Empty,
                1);
        }

        private static PubSubApplicationBuilder CreateBuilder(
            InMemoryBus bus,
            MessageSecurityMode securityMode)
        {
            PubSubApplicationBuilder builder = new PubSubApplicationBuilder(
                    NUnitTelemetryContext.Create())
                .UseAllStandardEncoders()
                .AddTransportFactory(new InMemoryBusTransportFactory(bus));
            if (securityMode != MessageSecurityMode.None)
            {
                builder.AddSecurityKeyProvider(CreateKeyProvider());
            }
            return builder;
        }

        private static PubSubConfigurationDataType CreatePublisherConfiguration(
            MessageSecurityMode securityMode)
        {
            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet(DataSetName, dataSet => dataSet
                    .AddField("Counter", (byte)BuiltInType.Int32, DataTypeIds.Int32)
                    .AddField("Toggle", (byte)BuiltInType.Boolean, DataTypeIds.Boolean))
                .AddConnection("Publisher Connection", connection => connection
                    .WithPublisherId(new Variant(PublisherIdValue))
                    .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                    .WithAddress(Address)
                    .AddWriterGroup("WriterGroup 1", group =>
                    {
                        group
                            .WithWriterGroupId(WriterGroupId)
                            .WithPublishingInterval(50)
                            .WithMessageSettings(new UadpWriterGroupMessageDataType
                            {
                                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                                NetworkMessageContentMask = (uint)NetworkMessageContentMask
                            })
                            .WithTransportSettings(new DatagramWriterGroupTransportDataType());
                        if (securityMode != MessageSecurityMode.None)
                        {
                            group.WithSecurity(
                                securityMode,
                                SecurityGroupId,
                                "opc.tcp://localhost:4840/SecurityKeyService");
                        }
                        group.AddDataSetWriter("Writer 1", writer => writer
                            .WithDataSetWriterId(DataSetWriterId)
                            .WithDataSetName(DataSetName)
                            .WithKeyFrameCount(1)
                            .WithFieldContentMask(DataSetFieldContentMask.RawData)
                            .WithMessageSettings(new UadpDataSetWriterMessageDataType
                            {
                                DataSetMessageContentMask = (uint)DataSetMessageContentMask
                            }));
                    }))
                .Build();
        }

        private static PubSubConfigurationDataType CreateSubscriberConfiguration(
            MessageSecurityMode securityMode,
            ushort publisherId,
            ushort writerGroupId)
        {
            return PubSubConfigurationBuilder.Create()
                .AddConnection("Subscriber Connection", connection => connection
                    .WithPublisherId(new Variant(publisherId))
                    .WithTransportProfile(Profiles.PubSubUdpUadpTransport)
                    .WithAddress(Address)
                    .AddReaderGroup("ReaderGroup 1", group =>
                    {
                        if (securityMode != MessageSecurityMode.None)
                        {
                            group.WithSecurity(
                                securityMode,
                                SecurityGroupId,
                                "opc.tcp://localhost:4840/SecurityKeyService");
                        }
                        group.AddDataSetReader(ReaderName, reader => reader
                            .WithFilter(new Variant(publisherId), writerGroupId, DataSetWriterId)
                            .WithFieldContentMask(DataSetFieldContentMask.RawData)
                            .WithMessageReceiveTimeout(5000)
                            .WithMirrorSubscribedDataSet(ReaderName)
                            .WithMessageSettings(new UadpDataSetReaderMessageDataType
                            {
                                NetworkMessageContentMask = (uint)NetworkMessageContentMask,
                                DataSetMessageContentMask = (uint)DataSetMessageContentMask
                            })
                            .WithDataSetMetaData(DataSetName, metaData => metaData
                                .WithoutFieldIds()
                                .AddField("Counter", (byte)BuiltInType.Int32, DataTypeIds.Int32)
                                .AddField("Toggle", (byte)BuiltInType.Boolean, DataTypeIds.Boolean)));
                    }))
                .Build();
        }

        private static UadpNetworkMessageContentMask NetworkMessageContentMask =>
            UadpNetworkMessageContentMask.PublisherId |
            UadpNetworkMessageContentMask.GroupHeader |
            UadpNetworkMessageContentMask.WriterGroupId |
            UadpNetworkMessageContentMask.PayloadHeader |
            UadpNetworkMessageContentMask.NetworkMessageNumber |
            UadpNetworkMessageContentMask.SequenceNumber;

        // No MajorVersion or MinorVersion: the Subscriber cannot compare versions.
        private static UadpDataSetMessageContentMask DataSetMessageContentMask =>
            UadpDataSetMessageContentMask.Status |
            UadpDataSetMessageContentMask.SequenceNumber;

        private static StaticSecurityKeyProvider CreateKeyProvider()
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            byte[] signing = new byte[policy.SigningKeyLength];
            byte[] encrypting = new byte[policy.EncryptingKeyLength];
            byte[] nonce = new byte[AesCtrNonceLayout.KeyNonceLength];
            for (int i = 0; i < signing.Length; i++)
            {
                signing[i] = (byte)(i + 1);
            }
            for (int i = 0; i < encrypting.Length; i++)
            {
                encrypting[i] = (byte)(i + 100);
            }
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)(i + 200);
            }

            var key = new PubSubSecurityKey(
                1U,
                ByteString.Create(signing),
                ByteString.Create(encrypting),
                ByteString.Create(nonce),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(60));
            var ring = new PubSubSecurityKeyRing(SecurityGroupId);
            ring.SetCurrent(key);
            return new StaticSecurityKeyProvider(SecurityGroupId, ring);
        }

        /// <summary>
        /// Produces an increasing counter and a toggle for each sample.
        /// </summary>
        private sealed class CounterSource : IPublishedDataSetSource
        {
            public DataSetMetaDataType BuildMetaData()
            {
                return new DataSetMetaDataType
                {
                    Name = DataSetName,
                    DataSetClassId = Uuid.Empty,
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "Counter",
                            BuiltInType = (byte)BuiltInType.Int32,
                            DataType = DataTypeIds.Int32,
                            ValueRank = ValueRanks.Scalar
                        },
                        new FieldMetaData
                        {
                            Name = "Toggle",
                            BuiltInType = (byte)BuiltInType.Boolean,
                            DataType = DataTypeIds.Boolean,
                            ValueRank = ValueRanks.Scalar
                        }
                    ],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                };
            }

            public ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                int counter = Interlocked.Increment(ref m_counter);
                ArrayOf<DataSetField> fields =
                [
                    new DataSetField { Name = "Counter", Value = new Variant(counter) },
                    new DataSetField { Name = "Toggle", Value = new Variant((counter & 1) == 0) }
                ];
                return new ValueTask<PublishedDataSetSnapshot>(
                    new PublishedDataSetSnapshot(
                        metaData.ConfigurationVersion ?? new ConfigurationVersionDataType(),
                        fields,
                        DateTimeUtc.From(DateTime.UtcNow)));
            }

            private int m_counter;
        }

        /// <summary>
        /// Completes with the fields of the first received DataSet.
        /// </summary>
        private sealed class FirstDataSetSink : ISubscribedDataSetSink
        {
            public Task<IReadOnlyList<DataSetField>> FirstDataSet => m_first.Task;

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                m_first.TrySetResult(fields);
                return default;
            }

            private readonly TaskCompletionSource<IReadOnlyList<DataSetField>> m_first =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Delivers every frame sent by any transport of the bus to every
        /// transport of the bus, like a datagram group on one host.
        /// </summary>
        private sealed class InMemoryBus
        {
            public void Attach(Channel<byte[]> receiver)
            {
                lock (m_lock)
                {
                    m_receivers.Add(receiver);
                }
            }

            public void Detach(Channel<byte[]> receiver)
            {
                lock (m_lock)
                {
                    m_receivers.Remove(receiver);
                }
            }

            public void Send(ReadOnlyMemory<byte> payload)
            {
                Channel<byte[]>[] receivers;
                lock (m_lock)
                {
                    receivers = [.. m_receivers];
                }
                foreach (Channel<byte[]> receiver in receivers)
                {
                    receiver.Writer.TryWrite(payload.ToArray());
                }
            }

            private readonly Lock m_lock = new();
            private readonly List<Channel<byte[]>> m_receivers = [];
        }

        private sealed class InMemoryBusTransportFactory : IPubSubTransportFactory
        {
            public InMemoryBusTransportFactory(InMemoryBus bus)
            {
                m_bus = bus;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                return new InMemoryBusTransport(m_bus);
            }

            private readonly InMemoryBus m_bus;
        }

        private sealed class InMemoryBusTransport : IPubSubTransport
        {
            public InMemoryBusTransport(InMemoryBus bus)
            {
                m_bus = bus;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected { get; private set; }

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                m_bus.Attach(m_received);
                IsConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                m_bus.Detach(m_received);
                IsConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                m_bus.Send(payload);
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (await m_received.Reader.WaitToReadAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    while (m_received.Reader.TryRead(out byte[]? frame))
                    {
                        yield return new PubSubTransportFrame(
                            frame, null, DateTimeUtc.From(DateTime.UtcNow));
                    }
                }
            }

            public ValueTask DisposeAsync()
            {
                m_bus.Detach(m_received);
                m_received.Writer.TryComplete();
                IsConnected = false;
                return default;
            }

            private readonly InMemoryBus m_bus;
            private readonly Channel<byte[]> m_received = Channel.CreateUnbounded<byte[]>();
        }
    }
}
