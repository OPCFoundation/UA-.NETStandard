// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Diagnostics
{
    [TestFixture]
    [TestSpec("9.1.11", Summary = "Per-component diagnostics")]
    public class PerComponentDiagnosticsTests
    {
        [Test]
        [TestSpec("9.1.11")]
        public async Task ConnectionHasOwnDiagnosticsInstance()
        {
            await using IPubSubApplication app = BuildAppWithConnection();
            var connection = (PubSubConnection)app.Connections[0];
            Assert.That(GetPrivateField<IPubSubDiagnostics>(connection, "m_diagnostics"), Is.Not.Null);
        }

        [Test]
        [TestSpec("9.1.11")]
        public async Task ReaderGroupHasOwnDiagnosticsInstance()
        {
            await using IPubSubApplication app = BuildAppWithReaderGroup();
            var group = (ReaderGroup)app.Connections[0].ReaderGroups[0];
            Assert.That(GetPrivateField<IPubSubDiagnostics?>(group, "m_diagnostics"), Is.Not.Null);
        }

        [Test]
        [TestSpec("9.1.11")]
        public async Task WriterGroupBuildsSuccessfully()
        {
            await using IPubSubApplication app = BuildAppWithWriterGroup();
            Assert.That(app.Connections[0].WriterGroups.Count, Is.EqualTo(1));
            Assert.That(app.Connections[0].WriterGroups[0].State, Is.Not.Null);
        }

        [Test]
        [TestSpec("9.1.11")]
        public async Task DataSetWriterBuildsSuccessfully()
        {
            await using IPubSubApplication app = BuildAppWithWriterGroup();
            var group = (WriterGroup)app.Connections[0].WriterGroups[0];
            Assert.That(((IDataSetWriter[]?)group.DataSetWriters) ?? [], Is.Empty);
        }

        [Test]
        [TestSpec("9.1.11")]
        public async Task DataSetReaderBuildsSuccessfully()
        {
            await using IPubSubApplication app = BuildAppWithReaderGroup();
            var group = (ReaderGroup)app.Connections[0].ReaderGroups[0];
            Assert.That(((IDataSetReader[]?)group.DataSetReaders) ?? [], Is.Empty);
        }

        [Test]
        [TestSpec("9.1.11")]
        public async Task ApplicationDiagnosticsIsNotNull()
        {
            await using IPubSubApplication app = BuildApp();
            Assert.That(app.Diagnostics, Is.Not.Null);
        }

        [Test]
        [TestSpec("9.1.11")]
        public async Task AggregatingDiagnosticsExposesLevel()
        {
            await using IPubSubApplication app = BuildApp();
            Assert.That(app.Diagnostics.Level, Is.Not.EqualTo((PubSubDiagnosticsLevel)255));
        }

        private static IPubSubApplication BuildApp()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("diag-test")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static IPubSubApplication BuildAppWithConnection()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("diag-conn")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "diag-test-conn",
                            TransportProfileUri =
                                "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
                        }
                    }),
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static IPubSubApplication BuildAppWithWriterGroup()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("diag-wg")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "wg-conn",
                            TransportProfileUri =
                                "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                            WriterGroups = new ArrayOf<WriterGroupDataType>(new[]
                            {
                                new WriterGroupDataType
                                {
                                    Name = "wg-1",
                                    WriterGroupId = 1,
                                    PublishingInterval = 1000
                                }
                            })
                        }
                    }),
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static IPubSubApplication BuildAppWithReaderGroup()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("diag-rg")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "rg-conn",
                            TransportProfileUri =
                                "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" }),
                            ReaderGroups = new ArrayOf<ReaderGroupDataType>(new[]
                            {
                                new ReaderGroupDataType
                                {
                                    Name = "rg-1"
                                }
                            })
                        }
                    }),
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static T? GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo? field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(instance) is T value ? value : default;
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
