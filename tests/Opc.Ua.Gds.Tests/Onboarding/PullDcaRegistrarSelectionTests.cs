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
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies explicit selection of a pull registrar only from configured device candidates.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PullDcaRegistrarSelectionTests
    {
        /// <summary>
        /// Pre-connection selection rejects missing and ambiguous candidates without invented remote NodeIds.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RequireConfiguredEndpointChecksCandidatesBeforeConnection(int count)
        {
            var endpoint = new Uri("opc.tcp://registrar.example:4840/Registrar");
            var candidates = new ApplicationDescription[count];
            for (int i = 0; i < count; i++)
            {
                candidates[i] = new ApplicationDescription
                {
                    ApplicationUri = kApplicationUri,
                    ApplicationType = ApplicationType.ClientAndServer,
                    DiscoveryUrls = [endpoint.AbsoluteUri]
                };
            }
            if (count == 1)
            {
                Assert.DoesNotThrow(() => PullDcaRegistrarSelection.RequireConfiguredEndpoint(
                    candidates, kApplicationUri, endpoint));
            }
            else
            {
                ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                    PullDcaRegistrarSelection.RequireConfiguredEndpoint(candidates, kApplicationUri, endpoint))!;
                Assert.That(error.StatusCode,
                    Is.EqualTo(count == 0 ? StatusCodes.BadNotFound : StatusCodes.BadInvalidArgument));
            }
        }

        /// <summary>
        /// The operator selects one advertised endpoint and supplies both resolved model identifiers.
        /// </summary>
        [Test]
        public void SelectConfiguredRequiresExactApplicationAndAdvertisedEndpoint()
        {
            var candidate = new ApplicationDescription
            {
                ApplicationUri = kApplicationUri,
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://registrar.example:4840/Registrar"]
            };

            PullDcaRegistrar selected = PullDcaRegistrarSelection.SelectConfigured(
                [candidate],
                kApplicationUri,
                new Uri("opc.tcp://registrar.example:4840/Registrar"),
                kRegistrarId,
                kDirectoryId);

            Assert.That(selected.EndpointUrl, Is.EqualTo(new Uri(candidate.DiscoveryUrls[0])));
            Assert.That(selected.RegistrarId, Is.EqualTo(kRegistrarId));
            Assert.That(selected.CertificateDirectoryId, Is.EqualTo(kDirectoryId));
        }

        /// <summary>
        /// An endpoint outside the candidate set is rejected rather than tried as an alternate registrar.
        /// </summary>
        [Test]
        public void SelectConfiguredRejectsEndpointNotInConfiguredCandidates()
        {
            var candidate = new ApplicationDescription
            {
                ApplicationUri = kApplicationUri,
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://registrar.example:4840/Registrar"]
            };

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                PullDcaRegistrarSelection.SelectConfigured(
                    [candidate],
                    kApplicationUri,
                    new Uri("opc.tcp://other.example:4840/Registrar"),
                    kRegistrarId,
                    kDirectoryId))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        /// <summary>
        /// Duplicate discovery entries cannot make an ambiguous selection appear unique.
        /// </summary>
        [Test]
        public void SelectConfiguredRejectsAmbiguousAdvertisedEndpoint()
        {
            var candidate = new ApplicationDescription
            {
                ApplicationUri = kApplicationUri,
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls =
                [
                    "opc.tcp://registrar.example:4840/Registrar",
                    "opc.tcp://registrar.example:4840/Registrar"
                ]
            };

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                PullDcaRegistrarSelection.SelectConfigured(
                    [candidate],
                    kApplicationUri,
                    new Uri(candidate.DiscoveryUrls[0]),
                    kRegistrarId,
                    kDirectoryId))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// An endpoint advertised by a different application does not satisfy the selected registrar identity.
        /// </summary>
        [Test]
        public void SelectConfiguredDoesNotMatchEndpointForDifferentApplicationUri()
        {
            var candidate = new ApplicationDescription
            {
                ApplicationUri = "urn:registrar:other",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://registrar.example:4840/Registrar"]
            };

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                PullDcaRegistrarSelection.SelectConfigured(
                    [candidate],
                    kApplicationUri,
                    new Uri(candidate.DiscoveryUrls[0]),
                    kRegistrarId,
                    kDirectoryId))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        /// <summary>
        /// A client-only description cannot be used as a registrar endpoint.
        /// </summary>
        [Test]
        public void SelectConfiguredRejectsClientOnlyCandidate()
        {
            var candidate = new ApplicationDescription
            {
                ApplicationUri = kApplicationUri,
                ApplicationType = ApplicationType.Client,
                DiscoveryUrls = ["opc.tcp://registrar.example:4840/Registrar"]
            };
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                PullDcaRegistrarSelection.SelectConfigured(
                    [candidate], kApplicationUri, new Uri(candidate.DiscoveryUrls[0]), kRegistrarId, kDirectoryId))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>The explicitly selected registrar application URI.</summary>
        private const string kApplicationUri = "urn:registrar:configured";

        /// <summary>The host-resolved registrar model identifier.</summary>
        private static readonly NodeId kRegistrarId = new(1001, 2);

        /// <summary>The host-resolved certificate-directory identifier.</summary>
        private static readonly NodeId kDirectoryId = new(1002, 2);
    }
}
