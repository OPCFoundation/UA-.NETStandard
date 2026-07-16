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
using Opc.Ua.Lds.Server;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Unit tests for the LDS / LDS-ME static surface that does not
    /// require a live server: OPC 10000-12 Annex C mDNS field shape and
    /// Annex D capability identifier rules.
    /// </summary>
    [TestFixture]
    [Category("LDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class LdsServerStaticTests
    {
        [Test]
        public void ComputeServerCapabilitiesEmptyReturnsLdsOnly()
        {
            // OPC 10000-12 Annex D: an LDS without multicast announces only
            // the "LDS" capability identifier.
            ArrayOf<string> result = LdsServer.ComputeServerCapabilities(
                default,
                hasMulticast: false);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Contains("LDS"), Is.True);
            Assert.That(result.Contains("LDS-ME"), Is.False);
        }

        [Test]
        public void ComputeServerCapabilitiesEmptyWithMulticastReturnsLdsAndLdsMe()
        {
            // OPC 10000-12 Annex D: when the MulticastExtension is
            // configured the LDS-ME identifier is added.
            ArrayOf<string> result = LdsServer.ComputeServerCapabilities(
                default,
                hasMulticast: true);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Contains("LDS"), Is.True);
            Assert.That(result.Contains("LDS-ME"), Is.True);
        }

        [Test]
        public void ComputeServerCapabilitiesAddsLdsMeToCustomList()
        {
            // Custom capabilities are preserved; LDS-ME is added when
            // the multicast layer is enabled.
            ArrayOf<string> existing = ["LDS", "custom"];
            ArrayOf<string> result = LdsServer.ComputeServerCapabilities(
                existing,
                hasMulticast: true);

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result.Contains("LDS"), Is.True);
            Assert.That(result.Contains("LDS-ME"), Is.True);
            Assert.That(result.Contains("custom"), Is.True);
        }

        [Test]
        public void ComputeServerCapabilitiesIsIdempotentForLdsMe()
        {
            // Repeated start-up should not duplicate LDS-ME.
            ArrayOf<string> existing = ["LDS", "LDS-ME", "custom"];
            ArrayOf<string> result = LdsServer.ComputeServerCapabilities(
                existing,
                hasMulticast: true);

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result.Contains("LDS"), Is.True);
            Assert.That(result.Contains("LDS-ME"), Is.True);
            Assert.That(result.Contains("custom"), Is.True);
        }

        [Test]
        public void ComputeServerCapabilitiesPreservesCallerLdsWithoutMulticast()
        {
            // Without multicast the existing list is returned unchanged.
            ArrayOf<string> existing = ["LDS", "custom"];
            ArrayOf<string> result = LdsServer.ComputeServerCapabilities(
                existing,
                hasMulticast: false);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Contains("LDS"), Is.True);
            Assert.That(result.Contains("custom"), Is.True);
            Assert.That(result.Contains("LDS-ME"), Is.False);
        }

        [Test]
        public void ComputeServerCapabilitiesAddsLdsWhenMissing()
        {
            // Defensive: if a caller forgot to seed "LDS", add it.
            ArrayOf<string> existing = ["custom"];
            ArrayOf<string> result = LdsServer.ComputeServerCapabilities(
                existing,
                hasMulticast: false);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Contains("LDS"), Is.True);
            Assert.That(result.Contains("custom"), Is.True);
        }

        [Test]
        public void MulticastDiscoveryAnnexCConstants()
        {
            // OPC 10000-12 Annex C: service type and reverse-connect
            // TXT-key shape must remain stable so peers interoperate.
            Assert.That(MulticastDiscovery.OpcUaServiceType, Is.EqualTo("_opcua-tcp._tcp"));
            Assert.That(MulticastDiscovery.ReverseConnectScheme, Is.EqualTo("rcp+"));
            Assert.That(MulticastDiscovery.ReverseConnectTxtKey, Is.EqualTo("rc"));
        }
    }
}
