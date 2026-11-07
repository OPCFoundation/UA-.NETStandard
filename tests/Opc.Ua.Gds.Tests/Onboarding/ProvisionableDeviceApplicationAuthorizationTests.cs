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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies independent device trust, software approval and application policy checks.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ProvisionableDeviceApplicationAuthorizationTests
    {
        private static readonly string[] s_expectedAuthorizationEvents =
        [
            "registrar", "software", "application", "registrar", "software", "application"
        ];

        /// <summary>
        /// Neither unconfigured nor interrupted device transitions can authorize independent application access.
        /// </summary>
        [TestCase(ProvisionableDeviceSecurityStage.Unconfigured)]
        [TestCase(ProvisionableDeviceSecurityStage.Installing)]
        [TestCase(ProvisionableDeviceSecurityStage.Configuring)]
        [TestCase(ProvisionableDeviceSecurityStage.Resetting)]
        public async Task ApplicationAccessRequiresInstalledDcaCredentials(ProvisionableDeviceSecurityStage stage)
        {
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(s => s.CreateApplicationOperations())
                .Returns(Mock.Of<IApplicationConfigurationPushOperations>());
            var rejection = new ServiceResultException(StatusCodes.BadInvalidState, stage.ToString());
            security.Setup(s => s.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Throws(rejection);
            var software = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            var authorization = new ProvisionableDeviceApplicationAuthorization("urn:application", security.Object,
                software.Object);

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.AuthorizeAsync(Mock.Of<ISystemContext>()).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(failure, Is.SameAs(rejection));
            security.Verify(s => s.RequireRegistrarAsync(It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
            software.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A registrar rejected by installed DCA trust never reaches software or application authorization.
        /// </summary>
        [Test]
        public async Task InstalledDeviceStillRequiresRegistrarTrust()
        {
            var security = InstalledSecurity();
            security.Setup(s => s.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadCertificateUntrusted));
            var software = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            var application = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
            var authorization = new ProvisionableDeviceApplicationAuthorization("urn:application", security.Object,
                software.Object, application.Object);

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.AuthorizeAsync(Mock.Of<ISystemContext>()).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            software.VerifyNoOtherCalls();
            application.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Device approval does not imply software approval for each independent application.
        /// </summary>
        [Test]
        public async Task InstalledDeviceDoesNotBypassPerApplicationSoftwareApproval()
        {
            var security = InstalledSecurity();
            var software = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            software.Setup(s => s.RequireReadyAsync("urn:application", It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadInvalidState));
            var application = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
            var authorization = new ProvisionableDeviceApplicationAuthorization("urn:application", security.Object,
                software.Object, application.Object);

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.AuthorizeAsync(Mock.Of<ISystemContext>()).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            security.Verify(s => s.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
            software.Verify(s => s.RequireReadyAsync("urn:application", It.IsAny<CancellationToken>()), Times.Once);
            application.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Every access reevaluates persisted DCA trust, exact application software readiness and target policy.
        /// </summary>
        [Test]
        public async Task ApprovedApplicationChecksDeviceSoftwareAndApplicationPolicyInOrder()
        {
            var events = new List<string>();
            var security = InstalledSecurity();
            security.Setup(s => s.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("registrar"))
                .Returns(default(ValueTask));
            var software = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            software.Setup(s => s.RequireReadyAsync("urn:application", It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("software"))
                .Returns(default(ValueTask));
            var application = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
            application.Setup(a => a.AuthorizeAsync(It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("application"))
                .Returns(default(ValueTask));
            var authorization = new ProvisionableDeviceApplicationAuthorization("urn:application", security.Object,
                software.Object, application.Object);

            await authorization.AuthorizeAsync(Mock.Of<ISystemContext>()).ConfigureAwait(false);
            await authorization.AuthorizeAsync(Mock.Of<ISystemContext>()).ConfigureAwait(false);

            Assert.That(events, Is.EqualTo(s_expectedAuthorizationEvents));
        }

        /// <summary>
        /// A deployment without a required software update still requires installed-device registrar trust.
        /// </summary>
        [Test]
        public async Task NoRequiredSoftwareUpdateDoesNotRemoveDeviceTrustGate()
        {
            var security = InstalledSecurity();
            var authorization = new ProvisionableDeviceApplicationAuthorization("urn:application", security.Object);

            await authorization.AuthorizeAsync(Mock.Of<ISystemContext>()).ConfigureAwait(false);

            security.Verify(s => s.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Typed calls and deferred effects use the same deployment provider with their original cancellation.
        /// </summary>
        [Test]
        public async Task ApplicationOperationsPreserveDeploymentAdmissionAndCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            var operations = new Mock<IApplicationConfigurationPushOperations>(MockBehavior.Strict);
            int calls = 0;
            Func<ValueTask<int>> call = () => new ValueTask<int>(++calls);
            Func<ValueTask> effect = () =>
            {
                calls++;
                return default;
            };
            operations.Setup(value => value.ExecuteAsync(call, cancellation.Token))
                .Returns((Func<ValueTask<int>> action, CancellationToken _) => action());
            operations.Setup(value => value.ExecuteAsync(effect, cancellation.Token))
                .Returns((Func<ValueTask> action, CancellationToken _) => action());
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(value => value.CreateApplicationOperations()).Returns(operations.Object);
            var authorization = new ProvisionableDeviceApplicationAuthorization("urn:application", security.Object);

            int result = await authorization.ExecuteAsync(call, cancellation.Token).ConfigureAwait(false);
            await authorization.ExecuteAsync(effect, cancellation.Token).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(2));
            security.Verify(value => value.CreateApplicationOperations(), Times.Once);
            operations.Verify(value => value.ExecuteAsync(call, cancellation.Token), Times.Once);
            operations.Verify(value => value.ExecuteAsync(effect, cancellation.Token), Times.Once);
            security.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A revoked deployment rejects a deferred effect before invoking the target runtime.
        /// </summary>
        [Test]
        public async Task RejectedDeploymentDoesNotInvokeApplicationEffect()
        {
            bool invoked = false;
            Func<ValueTask> effect = () =>
            {
                invoked = true;
                return default;
            };
            var expected = new ServiceResultException(StatusCodes.BadInvalidState);
            var operations = new Mock<IApplicationConfigurationPushOperations>(MockBehavior.Strict);
            operations.Setup(value => value.ExecuteAsync(effect, It.IsAny<CancellationToken>())).Throws(expected);
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(value => value.CreateApplicationOperations()).Returns(operations.Object);
            var authorization = new ProvisionableDeviceApplicationAuthorization("urn:application", security.Object);

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.ExecuteAsync(effect).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(invoked, Is.False);
            security.Verify(value => value.CreateApplicationOperations(), Times.Once);
            operations.Verify(value => value.ExecuteAsync(effect, It.IsAny<CancellationToken>()), Times.Once);
            operations.VerifyNoOtherCalls();
            security.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The fluent registration uses the supplied runtime provider without constructing replacement security.
        /// </summary>
        [Test]
        public void FluentRegistrationPreservesProviderAndRejectsAmbiguousOwnership()
        {
            var services = new ServiceCollection();
            var provider = Mock.Of<IManagedApplicationPushProvider>();
            var builder = services.AddOpcUa().AddGdsServer(_ => { });
            builder.AddManagedApplicationPush(_ => provider);
            Assert.Throws<InvalidOperationException>(() => builder.AddManagedApplicationPush(_ => provider));
            using ServiceProvider container = services.BuildServiceProvider();

            Assert.That(container.GetRequiredService<IManagedApplicationPushProvider>(), Is.SameAs(provider));
            Assert.That(container.GetService<IProvisionableDeviceSoftwareUpdatePolicy>(), Is.Null);
        }

        /// <summary>
        /// Returns a device whose installed-state and registrar checks are explicitly configured.
        /// </summary>
        private static Mock<IProvisionableDeviceSecurity> InstalledSecurity()
        {
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(s => s.CreateApplicationOperations())
                .Returns(Mock.Of<IApplicationConfigurationPushOperations>());
            security.Setup(s => s.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            return security;
        }
    }
}
