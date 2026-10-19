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
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Lds.Server;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Lds.Tests
{
    [TestFixture]
    [Category("DiscoveryServices")]
    [Parallelizable]
    public sealed class RegistrationValidationTests : LdsTestFixture
    {
        private const string ServerUri = "urn:localhost:opcfoundation.org:RegistrationValidation";

        [Test]
        public void RegistrationAcceptsOneExactCertificateApplicationUri()
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void RegistrationRejectsMissingClientCertificate()
        {
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(null),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void RegistrationRejectsMalformedClientCertificate()
        {
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel([1, 2, 3, 4]),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public async Task RegisterServerRejectsMalformedSubjectAltNameWithoutMutatingStore()
        {
            using Certificate certificate = CreateCertificateWithMalformedSubjectAltName();

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Lds.RegisterServerAsync(
                    CreateChannel(certificate.RawData),
                    new RequestHeader(),
                    CreateRegisteredServer(ServerUri),
                    RequestLifetime.None).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(Lds.RegistrationStore.Snapshot(), Is.Empty);
        }

        [Test]
        public async Task RegisterServer2RejectsMissingCertificateWithoutMutatingStore()
        {
            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Lds.RegisterServer2Async(
                    CreateChannel(null),
                    new RequestHeader(),
                    CreateRegisteredServer(ServerUri),
                    default,
                    RequestLifetime.None).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(Lds.RegistrationStore.Snapshot(), Is.Empty);
        }

        [Test]
        public void RegistrationRejectsCertificateWithoutApplicationUri()
        {
            using Certificate certificate = CreateCertificate([]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        [Test]
        public void RegistrationRejectsSubjectAltNameWithoutApplicationUri()
        {
            using Certificate certificate = CreateCertificate([], includeSubjectAltName: true);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        [Test]
        public void RegistrationAcceptsExactApplicationUriAmongMultipleUris()
        {
            using Certificate certificate = CreateCertificate(["urn:test:other", ServerUri]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void RegistrationRejectsMultipleApplicationUrisWithoutExactMatch()
        {
            using Certificate certificate = CreateCertificate(
                ["urn:test:other", "urn:test:another"]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        [Test]
        public void RegistrationRequiresOrdinalApplicationUriMatch()
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            using var server = new TestLdsServer();

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData),
                CreateRegisteredServer(ServerUri.ToUpperInvariant()));

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        [Test]
        public async Task RegistrationEntryPointsRejectUnsafeSemaphorePaths(
            [ValueSource(typeof(SemaphorePathTests), nameof(SemaphorePathTests.UnsafePaths))] string path,
            [Values] bool registerServer2,
            [Values] bool isOnline)
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            RegisteredServer registration = CreateRegisteredServer(ServerUri);
            registration.SemaphoreFilePath = path;
            registration.IsOnline = isOnline;

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (registerServer2)
                {
                    await Lds.RegisterServer2Async(
                        CreateChannel(certificate.RawData, IPAddress.Loopback),
                        new RequestHeader(),
                        registration,
                        [new ExtensionObject(new MdnsDiscoveryConfiguration
                        {
                            MdnsServerName = "unsafe",
                            ServerCapabilities = ["DA"]
                        })],
                        RequestLifetime.None).ConfigureAwait(false);
                }
                else
                {
                    await Lds.RegisterServerAsync(
                        CreateChannel(certificate.RawData, IPAddress.Loopback),
                        new RequestHeader(),
                        registration,
                        RequestLifetime.None).ConfigureAwait(false);
                }
            });

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(Lds.Store.Snapshot(), Is.Empty);
            Assert.That(Lds.Store.SnapshotNetworkRecords(), Is.Empty);
        }

        [Test]
        public async Task RegistrationEntryPointsRequireObservedLoopbackForSemaphores(
            [Values(null, "192.0.2.1", "2001:db8::1", "::ffff:192.0.2.1")] string? peer,
            [Values] bool registerServer2)
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            RegisteredServer registration = CreateRegisteredServer(ServerUri);
            registration.SemaphoreFilePath = Path.GetFullPath("local.sem");
            SecureChannelContext channel = CreateChannel(
                certificate.RawData, peer == null ? null : IPAddress.Parse(peer));

            ServiceResultException exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (registerServer2)
                {
                    await Lds.RegisterServer2Async(channel, new RequestHeader(), registration,
                        default, RequestLifetime.None).ConfigureAwait(false);
                }
                else
                {
                    await Lds.RegisterServerAsync(channel, new RequestHeader(), registration,
                        RequestLifetime.None).ConfigureAwait(false);
                }
            });

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(Lds.Store.Snapshot(), Is.Empty);
        }

        [Test]
        public async Task RegistrationEntryPointsAllowRemotePeersWithoutSemaphores(
            [Values(null, "192.0.2.1")] string? peer,
            [Values] bool registerServer2)
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            SecureChannelContext channel = CreateChannel(
                certificate.RawData, peer == null ? null : IPAddress.Parse(peer));
            RegisteredServer registration = CreateRegisteredServer(ServerUri);
            if (registerServer2)
            {
                RegisterServer2Response response = await Lds.RegisterServer2Async(
                    channel, new RequestHeader(), registration, default, RequestLifetime.None).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            }
            else
            {
                RegisterServerResponse response = await Lds.RegisterServerAsync(
                    channel, new RequestHeader(), registration, RequestLifetime.None).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            }

            Assert.That(Lds.Store.Snapshot(), Has.Count.EqualTo(1));
        }

        [TestCase("127.0.0.1")]
        [TestCase("::1")]
        [TestCase("::ffff:127.0.0.1")]
        public void RegistrationAcceptsNativeSemaphorePathFromLoopback(string peer)
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            using var server = new TestLdsServer();
            RegisteredServer registration = CreateRegisteredServer(ServerUri);
            registration.SemaphoreFilePath = Path.GetFullPath("local.sem");

            ServiceResult result = server.Validate(
                CreateChannel(certificate.RawData, IPAddress.Parse(peer)), registration);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task RegistrationEntryPointsPreserveLocalSemaphoreLivenessAsync([Values] bool registerServer2)
        {
            using Certificate certificate = CreateCertificate([ServerUri]);
            string path = Path.GetFullPath("local.sem");
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(f => f.Exists(path, false)).Returns(true);
            using var store = new RegisteredServerStore(null, null, fileSystem.Object);
            using var lds = new LdsServer(Telemetry, store);
            RegisteredServer registration = CreateRegisteredServer(ServerUri);
            registration.SemaphoreFilePath = path;
            SecureChannelContext channel = CreateChannel(certificate.RawData, IPAddress.Loopback);

            if (registerServer2)
            {
                await lds.RegisterServer2Async(channel, new RequestHeader(), registration,
                    default, RequestLifetime.None).ConfigureAwait(false);
            }
            else
            {
                await lds.RegisterServerAsync(channel, new RequestHeader(), registration,
                    RequestLifetime.None).ConfigureAwait(false);
            }

            Assert.That(store.Snapshot(), Has.Count.EqualTo(1));
            Assert.That(store.Snapshot()[0].SemaphoreFilePath, Is.EqualTo(path));
            fileSystem.Setup(f => f.Exists(path, false)).Returns(false);
            store.Prune(store.Snapshot()[0].LastSeenUtc);
            Assert.That(store.Snapshot(), Is.Empty);
            fileSystem.Verify(f => f.Exists(path, false), Times.Exactly(2));
            fileSystem.VerifyNoOtherCalls();
        }

        private static Certificate CreateCertificate(
            string[] applicationUris,
            bool includeSubjectAltName = false)
        {
            ICertificateBuilder builder = CreateCertificateBuilder();
            if (applicationUris.Length > 0 || includeSubjectAltName)
            {
                builder = builder.AddExtension(
                    new X509SubjectAltNameExtension(applicationUris, ["localhost"]));
            }

            return builder.SetRSAKeySize(2048).CreateForRSA();
        }

        private static Certificate CreateCertificateWithMalformedSubjectAltName()
        {
            return CreateCertificateBuilder()
                .AddExtension(
                    new X509Extension(
                        X509SubjectAltNameExtension.SubjectAltName2Oid,
                        [0x30, 0x01, 0x86],
                        critical: false))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static ICertificateBuilder CreateCertificateBuilder()
        {
            return CertificateBuilder
                .Create("CN=RegistrationValidation")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(30));
        }

        private static SecureChannelContext CreateChannel(byte[]? clientCertificate, IPAddress? peerAddress = null)
        {
            return new SecureChannelContext(
                "registration-validation",
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt
                },
                RequestEncoding.Binary,
                clientCertificate,
                peerAddress: peerAddress);
        }

        private static RegisteredServer CreateRegisteredServer(string serverUri)
        {
            return new RegisteredServer
            {
                ServerUri = serverUri,
                ProductUri = "urn:test:product",
                ServerNames = [new LocalizedText("en-US", "Registration Validation")],
                ServerType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://localhost:4840"],
                IsOnline = true
            };
        }

        private sealed class TestLdsServer : LdsServer
        {
            public ServiceResult Validate(
                SecureChannelContext secureChannelContext,
                RegisteredServer registeredServer)
            {
                return ValidateRegistration(secureChannelContext, registeredServer);
            }
        }
    }
}
