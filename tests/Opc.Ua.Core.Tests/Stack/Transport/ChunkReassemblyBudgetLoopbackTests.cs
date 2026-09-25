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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Loopback tests of the chunk reassembly budget across a real
    /// <see cref="TcpTransportListener"/> and UA-TCP client.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ChunkReassemblyBudgetLoopbackTests
    {
        [Test]
        [CancelAfter(15000)]
        public async Task ListenerSizesADefaultBudgetFromItsMaximumMessageSizeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            EndpointConfiguration configuration = CreateConfiguration(maxBufferSize: 65535);
            configuration.MaxMessageSize = 8 * 1024 * 1024;

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, configuration, budget: null),
                new RespondingCallback(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(listener.ChunkReassemblyBudget, Is.Not.Null);
            Assert.That(
                listener.ChunkReassemblyBudget.MaxBytes,
                Is.EqualTo(ChunkReassemblyBudget.GetDefaultMaxBytes(8 * 1024 * 1024)));
            await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(15000)]
        public async Task ListenersShareTheBudgetTheyAreGivenAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var budget = new ChunkReassemblyBudget(1024 * 1024);
            Uri firstUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            Uri secondUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            EndpointConfiguration configuration = CreateConfiguration(maxBufferSize: 65535);

            await using var first = new TcpTransportListener(telemetry);
            await using var second = new TcpTransportListener(telemetry);
            await first.OpenAsync(
                firstUrl,
                CreateListenerSettings(firstUrl, configuration, budget),
                new RespondingCallback(),
                CancellationToken.None).ConfigureAwait(false);
            await second.OpenAsync(
                secondUrl,
                CreateListenerSettings(secondUrl, configuration, budget),
                new RespondingCallback(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(first.ChunkReassemblyBudget, Is.SameAs(budget));
            Assert.That(second.ChunkReassemblyBudget, Is.SameAs(budget));
            await first.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await second.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A client that negotiates small chunks sends many of them, and each is
        /// charged the buffer it is received into. The buffers are sized to the
        /// negotiated chunk size, so the request fits a budget that buffers of
        /// the size the listener accepts connections with would overrun several
        /// times over; and once it is served nothing is left reserved.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ChunksOfARequestAreChargedAtTheirNegotiatedSizeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var budget = new ChunkReassemblyBudget(256 * 1024);
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            EndpointDescription endpoint = CreateEndpoint(endpointUrl);
            var callback = new RespondingCallback();

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, CreateConfiguration(maxBufferSize: 65535), budget),
                callback,
                CancellationToken.None).ConfigureAwait(false);

            using UaSCUaBinaryTransportChannel channel = await OpenClientAsync(
                telemetry, endpointUrl, endpoint).ConfigureAwait(false);

            IServiceResponse response = await channel.SendRequestAsync(
                CreateWriteRequest(50 * 1000),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<WriteResponse>());
            Assert.That(callback.RequestCount, Is.EqualTo(1));
            Assert.That(budget.ReservedBytes, Is.Zero);

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A request whose chunks do not fit in the budget is refused and its
        /// channel closed, without holding more than the budget meanwhile and
        /// without affecting other clients of the listener.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task RequestBeyondTheBudgetIsRefusedWithoutAffectingOtherClientsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var budget = new ChunkReassemblyBudget(256 * 1024);
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            EndpointDescription endpoint = CreateEndpoint(endpointUrl);
            var callback = new RespondingCallback();

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, CreateConfiguration(maxBufferSize: 65535), budget),
                callback,
                CancellationToken.None).ConfigureAwait(false);

            using (UaSCUaBinaryTransportChannel refused = await OpenClientAsync(
                telemetry, endpointUrl, endpoint).ConfigureAwait(false))
            {
                ServiceResultException error = await CaptureServiceResultExceptionAsync(
                    () => refused.SendRequestAsync(
                        CreateWriteRequest(400 * 1000),
                        CancellationToken.None).AsTask()).ConfigureAwait(false);

                // The status depends on whether the client reads the server's
                // Bad_TcpNotEnoughResources before it notices the connection
                // close; the unit tests of the channel pin the error itself.
                Assert.That(error, Is.Not.Null, "the oversized request was not refused.");
                Assert.That(callback.RequestCount, Is.Zero);
            }

            Assert.That(
                await WaitForAsync(() => budget.ReservedBytes == 0).ConfigureAwait(false),
                Is.True,
                "the refused request left bytes reserved.");

            using UaSCUaBinaryTransportChannel other = await OpenClientAsync(
                telemetry, endpointUrl, endpoint).ConfigureAwait(false);
            IServiceResponse response = await other.SendRequestAsync(
                CreateWriteRequest(50 * 1000),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<WriteResponse>());
            Assert.That(callback.RequestCount, Is.EqualTo(1));

            await other.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<UaSCUaBinaryTransportChannel> OpenClientAsync(
            ITelemetryContext telemetry,
            Uri endpointUrl,
            EndpointDescription endpoint)
        {
            var channel = new UaSCUaBinaryTransportChannel(new TcpByteTransportFactory(telemetry), telemetry)
            {
                OperationTimeout = 5000
            };

            try
            {
                // the client negotiates the smallest chunks the protocol allows.
                await channel.OpenAsync(
                    endpointUrl,
                    new TransportChannelSettings
                    {
                        Description = endpoint,
                        Configuration = CreateConfiguration(maxBufferSize: TcpMessageLimits.MinBufferSize),
                        NamespaceUris = new NamespaceTable(),
                        Factory = EncodeableFactory.Create()
                    },
                    CancellationToken.None).ConfigureAwait(false);
                return channel;
            }
            catch
            {
                channel.Dispose();
                throw;
            }
        }

        private static WriteRequest CreateWriteRequest(int payloadSize)
        {
            return new WriteRequest
            {
                RequestHeader = new RequestHeader { TimeoutHint = 5000 },
                NodesToWrite =
                [
                    new WriteValue
                    {
                        NodeId = new NodeId("payload", 1),
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(ByteString.From(new byte[payloadSize])))
                    }
                ]
            };
        }

        private static EndpointConfiguration CreateConfiguration(int maxBufferSize)
        {
            var configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            configuration.MaxMessageSize = 1024 * 1024;
            configuration.MaxBufferSize = maxBufferSize;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;
            return configuration;
        }

        private static TransportListenerSettings CreateListenerSettings(
            Uri endpointUrl,
            EndpointConfiguration configuration,
            ChunkReassemblyBudget budget)
        {
            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                .Returns((CertificateEntry)null);

            return new TransportListenerSettings
            {
                Descriptions = [CreateEndpoint(endpointUrl)],
                Configuration = configuration,
                ServerCertificates = certificateRegistry.Object,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 10,
                ChunkReassemblyBudget = budget
            };
        }

        private static EndpointDescription CreateEndpoint(Uri endpointUrl)
        {
            return new EndpointDescription
            {
                EndpointUrl = endpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };
        }

        private static async Task<ServiceResultException> CaptureServiceResultExceptionAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex;
            }

            return null;
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            return condition();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// Answers every request with an empty good response of its kind.
        /// </summary>
        private sealed class RespondingCallback : ITransportListenerCallback
        {
            public int RequestCount => Volatile.Read(ref m_requestCount);

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_requestCount);
                var header = new ResponseHeader { ServiceResult = StatusCodes.Good };
                IServiceResponse response = request is WriteRequest
                    ? new WriteResponse { ResponseHeader = header }
                    : new ReadResponse { ResponseHeader = header };
                return new ValueTask<IServiceResponse>(response);
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }

            private int m_requestCount;
        }
    }
}
