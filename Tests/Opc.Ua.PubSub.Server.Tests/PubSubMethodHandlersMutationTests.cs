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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    [TestFixture]
    [TestSpec("9.1.3.4", Summary = "AddConnection handler")]
    [TestSpec("9.1.3.5", Summary = "RemoveConnection handler")]
    [TestSpec("9.1.6", Summary = "Configuration methods")]
    public class PubSubMethodHandlersMutationTests
    {
        [Test]
        [TestSpec("9.1.3.4")]
        public void OnAddConnection_ValidInput_ReturnsGoodAndNodeId()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "handler-conn",
                TransportProfileUri =
                    "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var inputs = BuildArray(Variant.From(new ExtensionObject(connCfg)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddConnection(
                BuildContext(), method: null!, inputArguments: inputs, outputArguments: outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out NodeId id), Is.True);
                Assert.That(id.IsNull, Is.False);
            });
        }

        [Test]
        [TestSpec("9.1.3.5")]
        public void OnRemoveConnection_ValidInput_ReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "to-remove",
                TransportProfileUri =
                    "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var addInputs = BuildArray(Variant.From(new ExtensionObject(connCfg)));
            var addOutputs = new List<Variant>();
            handlers.OnAddConnection(
                BuildContext(), method: null!, inputArguments: addInputs, outputArguments: addOutputs);
            Assert.That(addOutputs[0].TryGetValue(out NodeId connId), Is.True);

            var removeInputs = BuildArray(Variant.From(connId));
            var removeOutputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveConnection(
                BuildContext(), method: null!, inputArguments: removeInputs,
                outputArguments: removeOutputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnGetConfiguration_ReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnGetConfiguration(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(1));
            });
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnSetConfiguration_ValidInput_ReturnsGood()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var cfg = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var inputs = BuildArray(Variant.From(new ExtensionObject(cfg)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnSetConfiguration(
                BuildContext(), method: null!, inputArguments: inputs, outputArguments: outputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddPublishedDataItems_ReturnsBadNotSupported()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddPublishedDataItems(
                BuildContext(), method: null!, inputArguments: default, outputArguments: outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        [TestSpec("9.1.5")]
        public void OnAddDataSetFolder_ReturnsGoodWithNodeId()
        {
            PubSubMethodHandlers handlers = CreateHandlers();
            var inputs = BuildArray(Variant.From("my-folder"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetFolder(
                BuildContext(), method: null!, inputArguments: inputs, outputArguments: outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs, Has.Count.EqualTo(1));
            });
        }

        private static PubSubMethodHandlers CreateHandlers()
        {
            IPubSubApplication app = new PubSubApplicationBuilder(
                    NUnitTelemetryContext.Create())
                .WithApplicationId("handler-mutation-tests")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
            var options = new PubSubServerOptions
            {
                ExposeConfigurationMethods = true
            };
            return new PubSubMethodHandlers(
                app, null, options, NUnitTelemetryContext.Create());
        }

        private static SystemContext BuildContext()
        {
            return new SystemContext(NUnitTelemetryContext.Create());
        }

        private static ArrayOf<Variant> BuildArray(params Variant[] values)
        {
            return new ArrayOf<Variant>(values);
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
                return AsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }
    }
}
