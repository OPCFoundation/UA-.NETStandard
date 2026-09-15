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
 *
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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Endpoint translation for the transport profiles that share a URL scheme.
    /// </summary>
    /// <remarks>
    /// A base address carries the one profile its URL scheme implies, but an
    /// HTTPS listener publishes binary, JSON and OpenAPI endpoints on that
    /// scheme and a WSS listener does the same. Those endpoints used to match no
    /// base address at all, so GetEndpoints dropped them; the ones that did match
    /// were then collapsed onto each other because the duplicate check ignored
    /// the transport profile.
    ///
    /// OPC 10000-4 5.5.4.2: the profileUris parameter is the list of transport
    /// profiles the returned endpoints shall support.
    /// </remarks>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ServerBaseTransportProfileTests : ServerBase
    {
        private const string kHttpsUrl = "https://localhost:51212/UA/SampleServer";
        private const string kWssUrl = "opc.wss://localhost:51213/UA/SampleServer";

        private readonly ApplicationDescription m_application;

        public ServerBaseTransportProfileTests()
            : base(NUnitTelemetryContext.Create(true))
        {
            m_application = new ApplicationDescription
            {
                ApplicationUri = "urn:localhost:TransportProfileTests",
                ApplicationName = new LocalizedText("en-US", "TransportProfileTests"),
                ApplicationType = ApplicationType.Server
            };
        }

        /// <summary>
        /// All three HTTPS profiles are published, not just the binary one whose
        /// URI the base address happens to carry.
        /// </summary>
        [Test]
        public void HttpsJsonAndOpenApiEndpointsSurviveTranslation()
        {
            ArrayOf<EndpointDescription> translated = TranslateEndpointDescriptions(
                new Uri(kHttpsUrl),
                [HttpsBaseAddress()],
                HttpsEndpoints(),
                m_application);

            Assert.That(
                ProfileUris(translated),
                Is.EquivalentTo(new[]
                {
                    Profiles.HttpsBinaryTransport,
                    Profiles.HttpsJsonTransport,
                    Profiles.HttpsOpenApiTransport
                }));
        }

        /// <summary>
        /// The same for the WSS scheme, whose listener serves binary, JSON and
        /// OpenAPI too.
        /// </summary>
        [Test]
        public void WssJsonAndOpenApiEndpointsSurviveTranslation()
        {
            ArrayOf<EndpointDescription> translated = TranslateEndpointDescriptions(
                new Uri(kWssUrl),
                [WssBaseAddress()],
                WssEndpoints(),
                m_application);

            Assert.That(
                ProfileUris(translated),
                Is.EquivalentTo(new[]
                {
                    Profiles.UaWssTransport,
                    Profiles.UaWssJsonTransport,
                    Profiles.WssOpenApiTransport
                }));
        }

        /// <summary>
        /// The binary endpoint is offered first. A client with no transport
        /// filter of its own takes the first endpoint that fits its security
        /// requirements, so the one it gets has to stay the one it can always
        /// speak - and List.Sort is not stable, so URL alone does not settle it.
        /// </summary>
        [Test]
        public void BinaryEndpointIsOfferedAheadOfJsonAndOpenApi()
        {
            ArrayOf<EndpointDescription> translated = TranslateEndpointDescriptions(
                new Uri(kHttpsUrl),
                [HttpsBaseAddress()],
                HttpsEndpoints(),
                m_application);

            Assert.That(
                translated[0].TransportProfileUri,
                Is.EqualTo(Profiles.HttpsBinaryTransport));
        }

        /// <summary>
        /// Endpoints differing only in transport profile are not treated as
        /// duplicates of each other.
        /// </summary>
        [Test]
        public void EndpointsAreNotDeduplicatedAcrossTransportProfiles()
        {
            ArrayOf<EndpointDescription> translated = TranslateEndpointDescriptions(
                new Uri(kHttpsUrl),
                [HttpsBaseAddress()],
                HttpsEndpoints(),
                m_application);

            Assert.That(translated.Count, Is.EqualTo(3));

            foreach (EndpointDescription endpoint in translated)
            {
                Assert.That(endpoint.EndpointUrl, Is.EqualTo(kHttpsUrl));
                Assert.That(
                    endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            }
        }

        /// <summary>
        /// Two descriptions that really are the same endpoint still collapse, so
        /// the transport profile was added to the identity rather than replacing
        /// it.
        /// </summary>
        [Test]
        public void IdenticalEndpointsAreStillDeduplicated()
        {
            ArrayOf<EndpointDescription> endpoints =
            [
                Endpoint(kHttpsUrl, Profiles.HttpsBinaryTransport),
                Endpoint(kHttpsUrl, Profiles.HttpsBinaryTransport)
            ];

            ArrayOf<EndpointDescription> translated = TranslateEndpointDescriptions(
                new Uri(kHttpsUrl),
                [HttpsBaseAddress()],
                endpoints,
                m_application);

            Assert.That(translated.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A client asking for the JSON or OpenAPI profile gets the HTTPS base
        /// address back. The filter used to compare profile URIs for equality,
        /// so such a request matched nothing and the endpoint list came back
        /// empty.
        /// </summary>
        [TestCaseSource(nameof(HttpsProfileUris))]
        public void FilterByProfileMatchesTheHttpsSchemeProfiles(string profileUri)
        {
            IList<BaseAddress> filtered = FilterByProfile(
                [profileUri],
                [HttpsBaseAddress()]);

            Assert.That(filtered, Has.Count.EqualTo(1));
        }

        [TestCaseSource(nameof(WssProfileUris))]
        public void FilterByProfileMatchesTheWssSchemeProfiles(string profileUri)
        {
            IList<BaseAddress> filtered = FilterByProfile(
                [profileUri],
                [WssBaseAddress()]);

            Assert.That(filtered, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A profile served by a different scheme still does not match, so the
        /// filter was widened to the scheme's own profiles and no further.
        /// </summary>
        [Test]
        public void FilterByProfileRejectsAProfileOfAnotherScheme()
        {
            IList<BaseAddress> filtered = FilterByProfile(
                [Profiles.UaTcpTransport],
                [HttpsBaseAddress()]);

            Assert.That(filtered, Is.Empty);
        }

        public static readonly string[] HttpsProfileUris =
        [
            Profiles.HttpsBinaryTransport,
            Profiles.HttpsJsonTransport,
            Profiles.HttpsOpenApiTransport
        ];

        public static readonly string[] WssProfileUris =
        [
            Profiles.UaWssTransport,
            Profiles.UaWssJsonTransport,
            Profiles.WssOpenApiTransport
        ];

        private static List<string> ProfileUris(ArrayOf<EndpointDescription> endpoints)
        {
            var profileUris = new List<string>();

            foreach (EndpointDescription endpoint in endpoints)
            {
                profileUris.Add(endpoint.TransportProfileUri);
            }

            return profileUris;
        }

        private static BaseAddress HttpsBaseAddress()
        {
            return new BaseAddress
            {
                Url = new Uri(kHttpsUrl),
                ProfileUri = Profiles.HttpsBinaryTransport
            };
        }

        private static BaseAddress WssBaseAddress()
        {
            return new BaseAddress
            {
                Url = new Uri(kWssUrl),
                ProfileUri = Profiles.UaWssTransport
            };
        }

        private static ArrayOf<EndpointDescription> HttpsEndpoints()
        {
            return
            [
                Endpoint(kHttpsUrl, Profiles.HttpsBinaryTransport),
                Endpoint(kHttpsUrl, Profiles.HttpsJsonTransport),
                Endpoint(kHttpsUrl, Profiles.HttpsOpenApiTransport)
            ];
        }

        private static ArrayOf<EndpointDescription> WssEndpoints()
        {
            return
            [
                Endpoint(kWssUrl, Profiles.UaWssTransport),
                Endpoint(kWssUrl, Profiles.UaWssJsonTransport),
                Endpoint(kWssUrl, Profiles.WssOpenApiTransport)
            ];
        }

        private static EndpointDescription Endpoint(string url, string transportProfileUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                TransportProfileUri = transportProfileUri,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss,
                SecurityLevel = 1,
                UserIdentityTokens =
                [
                    new UserTokenPolicy { TokenType = UserTokenType.Anonymous }
                ]
            };
        }
    }
}
