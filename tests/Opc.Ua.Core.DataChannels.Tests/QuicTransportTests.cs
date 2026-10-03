#if NET9_0_OR_GREATER
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
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Checks the parts of the QUIC transport that need no network: the
    /// url scheme, the ALPN identifier and the rule that fallback shall
    /// not be a downgrade.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class QuicTransportTests
    {
        [Test]
        public void TheProvisionalAlpnIdentifierIsOffered()
        {
            Assert.That(
                QuicTransport.ApplicationProtocol.ToString(),
                Is.EqualTo(DataChannelConstants.QuicAlpnProtocol));
        }

        [Test]
        public void TheUrlSchemeAndDefaultPortMatchTheErrata()
        {
            Uri url = QuicTransport.CreateUrl("server.example");

            Assert.Multiple(() =>
            {
                Assert.That(url.Scheme, Is.EqualTo("opc.quic"));
                Assert.That(url.Port, Is.EqualTo(4840));
                Assert.That(QuicTransport.IsQuicUrl(url), Is.True);
                Assert.That(
                    QuicTransport.IsQuicUrl(new Uri("opc.tcp://server.example:4840")),
                    Is.False);
            });
        }

        // DCQ-008: fallback is not a downgrade. Blocking UDP is a single
        // firewall rule, so an unconditional fallback would hand an
        // off-path attacker a downgrade primitive.
        [Test]
        public void DcQ008AWeakerSecurityModeIsRefusedAsAFallback()
        {
            EndpointDescription required = Endpoint(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256);

            Assert.Multiple(() =>
            {
                Assert.That(
                    QuicTransport.IsAcceptableFallback(
                        required,
                        Endpoint(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256)),
                    Is.False,
                    "Sign is weaker than SignAndEncrypt");
                Assert.That(
                    QuicTransport.IsAcceptableFallback(
                        required,
                        Endpoint(MessageSecurityMode.None, SecurityPolicies.None)),
                    Is.False,
                    "None is weaker still");
            });
        }

        [Test]
        public void DcQ008ADifferentSecurityPolicyIsRefusedAsAFallback()
        {
            EndpointDescription required = Endpoint(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Aes256_Sha256_RsaPss);

            Assert.That(
                QuicTransport.IsAcceptableFallback(
                    required,
                    Endpoint(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)),
                Is.False);
        }

        [Test]
        public void AnEquivalentEndpointIsAnAcceptableFallback()
        {
            EndpointDescription required = Endpoint(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256);

            Assert.That(
                QuicTransport.IsAcceptableFallback(
                    required,
                    Endpoint(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)),
                Is.True);
        }

        private static EndpointDescription Endpoint(
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            return new EndpointDescription
            {
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri
            };
        }
    }
}
#endif
