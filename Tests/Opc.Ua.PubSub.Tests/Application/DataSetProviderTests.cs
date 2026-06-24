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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Tests for runtime mutable PubSub data-set source and sink providers.
    /// </summary>
    [TestFixture]
    public sealed class DataSetProviderTests
    {
        [Test]
        public void MutableDataSetSourceProviderReturnsRegisteredSourceByName()
        {
            var provider = new MutableDataSetSourceProvider();
            IPublishedDataSetSource source = CreateSource(11);

            provider.Register("pds", source);

            Assert.That(provider.TryGetSource("pds", out IPublishedDataSetSource resolved), Is.True);
            Assert.That(resolved, Is.SameAs(source));
        }

        [Test]
        public void MutableDataSetSinkProviderReturnsRegisteredSinkByName()
        {
            var provider = new MutableDataSetSinkProvider();
            var sink = new Mock<ISubscribedDataSetSink>().Object;

            provider.Register("reader", sink);

            Assert.That(provider.TryGetSink("reader", out ISubscribedDataSetSink resolved), Is.True);
            Assert.That(resolved, Is.SameAs(sink));
        }

        [Test]
        public async Task ReplaceConfigurationAsyncUsesSourceRegisteredInProviderForNewPublishedDataSet()
        {
            var provider = new MutableDataSetSourceProvider();
            IPublishedDataSetSource source = CreateSource(21);
            await using IPubSubApplication app = CreateApplication(
                CreateConfiguration("initial"),
                provider,
                null);

            provider.Register("dynamic", source);
            await app.ReplaceConfigurationAsync(CreateConfiguration("dynamic"));

            PublishedDataSetSnapshot snapshot = await SampleFirstWriterAsync(app);
            Assert.That(snapshot.MetaDataVersion.MajorVersion, Is.EqualTo(21));
        }

        [Test]
        public async Task RemoveFromProviderFallsBackToEmptyPublishedDataSetSource()
        {
            var provider = new MutableDataSetSourceProvider();
            provider.Register("dynamic", CreateSource(31));
            await using IPubSubApplication app = CreateApplication(
                CreateConfiguration("dynamic"),
                provider,
                null);

            Assert.That(provider.Remove("dynamic"), Is.True);
            await app.ReplaceConfigurationAsync(CreateConfiguration("dynamic"));

            PublishedDataSetSnapshot snapshot = await SampleFirstWriterAsync(app);
            Assert.That(snapshot.MetaDataVersion.MajorVersion, Is.Zero);
        }

        [Test]
        public async Task BuildTimeDictionarySourceTakesPrecedenceOverProviderSource()
        {
            var provider = new MutableDataSetSourceProvider();
            provider.Register("pds", CreateSource(41));
            IPublishedDataSetSource buildTimeSource = CreateSource(42);
            await using IPubSubApplication app = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("provider-tests")
                .WithDataSetSourceProvider(provider)
                .UseConfiguration(CreateConfiguration("pds"))
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .AddDataSetSource("pds", buildTimeSource)
                .Build();

            PublishedDataSetSnapshot snapshot = await SampleFirstWriterAsync(app);
            Assert.That(snapshot.MetaDataVersion.MajorVersion, Is.EqualTo(42));
        }

        [Test]
        public async Task DataSetReaderUsesSinkRegisteredInProvider()
        {
            var provider = new MutableDataSetSinkProvider();
            var sink = new Mock<ISubscribedDataSetSink>().Object;
            provider.Register("reader", sink);
            await using IPubSubApplication app = CreateApplication(
                CreateSubscriberConfiguration("reader"),
                null,
                provider);

            IDataSetReader reader = app.Connections[0].ReaderGroups[0].DataSetReaders[0];

            Assert.That(reader.Sink, Is.SameAs(sink));
        }

        [Test]
        public async Task BuildTimeDictionarySinkTakesPrecedenceOverProviderSink()
        {
            var provider = new MutableDataSetSinkProvider();
            var providerSink = new Mock<ISubscribedDataSetSink>().Object;
            var buildTimeSink = new Mock<ISubscribedDataSetSink>().Object;
            provider.Register("reader", providerSink);
            await using IPubSubApplication app = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("provider-tests")
                .WithDataSetSinkProvider(provider)
                .UseConfiguration(CreateSubscriberConfiguration("reader"))
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .AddSubscribedDataSetSink("reader", buildTimeSink)
                .Build();

            IDataSetReader reader = app.Connections[0].ReaderGroups[0].DataSetReaders[0];

            Assert.That(reader.Sink, Is.SameAs(buildTimeSink));
        }

        private static IPubSubApplication CreateApplication(
            PubSubConfigurationDataType configuration,
            IDataSetSourceProvider? sourceProvider,
            IDataSetSinkProvider? sinkProvider)
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("provider-tests")
                .UseConfiguration(configuration)
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory());

            if (sourceProvider is not null)
            {
                builder.WithDataSetSourceProvider(sourceProvider);
            }

            if (sinkProvider is not null)
            {
                builder.WithDataSetSinkProvider(sinkProvider);
            }

            return builder.Build();
        }

        private static ValueTask<PublishedDataSetSnapshot> SampleFirstWriterAsync(
            IPubSubApplication app)
        {
            IDataSetWriter writer = app.Connections[0].WriterGroups[0].DataSetWriters[0];
            return writer.PublishedDataSet.SampleAsync();
        }

        private static IPublishedDataSetSource CreateSource(uint majorVersion)
        {
            var source = new Mock<IPublishedDataSetSource>();
            source
                .Setup(s => s.BuildMetaData())
                .Returns(new DataSetMetaDataType
                {
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = majorVersion,
                        MinorVersion = 1
                    }
                });
            source
                .Setup(s => s.SampleAsync(
                    It.IsAny<DataSetMetaDataType>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DataSetMetaDataType metaData, CancellationToken _) =>
                    new PublishedDataSetSnapshot(
                        metaData.ConfigurationVersion ?? new ConfigurationVersionDataType(),
                        [],
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
            return source.Object;
        }

        private static PubSubConfigurationDataType CreateConfiguration(string publishedDataSetName)
        {
            return new PubSubConfigurationDataType
            {
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(new[]
                {
                    new PublishedDataSetDataType
                    {
                        Name = publishedDataSetName
                    }
                }),
                Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                {
                    new PubSubConnectionDataType
                    {
                        Name = "connection",
                        TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                        Address = new ExtensionObject(new NetworkAddressUrlDataType
                        {
                            Url = "opc.udp://224.0.0.22:4840"
                        }),
                        WriterGroups = new ArrayOf<WriterGroupDataType>(new[]
                        {
                            new WriterGroupDataType
                            {
                                Name = "writer-group",
                                WriterGroupId = 1,
                                PublishingInterval = 1000,
                                DataSetWriters = new ArrayOf<DataSetWriterDataType>(new[]
                                {
                                    new DataSetWriterDataType
                                    {
                                        Name = "writer",
                                        DataSetName = publishedDataSetName,
                                        DataSetWriterId = 1
                                    }
                                })
                            }
                        })
                    }
                })
            };
        }

        private static PubSubConfigurationDataType CreateSubscriberConfiguration(string dataSetReaderName)
        {
            return new PubSubConfigurationDataType
            {
                PublishedDataSets = [],
                Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                {
                    new PubSubConnectionDataType
                    {
                        Name = "connection",
                        TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                        Address = new ExtensionObject(new NetworkAddressUrlDataType
                        {
                            Url = "opc.udp://224.0.0.22:4840"
                        }),
                        ReaderGroups = new ArrayOf<ReaderGroupDataType>(new[]
                        {
                            new ReaderGroupDataType
                            {
                                Name = "reader-group",
                                SecurityMode = MessageSecurityMode.None,
                                DataSetReaders = new ArrayOf<DataSetReaderDataType>(new[]
                                {
                                    new DataSetReaderDataType
                                    {
                                        Name = dataSetReaderName,
                                        DataSetWriterId = 1,
                                        MessageReceiveTimeout = 1000.0,
                                        SecurityMode = MessageSecurityMode.None,
                                        SubscribedDataSet = new ExtensionObject(
                                            new TargetVariablesDataType())
                                    }
                                })
                            }
                        })
                    }
                })
            };
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                return new StubTransport();
            }
        }

        private sealed class StubTransport : IPubSubTransport
        {
            private bool m_isConnected;

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = payload;
                _ = topic;
                _ = cancellationToken;
                return default;
            }

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                return TestAsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }
    }
}
