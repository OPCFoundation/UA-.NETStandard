/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// UADP Network Message
    /// </summary>
    public class UadpNetworkMessage : UaNetworkMessage
    {
        /// <summary>
        /// The UADPVersion for this specification version is 1.
        /// </summary>
        private const byte kUadpVersion = 1;
        private const byte kPublishedIdTypeUsedBits = 0x07;
        private const byte kUADPVersionBitMask = 0x0F;
        private const byte kPublishedIdResetMask = 0xFC;

        private byte m_uadpVersion;
        private object m_publisherId;

        /// <summary>
        /// Create new instance of UadpNetworkMessage
        /// </summary>
        internal UadpNetworkMessage(ILogger logger)
            : this(null, [], logger)
        {
        }

        /// <summary>
        /// Create new instance of UadpNetworkMessage
        /// </summary>
        /// <param name="writerGroupConfiguration">The <see cref="WriterGroupDataType"/> conflagration object that produced this message.</param>
        /// <param name="uadpDataSetMessages"><see cref="UadpDataSetMessage"/> list as input</param>
        /// <param name="logger">A contextual logger to log to</param>
        public UadpNetworkMessage(
            WriterGroupDataType writerGroupConfiguration,
            List<UadpDataSetMessage> uadpDataSetMessages,
            ILogger logger = null)
            : base(
                writerGroupConfiguration,
                uadpDataSetMessages?.ConvertAll<UaDataSetMessage>(x => x) ?? [],
                logger)
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            UADPNetworkMessageType = UADPNetworkMessageType.DataSetMessage;
        }

        /// <summary>
        /// Create new instance of <see cref="UadpNetworkMessage"/> as a DiscoveryResponse DataSetMetaData message
        /// </summary>
        public UadpNetworkMessage(
            WriterGroupDataType writerGroupConfiguration,
            DataSetMetaDataType metadata,
            ILogger logger = null)
            : base(writerGroupConfiguration, metadata, logger)
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            UADPNetworkMessageType = UADPNetworkMessageType.DiscoveryResponse;
            UADPDiscoveryType = UADPNetworkMessageDiscoveryType.DataSetMetaData;

            SetFlagsDiscoveryResponse();
        }

        /// <summary>
        /// Create new instance of <see cref="UadpNetworkMessage"/> as a DiscoveryRequest of specified type
        /// </summary>
        public UadpNetworkMessage(
            UADPNetworkMessageDiscoveryType discoveryType,
            ILogger logger = null)
            : base(null, [], logger)
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            UADPNetworkMessageType = UADPNetworkMessageType.DiscoveryRequest;
            UADPDiscoveryType = discoveryType;

            SetFlagsDiscoveryRequest();
        }

        /// <summary>
        /// Create new instance of <see cref="UadpNetworkMessage"/> as a DiscoveryResponse of PublisherEndpoints type
        /// </summary>
        public UadpNetworkMessage(
            EndpointDescription[] publisherEndpoints,
            StatusCode publisherProvidesEndpoints,
            ILogger logger = null)
            : base(null, [], logger)
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            PublisherEndpoints = publisherEndpoints;
            PublisherProvideEndpoints = publisherProvidesEndpoints;

            UADPNetworkMessageType = UADPNetworkMessageType.DiscoveryResponse;
            UADPDiscoveryType = UADPNetworkMessageDiscoveryType.PublisherEndpoint;

            SetFlagsDiscoveryResponse();
        }

        /// <summary>
        /// Create new instance of <see cref="UadpNetworkMessage"/> as a DiscoveryResponse of DataSetWriterConfiguration message
        /// </summary>
        public UadpNetworkMessage(
            ushort[] writerIds,
            WriterGroupDataType writerConfig,
            StatusCode[] streamStatusCodes,
            ILogger logger = null)
            : base(null, [], logger)
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            DataSetWriterIds = writerIds;

            UADPNetworkMessageType = UADPNetworkMessageType.DiscoveryResponse;
            UADPDiscoveryType = UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration;
            DataSetWriterConfiguration = writerConfig;
            MessageStatusCodes = streamStatusCodes;

            SetFlagsDiscoveryResponse();
        }

        /// <summary>
        /// NetworkMessageContentMask contains the mask that will be used to check NetworkMessage options selected for usage
        /// </summary>
        public UadpNetworkMessageContentMask NetworkMessageContentMask { get; private set; }

        /// <summary>
        /// Get the UADP network message type
        /// </summary>
        public UADPNetworkMessageType UADPNetworkMessageType { get; private set; }

        /// <summary>
        /// Get the UADP network message discovery type
        /// </summary>
        public UADPNetworkMessageDiscoveryType UADPDiscoveryType { get; private set; }

        /// <summary>
        /// Get/Set the StatusCodes
        /// </summary>
        public StatusCode[] MessageStatusCodes { get; set; }

        /// <summary>
        /// Get the DataSetWriterConfig
        /// </summary>
        public WriterGroupDataType DataSetWriterConfiguration { get; set; }

        /// <summary>
        /// Discovery DataSetWriter Identifiers
        /// </summary>
        public ushort[] DataSetWriterIds { get; set; }

        /// <summary>
        /// Get and Set Uadp version
        /// </summary>
        public byte UADPVersion
        {
            get => m_uadpVersion;
            set => m_uadpVersion = Convert.ToByte(value & kUADPVersionBitMask);
        }

        /// <summary>
        /// Get Uadp Flags
        /// </summary>
        public UADPFlagsEncodingMask UADPFlags { get; private set; }

        /// <summary>
        /// Get ExtendedFlags1
        /// </summary>
        public ExtendedFlags1EncodingMask ExtendedFlags1 { get; private set; }

        /// <summary>
        /// Get ExtendedFlags2
        /// </summary>
        public ExtendedFlags2EncodingMask ExtendedFlags2 { get; private set; }

        /// <summary>
        /// Get and Set PublisherId type
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public object PublisherId
        {
            get => m_publisherId;
            set
            {
                // Just in case value is a positive signed Integer
                // Try to bring it to an accepted type (will overflow if value doesn't fit)
                switch (value)
                {
                    case short int16Value:
                        m_publisherId = int16Value > 0 ? (ushort)int16Value : value;
                        break;
                    case int int32Value:
                        m_publisherId = int32Value > 0 ? (uint)int32Value : value;
                        break;
                    case long int64Value:
                        m_publisherId = int64Value > 0 ? (ulong)int64Value : value;
                        break;
                    default:
                        m_publisherId = value;
                        break;
                }

                // Remove previous PublisherId data type
                ExtendedFlags1 &= (ExtendedFlags1EncodingMask)kPublishedIdResetMask;

                // ExtendedFlags1: Bit range 0-2: PublisherId Type
                PublisherIdTypeEncodingMask publishedIdTypeType
                    = PublisherIdTypeEncodingMask.Reserved;

                if (m_publisherId is byte)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.Byte;
                }
                else if (m_publisherId is ushort)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.UInt16;
                }
                else if (m_publisherId is uint)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.UInt32;
                }
                else if (m_publisherId is ulong)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.UInt64;
                }
                else if (m_publisherId is string)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.String;
                }

                ExtendedFlags1 |= (ExtendedFlags1EncodingMask)publishedIdTypeType;
            }
        }

        /// <summary>
        /// Get and Set DataSetClassId
        /// </summary>
        public Guid DataSetClassId { get; set; }

        /// <summary>
        /// Get and Set GroupFlags
        /// </summary>
        public GroupFlagsEncodingMask GroupFlags { get; private set; }

        /// <summary>
        /// Get and Set VersionTime type: it represents the time in seconds since the year 2000
        /// </summary>
        public uint GroupVersion { get; set; }

        /// <summary>
        /// Get and Set NetworkMessageNumber
        /// </summary>
        public ushort NetworkMessageNumber { get; set; }

        /// <summary>
        /// Get and Set SequenceNumber
        /// </summary>
        public ushort SequenceNumber { get; set; }

        /// <summary>
        /// Get and Set Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// PicoSeconds
        /// </summary>
        public ushort PicoSeconds { get; set; }

        /// <summary>
        /// Get and Set SecurityFlags
        /// </summary>
        public SecurityFlagsEncodingMask SecurityFlags { get; set; }

        /// <summary>
        /// Get and Set SecurityTokenId has IntegerId type
        /// </summary>
        public uint SecurityTokenId { get; set; }

        /// <summary>
        /// Get and Set NonceLength
        /// </summary>
        public byte NonceLength { get; set; }

        /// <summary>
        /// Get and Set MessageNonce contains [NonceLength]
        /// </summary>
        public byte[] MessageNonce { get; set; }

        /// <summary>
        /// Get and Set SecurityFooterSize
        /// </summary>
        public ushort SecurityFooterSize { get; set; }

        /// <summary>
        /// Get and Set SecurityFooter
        /// </summary>
        public byte[] SecurityFooter { get; set; }

        /// <summary>
        /// Get and Set Signature
        /// </summary>
        public byte[] Signature { get; set; }

        /// <summary>
        /// Discovery Publisher Endpoints message
        /// </summary>
        internal EndpointDescription[] PublisherEndpoints { get; set; }

        /// <summary>
        /// StatusCode that specifies if a Discovery message provides PublisherEndpoints
        /// </summary>
        internal StatusCode PublisherProvideEndpoints { get; set; }

        /// <summary>
        /// Set network message content mask
        /// </summary>
        public void SetNetworkMessageContentMask(
            UadpNetworkMessageContentMask networkMessageContentMask)
        {
            NetworkMessageContentMask = networkMessageContentMask;

            SetFlagsDataSetNetworkMessageType();
        }

        /// <summary>
        /// Encodes the object and returns the resulting byte array.
        /// </summary>
        /// <param name="messageContext">The context.</param>
        public override byte[] Encode(IServiceMessageContext messageContext)
        {
            using var stream = new MemoryStream();
            Encode(messageContext, stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Encodes the object in the specified stream.
        /// </summary>
        /// <param name="messageContext">The system context.</param>
        /// <param name="stream">The stream to use.</param>
        public override void Encode(IServiceMessageContext messageContext, Stream stream)
        {
            using var binaryEncoder = new BinaryEncoder(stream, messageContext, true);
            if (UADPNetworkMessageType == UADPNetworkMessageType.DataSetMessage)
            {
                EncodeDataSetNetworkMessageType(binaryEncoder);
            }
            else
            {
                EncodeNetworkMessageHeader(binaryEncoder);

                if (UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse)
                {
                    EncodeDiscoveryResponse(binaryEncoder);
                }
                else if (UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest)
                {
                    EncodeDiscoveryRequest(binaryEncoder);
                }
            }
        }

        /// <summary>
        /// Decodes the message
        /// </summary>
        public override void Decode(
            IServiceMessageContext context,
            byte[] message,
            IList<DataSetReaderDataType> dataSetReaders)
        {
            using var binaryDecoder = new BinaryDecoder(message, context);
            // 1. decode network message header (PublisherId & DataSetClassId)
            DecodeNetworkMessageHeader(binaryDecoder);

            //decode network messages according to their type
            if (UADPNetworkMessageType == UADPNetworkMessageType.DataSetMessage)
            {
                if (dataSetReaders == null || dataSetReaders.Count == 0)
                {
                    return;
                }
                //decode bytes using dataset reader information
                DecodeSubscribedDataSets(binaryDecoder, dataSetReaders);
            }
            else if (UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse)
            {
                DecodeDiscoveryResponse(binaryDecoder);
            }
            else if (UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest)
            {
                DecodeDiscoveryRequest(binaryDecoder);
            }
        }

        /// <summary>
        /// Encodes the DataSet Network message in a binary stream.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="binaryEncoder"/></exception>
        private void EncodeDataSetNetworkMessageType(BinaryEncoder binaryEncoder)
        {
            if (binaryEncoder == null)
            {
                throw new ArgumentException(null, nameof(binaryEncoder));
            }
            EncodeNetworkMessageHeader(binaryEncoder);
            EncodeGroupMessageHeader(binaryEncoder);
            EncodePayloadHeader(binaryEncoder);
            EncodeExtendedNetworkMessageHeader(binaryEncoder);
            EncodeSecurityHeader(binaryEncoder);
            EncodePayload(binaryEncoder);
            EncodeSecurityFooter(binaryEncoder);
            //EncodeSignature(encoder);
        }

        /// <summary>
        /// Encodes the NetworkMessage as a DiscoveryResponse of DataSetMetaData Type
        /// </summary>
        private void EncodeDataSetMetaData(BinaryEncoder binaryEncoder)
        {
            if (DataSetWriterId != null)
            {
                binaryEncoder.WriteUInt16("DataSetWriterId", DataSetWriterId.Value);
            }
            else
            {
                m_logger.LogInformation(
                    "The UADP DiscoveryResponse DataSetMetaData message cannot be encoded: The DataSetWriterId property is missing. Value 0 will be used.");
                binaryEncoder.WriteUInt16("DataSetWriterId", 0);
            }

            if (m_metadata == null)
            {
                m_logger.LogInformation(
                    "The UADP DiscoveryResponse DataSetMetaData message cannot be encoded: The MetaData property is missing. Value null will be used.");
            }
            binaryEncoder.WriteEncodeable("MetaData", m_metadata, typeof(DataSetMetaDataType));

            binaryEncoder.WriteStatusCode("StatusCode", StatusCodes.Good);
        }

        /// <summary>
        /// Encodes the NetworkMessage as a DiscoveryResponse of DataSetWriterConfiguration Type
        /// </summary>
        private void EncodeDataSetWriterConfiguration(BinaryEncoder binaryEncoder)
        {
            if (DataSetWriterIds != null)
            {
                binaryEncoder.WriteUInt16Array("DataSetWriterId", DataSetWriterIds);
            }
            else
            {
                m_logger.LogInformation(
                    "The UADP DiscoveryResponse DataSetWriterConfiguration message cannot be encoded: The DataSetWriterId property is missing. Value 0 will be used.");
                binaryEncoder.WriteUInt16Array("DataSetWriterIds", []);
            }

            if (DataSetWriterIds == null)
            {
                m_logger.LogInformation(
                    "The UADP DiscoveryResponse DataSetWriterConfiguration message cannot be encoded: The DataSetWriterConfiguration property is missing. Value null will be used.");
            }
            else
            {
                binaryEncoder.WriteEncodeable(
                    "DataSetWriterConfiguration",
                    DataSetWriterConfiguration,
                    typeof(WriterGroupDataType));
            }

            binaryEncoder.WriteStatusCodeArray("StatusCodes", MessageStatusCodes);
        }

        /// <summary>
        /// Encodes the NetworkMessage as a DiscoveryResponse of EndpointDescription[] Type
        /// </summary>
        private void EncodePublisherEndpoints(BinaryEncoder binaryEncoder)
        {
            binaryEncoder.WriteEncodeableArray(
                "Endpoints",
                PublisherEndpoints,
                typeof(EndpointDescription));

            binaryEncoder.WriteStatusCode("statusCode", PublisherProvideEndpoints);
        }

        /// <summary>
        /// Set All flags before encode/decode for a NetworkMessage that contains DataSet messages
        /// </summary>
        private void SetFlagsDataSetNetworkMessageType()
        {
            UADPFlags = 0;
            ExtendedFlags1 &= (ExtendedFlags1EncodingMask)kPublishedIdTypeUsedBits;
            ExtendedFlags2 = 0;
            GroupFlags = 0;

            if (((int)NetworkMessageContentMask &
                ((int)UadpNetworkMessageContentMask.PublisherId |
                    (int)UadpNetworkMessageContentMask.DataSetClassId)) != 0)
            {
                //  UADPFlags: The ExtendedFlags1 shall be omitted if bit 7 of the UADPFlags is false.
                // Enable ExtendedFlags1 usage
                UADPFlags |= UADPFlagsEncodingMask.ExtendedFlags1;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.PublisherId) != 0)
            {
                // UADPFlags: Bit 4: PublisherId enabled
                UADPFlags |= UADPFlagsEncodingMask.PublisherId;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                // ExtendedFlags1 Bit 3: DataSetClassId enabled
                ExtendedFlags1 |= ExtendedFlags1EncodingMask.DataSetClassId;
            }

            if (((int)NetworkMessageContentMask &
                ((int)UadpNetworkMessageContentMask.GroupHeader |
                    (int)UadpNetworkMessageContentMask.WriterGroupId |
                    (int)UadpNetworkMessageContentMask.GroupVersion |
                    (int)UadpNetworkMessageContentMask.NetworkMessageNumber |
                    (int)UadpNetworkMessageContentMask.SequenceNumber)) != 0)
            {
                // UADPFlags: Bit 5: GroupHeader enabled
                UADPFlags |= UADPFlagsEncodingMask.GroupHeader;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                // GroupFlags: Bit 0: WriterGroupId enabled
                GroupFlags |= GroupFlagsEncodingMask.WriterGroupId;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                // GroupFlags: Bit 1: GroupVersion enabled
                GroupFlags |= GroupFlagsEncodingMask.GroupVersion;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                // GroupFlags: Bit 2: NetworkMessageNumber enabled
                GroupFlags |= GroupFlagsEncodingMask.NetworkMessageNumber;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                // GroupFlags: Bit 3: SequenceNumber enabled
                GroupFlags |= GroupFlagsEncodingMask.SequenceNumber;
            }

            if (((int)NetworkMessageContentMask &
                ((int)UadpNetworkMessageContentMask.Timestamp |
                    (int)UadpNetworkMessageContentMask.PicoSeconds |
                    (int)UadpNetworkMessageContentMask.PromotedFields)) != 0)
            {
                // Enable ExtendedFlags1 usage
                UADPFlags |= UADPFlagsEncodingMask.ExtendedFlags1;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                // ExtendedFlags1: Bit 5: Timestamp enabled
                ExtendedFlags1 |= ExtendedFlags1EncodingMask.Timestamp;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.PicoSeconds) != 0)
            {
                // ExtendedFlags1: Bit 6: PicoSeconds enabled
                ExtendedFlags1 |= ExtendedFlags1EncodingMask.PicoSeconds;
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.PromotedFields) != 0)
            {
                // ExtendedFlags1: Bit 7: ExtendedFlags2 enabled
                ExtendedFlags1 |= ExtendedFlags1EncodingMask.ExtendedFlags2;

                // The PromotedFields shall be omitted if bit 4 of the ExtendedFlags2 is false.
                // ExtendedFlags2: Bit 1: PromotedFields enabled
                // Wireshark: PromotedFields; omitted if bit 1 of ExtendedFlags2 is false
                ExtendedFlags2 |= ExtendedFlags2EncodingMask.PromotedFields;

                // Bit range 2-4: UADP NetworkMessage type
                // 000 NetworkMessage with DataSetMessage payload for now
            }

            if (((int)NetworkMessageContentMask &
                (int)UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // UADPFlag: Bit 6: PayloadHeader enabled
                UADPFlags |= UADPFlagsEncodingMask.PayloadHeader;
            }

            // ExtendedFlags1: Bit 4: Security enabled
            // Disable security for now
            ExtendedFlags1 &= ~ExtendedFlags1EncodingMask.Security;

            // The security footer size shall be omitted if bit 2 of the SecurityFlags is false.
            SecurityFlags &= ~SecurityFlagsEncodingMask.SecurityFooter;
        }

        /// <summary>
        /// Set All flags before encode/decode for a NetworkMessage that contains a DiscoveryResponse containing data set metadata
        /// </summary>
        private void SetFlagsDiscoveryResponse()
        {
            /* DiscoveryResponse:
             * UADPFlags bits 5 and 6 shall be false, bits 4 and 7 shall be true
             * ExtendedFlags1 bits 3, 5 and 6 shall be false, bit 7 shall be true (erata 9):Bit 4 of ExtendedFlags1 shall be true
             * ExtendedFlags2 bit 1 shall be false and the NetworkMessage type shall be discovery response
             * */
            UADPFlags = UADPFlagsEncodingMask.PublisherId | UADPFlagsEncodingMask.ExtendedFlags1;
            ExtendedFlags1 = ExtendedFlags1EncodingMask.Security |
                ExtendedFlags1EncodingMask.ExtendedFlags2;
            ExtendedFlags2 = ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse;

            // enable encoding of PublisherId in message header
            NetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId;
        }

        /// <summary>
        /// Set All flags before encode/decode for a NetworkMessage that contains A DiscoveryRequest
        /// </summary>
        private void SetFlagsDiscoveryRequest()
        {
            /* The NetworkMessage flags used with the discovery request messages shall use the following
             * bit values.
             *  UADPFlags bits 5 and 6 shall be false, bits 4 and 7 shall be true
             *  ExtendedFlags1 bits 3, 5 and 6 shall be false, bits 4 and 7 shall be true
             *  ExtendedFlags2 bit 2 shall be true, all other bits shall be false
             */
            UADPFlags = UADPFlagsEncodingMask.PublisherId | UADPFlagsEncodingMask.ExtendedFlags1;
            ExtendedFlags1 = ExtendedFlags1EncodingMask.Security |
                ExtendedFlags1EncodingMask.ExtendedFlags2;
            ExtendedFlags2 = ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest;
        }

        /// <summary>
        /// Decode the stream from decoder parameter and produce a Dataset
        /// </summary>
        public void DecodeSubscribedDataSets(
            BinaryDecoder binaryDecoder,
            IList<DataSetReaderDataType> dataSetReaders)
        {
            if (dataSetReaders == null || dataSetReaders.Count == 0)
            {
                return;
            }

            try
            {
                var dataSetReadersFiltered = new List<DataSetReaderDataType>();

                /* 6.2.8.1 PublisherId
                 The parameter PublisherId defines the Publisher to receive NetworkMessages from.
                 If the value is null, the parameter shall be ignored and all received NetworkMessages pass the PublisherId filter. */
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    //check Enabled & publisher id
                    if (dataSetReader.PublisherId.Value == null ||
                        (PublisherId != null &&
                            PublisherId.Equals(dataSetReader.PublisherId.Value)))
                    {
                        dataSetReadersFiltered.Add(dataSetReader);
                    }
                }
                if (dataSetReadersFiltered.Count == 0)
                {
                    return;
                }
                dataSetReaders = dataSetReadersFiltered;

                //continue filtering
                dataSetReadersFiltered = [];

                // 2. decode WriterGroupId
                DecodeGroupMessageHeader(binaryDecoder);
                /* 6.2.8.2 WriterGroupId
                The parameter WriterGroupId with DataType UInt16 defines the identifier of the corresponding WriterGroup.
                The default value 0 is defined as null value, and means this parameter shall be ignored.*/
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    //check WriterGroupId id
                    if (dataSetReader.WriterGroupId == 0 ||
                        dataSetReader.WriterGroupId == WriterGroupId)
                    {
                        dataSetReadersFiltered.Add(dataSetReader);
                    }
                }
                if (dataSetReadersFiltered.Count == 0)
                {
                    return;
                }
                dataSetReaders = dataSetReadersFiltered;

                // 3. decode payload header
                DecodePayloadHeader(binaryDecoder);
                // 4.
                DecodeExtendedNetworkMessageHeader(binaryDecoder);
                // 5.
                DecodeSecurityHeader(binaryDecoder);

                //6.1
                DecodePayloadSize(binaryDecoder);

                // the list of decode dataset messages for this network message
                var dataSetMessages = new List<UaDataSetMessage>();

                /* 6.2.8.3 DataSetWriterId
                The parameter DataSetWriterId with DataType UInt16 defines the DataSet selected in the Publisher for the DataSetReader.
                If the value is 0 (null), the parameter shall be ignored and all received DataSetMessages pass the DataSetWriterId filter.*/
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    var uadpDataSetMessages = new List<UaDataSetMessage>(DataSetMessages);
                    //if there is no information regarding dataSet in network message, add dummy datasetMessage to try decoding
                    if (uadpDataSetMessages.Count == 0)
                    {
                        uadpDataSetMessages.Add(new UadpDataSetMessage(m_logger));
                    }

                    // 6.2 Decode payload into DataSets
                    // Restore the encoded fields (into dataset for now) for each possible dataset reader
                    foreach (UadpDataSetMessage uadpDataSetMessage in uadpDataSetMessages
                        .OfType<UadpDataSetMessage>())
                    {
                        if (uadpDataSetMessage.DataSet != null)
                        {
                            continue; // this dataset message was already decoded
                        }

                        if (dataSetReader.DataSetWriterId == 0 ||
                            uadpDataSetMessage.DataSetWriterId == dataSetReader.DataSetWriterId)
                        {
                            //attempt to decode dataset message using the reader
                            uadpDataSetMessage.DecodePossibleDataSetReader(
                                binaryDecoder,
                                dataSetReader);
                            if (uadpDataSetMessage.DataSet != null)
                            {
                                dataSetMessages.Add(uadpDataSetMessage);
                            }
                            else if (uadpDataSetMessage.IsMetadataMajorVersionChange)
                            {
                                OnDataSetDecodeErrorOccurred(
                                    new DataSetDecodeErrorEventArgs(
                                        DataSetDecodeErrorReason.MetadataMajorVersion,
                                        this,
                                        dataSetReader));
                            }
                        }
                    }
                }

                if (m_uaDataSetMessages.Count == 0)
                {
                    // set the list of dataset messages to the network message
                    m_uaDataSetMessages.AddRange(dataSetMessages);
                }
                else
                {
                    dataSetMessages = [];
                    // check if DataSets are decoded into the existing dataSetMessages
                    foreach (UaDataSetMessage dataSetMessage in m_uaDataSetMessages)
                    {
                        if (dataSetMessage.DataSet != null)
                        {
                            dataSetMessages.Add(dataSetMessage);
                        }
                    }
                    m_uaDataSetMessages.Clear();
                    m_uaDataSetMessages.AddRange(dataSetMessages);
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception in DecodeSubscribedDataSets
                m_logger.LogError(ex, "UadpNetworkMessage.DecodeSubscribedDataSets");
            }
        }

        /// <summary>
        /// Decode the binaryDecoder content as a MetaData message
        /// </summary>
        private void DecodeMetaDataMessage(BinaryDecoder binaryDecoder)
        {
            DataSetWriterId = binaryDecoder.ReadUInt16("DataSetWriterId");
            m_metadata = binaryDecoder.ReadEncodeable(
                "MetaData",
                typeof(DataSetMetaDataType)) as DataSetMetaDataType;

            // temporary write StatusCode.Good
            StatusCode statusCode = binaryDecoder.ReadStatusCode("StatusCode");
            m_logger.LogInformation("DecodeMetaDataMessage returned: {StatusCode}", statusCode);
        }

        /// <summary>
        /// Decode the binaryDecoder content as Endpoints message
        /// </summary>
        private void DecodePublisherEndpoints(BinaryDecoder binaryDecoder)
        {
            PublisherEndpoints = (EndpointDescription[])
                binaryDecoder.ReadEncodeableArray("Endpoints", typeof(EndpointDescription));

            PublisherProvideEndpoints = binaryDecoder.ReadStatusCode("statusCode");

            m_logger.LogInformation("DecodePublisherEndpointsMessage returned: {PublisherProvideEndpoints}", PublisherProvideEndpoints);
        }

        /// <summary>
        /// Decode the binaryDecoder content as a DataSetWriterConfiguration message
        /// </summary>
        /// <param name="binaryDecoder">the decoder</param>
        private void DecodeDataSetWriterConfigurationMessage(BinaryDecoder binaryDecoder)
        {
            DataSetWriterIds = [.. binaryDecoder.ReadUInt16Array("DataSetWriterIds")];

            var dataSetWriterConfigurationDecoded =
                binaryDecoder.ReadEncodeable(
                    "DataSetWriterConfiguration",
                    typeof(WriterGroupDataType)) as
                WriterGroupDataType;

            DataSetWriterConfiguration =
                dataSetWriterConfigurationDecoded.MaxNetworkMessageSize != 0
                    ? dataSetWriterConfigurationDecoded
                    : null;

            // temporary write StatusCode.Good
            MessageStatusCodes = [.. binaryDecoder.ReadStatusCodeArray("StatusCodes")];
            m_logger.LogInformation("DecodeDataSetWriterConfigurationMessage returned: {MessageStatusCodes}", MessageStatusCodes);
        }

        /// <summary>
        ///  Encode Network Message Header
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void EncodeNetworkMessageHeader(BinaryEncoder encoder)
        {
            // byte[0..3] UADPVersion value 1 (for now)
            // byte[4..7] UADPFlags
            encoder.WriteByte("VersionFlags", (byte)(UADPVersion | (byte)UADPFlags));

            if ((UADPFlags & UADPFlagsEncodingMask.ExtendedFlags1) != 0)
            {
                encoder.WriteByte("ExtendedFlags1", (byte)ExtendedFlags1);
            }

            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.ExtendedFlags2) != 0)
            {
                encoder.WriteByte("ExtendedFlags2", (byte)ExtendedFlags2);
            }

            if ((UADPFlags & UADPFlagsEncodingMask.PublisherId) != 0)
            {
                if (PublisherId == null)
                {
                    m_logger.LogError(
                        Utils.TraceMasks.Error,
                        "NetworkMessageHeader cannot be encoded. PublisherId is null but it is expected to be encoded.");
                }
                else
                {
                    var publisherIdEncoding = (PublisherIdTypeEncodingMask)
                        ((byte)ExtendedFlags1 & kPublishedIdTypeUsedBits);
                    switch (publisherIdEncoding)
                    {
                        case PublisherIdTypeEncodingMask.Byte:
                            encoder.WriteByte(
                                "PublisherId",
                                Convert.ToByte(PublisherId, CultureInfo.InvariantCulture));
                            break;
                        case PublisherIdTypeEncodingMask.UInt16:
                            encoder.WriteUInt16(
                                "PublisherId",
                                Convert.ToUInt16(PublisherId, CultureInfo.InvariantCulture));
                            break;
                        case PublisherIdTypeEncodingMask.UInt32:
                            encoder.WriteUInt32(
                                "PublisherId",
                                Convert.ToUInt32(PublisherId, CultureInfo.InvariantCulture));
                            break;
                        case PublisherIdTypeEncodingMask.UInt64:
                            encoder.WriteUInt64(
                                "PublisherId",
                                Convert.ToUInt64(PublisherId, CultureInfo.InvariantCulture));
                            break;
                        case PublisherIdTypeEncodingMask.String:
                            encoder.WriteString(
                                "PublisherId",
                                Convert.ToString(PublisherId, CultureInfo.InvariantCulture));
                            break;
                        case PublisherIdTypeEncodingMask.Reserved:
                            break;
                        default:
                            throw ServiceResultException.Unexpected(
                                $"Unexpected PublisherIdTypeEncodingMask {publisherIdEncoding}");
                    }
                }
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                encoder.WriteGuid("DataSetClassId", DataSetClassId);
            }
        }

        /// <summary>
        /// Encode Group Message Header
        /// </summary>
        private void EncodeGroupMessageHeader(BinaryEncoder encoder)
        {
            if ((
                    NetworkMessageContentMask &
                    (
                        UadpNetworkMessageContentMask.GroupHeader |
                        UadpNetworkMessageContentMask.WriterGroupId |
                        UadpNetworkMessageContentMask.GroupVersion |
                        UadpNetworkMessageContentMask.NetworkMessageNumber |
                        UadpNetworkMessageContentMask.SequenceNumber)
                ) != UadpNetworkMessageContentMask.None)
            {
                encoder.WriteByte("GroupFlags", (byte)GroupFlags);
            }
            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                encoder.WriteUInt16("WriterGroupId", WriterGroupId);
            }
            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                encoder.WriteUInt32("GroupVersion", GroupVersion);
            }
            if ((NetworkMessageContentMask &
                UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                encoder.WriteUInt16("NetworkMessageNumber", NetworkMessageNumber);
            }
            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                encoder.WriteUInt16("SequenceNumber", SequenceNumber);
            }
        }

        /// <summary>
        /// Encode Payload Header
        /// </summary>
        private void EncodePayloadHeader(BinaryEncoder encoder)
        {
            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                encoder.WriteByte("Count", (byte)DataSetMessages.Count);

                // Collect DataSetSetMessages headers
                for (int index = 0; index < DataSetMessages.Count; index++)
                {
                    if (DataSetMessages[index] is UadpDataSetMessage uadpDataSetMessage &&
                        uadpDataSetMessage.DataSet != null)
                    {
                        encoder.WriteUInt16("DataSetWriterId", uadpDataSetMessage.DataSetWriterId);
                    }
                }
            }
        }

        /// <summary>
        ///  Encode Extended network message header
        /// </summary>
        private void EncodeExtendedNetworkMessageHeader(BinaryEncoder encoder)
        {
            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                encoder.WriteDateTime("Timestamp", Timestamp);
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
            {
                encoder.WriteUInt16("PicoSeconds", PicoSeconds);
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PromotedFields) != 0)
            {
                EncodePromotedFields(encoder);
            }
        }

        /// <summary>
        /// Encode promoted fields
        /// </summary>
        private static void EncodePromotedFields(BinaryEncoder encoder)
        {
            // todo: Promoted fields not supported
        }

        /// <summary>
        /// Encode security header
        /// </summary>
        private void EncodeSecurityHeader(BinaryEncoder encoder)
        {
            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.Security) != 0)
            {
                encoder.WriteByte("SecurityFlags", (byte)SecurityFlags);

                encoder.WriteUInt32("SecurityTokenId", SecurityTokenId);
                encoder.WriteByte("NonceLength", NonceLength);
                MessageNonce = new byte[NonceLength];
                encoder.WriteByteArray("MessageNonce", MessageNonce);

                if ((SecurityFlags & SecurityFlagsEncodingMask.SecurityFooter) != 0)
                {
                    encoder.WriteUInt16("SecurityFooterSize", SecurityFooterSize);
                }
            }
        }

        /// <summary>
        /// Encode payload
        /// </summary>
        private void EncodePayload(BinaryEncoder encoder)
        {
            int payloadStartPositionInStream = encoder.Position;
            if (DataSetMessages.Count > 1 &&
                (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                //skip 2 * dataset count for each dataset payload size
                encoder.Position += 2 * DataSetMessages.Count;
            }
            //encode dataset message payload
            foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages
                .OfType<UadpDataSetMessage>())
            {
                uadpDataSetMessage.Encode(encoder);
            }

            if (DataSetMessages.Count > 1 &&
                (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                int payloadEndPositionInStream = encoder.Position;
                encoder.Position = payloadStartPositionInStream;
                foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages
                    .OfType<UadpDataSetMessage>())
                {
                    encoder.WriteUInt16("Size", uadpDataSetMessage.PayloadSizeInStream);
                }
                encoder.Position = payloadEndPositionInStream;
            }
        }

        /// <summary>
        /// Encode security footer
        /// </summary>
        private void EncodeSecurityFooter(BinaryEncoder encoder)
        {
            if ((SecurityFlags & SecurityFlagsEncodingMask.SecurityFooter) != 0)
            {
                encoder.WriteByteArray("SecurityFooter", SecurityFooter);
            }
        }

        private void EncodeDiscoveryResponse(BinaryEncoder binaryEncoder)
        {
            binaryEncoder.WriteByte("ResponseType", (byte)UADPDiscoveryType);
            // A strictly monotonically increasing sequence number assigned to each discovery response sent in the scope of a PublisherId.
            binaryEncoder.WriteUInt16("SequenceNumber", SequenceNumber);

            switch (UADPDiscoveryType)
            {
                case UADPNetworkMessageDiscoveryType.DataSetMetaData:
                    EncodeDataSetMetaData(binaryEncoder);
                    break;
                case UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration:
                    EncodeDataSetWriterConfiguration(binaryEncoder);
                    break;
                case UADPNetworkMessageDiscoveryType.PublisherEndpoint:
                    EncodePublisherEndpoints(binaryEncoder);
                    break;
                case UADPNetworkMessageDiscoveryType.None:
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected UADPNetworkMessageDiscoveryType {UADPDiscoveryType}");
            }
        }

        private void EncodeDiscoveryRequest(BinaryEncoder binaryEncoder)
        {
            // RequestType => InformationType
            binaryEncoder.WriteByte("RequestType", (byte)UADPDiscoveryType);
            binaryEncoder.WriteUInt16Array("DataSetWriterIds", DataSetWriterIds);
        }

        /// <summary>
        /// Encode Network Message Header
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void DecodeNetworkMessageHeader(BinaryDecoder decoder)
        {
            // byte[0..3] UADPVersion value 1 (for now)
            // byte[4..7] UADPFlags
            byte versionFlags = decoder.ReadByte("VersionFlags");
            UADPVersion = (byte)(versionFlags & kUADPVersionBitMask);
            // Decode UADPFlags
            UADPFlags = (UADPFlagsEncodingMask)(versionFlags & 0xF0);

            // Decode the ExtendedFlags1
            if ((UADPFlags & UADPFlagsEncodingMask.ExtendedFlags1) != 0)
            {
                ExtendedFlags1 = (ExtendedFlags1EncodingMask)decoder.ReadByte("ExtendedFlags1");
            }

            // Decode the ExtendedFlags2
            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.ExtendedFlags2) != 0)
            {
                ExtendedFlags2 = (ExtendedFlags2EncodingMask)decoder.ReadByte("ExtendedFlags2");
            }
            // calculate UADPNetworkMessageType
            if ((ExtendedFlags2 &
                ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest) != 0)
            {
                UADPNetworkMessageType = UADPNetworkMessageType.DiscoveryRequest;
            }
            else if ((ExtendedFlags2 &
                ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse) != 0)
            {
                UADPNetworkMessageType = UADPNetworkMessageType.DiscoveryResponse;
            }
            else
            {
                UADPNetworkMessageType = UADPNetworkMessageType.DataSetMessage;
            }

            // Decode PublisherId
            if ((UADPFlags & UADPFlagsEncodingMask.PublisherId) != 0)
            {
                var publisherIdEncoding = (PublisherIdTypeEncodingMask)
                    ((byte)ExtendedFlags1 & kPublishedIdTypeUsedBits);
                switch (publisherIdEncoding)
                {
                    case PublisherIdTypeEncodingMask.UInt16:
                        m_publisherId = decoder.ReadUInt16("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.UInt32:
                        m_publisherId = decoder.ReadUInt32("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.UInt64:
                        m_publisherId = decoder.ReadUInt64("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.String:
                        m_publisherId = decoder.ReadString("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.Byte:
                        m_publisherId = decoder.ReadByte("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.Reserved:
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected PublisherIdTypeEncodingMask {publisherIdEncoding}");
                }
            }

            // Decode DataSetClassId
            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.DataSetClassId) != 0)
            {
                DataSetClassId = decoder.ReadGuid("DataSetClassId");
            }
        }

        /// <summary>
        /// Decode Group Message Header
        /// </summary>
        private void DecodeGroupMessageHeader(BinaryDecoder decoder)
        {
            // Decode GroupHeader (that holds GroupFlags)
            if ((UADPFlags & UADPFlagsEncodingMask.GroupHeader) != 0)
            {
                GroupFlags = (GroupFlagsEncodingMask)decoder.ReadByte("GroupFlags");
            }

            // Decode WriterGroupId
            if ((GroupFlags & GroupFlagsEncodingMask.WriterGroupId) != 0)
            {
                WriterGroupId = decoder.ReadUInt16("WriterGroupId");
            }

            // Decode GroupVersion
            if ((GroupFlags & GroupFlagsEncodingMask.GroupVersion) != 0)
            {
                GroupVersion = decoder.ReadUInt32("GroupVersion");
            }

            // Decode NetworkMessageNumber
            if ((GroupFlags & GroupFlagsEncodingMask.NetworkMessageNumber) != 0)
            {
                NetworkMessageNumber = decoder.ReadUInt16("NetworkMessageNumber");
            }

            // Decode SequenceNumber
            if ((GroupFlags & GroupFlagsEncodingMask.SequenceNumber) != 0)
            {
                SequenceNumber = decoder.ReadUInt16("SequenceNumber");
            }
        }

        /// <summary>
        /// Decode Payload Header
        /// </summary>
        private void DecodePayloadHeader(BinaryDecoder decoder)
        {
            // Decode PayloadHeader
            if ((UADPFlags & UADPFlagsEncodingMask.PayloadHeader) != 0)
            {
                byte count = decoder.ReadByte("Count");
                for (int idx = 0; idx < count; idx++)
                {
                    m_uaDataSetMessages.Add(new UadpDataSetMessage(m_logger));
                }

                // collect DataSetSetMessages headers
                foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages
                    .OfType<UadpDataSetMessage>())
                {
                    uadpDataSetMessage.DataSetWriterId = decoder.ReadUInt16("DataSetWriterId");
                }
            }
        }

        /// <summary>
        /// Decode extended network message header
        /// </summary>
        private void DecodeExtendedNetworkMessageHeader(BinaryDecoder decoder)
        {
            // Decode Timestamp
            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.Timestamp) != 0)
            {
                Timestamp = decoder.ReadDateTime("Timestamp");
            }

            // Decode PicoSeconds
            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.PicoSeconds) != 0)
            {
                PicoSeconds = decoder.ReadUInt16("PicoSeconds");
            }

            // Decode Promoted Fields
            if ((ExtendedFlags2 & ExtendedFlags2EncodingMask.PromotedFields) != 0)
            {
                DecodePromotedFields(decoder);
            }
        }

        /// <summary>
        /// Decode promoted fields
        /// </summary>
        private static void DecodePromotedFields(BinaryDecoder decoder)
        {
            // todo: Promoted fields not supported
        }

        /// <summary>
        /// Decode  payload size and prepare for decoding payload
        /// </summary>
        private void DecodePayloadSize(BinaryDecoder decoder)
        {
            if (DataSetMessages.Count > 1)
            {
                // Decode PayloadHeader Size
                if ((UADPFlags & UADPFlagsEncodingMask.PayloadHeader) != 0)
                {
                    foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages
                        .OfType<UadpDataSetMessage>())
                    {
                        // Save the size
                        uadpDataSetMessage.PayloadSizeInStream = decoder.ReadUInt16("Size");
                    }
                }
            }
            BinaryDecoder binaryDecoder = decoder;
            if (binaryDecoder != null)
            {
                int offset = 0;
                // set start position of dataset message in binary stream
                foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages
                    .OfType<UadpDataSetMessage>())
                {
                    uadpDataSetMessage.StartPositionInStream = binaryDecoder.Position + offset;
                    offset += uadpDataSetMessage.PayloadSizeInStream;
                }
            }
        }

        /// <summary>
        /// Decode security header
        /// </summary>
        private void DecodeSecurityHeader(BinaryDecoder decoder)
        {
            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.Security) != 0)
            {
                SecurityFlags = (SecurityFlagsEncodingMask)decoder.ReadByte("SecurityFlags");

                SecurityTokenId = decoder.ReadUInt32("SecurityTokenId");
                NonceLength = decoder.ReadByte("NonceLength");
                MessageNonce = [.. decoder.ReadByteArray("MessageNonce")];

                if ((SecurityFlags & SecurityFlagsEncodingMask.SecurityFooter) != 0)
                {
                    SecurityFooterSize = decoder.ReadUInt16("SecurityFooterSize");
                }
            }
        }

        /// <summary>
        /// Decode the Discovery Request Header
        /// </summary>
        private void DecodeDiscoveryRequest(BinaryDecoder binaryDecoder)
        {
            UADPDiscoveryType = (UADPNetworkMessageDiscoveryType)binaryDecoder.ReadByte(
                "RequestType");
            DataSetWriterIds = binaryDecoder.ReadUInt16Array("DataSetWriterIds")?.ToArray();
        }

        /// <summary>
        /// Decode the Discovery Response Header
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void DecodeDiscoveryResponse(BinaryDecoder binaryDecoder)
        {
            UADPDiscoveryType = (UADPNetworkMessageDiscoveryType)binaryDecoder.ReadByte(
                "ResponseType");
            // A strictly monotonically increasing sequence number assigned to each discovery response sent in the scope of a PublisherId.
            SequenceNumber = binaryDecoder.ReadUInt16("SequenceNumber");

            switch (UADPDiscoveryType)
            {
                case UADPNetworkMessageDiscoveryType.DataSetMetaData:
                    DecodeMetaDataMessage(binaryDecoder);
                    break;
                case UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration:
                    DecodeDataSetWriterConfigurationMessage(binaryDecoder);
                    break;
                case UADPNetworkMessageDiscoveryType.PublisherEndpoint:
                    DecodePublisherEndpoints(binaryDecoder);
                    break;
                case UADPNetworkMessageDiscoveryType.None:
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected UADPNetworkMessageDiscoveryType {UADPDiscoveryType}");
            }
        }
    }
}
