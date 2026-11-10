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
using System.Threading;
using System.Threading.Tasks;
using AmbSample.Client;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.Samples;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Exercises the AMB sample's connection builder with real domain validation and local service dispatch.
    /// </summary>
    [TestFixture]
    public sealed class AmbSampleSessionSecurityRegressionTests
    {
        /// <summary>
        /// Remote endpoint mappings do not change the default traversal policy.
        /// </summary>
        [Test]
        public void RemoteMappingsRequireExplicitTraversalOptIn()
        {
            AmbClientOptions options = AmbClientOptions.Parse(
                ["--remote-server", "urn:amb:remote=opc.tcp://localhost:62554/AmbServer"]);

            Assert.That(options.FollowRemoteSubAssets, Is.False);
            Assert.That(options.RemoteServers["urn:amb:remote"], Is.EqualTo("opc.tcp://localhost:62554/AmbServer"));
            Assert.That(AmbClientOptions.Parse(["--follow-remote"]).FollowRemoteSubAssets, Is.True);
        }

        /// <summary>
        /// Malformed server mappings are rejected before any connection is opened.
        /// </summary>
        [TestCase("missing-separator")]
        [TestCase("=opc.tcp://localhost:62554")]
        [TestCase("urn:amb:remote=")]
        [TestCase("relative=opc.tcp://localhost:62554")]
        [TestCase("urn:amb:remote=relative")]
        public void MalformedRemoteMappingIsRejected(string mapping)
        {
            Assert.Throws<ArgumentException>(() => AmbClientOptions.Parse(["--remote-server", mapping]));
        }

        /// <summary>
        /// A missing mapping is reported instead of guessing an endpoint or opening a connection.
        /// </summary>
        [Test]
        public async Task UnmappedRemoteServerIsRejectedAsync()
        {
            await using var resolver = new SampleRemoteSessionResolver(
                new AmbClientOptions(), Opc.Ua.Tests.NUnitTelemetryContext.Create());

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resolver.ResolveAsync("urn:amb:unmapped", CancellationToken.None).ConfigureAwait(false))
                ?? throw new AssertionException("Expected an unresolved-server failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServerUriInvalid));
        }

        /// <summary>
        /// A trusted certificate with a matching advertised URI still cannot identify the requested server.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF21AmbSampleRejectsTrustedWrongHost()
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: false);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            var options = new AmbClientOptions { ServerUrl = SampleSessionSecurityTestContext.RequestedUrl };

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ConnectAsync(fixture, options).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(fixture.Attempts, Is.Zero);
            Assert.That(fixture.Activations, Is.Zero);
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// A matching trusted hostname permits actual service data without adopting the advertised hostname.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF21AmbSampleBindsOriginalHost()
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: true);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            var options = new AmbClientOptions { ServerUrl = SampleSessionSecurityTestContext.RequestedUrl };

            await using ManagedSession session = await ConnectAsync(fixture, options).ConfigureAwait(false);
            await SampleSessionSecurityTestContext.AssertMarkerReadAsync(session).ConfigureAwait(false);

            Assert.That(fixture.Attempts, Is.EqualTo(1));
            Assert.That(fixture.Activations, Is.EqualTo(1));
            Assert.That(fixture.CheckDomain, Is.True);
            Assert.That(fixture.Identity!.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(fixture.Endpoint!.EndpointUrl!.IdnHost, Is.EqualTo("requested.example"));
            Assert.That(fixture.Endpoint.EndpointUrl.Port, Is.EqualTo(4840));
            Assert.That(fixture.Endpoint.Description.Server.DiscoveryUrls,
                Is.EqualTo([SampleSessionSecurityTestContext.RequestedUrl]));
            Assert.That(fixture.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://advertised.example:62553/AdvertisedPath"));
            Assert.That(fixture.Description.Server.DiscoveryUrls.Count, Is.EqualTo(1));
            Assert.That(fixture.Description.Server.DiscoveryUrls[0],
                Is.EqualTo("opc.tcp://other-trusted.example:62553/Discovery"));
        }

        /// <summary>
        /// Only the explicit demo option bypasses hostname errors, and a subsequent default connect rejects them.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF21InsecureDemoDoesNotDisableLaterHostnameChecks()
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: false);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            var insecure = new AmbClientOptions
            {
                ServerUrl = SampleSessionSecurityTestContext.RequestedUrl,
                Insecure = true
            };
            await using (ManagedSession session = await ConnectAsync(fixture, insecure).ConfigureAwait(false))
            {
                await SampleSessionSecurityTestContext.AssertMarkerReadAsync(session).ConfigureAwait(false);
            }
            int requestsBeforeDefaultConnect = fixture.Requests.Count;
            var secure = new AmbClientOptions { ServerUrl = SampleSessionSecurityTestContext.RequestedUrl };

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ConnectAsync(fixture, secure).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(fixture.Attempts, Is.EqualTo(1));
            Assert.That(fixture.Activations, Is.EqualTo(1));
            Assert.That(fixture.Requests, Has.Count.EqualTo(requestsBeforeDefaultConnect));
        }

        /// <summary>
        /// Discovery cannot replace the requested transport while retaining a matching trusted certificate.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF21AmbSampleRejectsDiscoveryTransportChanges()
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: true);
            fixture.Description.EndpointUrl = "https://advertised.example:443/";
            var options = new AmbClientOptions { ServerUrl = SampleSessionSecurityTestContext.RequestedUrl };

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ConnectAsync(fixture, options).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Attempts, Is.Zero);
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Runs the sample's real builder while replacing only network creation and retry delays.
        /// </summary>
        private static Task<ManagedSession> ConnectAsync(
            SampleSessionSecurityTestContext fixture,
            AmbClientOptions options)
        {
            return SampleSession.CreateSessionBuilder(
                    fixture.Configuration, fixture.Telemetry, options, fixture.Description)
                .UseSessionFactory(fixture.Factory)
                .WithReconnectPolicy(policy => policy with { MaxRetries = 0, JitterFactor = 0 })
                .ConnectAsync(CancellationToken.None);
        }
    }
}
