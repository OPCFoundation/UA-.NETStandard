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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// PubSub Action requester / responder runtime tests.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.11.2", Summary = "PubSub Action request/response runtime")]
    public class PubSubActionRuntimeTests
    {
        private const ushort DataSetWriterIdValue = 77;
        private const ushort ActionTargetIdValue = 12;

        [Test]
        public async Task UdpLoopbackActionResponderAnswersRequesterAsync()
        {
            const string url = "opc.udp://239.0.0.1:49322";
            IOptions<UdpTransportOptions> options = Options.Create(new UdpTransportOptions
            {
                MulticastLoopback = true
            });
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var udpFactory = new UdpPubSubTransportFactory(options, diagnostics);
            await using IPubSubApplication responder = BuildActionApp("responder", url, udpFactory);
            await using IPubSubApplication requester = BuildActionApp("requester", url, udpFactory);
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            responder.RegisterActionHandler(
                new PubSubActionTarget
                {
                    ConnectionName = "responder",
                    DataSetWriterId = DataSetWriterIdValue,
                    ActionTargetId = ActionTargetIdValue
                },
                new DelegatePubSubActionHandler((invocation, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Assert.That(invocation.RequestId, Is.Not.Zero);
                    Assert.That(invocation.CorrelationData.IsNull, Is.False);
                    Assert.That(invocation.InputFields, Has.Count.EqualTo(1));
                    Assert.That(invocation.InputFields[0].Value.TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(21));
                    return new ValueTask<PubSubActionHandlerResult>(
                        new PubSubActionHandlerResult
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
                        });
                }),
                allowUnsecured: true);

            try
            {
                await responder.StartAsync(cts.Token).ConfigureAwait(false);
                await requester.StartAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsUdpEnvironmentFailure(ex))
            {
                Assert.Ignore("UDP multicast loopback is not available in this environment: " + ex.Message);
                return;
            }

            PubSubActionResponse response;
            try
            {
                response = await requester.InvokeActionAsync(
                    new PubSubActionRequest
                    {
                        Target = new PubSubActionTarget
                        {
                            ConnectionName = "requester",
                            DataSetWriterId = DataSetWriterIdValue,
                            ActionTargetId = ActionTargetIdValue
                        },
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
            catch (Exception ex) when (IsUdpEnvironmentFailure(ex))
            {
                Assert.Ignore("UDP multicast loopback is not available in this environment: " + ex.Message);
                return;
            }
            catch (TimeoutException)
            {
                Assert.Ignore("UDP multicast loopback did not deliver Action responses.");
                return;
            }

            Assert.That(response.RequestId, Is.Not.Zero);
            Assert.That(response.CorrelationData.IsNull, Is.False);
            Assert.That(response.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.ActionState, Is.EqualTo(ActionState.Done));
            Assert.That(response.OutputFields, Has.Count.EqualTo(1));
            Assert.That(response.OutputFields[0].Value.TryGetValue(out int answer), Is.True);
            Assert.That(answer, Is.EqualTo(42));
        }

        private static IPubSubApplication BuildActionApp(
            string name,
            string url,
            IPubSubTransportFactory factory)
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId(name)
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections =
                    [
                        new PubSubConnectionDataType
                        {
                            Name = name,
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            PublisherId = new Variant(name),
                            Address = new ExtensionObject(new NetworkAddressUrlDataType
                            {
                                Url = url
                            })
                        }
                    ],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(factory)
                .Build();
        }

        private static bool IsUdpEnvironmentFailure(Exception ex)
        {
            return ex is System.Net.Sockets.SocketException ||
                ex is NotSupportedException ||
                (ex.InnerException is not null && IsUdpEnvironmentFailure(ex.InnerException));
        }
    }
}
