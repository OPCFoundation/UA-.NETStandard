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

using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Distinguishes trusted transport protection from a declared profile or URL.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SecureChannelProtectionTests
    {
        /// <summary>
        /// Transport protection does not relabel the negotiated UA mode or policy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void HttpsJsonProtectionRequiresTransportEvidence(bool transportProtected)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "https://localhost:443/synthetic",
                TransportProfileUri = Profiles.HttpsJsonTransport,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            var context = new SecureChannelContext("synthetic", endpoint, RequestEncoding.Json)
            {
                HasTransportSecurity = transportProtected
            };
            Assert.That(context.ProvidesEncryption, Is.EqualTo(transportProtected));
            Assert.That(context.ProvidesIntegrity, Is.EqualTo(transportProtected));
            Assert.That(context.EndpointDescription!.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
            Assert.That(context.EndpointDescription.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        /// <summary>
        /// Existing callers without transport evidence retain the ordinary UA message-security semantics.
        /// </summary>
        [TestCase(MessageSecurityMode.None, false, false)]
        [TestCase(MessageSecurityMode.Sign, false, true)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, true, true)]
        public void ExistingConstructorUsesMessageProtection(
            MessageSecurityMode mode, bool encryption, bool integrity)
        {
            var context = new SecureChannelContext(
                "synthetic",
                new EndpointDescription { SecurityMode = mode, TransportProfileUri = Profiles.HttpsBinaryTransport },
                RequestEncoding.Binary);
            Assert.That(context.ProvidesEncryption, Is.EqualTo(encryption));
            Assert.That(context.ProvidesIntegrity, Is.EqualTo(integrity));
        }
    }
}
