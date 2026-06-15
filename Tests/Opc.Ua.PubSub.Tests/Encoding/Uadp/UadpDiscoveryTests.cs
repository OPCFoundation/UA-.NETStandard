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

using System;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Coverage for UADP discovery encoder/decoder. Validates round-trip
    /// for each discovery type (request + response).
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.6.4")]
    [TestSpec("7.2.4.6.7")]
    [TestSpec("7.2.4.6.8")]
    [TestSpec("7.2.4.6.9")]
    public class UadpDiscoveryTests
    {
        [Test]
        public void DiscoveryRequest_DataSetMetaData_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var request = new UadpDiscoveryRequestMessage
            {
                PublisherId = PublisherId.FromUInt16(0x4242),
                DataSetClassId = (Uuid)Guid.NewGuid(),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterIds = new ushort[] { 1, 2, 3 }
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(request, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryRequestMessage>());
            var decReq = (UadpDiscoveryRequestMessage)decoded!;
            Assert.That(decReq.PublisherId, Is.EqualTo(request.PublisherId));
            Assert.That(decReq.DataSetClassId, Is.EqualTo(request.DataSetClassId));
            Assert.That(decReq.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.DataSetMetaData));
            Assert.That(decReq.DataSetWriterIds, Is.EqualTo(new ushort[] { 1, 2, 3 }));
        }

        [Test]
        public void DiscoveryRequest_PublisherEndpoints_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var request = new UadpDiscoveryRequestMessage
            {
                PublisherId = PublisherId.FromString("publisher-A"),
                DiscoveryType = UadpDiscoveryType.PublisherEndpoints,
                DataSetWriterIds = []
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(request, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryRequestMessage>());
            var decReq = (UadpDiscoveryRequestMessage)decoded!;
            Assert.That(decReq.PublisherId, Is.EqualTo(request.PublisherId));
            Assert.That(decReq.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.PublisherEndpoints));
            Assert.That(decReq.DataSetWriterIds, Is.Empty);
        }

        [Test]
        public void DiscoveryRequest_DataSetWriterConfiguration_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var request = new UadpDiscoveryRequestMessage
            {
                PublisherId = PublisherId.FromUInt32(0x12345678),
                DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                DataSetWriterIds = new ushort[] { 7 }
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(request, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryRequestMessage>());
            var decReq = (UadpDiscoveryRequestMessage)decoded!;
            Assert.That(decReq.PublisherId, Is.EqualTo(request.PublisherId));
            Assert.That(decReq.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.DataSetWriterConfiguration));
            Assert.That(decReq.DataSetWriterIds, Is.EqualTo(new ushort[] { 7 }));
        }

        [Test]
        [TestSpec("7.2.4.6.8")]
        public void DiscoveryResponse_DataSetMetaData_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var meta = new DataSetMetaDataType
            {
                Name = "TestMeta",
                Description = new LocalizedText("en-US", "Round-trip metadata"),
                DataSetClassId = (Uuid)Guid.NewGuid(),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 11,
                    MinorVersion = 22
                }
            };
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromByte(0xAA),
                SequenceNumber = 99,
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterId = 0x100,
                DataSetMetaData = meta,
                StatusCode = StatusCodes.Good
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(response, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryResponseMessage>());
            var decRes = (UadpDiscoveryResponseMessage)decoded!;
            Assert.That(decRes.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.DataSetMetaData));
            Assert.That(decRes.SequenceNumber, Is.EqualTo(99));
            Assert.That(decRes.DataSetWriterId, Is.EqualTo(0x100));
            Assert.That(decRes.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(decRes.DataSetMetaData, Is.Not.Null);
            Assert.That(decRes.DataSetMetaData!.Name, Is.EqualTo("TestMeta"));
            Assert.That(decRes.DataSetMetaData!.ConfigurationVersion.MajorVersion,
                Is.EqualTo(11u));
            Assert.That(decRes.DataSetMetaData!.ConfigurationVersion.MinorVersion,
                Is.EqualTo(22u));
        }

        [Test]
        [TestSpec("7.2.4.6.9")]
        public void DiscoveryResponse_DataSetWriterConfiguration_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var writerConfig = new WriterGroupDataType
            {
                Name = "Group1",
                WriterGroupId = 5,
                PublishingInterval = 1000.0,
                KeepAliveTime = 5000.0,
                MaxNetworkMessageSize = 1500
            };
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt16(0x33),
                SequenceNumber = 1234,
                DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                DataSetWriterIds = new ushort[] { 10, 20 },
                WriterConfiguration = writerConfig,
                StatusCode = StatusCodes.Good
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(response, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryResponseMessage>());
            var decRes = (UadpDiscoveryResponseMessage)decoded!;
            Assert.That(decRes.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.DataSetWriterConfiguration));
            Assert.That(decRes.SequenceNumber, Is.EqualTo(1234));
            Assert.That(decRes.DataSetWriterIds, Is.EqualTo(new ushort[] { 10, 20 }));
            Assert.That(decRes.WriterConfiguration, Is.Not.Null);
            Assert.That(decRes.WriterConfiguration!.Name, Is.EqualTo("Group1"));
            Assert.That(decRes.WriterConfiguration!.WriterGroupId, Is.EqualTo((ushort)5));
            Assert.That(decRes.WriterConfiguration!.PublishingInterval, Is.EqualTo(1000.0));
        }

        [Test]
        [TestSpec("7.2.4.6.7")]
        public void DiscoveryResponse_PublisherEndpoints_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://host:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromGuid((Uuid)Guid.NewGuid()),
                SequenceNumber = 7,
                DiscoveryType = UadpDiscoveryType.PublisherEndpoints,
                PublisherEndpoints = new[] { endpoint },
                StatusCode = StatusCodes.Good
            };

            byte[] encoded = UadpDiscoveryCoder.Encode(response, context);
            PubSubNetworkMessage? decoded = UadpDecoder.Decode(encoded, context);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryResponseMessage>());
            var decRes = (UadpDiscoveryResponseMessage)decoded!;
            Assert.That(decRes.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.PublisherEndpoints));
            Assert.That(decRes.SequenceNumber, Is.EqualTo(7));
            Assert.That(decRes.PublisherEndpoints, Has.Count.EqualTo(1));
            Assert.That(decRes.PublisherEndpoints[0].EndpointUrl,
                Is.EqualTo("opc.tcp://host:4840"));
        }

        [Test]
        public void DiscoveryEncoder_NullMessage_Throws()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            Assert.That(() => UadpDiscoveryCoder.Encode(null!, context),
                Throws.ArgumentNullException);
        }

        [Test]
        public void DiscoveryEncoder_NullContext_Throws()
        {
            var request = new UadpDiscoveryRequestMessage
            {
                PublisherId = PublisherId.FromByte(1),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData
            };
            Assert.That(() => UadpDiscoveryCoder.Encode(request, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void DiscoveryEncoder_ForeignMessageType_Throws()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var foreign = new UadpNetworkMessage
            {
                PublisherId = PublisherId.FromByte(1)
            };
            Assert.That(() => UadpDiscoveryCoder.Encode(foreign, context),
                Throws.InvalidOperationException);
        }

        [Test]
        public void DiscoveryRequest_DefaultTransportProfile_IsUadp()
        {
            var request = new UadpDiscoveryRequestMessage
            {
                PublisherId = PublisherId.FromByte(1)
            };
            Assert.That(request.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }

        [Test]
        public void DiscoveryResponse_DefaultTransportProfile_IsUadp()
        {
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromByte(1)
            };
            Assert.That(response.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }
    }
}
