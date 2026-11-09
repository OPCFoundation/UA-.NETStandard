/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.IO;
using Moq;
using NUnit.Framework;
using OnboardingRegistrar;
using Opc.Ua.Identity;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the sample's operator-selected transport policy without network or credential access.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PushManagedApplicationTargetProviderTests
    {
        /// <summary>
        /// HTTPS selects Sign while UA-TCP retains SignAndEncrypt; neither accepts an unsecured sibling.
        /// </summary>
        [TestCase("opc.tcp://localhost:4840", MessageSecurityMode.SignAndEncrypt)]
        [TestCase("https://localhost:4843", MessageSecurityMode.Sign)]
        [TestCase("opc.https://localhost:4843", MessageSecurityMode.Sign)]
        public void SampleSelectsTransportSpecificSecuredEndpoint(string url, MessageSecurityMode mode)
        {
            var identity = new Mock<IClientIdentityProvider>(MockBehavior.Strict);
            PushManagedApplicationTargetProvider provider = CreateProvider(url, identity.Object);
            EndpointDescription expected = Endpoint(url, mode);
            EndpointDescription selected = provider.SelectEndpoint(
                [Endpoint(url, MessageSecurityMode.None), expected]);

            Assert.That(selected.SecurityMode, Is.EqualTo(mode));
            Assert.That(selected.EndpointUrl, Is.EqualTo(url));
            Assert.That(selected.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(selected.Server.ApplicationUri, Is.EqualTo("urn:sample:managed"));
            Assert.That(selected, Is.Not.SameAs(expected));
            identity.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Discovery cannot substitute another application, URL, policy, or transport security mode.
        /// </summary>
        [TestCase("application")]
        [TestCase("url")]
        [TestCase("policy")]
        [TestCase("mode")]
        [TestCase("empty")]
        [TestCase("duplicate")]
        public void SampleRejectsUnselectedOrAmbiguousEndpointBeforeCredentials(string change)
        {
            const string url = "https://localhost:4843";
            var identity = new Mock<IClientIdentityProvider>(MockBehavior.Strict);
            PushManagedApplicationTargetProvider provider = CreateProvider(url, identity.Object);
            EndpointDescription offered = Endpoint(url, MessageSecurityMode.Sign);
            switch (change)
            {
                case "application":
                    offered.Server.ApplicationUri = "urn:sample:other";
                    break;
                case "url":
                    offered.EndpointUrl = "https://localhost:4844";
                    break;
                case "policy":
                    offered.SecurityPolicyUri = SecurityPolicies.None;
                    break;
                case "mode":
                    offered.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    break;
            }
            ArrayOf<EndpointDescription> endpoints = change switch
            {
                "empty" => [],
                "duplicate" => [offered, Endpoint(url, MessageSecurityMode.Sign)],
                _ => [offered]
            };

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => provider.SelectEndpoint(endpoints));
            Assert.That(error!.StatusCode, Is.EqualTo(change == "duplicate" ?
                StatusCodes.BadConfigurationError : StatusCodes.BadSecurityChecksFailed));
            identity.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Rejects unsupported schemes and TLS pins that cannot apply to the selected transport.
        /// </summary>
        [TestCase("http://localhost:4843", null)]
        [TestCase("opc.tcp://localhost:4840", "absolute")]
        [TestCase("https://localhost:4843", "relative")]
        public void SampleRejectsInvalidTransportOrTlsPinConfiguration(string url, string? pin)
        {
            string? path = pin == "absolute" ? Path.GetFullPath("tls.der") :
                pin == "relative" ? "tls.der" : null;
            Assert.Throws<ArgumentException>(() => CreateProvider(
                url, new Mock<IClientIdentityProvider>(MockBehavior.Strict).Object, path));
        }

        /// <summary>
        /// Creates an isolated sample policy with no initialized session or filesystem access.
        /// </summary>
        private static PushManagedApplicationTargetProvider CreateProvider(
            string url,
            IClientIdentityProvider identity,
            string? tlsPin = null)
        {
            return new PushManagedApplicationTargetProvider(
                "urn:sample:managed", url, SecurityPolicies.Basic256Sha256,
                Path.GetFullPath("application.der"), ["DA"], default,
                identity, () => new ApplicationConfiguration(), tlsPin);
        }

        /// <summary>
        /// Creates discovery metadata for one endpoint without treating it as peer proof.
        /// </summary>
        private static EndpointDescription Endpoint(string url, MessageSecurityMode mode)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                Server = new ApplicationDescription { ApplicationUri = "urn:sample:managed" },
                SecurityMode = mode,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
        }
    }
}
#endif
