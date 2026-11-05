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
using NUnit.Framework;
using OnboardingRegistrar;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the connected pull sample's explicit one-device admission and unsupported-operation boundaries.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PullRegistrarPolicyTests
    {
        /// <summary>
        /// External issuer validation does not replace the sample's configured product and DCA approval.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalCertificateRequiresConfiguredProductAndApplicationUris(bool approved)
        {
            var policy = new PullRegistrarPolicy(kProduct, kApplication);
            ByteString certificate = ByteString.From(new byte[] { 1 });
            if (approved)
            {
                DeviceRegistrarExternalAuthenticationResult result = await policy.EvaluateAsync(
                    certificate, [kProduct, kApplication]).ConfigureAwait(false);
                Assert.That(result.ProductInstanceUri, Is.EqualTo(kProduct));
                Assert.That(result.Decision.Application.ApplicationUri, Is.EqualTo(kApplication));
                Assert.That(result.Decision.RequiresSoftwareUpdate, Is.False);
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await policy.EvaluateAsync(certificate, [kProduct, "urn:other:dca"]).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        /// <summary>
        /// Each decision supplies a detached client-only DCA record for the configured device and URI.
        /// </summary>
        [Test]
        public async Task ApprovedDeviceReceivesOnlyItsConfiguredDcaApplication()
        {
            var policy = new PullRegistrarPolicy(kProduct, kApplication);
            OnboardingIdentitySelection identity = Selection(kProduct);
            DeviceRegistrarOnboardingDecision first = await policy.EvaluateAsync(identity).ConfigureAwait(false);
            first.Application.ApplicationUri = "urn:modified";

            DeviceRegistrarOnboardingDecision second = await policy.EvaluateAsync(identity).ConfigureAwait(false);

            Assert.That(second.Application.ApplicationUri, Is.EqualTo(kApplication));
            Assert.That(second.Application.ApplicationType, Is.EqualTo(ApplicationType.Client));
            Assert.That(second.Application.ApplicationNames.Count, Is.EqualTo(1));
            Assert.That(second.Application.ApplicationNames[0].Text, Is.EqualTo("OnboardingClient"));
            Assert.That(second.Application.ProductUri,
                Is.EqualTo("uri:opcfoundation.org:UA-.NETStandard:OnboardingClient"));
            Assert.That(second.Application.DiscoveryUrls.IsEmpty, Is.True);
            Assert.That(second.Application.ServerCapabilities.IsEmpty, Is.True);
            Assert.That(second.RequiresSoftwareUpdate, Is.False);
            Assert.That(second.SoftwareUpdateManager, Is.Null);
        }

        /// <summary>
        /// A valid selection for a different product, including different URI casing, is not operator approval.
        /// </summary>
        [TestCase("urn:sample:other")]
        [TestCase("urn:sample:Device")]
        public async Task AnotherProductCannotBeAdmittedOrReadManagers(string product)
        {
            var policy = new PullRegistrarPolicy(kProduct, kApplication);
            ServiceResultException? admission = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await policy.EvaluateAsync(Selection(product)).ConfigureAwait(false));
            ServiceResultException? managers = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await policy.GetManagersAsync(product).ConfigureAwait(false));

            Assert.That(admission!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(managers!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Pull-only support cannot silently accept queued push work or authorize other device applications.
        /// </summary>
        [Test]
        public async Task PullOnlyModeRejectsPushAndManagedApplications()
        {
            var policy = new PullRegistrarPolicy(kProduct, kApplication);
            Assert.That(await policy.CanManageAsync(kProduct, new ApplicationRecordDataType(), string.Empty)
                .ConfigureAwait(false), Is.False);
            Assert.That((await policy.GetManagersAsync(kProduct).ConfigureAwait(false)).IsEmpty, Is.True);

            ServiceResultException? push = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await policy.EnqueueAsync(new ApplicationDescription()).ConfigureAwait(false));

            Assert.That(push!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Configuration rejects missing or relative URIs before a registrar is started.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("relative")]
        public void InvalidConfigurationIsRejected(string? invalid)
        {
            Assert.Throws<ArgumentException>(() => new PullRegistrarPolicy(invalid!, kApplication));
            Assert.Throws<ArgumentException>(() => new PullRegistrarPolicy(kProduct, invalid!));
        }

        /// <summary>
        /// Every asynchronous policy boundary observes cancellation before producing an admission decision.
        /// </summary>
        [Test]
        public async Task CancellationPreventsAllPolicyOperations()
        {
            var policy = new PullRegistrarPolicy(kProduct, kApplication);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await policy.EvaluateAsync(Selection(kProduct), cancellation.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await policy.CanManageAsync(kProduct, new ApplicationRecordDataType(), string.Empty, cancellation.Token)
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await policy.GetManagersAsync(kProduct, cancellation.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await policy.EnqueueAsync(new ApplicationDescription(), cancellation.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await policy.EvaluateAsync(null!).ConfigureAwait(false));
        }

        /// <summary>
        /// Represents identity selection already validated by the registrar's ticket and channel policy.
        /// </summary>
        private static OnboardingIdentitySelection Selection(string product)
        {
            return new OnboardingIdentitySelection(
                ByteString.From(new byte[] { 1 }), new DeviceIdentityTicketType(), product);
        }

        /// <summary>The explicitly admitted product instance.</summary>
        private const string kProduct = "urn:sample:device";

        /// <summary>The separately configured DCA application URI.</summary>
        private const string kApplication = "urn:sample:dca";
    }
}
