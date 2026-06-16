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

using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Round-trip coverage for the new UADP discovery variants closed in
    /// Phase 16 follow-up (sub-task 16c): ApplicationInformation,
    /// PubSubConnection announcement and the generic discovery probe
    /// request.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public class UadpDiscoveryFamilyTests
    {
        [Test]
        [TestSpec("7.2.4.6.7")]
        public void Encode_ApplicationInformation_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var info = new UadpApplicationInformation
            {
                ApplicationName = new LocalizedText("en-US", "Test Publisher"),
                ApplicationUri = "urn:test:publisher",
                ProductUri = "urn:test:product",
                ApplicationType = ApplicationType.Server,
                Capabilities = new[] { "UA", "UAMA" },
                SupportedTransportProfiles = new[] { Profiles.PubSubUdpUadpTransport },
                SupportedSecurityPolicies = new[] { "http://opcfoundation.org/UA/SecurityPolicy#PubSub-Aes128-CTR" }
            };
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt16(0x4242),
                SequenceNumber = 7,
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationInformation = info,
                StatusCode = StatusCodes.Good
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(response, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryResponseMessage>());
            var decRes = (UadpDiscoveryResponseMessage)decoded!;
            Assert.That(decRes.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.ApplicationInformation));
            Assert.That(decRes.SequenceNumber, Is.EqualTo(7));
            Assert.That(decRes.ApplicationInformation, Is.Not.Null);
            UadpApplicationInformation rt = decRes.ApplicationInformation!;
            Assert.That(rt.ApplicationName.Text, Is.EqualTo("Test Publisher"));
            Assert.That(rt.ApplicationName.Locale, Is.EqualTo("en-US"));
            Assert.That(rt.ApplicationUri, Is.EqualTo("urn:test:publisher"));
            Assert.That(rt.ProductUri, Is.EqualTo("urn:test:product"));
            Assert.That(rt.ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(rt.Capabilities, Has.Count.EqualTo(2));
            Assert.That(rt.Capabilities, Has.Member("UA"));
            Assert.That(rt.Capabilities, Has.Member("UAMA"));
            Assert.That(rt.SupportedTransportProfiles, Has.Count.EqualTo(1));
            Assert.That(rt.SupportedTransportProfiles,
                Has.Member(Profiles.PubSubUdpUadpTransport));
            Assert.That(rt.SupportedSecurityPolicies, Has.Count.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.4.6.8")]
        public void Encode_PubSubConnection_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var connection = new PubSubConnectionDataType
            {
                Name = "Conn-1",
                Enabled = true,
                PublisherId = new Variant((ushort)100),
                TransportProfileUri = Profiles.PubSubUdpUadpTransport
            };
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt16(0x100),
                SequenceNumber = 99,
                DiscoveryType = UadpDiscoveryType.PubSubConnection,
                Connection = connection,
                StatusCode = StatusCodes.Good
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(response, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryResponseMessage>());
            var decRes = (UadpDiscoveryResponseMessage)decoded!;
            Assert.That(decRes.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.PubSubConnection));
            Assert.That(decRes.SequenceNumber, Is.EqualTo(99));
            Assert.That(decRes.Connection, Is.Not.Null);
            Assert.That(decRes.Connection!.Name, Is.EqualTo("Conn-1"));
            Assert.That(decRes.Connection!.Enabled, Is.True);
            Assert.That(decRes.Connection!.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }

        [Test]
        [TestSpec("7.2.4.6.12")]
        public void Encode_DiscoveryProbe_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var filter = new UadpDiscoveryProbeFilter
            {
                ApplicationUri = "urn:filter:app",
                ProductUri = "urn:filter:product",
                Capability = "UAMA"
            };
            var probe = new UadpDiscoveryRequestMessage
            {
                PublisherId = PublisherId.FromUInt16(0xABCD),
                DiscoveryType = UadpDiscoveryType.Probe,
                DataSetWriterIds = new ushort[] { 1, 2 },
                ProbeFilter = filter
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(probe, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryRequestMessage>());
            var decReq = (UadpDiscoveryRequestMessage)decoded!;
            Assert.That(decReq.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.Probe));
            Assert.That(decReq.DataSetWriterIds, Is.EqualTo(new ushort[] { 1, 2 }));
            Assert.That(decReq.ProbeFilter, Is.Not.Null);
            Assert.That(decReq.ProbeFilter!.ApplicationUri, Is.EqualTo("urn:filter:app"));
            Assert.That(decReq.ProbeFilter!.ProductUri, Is.EqualTo("urn:filter:product"));
            Assert.That(decReq.ProbeFilter!.Capability, Is.EqualTo("UAMA"));
        }

        [Test]
        [TestSpec("7.2.4.6.7")]
        public void Encode_ApplicationInformation_EmptyDefaults_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromByte(1),
                SequenceNumber = 1,
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationInformation = new UadpApplicationInformation(),
                StatusCode = StatusCodes.Good
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(response, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            var decRes = (UadpDiscoveryResponseMessage)decoded!;
            Assert.That(decRes.ApplicationInformation, Is.Not.Null);
            Assert.That(decRes.ApplicationInformation!.Capabilities, Is.Empty);
            Assert.That(decRes.ApplicationInformation!.SupportedTransportProfiles, Is.Empty);
            Assert.That(decRes.ApplicationInformation!.SupportedSecurityPolicies, Is.Empty);
        }

        [Test]
        [TestSpec("7.2.4.6.12")]
        public void Encode_DiscoveryProbe_NullFilter_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var probe = new UadpDiscoveryRequestMessage
            {
                PublisherId = PublisherId.FromUInt16(0x4242),
                DiscoveryType = UadpDiscoveryType.Probe,
                DataSetWriterIds = []
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(probe, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            var decReq = (UadpDiscoveryRequestMessage)decoded!;
            Assert.That(decReq.DiscoveryType, Is.EqualTo(UadpDiscoveryType.Probe));
            Assert.That(decReq.ProbeFilter, Is.Not.Null);
            Assert.That(decReq.ProbeFilter!.ApplicationUri, Is.Empty);
        }
    }
}
