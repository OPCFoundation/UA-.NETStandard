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

#nullable enable

using System;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.WebApi
{
    /// <summary>
    /// Unit tests for the <see cref="WebApiMediaType"/> media-type
    /// parameter parser used by the HTTPS REST binding to negotiate
    /// the OPC UA JSON encoding flavour (Compact / Verbose) per
    /// OPC UA Part 6 §G.3 "OpenAPI Mapping".
    /// </summary>
    [TestFixture]
    [Category("WebApiMediaType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiMediaTypeTests
    {
        [Test]
        public void DefaultEncodingIsCompactPerSpec()
        {
            Assert.That(WebApiMediaType.DefaultEncoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/json; encoding=compact", WebApiEncoding.Compact)]
        [TestCase("application/json;encoding=compact", WebApiEncoding.Compact)]
        [TestCase("application/json; encoding=verbose", WebApiEncoding.Verbose)]
        [TestCase("application/json;encoding=verbose", WebApiEncoding.Verbose)]
        [TestCase("APPLICATION/JSON; ENCODING=VERBOSE", WebApiEncoding.Verbose)]
        [TestCase("application/json; charset=utf-8; encoding=verbose", WebApiEncoding.Verbose)]
        [TestCase("application/json; encoding=\"verbose\"", WebApiEncoding.Verbose)]
        [TestCase("application/json; encoding=\"compact\"", WebApiEncoding.Compact)]
        public void TryParseExplicitEncodingReturnsTrue(string header, WebApiEncoding expected)
        {
            bool parsed = WebApiMediaType.TryParseEncoding(header, out WebApiEncoding encoding);
            Assert.That(parsed, Is.True, $"expected an explicit encoding match for header '{header}'");
            Assert.That(encoding, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void TryParseNullOrEmptyHeaderReturnsFalseAndCompact(string? header)
        {
            bool parsed = WebApiMediaType.TryParseEncoding(header, out WebApiEncoding encoding);
            Assert.That(parsed, Is.False);
            Assert.That(encoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/json")]
        [TestCase("application/json; charset=utf-8")]
        [TestCase("application/json; encoding=")]
        public void TryParseHeaderWithoutEncodingParameterReturnsFalseAndCompact(string header)
        {
            bool parsed = WebApiMediaType.TryParseEncoding(header, out WebApiEncoding encoding);
            Assert.That(parsed, Is.False);
            Assert.That(encoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/json; encoding=unknown")]
        [TestCase("application/json; encoding=raw")]
        public void TryParseUnknownEncodingValueFallsBackToCompactAndReturnsFalse(string header)
        {
            bool parsed = WebApiMediaType.TryParseEncoding(header, out WebApiEncoding encoding);
            Assert.That(parsed, Is.False, "unknown encoding value must not be treated as explicit match");
            Assert.That(encoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/opcua+uajson; encoding=verbose")]
        [TestCase("text/plain; encoding=verbose")]
        [TestCase("application/xml")]
        public void TryParseNonJsonMediaTypeReturnsFalseAndCompact(string header)
        {
            bool parsed = WebApiMediaType.TryParseEncoding(header, out WebApiEncoding encoding);
            Assert.That(parsed, Is.False);
            Assert.That(encoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        public void ParseEncodingUsesProvidedFallbackWhenHeaderIsMissing()
        {
            WebApiEncoding encoding = WebApiMediaType.ParseEncoding(
                headerValue: null,
                fallback: WebApiEncoding.Verbose);

            Assert.That(encoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void ParseEncodingPrefersExplicitEncodingOverFallback()
        {
            WebApiEncoding encoding = WebApiMediaType.ParseEncoding(
                headerValue: "application/json; encoding=compact",
                fallback: WebApiEncoding.Verbose);

            Assert.That(encoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        [TestCase(WebApiEncoding.Compact, "application/json; encoding=compact")]
        [TestCase(WebApiEncoding.Verbose, "application/json; encoding=verbose")]
        public void FormatContentTypeReturnsParameterizedHeader(WebApiEncoding encoding, string expected)
        {
            string actual = WebApiMediaType.FormatContentType(encoding);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ToEncoderOptionsMapsCompactToCompactProfile()
        {
            JsonEncoderOptions options = WebApiMediaType.ToEncoderOptions(WebApiEncoding.Compact);
            Assert.That(options, Is.SameAs(JsonEncoderOptions.Compact));
        }

        [Test]
        public void ToEncoderOptionsMapsVerboseToVerboseProfile()
        {
            JsonEncoderOptions options = WebApiMediaType.ToEncoderOptions(WebApiEncoding.Verbose);
            Assert.That(options, Is.SameAs(JsonEncoderOptions.Verbose));
        }

        [Test]
        public void FormatThenParseRoundTripsCompact()
        {
            string formatted = WebApiMediaType.FormatContentType(WebApiEncoding.Compact);
            bool parsed = WebApiMediaType.TryParseEncoding(formatted, out WebApiEncoding roundTrip);

            Assert.That(parsed, Is.True);
            Assert.That(roundTrip, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        public void FormatThenParseRoundTripsVerbose()
        {
            string formatted = WebApiMediaType.FormatContentType(WebApiEncoding.Verbose);
            bool parsed = WebApiMediaType.TryParseEncoding(formatted, out WebApiEncoding roundTrip);

            Assert.That(parsed, Is.True);
            Assert.That(roundTrip, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void ContentTypeConstantIsPlainApplicationJson()
        {
            Assert.That(WebApiMediaType.ContentType, Is.EqualTo("application/json"));
        }

        [Test]
        public void EncodingParameterConstantIsLowercaseEncoding()
        {
            Assert.That(WebApiMediaType.EncodingParameter, Is.EqualTo("encoding"));
        }
    }

    /// <summary>
    /// Unit tests for the HTTPS REST API <see cref="Profiles"/> URI helper.
    /// </summary>
    [TestFixture]
    [Category("OpenApiProfile")]
    [Parallelizable]
    public class WebApiProfileTests
    {
        [Test]
        public void HttpsOpenApiTransportConstantMatchesProfile2338Uri()
        {
            Assert.That(
                Profiles.HttpsOpenApiTransport,
                Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/https-uajson-openapi"));
        }

        [Test]
        public void WssOpenApiTransportConstantMatchesProfile2339Uri()
        {
            Assert.That(
                Profiles.WssOpenApiTransport,
                Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/wss-uajson-openapi"));
        }

        [Test]
        public void IsHttpsOpenApiReturnsTrueForExactMatch()
        {
            Assert.That(Profiles.IsHttpsOpenApi(Profiles.HttpsOpenApiTransport), Is.True);
        }

        [Test]
        public void IsWssOpenApiReturnsTrueForExactMatch()
        {
            Assert.That(Profiles.IsWssOpenApi(Profiles.WssOpenApiTransport), Is.True);
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uajson")]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uabinary")]
        [TestCase("HTTP://OPCFOUNDATION.ORG/UA-PROFILE/TRANSPORT/HTTPS-UAJSON-OPENAPI")]
        public void IsHttpsOpenApiReturnsFalseForOtherProfiles(string? uri)
        {
            Assert.That(Profiles.IsHttpsOpenApi(uri), Is.False);
        }

        [Test]
        [Obsolete("Validating the obsolete HttpsWebApiTransport alias works.")]
        public void HttpsWebApiTransportAliasResolvesToHttpsOpenApiTransport()
        {
            Assert.That(Profiles.HttpsWebApiTransport, Is.EqualTo(Profiles.HttpsOpenApiTransport));
            Assert.That(Profiles.IsHttpsWebApi(Profiles.HttpsOpenApiTransport), Is.True);
        }
    }
}
