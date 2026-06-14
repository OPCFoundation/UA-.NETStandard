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

using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.RestApi
{
    /// <summary>
    /// Unit tests for the <see cref="RestApiMediaType"/> media-type
    /// parameter parser used by the HTTPS REST binding to negotiate
    /// the OPC UA JSON encoding flavour (Compact / Verbose) per
    /// OPC UA Part 6 §G.3 "OpenAPI Mapping".
    /// </summary>
    [TestFixture]
    [Category("RestApiMediaType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RestApiMediaTypeTests
    {
        [Test]
        public void DefaultEncodingIsCompactPerSpec()
        {
            Assert.That(RestApiMediaType.DefaultEncoding, Is.EqualTo(RestApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/json; encoding=compact", RestApiEncoding.Compact)]
        [TestCase("application/json;encoding=compact", RestApiEncoding.Compact)]
        [TestCase("application/json; encoding=verbose", RestApiEncoding.Verbose)]
        [TestCase("application/json;encoding=verbose", RestApiEncoding.Verbose)]
        [TestCase("APPLICATION/JSON; ENCODING=VERBOSE", RestApiEncoding.Verbose)]
        [TestCase("application/json; charset=utf-8; encoding=verbose", RestApiEncoding.Verbose)]
        [TestCase("application/json; encoding=\"verbose\"", RestApiEncoding.Verbose)]
        [TestCase("application/json; encoding=\"compact\"", RestApiEncoding.Compact)]
        public void TryParseExplicitEncodingReturnsTrue(string header, RestApiEncoding expected)
        {
            bool parsed = RestApiMediaType.TryParseEncoding(header, out RestApiEncoding encoding);
            Assert.That(parsed, Is.True, $"expected an explicit encoding match for header '{header}'");
            Assert.That(encoding, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void TryParseNullOrEmptyHeaderReturnsFalseAndCompact(string? header)
        {
            bool parsed = RestApiMediaType.TryParseEncoding(header, out RestApiEncoding encoding);
            Assert.That(parsed, Is.False);
            Assert.That(encoding, Is.EqualTo(RestApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/json")]
        [TestCase("application/json; charset=utf-8")]
        [TestCase("application/json; encoding=")]
        public void TryParseHeaderWithoutEncodingParameterReturnsFalseAndCompact(string header)
        {
            bool parsed = RestApiMediaType.TryParseEncoding(header, out RestApiEncoding encoding);
            Assert.That(parsed, Is.False);
            Assert.That(encoding, Is.EqualTo(RestApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/json; encoding=unknown")]
        [TestCase("application/json; encoding=raw")]
        public void TryParseUnknownEncodingValueFallsBackToCompactAndReturnsFalse(string header)
        {
            bool parsed = RestApiMediaType.TryParseEncoding(header, out RestApiEncoding encoding);
            Assert.That(parsed, Is.False, "unknown encoding value must not be treated as explicit match");
            Assert.That(encoding, Is.EqualTo(RestApiEncoding.Compact));
        }

        [Test]
        [TestCase("application/opcua+uajson; encoding=verbose")]
        [TestCase("text/plain; encoding=verbose")]
        [TestCase("application/xml")]
        public void TryParseNonJsonMediaTypeReturnsFalseAndCompact(string header)
        {
            bool parsed = RestApiMediaType.TryParseEncoding(header, out RestApiEncoding encoding);
            Assert.That(parsed, Is.False);
            Assert.That(encoding, Is.EqualTo(RestApiEncoding.Compact));
        }

        [Test]
        public void ParseEncodingUsesProvidedFallbackWhenHeaderIsMissing()
        {
            RestApiEncoding encoding = RestApiMediaType.ParseEncoding(
                headerValue: null,
                fallback: RestApiEncoding.Verbose);

            Assert.That(encoding, Is.EqualTo(RestApiEncoding.Verbose));
        }

        [Test]
        public void ParseEncodingPrefersExplicitEncodingOverFallback()
        {
            RestApiEncoding encoding = RestApiMediaType.ParseEncoding(
                headerValue: "application/json; encoding=compact",
                fallback: RestApiEncoding.Verbose);

            Assert.That(encoding, Is.EqualTo(RestApiEncoding.Compact));
        }

        [Test]
        [TestCase(RestApiEncoding.Compact, "application/json; encoding=compact")]
        [TestCase(RestApiEncoding.Verbose, "application/json; encoding=verbose")]
        public void FormatContentTypeReturnsParameterizedHeader(RestApiEncoding encoding, string expected)
        {
            string actual = RestApiMediaType.FormatContentType(encoding);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ToEncoderOptionsMapsCompactToCompactProfile()
        {
            JsonEncoderOptions options = RestApiMediaType.ToEncoderOptions(RestApiEncoding.Compact);
            Assert.That(options, Is.SameAs(JsonEncoderOptions.Compact));
        }

        [Test]
        public void ToEncoderOptionsMapsVerboseToVerboseProfile()
        {
            JsonEncoderOptions options = RestApiMediaType.ToEncoderOptions(RestApiEncoding.Verbose);
            Assert.That(options, Is.SameAs(JsonEncoderOptions.Verbose));
        }

        [Test]
        public void FormatThenParseRoundTripsCompact()
        {
            string formatted = RestApiMediaType.FormatContentType(RestApiEncoding.Compact);
            bool parsed = RestApiMediaType.TryParseEncoding(formatted, out RestApiEncoding roundTrip);

            Assert.That(parsed, Is.True);
            Assert.That(roundTrip, Is.EqualTo(RestApiEncoding.Compact));
        }

        [Test]
        public void FormatThenParseRoundTripsVerbose()
        {
            string formatted = RestApiMediaType.FormatContentType(RestApiEncoding.Verbose);
            bool parsed = RestApiMediaType.TryParseEncoding(formatted, out RestApiEncoding roundTrip);

            Assert.That(parsed, Is.True);
            Assert.That(roundTrip, Is.EqualTo(RestApiEncoding.Verbose));
        }

        [Test]
        public void ContentTypeConstantIsPlainApplicationJson()
        {
            Assert.That(RestApiMediaType.ContentType, Is.EqualTo("application/json"));
        }

        [Test]
        public void EncodingParameterConstantIsLowercaseEncoding()
        {
            Assert.That(RestApiMediaType.EncodingParameter, Is.EqualTo("encoding"));
        }
    }

    /// <summary>
    /// Unit tests for the HTTPS REST API <see cref="Profiles"/> URI helper.
    /// </summary>
    [TestFixture]
    [Category("RestApiProfile")]
    [Parallelizable]
    public class RestApiProfileTests
    {
        [Test]
        public void HttpsRestApiTransportConstantMatchesSpecUrlShape()
        {
            Assert.That(
                Profiles.HttpsRestApiTransport,
                Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/https-restapi"));
        }

        [Test]
        public void IsHttpsRestApiReturnsTrueForExactMatch()
        {
            Assert.That(Profiles.IsHttpsRestApi(Profiles.HttpsRestApiTransport), Is.True);
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uajson")]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uabinary")]
        [TestCase("HTTP://OPCFOUNDATION.ORG/UA-PROFILE/TRANSPORT/HTTPS-RESTAPI")]
        public void IsHttpsRestApiReturnsFalseForOtherProfiles(string? uri)
        {
            Assert.That(Profiles.IsHttpsRestApi(uri), Is.False);
        }
    }
}
