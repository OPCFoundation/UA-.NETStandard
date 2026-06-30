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

#nullable enable

using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security
{
    /// <summary>
    /// Tests for transport-related constants and helpers on <see cref="Profiles"/>
    /// and <see cref="Utils"/>.
    /// </summary>
    [TestFixture]
    [Category("Transport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TransportProfileHelpersTests
    {
        [Test]
        public void HttpsBinaryTransportUriIsStable()
        {
            Assert.That(
                Profiles.HttpsBinaryTransport,
                Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/https-uabinary"));
        }

        [Test]
        public void HttpsJsonTransportUriIsStable()
        {
            Assert.That(
                Profiles.HttpsJsonTransport,
                Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/https-uajson"));
        }

        [Test]
        public void UaWssTransportUriIsStable()
        {
            Assert.That(
                Profiles.UaWssTransport,
                Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/uawss-uasc-uabinary"));
        }

        [Test]
        public void UaWssJsonTransportUriIsStable()
        {
            Assert.That(
                Profiles.UaWssJsonTransport,
                Is.EqualTo("http://opcfoundation.org/UA-Profile/Transport/uawss-uajson"));
        }

        [Test]
        public void OpcUaContentTypeConstantsMatchPart6()
        {
            Assert.That(Profiles.OpcUaBinaryContentType, Is.EqualTo("application/opcua+uabinary"));
            Assert.That(Profiles.OpcUaJsonContentType, Is.EqualTo("application/opcua+uajson"));
        }

        [Test]
        public void OpcUaWebSocketSubProtocolsMatchPart6()
        {
            Assert.That(Profiles.OpcUaWsSubProtocolUacp, Is.EqualTo("opcua+uacp"));
            Assert.That(Profiles.OpcUaWsSubProtocolUaJson, Is.EqualTo("opcua+uajson"));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uabinary", true)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uajson", false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uawss-uasc-uabinary", false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary", false)]
        public void IsHttpsBinaryRecognizesExactProfile(string? profileUri, bool expected)
        {
            Assert.That(Profiles.IsHttpsBinary(profileUri), Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uajson", true)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uabinary", false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uawss-uajson", false)]
        public void IsHttpsJsonRecognizesExactProfile(string? profileUri, bool expected)
        {
            Assert.That(Profiles.IsHttpsJson(profileUri), Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uawss-uasc-uabinary", true)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uawss-uajson", false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary", false)]
        public void IsWssBinaryRecognizesExactProfile(string? profileUri, bool expected)
        {
            Assert.That(Profiles.IsWssBinary(profileUri), Is.EqualTo(expected));
        }

        [TestCase(null, false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uawss-uajson", true)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uawss-uasc-uabinary", false)]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uajson", false)]
        public void IsWssJsonRecognizesExactProfile(string? profileUri, bool expected)
        {
            Assert.That(Profiles.IsWssJson(profileUri), Is.EqualTo(expected));
        }

        [Test]
        public void ToWebSocketSubProtocolMapsBinaryToUacp()
        {
            Assert.That(
                Profiles.ToWebSocketSubProtocol(Profiles.UaWssTransport),
                Is.EqualTo(Profiles.OpcUaWsSubProtocolUacp));
        }

        [Test]
        public void ToWebSocketSubProtocolMapsJsonToUaJson()
        {
            Assert.That(
                Profiles.ToWebSocketSubProtocol(Profiles.UaWssJsonTransport),
                Is.EqualTo(Profiles.OpcUaWsSubProtocolUaJson));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/https-uabinary")]
        [TestCase("http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary")]
        public void ToWebSocketSubProtocolReturnsNullForNonWssProfile(string? profileUri)
        {
            Assert.That(Profiles.ToWebSocketSubProtocol(profileUri), Is.Null);
        }

        [TestCase("opc.wss://example.com:4843/path", true)]
        [TestCase("wss://example.com:443/path", true)]
        [TestCase("wss:nohost", true)]
        [TestCase("opc.tcp://example.com:4840", false)]
        [TestCase("https://example.com/", false)]
        [TestCase("ws://example.com/", false)]
        public void IsUriWssSchemeMatchesOpcWssAndWss(string url, bool expected)
        {
            Assert.That(Utils.IsUriWssScheme(url), Is.EqualTo(expected));
        }

        [Test]
        public void UriSchemeWssIsExposed()
        {
            Assert.That(Utils.UriSchemeWss, Is.EqualTo("wss"));
            Assert.That(Utils.UriSchemeWs, Is.EqualTo("ws"));
        }

        [Test]
        public void DefaultUriSchemesIncludesWss()
        {
            Assert.That(Utils.DefaultUriSchemes, Does.Contain(Utils.UriSchemeWss));
            Assert.That(Utils.DefaultUriSchemes, Does.Contain(Utils.UriSchemeOpcWss));
        }
    }
}
