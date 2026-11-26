// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    public sealed partial class RealHttpsListenerIntegrationTests
    {
        /// <summary>
        /// A request and a response larger than the negotiated buffer size travel as
        /// several chunks over an opcua+uacp WebSocket, one chunk per WebSocket message.
        /// The listener used to send all chunks of a response as one message, which
        /// failed before any byte went out once the message exceeded MaxBufferSize.
        /// </summary>
        [TestCase(70 * 1024)]
        [TestCase(512 * 1024)]
        [CancelAfter(60000)]
        public async Task WssBinaryChannelExchangesMessagesLargerThanOneChunkAsync(
            int size,
            CancellationToken ct)
        {
            const int maxMessageSize = 4 * 1024 * 1024;
            const int maxBufferSize = 65535;

            Certificate certificate = CreateServerCertificate();
            using var certificateRegistry = new InMemoryCertificateRegistry(certificate);
            certificate.Dispose();
            var callback = new SizedReadResponseCallback(size);

            (HttpsTransportListener listener, int port) = await OpenListenerOnFreePortAsync(
                () => (HttpsTransportListener)new HttpsTransportListenerFactory().Create(m_telemetry!),
                p =>
                {
                    TransportListenerSettings settings = CreateListenerSettings(certificateRegistry, p);
                    ConfigureLargeMessages(settings.Configuration!);
                    return settings;
                },
                callback).ConfigureAwait(false);
            try
            {
                await WaitForListenerReadyAsync(port).ConfigureAwait(false);
                var endpointUrl = new Uri($"wss://127.0.0.1:{port}/");
                using CertificateEntry entry = certificateRegistry
                    .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.None)!;
                var messageContext = ServiceMessageContext.Create(m_telemetry);
                var configuration = EndpointConfiguration.Create();
                ConfigureLargeMessages(configuration);
                var settings = new TransportChannelSettings
                {
                    Description = new EndpointDescription
                    {
                        EndpointUrl = endpointUrl.AbsoluteUri,
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = Profiles.UaWssTransport,
                        ServerCertificate = entry.Certificate.RawData.ToByteString()
                    },
                    Configuration = configuration,
                    CertificateValidator = new AcceptAllCertificateValidator(),
                    Factory = messageContext.Factory,
                    NamespaceUris = messageContext.NamespaceUris
                };

                using var channel = new WssTransportChannel(m_telemetry!);
                await ((ISecureChannel)channel).OpenAsync(endpointUrl, settings, ct).ConfigureAwait(false);

                // a request whose NodeIds alone exceed one chunk
                int nodeCount = (size / 128) + 1;
                var nodesToRead = new ReadValueId[nodeCount];
                for (int ii = 0; ii < nodeCount; ii++)
                {
                    nodesToRead[ii] = new ReadValueId
                    {
                        NodeId = new NodeId(new string('n', 120) + ii, 2),
                        AttributeId = Attributes.Value
                    };
                }
                var request = new ReadRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 1, TimeoutHint = 60000 },
                    TimestampsToReturn = TimestampsToReturn.Neither,
                    NodesToRead = nodesToRead
                };

                var response = (ReadResponse)await channel.SendRequestAsync(request, ct).ConfigureAwait(false);

                Assert.That(callback.LastNodeCount, Is.EqualTo(nodeCount));
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(response.Results[0].WrappedValue.TryGetValue(out ByteString payload), Is.True);
                Assert.That(payload.Length, Is.EqualTo(size));

                await channel.CloseAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await listener.DisposeAsync().ConfigureAwait(false);
            }

            static void ConfigureLargeMessages(EndpointConfiguration configuration)
            {
                configuration.MaxMessageSize = maxMessageSize;
                configuration.MaxBufferSize = maxBufferSize;
                configuration.MaxByteStringLength = maxMessageSize;
                configuration.MaxArrayLength = maxMessageSize;
                configuration.OperationTimeout = 60000;
            }
        }

        /// <summary>
        /// Answers every Read with one ByteString of the given size.
        /// </summary>
        private sealed class SizedReadResponseCallback : ITransportListenerCallback
        {
            public SizedReadResponseCallback(int responseBytes)
            {
                byte[] payload = new byte[responseBytes];
                for (int ii = 0; ii < payload.Length; ii++)
                {
                    payload[ii] = (byte)ii;
                }
                m_payload = ByteString.From(payload);
            }

            public int LastNodeCount => Volatile.Read(ref m_lastNodeCount);

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                if (request is ReadRequest read)
                {
                    Volatile.Write(ref m_lastNodeCount, read.NodesToRead.Count);
                }
                return new ValueTask<IServiceResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                        ServiceResult = StatusCodes.Good
                    },
                    Results = [new DataValue(new Variant(m_payload))],
                    DiagnosticInfos = []
                });
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

            private readonly ByteString m_payload;
            private int m_lastNodeCount;
        }
    }
}
