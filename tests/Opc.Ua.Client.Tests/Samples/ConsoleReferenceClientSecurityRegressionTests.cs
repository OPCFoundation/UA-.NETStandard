/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Quickstarts;

namespace Opc.Ua.Client.Tests.Samples
{
    /// <summary>
    /// Exercises the reference client's actual direct and reverse session-establishment step.
    /// </summary>
    [TestFixture]
    public sealed class ConsoleReferenceClientSecurityRegressionTests
    {
        /// <summary>
        /// Trust and an internally consistent application URI cannot replace the operator's hostname.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF10ConsoleClientRejectsTrustedWrongHost(bool reverse, bool autoAccept)
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: false);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            using var client = new UAClient(fixture.Configuration, fixture.Telemetry, null)
            {
                AutoAccept = autoAccept
            };
            var provider = new Mock<IClientIdentityProvider>(MockBehavior.Strict);
            client.IdentityProvider = provider.Object;
            ITransportWaitingConnection? connection = reverse
                ? new Mock<ITransportWaitingConnection>().Object
                : null;

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.CreateSessionAsync(
                    SampleSessionSecurityTestContext.RequestedUrl, fixture.Description, connection, fixture.Factory,
                    CancellationToken.None)
                    .ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(fixture.Attempts, Is.Zero);
            Assert.That(fixture.Activations, Is.Zero);
            Assert.That(fixture.Requests, Is.Empty);
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The secure success path keeps the original host for both creation and later discovery refresh.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF10ConsoleClientBindsRequestedHostBeforeActivation(bool reverse)
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: true);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            using var client = new UAClient(fixture.Configuration, fixture.Telemetry, null);
            var identity = new UserIdentity("operator-marker", "credential-marker"u8.ToArray());
            var provider = new Mock<IClientIdentityProvider>();
            provider.SetupGet(value => value.ExpiresAt).Returns(DateTime.MaxValue);
            provider.Setup(value => value.CanSatisfyAsync(
                    It.IsAny<UserTokenPolicy>(), It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CanSatisfyResult.Yes);
            provider.Setup(value => value.GetIdentityAsync(
                    It.IsAny<UserTokenPolicy>(), It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(identity);
            client.IdentityProvider = provider.Object;
            ITransportWaitingConnection? connection = reverse
                ? new Mock<ITransportWaitingConnection>().Object
                : null;

            ISession session = await client.CreateSessionAsync(
                SampleSessionSecurityTestContext.RequestedUrl, fixture.Description, connection, fixture.Factory,
                CancellationToken.None)
                .ConfigureAwait(false);
            await SampleSessionSecurityTestContext.AssertMarkerReadAsync(session).ConfigureAwait(false);

            Assert.That(fixture.Attempts, Is.EqualTo(1));
            Assert.That(fixture.Activations, Is.EqualTo(1));
            Assert.That(fixture.Identity, Is.SameAs(identity));
            Assert.That(fixture.Connection, Is.SameAs(connection));
            Assert.That(fixture.CheckDomain, Is.True);
            Assert.That(fixture.UpdateBeforeConnect, Is.EqualTo(!reverse));
            Assert.That(fixture.Endpoint!.EndpointUrl!.IdnHost, Is.EqualTo("requested.example"));
            Assert.That(fixture.Endpoint.EndpointUrl.Port, Is.EqualTo(4840));
            Assert.That(fixture.Endpoint.Description.Server.DiscoveryUrls, Is.EqualTo(
                [SampleSessionSecurityTestContext.RequestedUrl]));
            Assert.That(fixture.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://advertised.example:62553/AdvertisedPath"));
            Assert.That(fixture.Description.Server.DiscoveryUrls.Count, Is.EqualTo(1));
            Assert.That(fixture.Description.Server.DiscoveryUrls[0],
                Is.EqualTo("opc.tcp://other-trusted.example:62553/Discovery"));
        }

        /// <summary>
        /// Discovery cannot substitute another transport protocol before identity acquisition.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF10ConsoleClientRejectsDiscoveryTransportChanges()
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: true);
            using var client = new UAClient(fixture.Configuration, fixture.Telemetry, null);
            fixture.Description.EndpointUrl = "https://advertised.example:443/";

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.CreateSessionAsync(
                    SampleSessionSecurityTestContext.RequestedUrl, fixture.Description, null, fixture.Factory,
                    CancellationToken.None)
                    .ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Attempts, Is.Zero);
            Assert.That(fixture.Requests, Is.Empty);
        }
    }
}
