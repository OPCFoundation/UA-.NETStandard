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

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using OnboardingDevice;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies startup admission and explicit operator inputs before the device opens an outbound session.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullTaskTests
    {
        /// <summary>
        /// Installed state skips issuance; an existing registrar transition cannot be replaced by outbound startup.
        /// </summary>
        [TestCase(ProvisionableDeviceSecurityStage.Installed)]
        [TestCase(ProvisionableDeviceSecurityStage.Configuring)]
        [TestCase(ProvisionableDeviceSecurityStage.Installing)]
        [TestCase(ProvisionableDeviceSecurityStage.Resetting)]
        public async Task StartupDoesNotConnectWhenDeviceIsNotUnconfigured(ProvisionableDeviceSecurityStage stage)
        {
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(value => value.GetStageAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ProvisionableDeviceSecurityStage>(stage));
            var registrars = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            DevicePullTask task = CreateTask(security.Object, registrars.Object);
            if (stage == ProvisionableDeviceSecurityStage.Installed)
            {
                await task.OnServerStartedAsync(Mock.Of<IServerContext>()).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await task.OnServerStartedAsync(Mock.Of<IServerContext>()).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            registrars.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An operator endpoint absent from persisted candidates is rejected before session creation.
        /// </summary>
        [Test]
        public async Task StartupRejectsMissingCandidateBeforeConnection()
        {
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(value => value.GetStageAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ProvisionableDeviceSecurityStage>(
                    ProvisionableDeviceSecurityStage.Unconfigured));
            var registrars = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            registrars.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ApplicationDescription>>(ArrayOf<ApplicationDescription>.Empty));
            DevicePullTask task = CreateTask(security.Object, registrars.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await task.OnServerStartedAsync(Mock.Of<IServerContext>()).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            registrars.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An admitted connection that fails channel verification is retired before startup returns.
        /// </summary>
        [Test]
        public async Task StartupRetiresSessionWhenChannelAdmissionFails()
        {
            await using var context = new SessionRequestContextTestContext();
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            security.Setup(value => value.GetStageAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ProvisionableDeviceSecurityStage>(
                    ProvisionableDeviceSecurityStage.Unconfigured));
            var registrars = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            var candidate = new ApplicationDescription
            {
                ApplicationUri = "urn:registrar",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://localhost:62560/Registrar"]
            };
            registrars.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ApplicationDescription>>([candidate]));
            int created = 0;
            var sessions = new DevicePullSession(_ =>
            {
                created++;
                return Task.FromResult(context.ManagedSession);
            });
            DevicePullTask task = CreateTask(security.Object, registrars.Object, sessions);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await task.OnServerStartedAsync(Mock.Of<IServerContext>()).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(created, Is.EqualTo(1));
            Assert.That(context.ManagedSession.Disposed, Is.True);
            Assert.That(context.Requests.FindAll(static request => request is CloseSessionRequest),
                Has.Count.EqualTo(1));
            ServiceResultException? unavailable = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sessions.GetCurrentAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(unavailable!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Both endpoint and expected identity are mandatory, encrypted UA-TCP selection excludes URL credentials,
        /// and reset cannot be combined with immediate reprovisioning.
        /// </summary>
        [TestCase("opc.tcp://localhost:62560/Registrar", null, false)]
        [TestCase(null, "urn:registrar", false)]
        [TestCase("", "urn:registrar", false)]
        [TestCase("https://localhost:62560/Registrar", "urn:registrar", false)]
        [TestCase("opc.tcp://localhost:0/Registrar", "urn:registrar", false)]
        [TestCase("opc.tcp://user:password@localhost:62560/Registrar", "urn:registrar", false)]
        [TestCase("opc.tcp://localhost:62560/Registrar?mode=none", "urn:registrar", false)]
        [TestCase("opc.tcp://localhost:62560/Registrar", "relative", false)]
        [TestCase("opc.tcp://localhost:62560/Registrar", "urn:registrar", true)]
        public void OptionsRejectIncompleteOrConflictingPullSelection(string? endpoint, string? application, bool reset)
        {
            Dictionary<string, string?> values = Inputs();
            values["pullRegistrarEndpoint"] = endpoint;
            values["pullRegistrarApplicationUri"] = application;
            values["physicalReset"] = reset.ToString();

            Assert.Throws<ArgumentException>(() =>
                new DeviceSampleOptions(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        }

        /// <summary>
        /// Outbound pull is disabled by default and enabled only with the explicit endpoint and identity pair.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OptionsPreserveExplicitPullSelection(bool enabled)
        {
            Dictionary<string, string?> values = Inputs();
            if (enabled)
            {
                values["pullRegistrarEndpoint"] = "opc.tcp://localhost:62560/Registrar";
                values["pullRegistrarApplicationUri"] = "urn:registrar";
            }
            var options = new DeviceSampleOptions(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

            Assert.That(options.PullRegistrarEndpoint,
                Is.EqualTo(enabled ? new Uri("opc.tcp://localhost:62560/Registrar") : null));
            Assert.That(options.PullRegistrarApplicationUri, Is.EqualTo(enabled ? "urn:registrar" : null));
        }

        /// <summary>
        /// Constructs startup dependencies whose unused operations reject accidental outbound work.
        /// </summary>
        private static DevicePullTask CreateTask(
            IProvisionableDeviceSecurity security,
            IProvisionableDeviceRegistrarStore registrars,
            DevicePullSession? sessions = null)
        {
            var identity = new Mock<IPullDcaIdentityProvider>(MockBehavior.Strict);
            var credentials = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            var workflow = new PullDcaWorkflow(new PullDcaOptions(),
                (_, _) => throw new AssertionException("Workflow connection was not admitted."),
                identity.Object, credentials.Object, NUnitTelemetryContext.Create());
            Dictionary<string, string?> values = Inputs();
            values["pullRegistrarEndpoint"] = "opc.tcp://localhost:62560/Registrar";
            values["pullRegistrarApplicationUri"] = "urn:registrar";
            return new DevicePullTask(
                new DeviceSampleOptions(new ConfigurationBuilder().AddInMemoryCollection(values).Build()),
                security, registrars, Mock.Of<IProvisionableDeviceRuntime>(),
                Mock.Of<IOpcUaApplicationConfigurationProvider>(), workflow,
                sessions ?? new DevicePullSession(
                    _ => throw new AssertionException("Session connection was not admitted.")));
        }

        /// <summary>
        /// Supplies valid absolute sample paths without reading or creating any files.
        /// </summary>
        private static Dictionary<string, string?> Inputs()
        {
            string root = TestContext.CurrentContext.WorkDirectory;
            return new Dictionary<string, string?>
            {
                ["pkiRoot"] = Path.Combine(root, "sample-pki"),
                ["applicationUri"] = "urn:device",
                ["productUri"] = "urn:product",
                ["productInstanceUri"] = "urn:product:instance",
                ["subjectName"] = "CN=Device",
                ["factoryCertificate"] = Path.Combine(root, "identity.der"),
                ["ticketFiles"] = Path.Combine(root, "ticket.json"),
                ["endpointUrl"] = "opc.tcp://localhost:62561/Device"
            };
        }
    }
}
#endif
