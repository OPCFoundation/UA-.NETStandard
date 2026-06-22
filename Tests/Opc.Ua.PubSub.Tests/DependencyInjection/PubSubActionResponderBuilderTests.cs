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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Tests fluent and DI PubSub Action responder registration.
    /// </summary>
    [TestFixture]
    public class PubSubActionResponderBuilderTests
    {
        private const string ConnectionName = "loop";
        private const ushort DataSetWriterId = 77;
        private const ushort ActionTargetId = 12;

        [Test]
        public async Task AddActionResponderDelegateAnswersInvokeActionAsync()
        {
            var factory = new LoopbackTransportFactory();
            await using IPubSubApplication app = new PubSubApplicationBuilder(
                NUnitTelemetryContext.Create())
                .UseConfiguration(CreateConfiguration())
                .UseAllStandardEncoders()
                .AddTransportFactory(factory)
                .AddActionResponder(
                    CreateTarget(),
                    (invocation, cancellationToken) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return new ValueTask<PubSubActionHandlerResult>(CreateHandlerResult(invocation));
                    })
                .Build();

            await app.StartAsync().ConfigureAwait(false);
            PubSubActionResponse response = await InvokeAsync(app).ConfigureAwait(false);

            Assert.That(response.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.OutputFields, Has.Count.EqualTo(1));
            Assert.That(response.OutputFields[0].Value.TryGetValue(out int answer), Is.True);
            Assert.That(answer, Is.EqualTo(42));
        }

        [Test]
        public async Task AddPubSubActionResponderResolvedFromDiAnswersInvokeActionAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddSingleton<IPubSubTransportFactory>(new LoopbackTransportFactory());
            services.AddSingleton(new DiActionHandler());
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .UseConfiguration(CreateConfiguration())
                .AddActionResponder<DiActionHandler>(CreateTarget()));
            ServiceProvider sp = services.BuildServiceProvider();
            await using IPubSubApplication app = sp.GetRequiredService<IPubSubApplication>();

            await app.StartAsync().ConfigureAwait(false);
            PubSubActionResponse response = await InvokeAsync(app).ConfigureAwait(false);

            Assert.That(response.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.OutputFields, Has.Count.EqualTo(1));
            Assert.That(response.OutputFields[0].Value.TryGetValue(out int answer), Is.True);
            Assert.That(answer, Is.EqualTo(42));
        }

        private static PubSubConfigurationDataType CreateConfiguration()
        {
            return new PubSubConfigurationDataType
            {
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = ConnectionName,
                        TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                        PublisherId = new Variant(ConnectionName),
                        Address = new ExtensionObject(new NetworkAddressUrlDataType
                        {
                            Url = "opc.udp://239.0.0.1:49323"
                        })
                    }
                ],
                PublishedDataSets = []
            };
        }

        private static PubSubActionTarget CreateTarget()
        {
            return new PubSubActionTarget
            {
                ConnectionName = ConnectionName,
                DataSetWriterId = DataSetWriterId,
                ActionTargetId = ActionTargetId
            };
        }

        private static async ValueTask<PubSubActionResponse> InvokeAsync(IPubSubApplication app)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await app.InvokeActionAsync(
                new PubSubActionRequest
                {
                    Target = CreateTarget(),
                    InputFields =
                    [
                        new DataSetField
                        {
                            Name = "input",
                            Value = new Variant(21),
                            Encoding = PubSubFieldEncoding.Variant
                        }
                    ],
                    TimeoutHint = 5_000
                },
                TimeSpan.FromSeconds(2),
                cts.Token).ConfigureAwait(false);
        }

        private static PubSubActionHandlerResult CreateHandlerResult(PubSubActionInvocation invocation)
        {
            Assert.That(invocation.InputFields, Has.Count.EqualTo(1));
            Assert.That(invocation.InputFields[0].Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(21));
            return new PubSubActionHandlerResult
            {
                StatusCode = StatusCodes.Good,
                OutputFields =
                [
                    new DataSetField
                    {
                        Name = "answer",
                        Value = new Variant(value * 2),
                        Encoding = PubSubFieldEncoding.Variant
                    }
                ]
            };
        }

        private sealed class DiActionHandler : IPubSubActionHandler
        {
            public ValueTask<PubSubActionHandlerResult> HandleAsync(
                PubSubActionInvocation invocation,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<PubSubActionHandlerResult>(CreateHandlerResult(invocation));
            }
        }

        private sealed class LoopbackTransportFactory : IPubSubTransportFactory
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
                return new LoopbackTransport();
            }
        }

        private sealed class LoopbackTransport : IPubSubTransport
        {
            private readonly Lock m_gate = new();
            private readonly Queue<PubSubTransportFrame> m_frames = [];
            private readonly SemaphoreSlim m_signal = new(0);

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
                cancellationToken.ThrowIfCancellationRequested();
                IsConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                IsConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = topic;
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    m_frames.Enqueue(new PubSubTransportFrame(
                        payload,
                        topic: null,
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
                }
                m_signal.Release();
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await m_signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    PubSubTransportFrame frame;
                    lock (m_gate)
                    {
                        frame = m_frames.Dequeue();
                    }
                    yield return frame;
                }
            }

            public ValueTask DisposeAsync()
            {
                IsConnected = false;
                m_signal.Dispose();
                return default;
            }
        }
    }
}
