// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for Security None CreateSession ActivateSession 1.0.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    [Category("SecurityNone")]
    public class SecurityNoneSession10Tests : TestFixture
    {
        [Test]
        public Task NoneSession001ClientSpecifiesCertWithNoSecurity()
        {
            // With SecurityMode.None, client may still specify a certificate
            // The fixture session uses None security
            Assert.That(Session.Connected, Is.True);
            Assert.That(
                Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None),
                "Session should use None security mode.");
            return Task.CompletedTask;
        }

        [Test]
        public Task NoneSession002ClientSpecifiesExpiredCertAsync()
        {
            return AssertNoneChannelAcceptsCertAsync(
                slug: "expired-none",
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateExpiredAppInstanceCert(subject, uri));
        }

        [Test]
        public Task NoneSession003ClientSpecifiesCertForAnotherComputerAsync()
        {
            return AssertNoneChannelAcceptsCertAsync(
                slug: "wrong-host-none",
                makeCert: TestCertificateFactory.CreateWrongHostnameAppInstanceCert);
        }

        [Test]
        public Task NoneSession004ClientSpecifiesCorruptedCertAsync()
        {
            return AssertNoneChannelAcceptsCertAsync(
                slug: "corrupted-none",
                makeCert: (subject, uri) =>
                {
                    using Certificate valid =
                        TestCertificateFactory.CreateValidAppInstanceCert(subject, uri);
                    return TestCertificateFactory.CorruptCertSignature(valid);
                });
        }

        private async Task AssertNoneChannelAcceptsCertAsync(
            string slug,
            Func<string, string, Certificate> makeCert)
        {
            string subject = "CN=" + slug + ", O=OPC Foundation";
            string appUri = $"urn:localhost:opcfoundation.org:NoneSessionTest:{slug}:{Guid.NewGuid():N}";
            Certificate cert = makeCert(subject, appUri);

            // Per Part 4 §5.4.2.2 a SecurityMode.None channel does
            // not transmit/validate the client application instance
            // certificate, so a flawed client cert must NOT prevent
            // session establishment. Either Connected=true or any
            // failure unrelated to the cert (e.g. socket reset) is
            // acceptable.
            CertSessionContext ctx = await CertSessionContext.CreateAsync(
                cert, appUri, Telemetry).ConfigureAwait(false);
            await using (ctx.ConfigureAwait(false))
            {
                EndpointDescription noneEndpoint = await GetNoneEndpointAsync().ConfigureAwait(false);
                if (noneEndpoint == null)
                {
                    Assert.Ignore("Server does not expose a SecurityMode.None endpoint.");
                }

                var endpointConfig = EndpointConfiguration.Create(ctx.ClientConfig);
                endpointConfig.OperationTimeout = 10000;
                var configured = new ConfiguredEndpoint(null, noneEndpoint, endpointConfig);

                ISession? session = null;
                try
                {
                    session = await ctx.OpenSessionAsync(configured, Telemetry).ConfigureAwait(false);
                    Assert.That(session.Connected, Is.True);
                }
                finally
                {
                    if (session != null)
                    {
                        try
                        {
                            await session.CloseAsync(5000, true, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            // best effort
                        }
                        session.Dispose();
                    }
                }
            }
        }

        private async Task<EndpointDescription> GetNoneEndpointAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient dc = await DiscoveryClient.CreateAsync(
                ServerUrl, endpointConfiguration, Telemetry, ct: CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<EndpointDescription> eps = await dc.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
            foreach (EndpointDescription ep in eps)
            {
                if (ep.SecurityMode == MessageSecurityMode.None)
                {
                    return ep;
                }
            }
            return null!;
        }
    }
}
