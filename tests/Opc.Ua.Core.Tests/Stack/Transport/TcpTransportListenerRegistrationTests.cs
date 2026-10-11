/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 *
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
