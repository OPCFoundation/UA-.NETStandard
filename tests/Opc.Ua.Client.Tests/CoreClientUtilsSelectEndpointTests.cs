// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// <c>CoreClientUtils.SelectEndpoint</c> selects by TransportProfileUri
    /// (OPC 10000-4 §5.5.4) and never returns an endpoint of the OpenAPI
    /// mapping (OPC 10000-6 §G.3), whatever the order of the endpoints
    /// returned by GetEndpoints.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CoreClientUtilsSelectEndpointTests
    {
        private static readonly Uri s_httpsUrl = new("https://localhost:4843/");
        private static readonly Uri s_wssUrl = new("opc.wss://localhost:4843/");

        [Test]
        public void SelectEndpointSkipsTheOpenApiEndpointListedFirst()
        {
            EndpointDescription openApi = CreateEndpoint(s_httpsUrl, Profiles.HttpsOpenApiTransport);
            EndpointDescription binary = CreateEndpoint(s_httpsUrl, Profiles.HttpsBinaryTransport);

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_httpsUrl,
                [openApi, binary],
                useSecurity: false,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.SameAs(binary));
        }

        [Test]
        public void SelectEndpointWithSecurityFallbackSkipsTheOpenApiEndpoint()
        {
            // No endpoint signs, so the HTTPS fallback for useSecurity applies.
            EndpointDescription openApi = CreateEndpoint(s_httpsUrl, Profiles.HttpsOpenApiTransport);
            EndpointDescription binary = CreateEndpoint(s_httpsUrl, Profiles.HttpsBinaryTransport);

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_httpsUrl,
                [openApi, binary],
                useSecurity: true,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.SameAs(binary));
        }

        [Test]
        public void SelectEndpointSkipsTheWssOpenApiEndpoint()
        {
            EndpointDescription openApi = CreateEndpoint(s_wssUrl, Profiles.WssOpenApiTransport);
            EndpointDescription binary = CreateEndpoint(s_wssUrl, Profiles.UaWssTransport);

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_wssUrl,
                [openApi, binary],
                useSecurity: false,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.SameAs(binary));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SelectEndpointReturnsNullWhenOnlyOpenApiEndpointsMatch(bool useSecurity)
        {
            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_httpsUrl,
                [CreateEndpoint(s_httpsUrl, Profiles.HttpsOpenApiTransport)],
                useSecurity,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.Null);
        }

        [Test]
        public void SelectEndpointKeepsTheFirstBinaryEndpointWithoutTransportProfile()
        {
            // Endpoints without TransportProfileUri are still selected by URL
            // scheme and mode, as before.
            EndpointDescription first = CreateEndpoint(s_httpsUrl, null);
            EndpointDescription second = CreateEndpoint(s_httpsUrl, Profiles.HttpsBinaryTransport);

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_httpsUrl,
                [first, second],
                useSecurity: false,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.SameAs(first));
        }

        private static EndpointDescription CreateEndpoint(Uri url, string? transportProfileUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = url.ToString(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = transportProfileUri
            };
        }
    }
}
