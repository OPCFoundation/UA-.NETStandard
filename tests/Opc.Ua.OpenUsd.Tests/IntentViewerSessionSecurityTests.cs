// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET10_0_OR_GREATER
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntentViewerClient;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.Samples;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Exercises the viewer's actual managed connection and recovery creation paths.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class IntentViewerSessionSecurityTests
    {
        [Test]
        public async Task SecurityR13ViewerRejectsTrustedWrongHostBeforeActivationAsync()
        {
            await using var fixture = new SampleSessionSecurityTestContext(matchingHost: false, trustPeerRoot: true);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            var options = new IntentViewerOptions { ServerUrl = SampleSessionSecurityTestContext.RequestedUrl };

            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await SampleSession.ConnectAsync(options, fixture.Telemetry, fixture.Configuration,
                    fixture.Description, fixture.Factory, CancellationToken.None).ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(fixture.Activations, Is.Zero);
            Assert.That(fixture.Attempts, Is.Zero);
            Assert.That(fixture.Requests, Is.Empty);
        }

        [TestCase("requested.example", false)]
        [TestCase("127.0.0.1", false)]
        [TestCase("localhost", false)]
        [TestCase("requested.example", true)]
        public async Task SecurityR13ViewerAcceptsConfiguredDnsAndIpIdentityAsync(string host, bool insecure)
        {
            await using var fixture = new SampleSessionSecurityTestContext(
                matchingHost: true, requestedHost: host, trustPeerRoot: true);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            var options = new IntentViewerOptions
            {
                ServerUrl = "opc.tcp://" + host + ":4840/RequestedPath",
                Insecure = insecure
            };
            await using SampleSession sample = await SampleSession.ConnectAsync(
                options, fixture.Telemetry, fixture.Configuration, fixture.Description, fixture.Factory,
                CancellationToken.None).ConfigureAwait(false);
            await SampleSessionSecurityTestContext.AssertMarkerReadAsync(sample.Session).ConfigureAwait(false);

            Assert.That(fixture.Activations, Is.EqualTo(1));
            Assert.That(fixture.Endpoint!.EndpointUrl!.IdnHost, Is.EqualTo(host));
            Assert.That(fixture.Endpoint.Description.Server.DiscoveryUrls, Is.EqualTo([options.ServerUrl]));
            Assert.That(fixture.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://advertised.example:62553/AdvertisedPath"));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task SecurityR13ViewerReconnectRetainsHostnameValidationAsync(bool wrongHost, bool insecure)
        {
            await using var fixture = new SampleSessionSecurityTestContext(
                matchingHost: !wrongHost, trustPeerRoot: true);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            var options = new IntentViewerOptions
            {
                ServerUrl = wrongHost
                    ? "opc.tcp://other-trusted.example:4840/RequestedPath"
                    : SampleSessionSecurityTestContext.RequestedUrl,
                Insecure = insecure
            };
            await using SampleSession sample = await SampleSession.ConnectAsync(
                options, fixture.Telemetry, fixture.Configuration, fixture.Description, fixture.Factory,
                CancellationToken.None).ConfigureAwait(false);
            var reconnectEndpoint = new ConfiguredEndpoint(null, CoreUtils.Clone(fixture.Description)!,
                EndpointConfiguration.Create(fixture.Configuration));
            reconnectEndpoint.Description.EndpointUrl = SampleSessionSecurityTestContext.RequestedUrl;
            reconnectEndpoint.Description.Server.DiscoveryUrls = [SampleSessionSecurityTestContext.RequestedUrl];
            var managed = (ManagedSession)sample.Session;

            // This is the recovery's full creation step (also used when no inner session remains).
            // Invoke it directly so failures cannot trigger unbounded timer retries or network discovery.
            var reconnect = (Task<ServiceResult>)typeof(ManagedSession)
                .GetMethod("HandleConnectEndpointAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(managed, [reconnectEndpoint, null, CancellationToken.None])!;
            ServiceResult result = await reconnect.ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(wrongHost
                ? StatusCodes.BadCertificateHostNameInvalid : StatusCodes.Good));
            Assert.That(fixture.Activations, Is.EqualTo(wrongHost ? 1 : 2));
            if (!wrongHost)
            {
                await SampleSessionSecurityTestContext.AssertMarkerReadAsync(sample.Session).ConfigureAwait(false);
            }
        }
    }
}
#endif
