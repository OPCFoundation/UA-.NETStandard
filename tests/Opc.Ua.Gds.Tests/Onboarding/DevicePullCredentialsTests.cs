/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingDevice;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies that the device sample borrows client sessions and routes installation through its security owner.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullCredentialsTests
    {
        /// <summary>
        /// Identity and CSR operations reuse local providers, but only the device security owner installs material.
        /// </summary>
        [Test]
        public async Task InstallUsesDeviceLifecycleAndRetainsBorrowedSession()
        {
            await using var context = new SessionRequestContextTestContext();
            using var cancellation = new CancellationTokenSource();
            CancellationToken ct = cancellation.Token;
            var identity = new Mock<IPullDcaIdentityProvider>(MockBehavior.Strict);
            var signing = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            ByteString selected = ByteString.From(new byte[] { 1, 2 });
            ByteString request = ByteString.From(new byte[] { 3, 4 });
            var applicationId = new NodeId("device", 1);
            var profile = new PullDcaCertificateProfile(new NodeId("group", 1),
                Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var credentials = new PullDcaCredentials(ByteString.From(new byte[] { 5, 6 }), [],
                new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All });
            identity.Setup(value => value.ActivateAsync(selected, ct)).Returns(default(ValueTask));
            signing.Setup(value => value.CreateSigningRequestAsync(applicationId, profile, ct))
                .Returns(new ValueTask<ByteString>(request));
            security.Setup(value => value.InstallFromPullAsync(
                    context.ManagedSession, "urn:registrar:expected",
                    It.Is<ProvisionableDeviceCredentials>(material =>
                        material.Certificate == credentials.Certificate &&
                        material.TrustList == credentials.TrustList), ct))
                .Returns(default(ValueTask));
            int connections = 0;
            var provider = new DevicePullCredentials(identity.Object, signing.Object, security.Object, token =>
            {
                Assert.That(token, Is.EqualTo(ct));
                connections++;
                return Task.FromResult(context.ManagedSession);
            }, "urn:registrar:expected");

            await provider.ActivateAsync(selected, ct).ConfigureAwait(false);
            ByteString csr = await provider.CreateSigningRequestAsync(applicationId, profile, ct)
                .ConfigureAwait(false);
            await provider.InstallAsync(applicationId, credentials, ct).ConfigureAwait(false);

            Assert.That(csr, Is.EqualTo(request));
            Assert.That(connections, Is.EqualTo(1));
            Assert.That(context.ManagedSession.Connected, Is.True);
            identity.VerifyAll();
            signing.VerifyAll();
            security.VerifyAll();
            signing.Verify(value => value.InstallAsync(
                It.IsAny<NodeId>(), It.IsAny<PullDcaCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Missing, failed or mismatched CSR admission cannot open a registrar session or install credentials.
        /// </summary>
        [TestCase("missing")]
        [TestCase("failed")]
        [TestCase("different")]
        public async Task InstallRejectsApplicationWithoutSuccessfulMatchingRequest(string failure)
        {
            var signing = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            var applicationId = new NodeId("device", 1);
            var profile = new PullDcaCertificateProfile(new NodeId("group", 1),
                Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var expected = new InvalidOperationException("The CSR could not be created.");
            var provider = new DevicePullCredentials(Mock.Of<IPullDcaIdentityProvider>(),
                signing.Object, security.Object, _ => throw new AssertionException("Connection was not admitted."),
                "urn:registrar:expected");
            if (failure == "failed")
            {
                signing.Setup(value => value.CreateSigningRequestAsync(applicationId, profile, CancellationToken.None))
                    .Throws(expected);
                InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await provider.CreateSigningRequestAsync(applicationId, profile).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(error, Is.SameAs(expected));
            }
            else if (failure == "different")
            {
                signing.Setup(value => value.CreateSigningRequestAsync(applicationId, profile, CancellationToken.None))
                    .Returns(new ValueTask<ByteString>(ByteString.From(new byte[] { 1 })));
                await provider.CreateSigningRequestAsync(applicationId, profile).ConfigureAwait(false);
                applicationId = new NodeId("other-device", 1);
            }
            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.InstallAsync(applicationId, new PullDcaCredentials(default, [], new TrustListDataType()))
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            security.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A device installation failure remains observable instead of invoking the standalone store writer.
        /// </summary>
        [Test]
        public async Task InstallPreservesDeviceFailureWithoutFallback()
        {
            await using var context = new SessionRequestContextTestContext();
            var signing = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            var applicationId = new NodeId("device", 1);
            var profile = new PullDcaCertificateProfile(new NodeId("group", 1),
                Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var expected = new InvalidOperationException("The device installation remains pending.");
            signing.Setup(value => value.CreateSigningRequestAsync(applicationId, profile, CancellationToken.None))
                .Returns(new ValueTask<ByteString>(ByteString.From(new byte[] { 1 })));
            security.Setup(value => value.InstallFromPullAsync(context.ManagedSession, "urn:registrar:expected",
                    It.IsAny<ProvisionableDeviceCredentials>(), CancellationToken.None))
                .Throws(expected);
            var provider = new DevicePullCredentials(Mock.Of<IPullDcaIdentityProvider>(),
                signing.Object, security.Object, _ => Task.FromResult(context.ManagedSession),
                "urn:registrar:expected");
            await provider.CreateSigningRequestAsync(applicationId, profile).ConfigureAwait(false);

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.InstallAsync(applicationId, new PullDcaCredentials(default, [], new TrustListDataType()))
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error, Is.SameAs(expected));
            Assert.That(context.ManagedSession.Connected, Is.True);
            signing.Verify(value => value.InstallAsync(
                It.IsAny<NodeId>(), It.IsAny<PullDcaCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
