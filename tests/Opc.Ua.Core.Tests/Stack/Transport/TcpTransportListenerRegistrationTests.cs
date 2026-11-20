/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
    /// Channels registered outside the accept path (reverse hello, a rejected
    /// handoff) are only registered while the listener is open, so none
    /// slips in after Dispose drained the channels and is never disposed.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class TcpTransportListenerRegistrationTests
    {
        [Test]
        [CancelAfter(15000)]
        public async Task RegistrationIsRefusedForATakenIdAndAfterDisposeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("registration-test", 8192, telemetry);
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(telemetry));
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");

            var listener = new TcpTransportListener(telemetry);
            using var registered = new TcpServerChannel(
                "registration-test", listener, buffers, quotas, null!, [], telemetry);
            using var late = new TcpServerChannel(
                "registration-test", listener, buffers, quotas, null!, [], telemetry);
            try
            {
                await listener.OpenAsync(
                    endpointUrl,
                    CreateListenerSettings(endpointUrl),
                    new Mock<ITransportListenerCallback>().Object,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(listener.TryRegisterChannel(4711, registered), Is.True);
                Assert.That(listener.TryRegisterChannel(4711, late), Is.False, "the id is taken");
            }
            finally
            {
                await listener.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(listener.TryRegisterChannel(4712, late), Is.False, "the listener is disposed");
        }

        private static TransportListenerSettings CreateListenerSettings(Uri endpointUrl)
        {
            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                .Returns((CertificateEntry?)null);

            var configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            return new TransportListenerSettings
            {
                Descriptions =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = endpointUrl.ToString(),
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = Profiles.UaTcpTransport
                    }
                ],
                Configuration = configuration,
                ServerCertificates = certificateRegistry.Object,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 10
            };
        }

        private static int GetFreeTcpPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }
}
