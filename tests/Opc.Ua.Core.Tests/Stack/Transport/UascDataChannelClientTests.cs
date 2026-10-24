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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies explicit selection and authenticated evidence at the public client boundary.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class UascDataChannelClientTests
    {
        [TestCase("/flow/v1/shared", DataChannelProfiles.DedicatedUasc)]
        [TestCase("/flow/v1/dedicated", DataChannelProfiles.SharedUasc)]
        [TestCase("/ordinary", DataChannelProfiles.SharedUasc)]
        [TestCase("/flow/v1/dedicated", Profiles.UaTcpTransport)]
        [TestCase("/flow/v1/shared/extra", DataChannelProfiles.SharedUasc)]
        [TestCase("/flow/v1/shared?fallback=true", DataChannelProfiles.SharedUasc)]
        public async Task MismatchedProfileAndResourceAreRejectedBeforeTransportCreation(
            string resource,
            string profile)
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840" + resource);
            TransportChannelSettings settings = CreateSettings(endpoint, profile);

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConnectAsync(endpoint, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            registry.VerifyNoOtherCalls();
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task UnprotectedOrImplicitIntegrityIsRejectedBeforeTransportCreation(MessageSecurityMode mode)
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/dedicated");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.DedicatedUasc);
            settings.Description!.SecurityMode = mode;

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConnectAsync(endpoint, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task MissingContextFactoryCannotOpenAFlowEndpoint()
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.DataChannels = null;

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConnectAsync(endpoint, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            registry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task AnOrdinaryRegisteredBindingIsNotTreatedAsAFlowTransport()
        {
            var registry = new Mock<ITransportBindingRegistry>();
            var ordinary = new Mock<ITransportChannel>();
            registry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(ordinary.Object);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(endpoint, CreateSettings(endpoint, DataChannelProfiles.SharedUasc))
                    .ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            ordinary.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        public async Task AnOpenedTransportWithoutAConfirmedContextIsNotReportedAsConnected()
        {
            var registry = new Mock<ITransportBindingRegistry>();
            var transport = new Mock<IDataChannelTransportChannel>();
            transport.Setup(value => value.OpenAsync(
                    It.IsAny<Uri>(), It.IsAny<TransportChannelSettings>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            registry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(transport.Object);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(endpoint, CreateSettings(endpoint, DataChannelProfiles.SharedUasc))
                    .ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            transport.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Rejects untrusted RHE substitution before creating or claiming a transport.
        /// </summary>
        [TestCase("urn:foreign:server", "opc.tcp://localhost:4840/flow/v1/shared")]
        [TestCase("", "opc.tcp://localhost:4840/flow/v1/shared")]
        [TestCase("urn:selected:server", "opc.tcp://foreign:4840/flow/v1/shared")]
        [TestCase("urn:selected:server", "opc.tcp://localhost:4841/flow/v1/shared")]
        public async Task ReverseHandoffMustMatchSelectedServerAndEndpoint(string serverUri, string url)
        {
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.Description!.Server.ApplicationUri = "urn:selected:server";
            var waiting = new Mock<ITransportWaitingConnection>(MockBehavior.Strict);
            waiting.SetupGet(value => value.EndpointUrl).Returns(new Uri(url));
            waiting.SetupGet(value => value.ServerUri).Returns(serverUri);

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(waiting.Object, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            registry.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A reverse open must confirm the same authenticated context and must not open a new outbound connection.
        /// </summary>
        [Test]
        public async Task ReverseHandoffWithoutConfirmedContextIsDisposedWithoutOutboundFallback()
        {
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.Description!.Server.ApplicationUri = "urn:selected:server";
            var waiting = new Mock<ITransportWaitingConnection>();
            waiting.SetupGet(value => value.EndpointUrl).Returns(endpoint);
            waiting.SetupGet(value => value.ServerUri).Returns("urn:selected:server");
            var transport = new Mock<IDataChannelTransportChannel>(MockBehavior.Strict);
            transport.Setup(value => value.OpenAsync(waiting.Object, settings, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            transport.SetupGet(value => value.DataChannels).Returns((DataChannelContext?)null);
            transport.Setup(value => value.Dispose());
            var registry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            registry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(transport.Object);
            var client = new UascDataChannelClient(registry.Object, NUnitTelemetryContext.Create());

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ConnectAsync(waiting.Object, settings).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            transport.Verify(value => value.OpenAsync(
                waiting.Object, settings, It.IsAny<CancellationToken>()), Times.Once);
            transport.VerifyGet(value => value.DataChannels, Times.Once);
            transport.Verify(value => value.Dispose(), Times.Once);
            transport.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DependencyInjectionKeepsTransportRegistriesIsolatedBetweenHosts(bool reverse)
        {
            var firstRegistry = new Mock<ITransportBindingRegistry>();
            var secondRegistry = new Mock<ITransportBindingRegistry>(MockBehavior.Strict);
            firstRegistry.Setup(value => value.CreateChannel(Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()))
                .Returns(Mock.Of<ITransportChannel>());
            var firstServices = new ServiceCollection();
            var secondServices = new ServiceCollection();
            firstServices.AddSingleton(firstRegistry.Object);
            secondServices.AddSingleton(secondRegistry.Object);
            firstServices.AddOpcUa().AddUascDataChannels();
            secondServices.AddOpcUa().AddUascDataChannels();
            using ServiceProvider first = firstServices.BuildServiceProvider();
            using ServiceProvider second = secondServices.BuildServiceProvider();
            IUascDataChannelClient firstClient = first.GetRequiredService<IUascDataChannelClient>();
            IUascDataChannelClient secondClient = second.GetRequiredService<IUascDataChannelClient>();
            var endpoint = new Uri("opc.tcp://localhost:4840/flow/v1/shared");
            TransportChannelSettings settings = CreateSettings(endpoint, DataChannelProfiles.SharedUasc);
            settings.Description!.Server.ApplicationUri = "urn:selected:server";
            var waiting = new Mock<ITransportWaitingConnection>();
            waiting.SetupGet(value => value.EndpointUrl).Returns(endpoint);
            waiting.SetupGet(value => value.ServerUri).Returns("urn:selected:server");

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (reverse)
                {
                    await firstClient.ConnectAsync(waiting.Object, settings).ConfigureAwait(false);
                }
                else
                {
                    await firstClient.ConnectAsync(endpoint, settings).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            Assert.That(firstClient, Is.Not.SameAs(secondClient));
            firstRegistry.Verify(value => value.CreateChannel(
                Utils.UriSchemeOpcTcp, It.IsAny<ITelemetryContext>()), Times.Once);
            secondRegistry.VerifyNoOtherCalls();
        }

        [Test]
        public async Task DisposalErrorsFromAnUndisposedTransportAreNotHidden()
        {
            var transport = new Mock<IDataChannelTransportChannel>();
            transport.SetupGet(value => value.IsDisposed).Returns(false);
            transport.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(new ObjectDisposedException("unrelated-component"))));
            await using var connection =
                new UascDataChannelConnection(transport.Object, DataChannelCarrierKind.SharedUasc);

            ObjectDisposedException failure = (await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await connection.CloseAsync().ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(failure.ObjectName, Is.EqualTo("unrelated-component"));
            transport.Verify(value => value.Dispose(), Times.Once);
        }

#if NET6_0_OR_GREATER
        [Test]
        public void KeyEvidenceHashesSubjectPublicKeyInfoRatherThanTheCertificate()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=FlowKeyEvidence").CreateForRSA();
            byte[] expected = SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());

            ByteString actual = DataChannelCertificateIdentity.GetKeyThumbprint(certificate);

            Assert.Multiple(() =>
            {
                Assert.That(actual.ToArray(), Is.EqualTo(expected));
                Assert.That(actual.ToArray(), Is.Not.EqualTo(SHA256.HashData(certificate.RawData)));
            });
        }
#endif

        private static TransportChannelSettings CreateSettings(Uri endpoint, string profile)
        {
            return new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = endpoint.AbsoluteUri,
                    TransportProfileUri = profile,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                },
                DataChannels = Mock.Of<IDataChannelContextFactory>()
            };
        }
    }
}
