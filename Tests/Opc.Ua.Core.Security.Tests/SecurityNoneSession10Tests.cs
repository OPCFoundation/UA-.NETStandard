/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
                makeCert: (subject, uri) =>
                    TestCertificateFactory.CreateWrongHostnameAppInstanceCert(subject, uri));
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

                ISession session = null;
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
            return null;
        }
    }
}
