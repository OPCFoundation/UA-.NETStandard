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
using System.IO;
using static Opc.Ua.Utils;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// UADP Network Message
    /// </summary>
    public class UadpNetworkMessage : UaNetworkMessage
    {
        #region Fields
        // The UADPVersion for this specification version is 1.
        private const byte kUadpVersion = 1;
        private const byte kPublishedIdTypeUsedBits = 0x07;
        private const byte kUADPVersionBitMask = 0x0F;
        private const byte kPublishedIdResetMask = 0xFC;

        private byte m_uadpVersion;
        private object m_publisherId;
        private UADPNetworkMessageType m_uadpNetworkMessageType;
        private UADPNetworkMessageDiscoveryType m_discoveryType;
        private UInt16[] m_dataSetWriterIds;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of UadpNetworkMessage
        /// </summary>
        internal UadpNetworkMessage() : this(null, new List<UadpDataSetMessage>())
        {

        }

        /// <summary>
        /// Create new instance of UadpNetworkMessage
        /// </summary>
        /// <param name="writerGroupConfiguration">The <see cref="WriterGroupDataType"/> conflagration object that produced this message.</param> 
        /// <param name="uadpDataSetMessages"><see cref="UadpDataSetMessage"/> list as input</param>
        public UadpNetworkMessage(WriterGroupDataType writerGroupConfiguration, List<UadpDataSetMessage> uadpDataSetMessages)
            : base(writerGroupConfiguration, uadpDataSetMessages?.ConvertAll<UaDataSetMessage>(x => (UaDataSetMessage)x) ?? new List<UaDataSetMessage>())
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            m_uadpNetworkMessageType = UADPNetworkMessageType.DataSetMessage;
        }

        /// <summary>
        /// Create new instance of <see cref="UadpNetworkMessage"/> as a DiscoveryResponse DataSetMetadata message
        /// </summary>
        public UadpNetworkMessage(WriterGroupDataType writerGroupConfiguration, DataSetMetaDataType metadata)
            : base(writerGroupConfiguration, metadata)
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            m_uadpNetworkMessageType = UADPNetworkMessageType.DiscoveryResponse;
            m_discoveryType = UADPNetworkMessageDiscoveryType.DataSetMetaData;

            SetFlagsDiscoveryResponseMetaData();
        }

        /// <summary>
        /// Create new instance of <see cref="UadpNetworkMessage"/> as a DiscoveryRequest of specified type
        /// </summary>
        public UadpNetworkMessage(UADPNetworkMessageDiscoveryType discoveryType)
            : base(null, new List<UaDataSetMessage>())
        {
            UADPVersion = kUadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            m_uadpNetworkMessageType = UADPNetworkMessageType.DiscoveryRequest;
            m_discoveryType = discoveryType;

            SetFlagsDiscoveryRequest();
        }

        #endregion

        #region Properties

        /// <summary>
        /// NetworkMessageContentMask contains the mask that will be used to check NetworkMessage options selected for usage  
        /// </summary>
        public UadpNetworkMessageContentMask NetworkMessageContentMask { get; private set; }

        /// <summary>
        /// Get the UADP network message type
        /// </summary>
        public UADPNetworkMessageType UADPNetworkMessageType
        {
            get
            {
                return m_uadpNetworkMessageType;
            }
        }

        /// <summary>
        /// Get the UADP network message discovery type 
        /// </summary>
        public UADPNetworkMessageDiscoveryType UADPDiscoveryType
        {
            get { return m_discoveryType;}
        }

        /// <summary>
        /// Get/Set the DataSetWriterIds
        /// </summary>
        public UInt16[] DataSetWriterIds
        {
            get
            {
                return m_dataSetWriterIds;
            }
            set
            {
                m_dataSetWriterIds = value;
            }
        }

        #region NetworkMessage Header

        /// <summary>
        /// Get and Set Uadp version
        /// </summary>
        public byte UADPVersion
        {
            get { return m_uadpVersion; }
            set { m_uadpVersion = Convert.ToByte(value & kUADPVersionBitMask); }
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
        public object PublisherId
        {
            get { return m_publisherId; }
            set
            {
                // Just in case value is a positive signed Integer 
                // Try to bring it to an accepted type (will overflow if value doesn't fit)

                object adjustedValue = value;
                switch (value)
                {
                    case Int16 int16Value:
                        if (int16Value > 0)
                        {
                            adjustedValue = (UInt16)int16Value;
                        }
                        break;
                    case Int32 int32Value:
                        if (int32Value > 0)
                        {
                            adjustedValue = (UInt32)int32Value;
                        }
                        break;
                    case Int64 int64Value:
                        if (int64Value > 0)
                        {
                            adjustedValue = (UInt64)int64Value;
                        }
                        break;
                }

                m_publisherId = adjustedValue;

                // Remove previous PublisherId data type
                ExtendedFlags1 &= (ExtendedFlags1EncodingMask)kPublishedIdResetMask;

                // ExtendedFlags1: Bit range 0-2: PublisherId Type
                PublisherIdTypeEncodingMask publishedIdTypeType = PublisherIdTypeEncodingMask.Reserved;

                if (m_publisherId is byte)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.Byte;
                }
                else if (m_publisherId is UInt16)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.UInt16;
                }
                else if (m_publisherId is UInt32)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.UInt32;
                }
                else if (m_publisherId is UInt64)
                {
                    publishedIdTypeType = PublisherIdTypeEncodingMask.UInt64;
                }
                else if (m_publisherId is String)
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
        #endregion

        #region Group Header

        /// <summary>
        /// Get and Set GroupFlags
        /// </summary>
        public GroupFlagsEncodingMask GroupFlags { get; private set; }

        /// <summary>
        /// Get and Set VersionTime type: it represents the time in seconds since the year 2000
        /// </summary>
        public UInt32 GroupVersion { get; set; }

        /// <summary>
        /// Get and Set NetworkMessageNumber
        /// </summary>
        public UInt16 NetworkMessageNumber { get; set; }

        /// <summary>
        /// Get and Set SequenceNumber
        /// </summary>
        public UInt16 SequenceNumber { get; set; }

        #endregion

        #region NetworkMessage Header Extended (ExtendedNetwork Header)

        /// <summary>
        /// Get and Set Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// PicoSeconds
        /// </summary>
        public UInt16 PicoSeconds { get; set; }

        #endregion

        #region Security Header

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
        public UInt16 SecurityFooterSize { get; set; }

        #endregion

        #region Security footer

        /// <summary>
        /// Get and Set SecurityFooter
        /// </summary>
        public byte[] SecurityFooter { get; set; }

        #endregion

        #region Signature

        /// <summary>
        /// Get and Set Signature
        /// </summary>
        public byte[] Signature { get; set; }

        #endregion

        #endregion

        #region Public Methods

        /// <summary>
        /// Set network message content mask
        /// </summary>
        /// <param name="networkMessageContentMask"></param>
        public void SetNetworkMessageContentMask(UadpNetworkMessageContentMask networkMessageContentMask)
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
            using (MemoryStream stream = new MemoryStream())
            {
                Encode(messageContext, stream);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Encodes the object in the specified stream.
        /// </summary>
        /// <param name="messageContext">The system context.</param>
        /// <param name="stream">The stream to use.</param>
        public override void Encode(IServiceMessageContext messageContext, Stream stream)
        {
            using (BinaryEncoder encoder = new BinaryEncoder(stream, messageContext))
            {
                if (m_uadpNetworkMessageType == UADPNetworkMessageType.DataSetMessage)
                {
                    EncodeDataSetNetworkMessageType(encoder);
                }
                else
                {
                    EncodeNetworkMessageHeader(encoder);

                    if (m_uadpNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse)
                    {
                        encoder.WriteByte("ResponseType", (byte)m_discoveryType);
                        // A strictly monotonically increasing sequence number assigned to each discovery response sent in the scope of a PublisherId.
                        encoder.WriteUInt16("SequenceNumber", SequenceNumber);

                        switch (m_discoveryType)
                        {
                            case UADPNetworkMessageDiscoveryType.DataSetMetaData:
                                EncodeDataSetMetaData(encoder);
                                break;
                            case UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration:
                            case UADPNetworkMessageDiscoveryType.PublisherEndpoint:
                                // not implemented
                                break;
                        }
                    }
                    else if (m_uadpNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest)
                    {
                        encoder.WriteByte("RequestType", (byte)m_discoveryType);
                        // write DataSetWriterIds
                        encoder.WriteUInt16Array("DataSetWriterIds", m_dataSetWriterIds);
                    }
                }
            }
        }

        /// <summary>
        /// Decodes the message 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="message"></param>
        /// <param name="dataSetReaders"></param>
        public override void Decode(IServiceMessageContext context, byte[] message, IList<DataSetReaderDataType> dataSetReaders)
        {
            using (BinaryDecoder binaryDecoder = new BinaryDecoder(message, context))
            {
                // 1. decode network message header (PublisherId & DataSetClassId)
                DecodeNetworkMessageHeader(binaryDecoder);

                //decode network messages according to their type
                if (m_uadpNetworkMessageType == UADPNetworkMessageType.DataSetMessage)
                {
                    if (dataSetReaders == null || dataSetReaders.Count == 0)
                    {
                        return;
                    }
                    //decode bytes using dataset reader information
                    DecodeSubscribedDataSets(binaryDecoder, dataSetReaders);
                }
                else if (m_uadpNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse)
                {
                    // Decode the Discovery Response Header
                    m_discoveryType = (UADPNetworkMessageDiscoveryType)binaryDecoder.ReadByte("ResponseType");
                    // A strictly monotonically increasing sequence number assigned to each discovery response sent in the scope of a PublisherId.
                    SequenceNumber = binaryDecoder.ReadUInt16("SequenceNumber");

                    switch (m_discoveryType)
                    {
                        case UADPNetworkMessageDiscoveryType.DataSetMetaData:
                            DecodeMetaDataMessage(binaryDecoder);
                            break;
                        case UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration:
                        case UADPNetworkMessageDiscoveryType.PublisherEndpoint:
                            // not implemented
                            break;
                    }
                }
                else if (m_uadpNetworkMessageType == UADPNetworkMessageType.DiscoveryRequest)
                {
                    // Decode the Discovery Response Header
                    m_discoveryType = (UADPNetworkMessageDiscoveryType)binaryDecoder.ReadByte("ResponseType");
                    m_dataSetWriterIds = binaryDecoder.ReadUInt16Array("DataSetWriterIds")?.ToArray();
                }
            }
        }
            #endregion

            #region Private Methods - Encoding
            /// <summary>
            /// Encodes the DataSet Network message in a binary stream.
            /// </summary>
            /// <param name="binaryEncoder"></param>
            private void EncodeDataSetNetworkMessageType(BinaryEncoder binaryEncoder)
            {
            if (binaryEncoder == null)
            {
                throw new ArgumentException(nameof(binaryEncoder));
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
        /// <param name="binaryEncoder"></param>
        private void EncodeDataSetMetaData(BinaryEncoder binaryEncoder)
        {
            if (DataSetWriterId != null)
            {
                binaryEncoder.WriteUInt16("DataSetWriterId", DataSetWriterId.Value);
            }
            else
            {
                Utils.Trace("The UADP DiscoveryResponse DataSetMetaData message cannot be encoded: The DataSetWriterId property is missing. Value 0 will be used.");
                binaryEncoder.WriteUInt16("DataSetWriterId", 0);
            }

            if (m_metadata == null)
            {
                Utils.Trace("The UADP DiscoveryResponse DataSetMetaData message cannot be encoded: The MetaData property is missing. Value null will be used.");
            }
            binaryEncoder.WriteEncodeable("MetaData", m_metadata, typeof(DataSetMetaDataType));

            // temporary write StatusCode.Good 
            binaryEncoder.WriteStatusCode("StatusCode", StatusCodes.Good);
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

            #region Network Message Header

            if ((NetworkMessageContentMask & (UadpNetworkMessageContentMask.PublisherId |
                                              UadpNetworkMessageContentMask.DataSetClassId)) != 0)
            {
                //  UADPFlags: The ExtendedFlags1 shall be omitted if bit 7 of the UADPFlags is false.
                // Enable ExtendedFlags1 usage
                UADPFlags |= UADPFlagsEncodingMask.ExtendedFlags1;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PublisherId) != 0)
            {
                // UADPFlags: Bit 4: PublisherId enabled
                UADPFlags |= UADPFlagsEncodingMask.PublisherId;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                // ExtendedFlags1 Bit 3: DataSetClassId enabled
                ExtendedFlags1 |= ExtendedFlags1EncodingMask.DataSetClassId;
            }

            #endregion

            #region Group Message Header

            if ((NetworkMessageContentMask & (UadpNetworkMessageContentMask.GroupHeader |
                                              UadpNetworkMessageContentMask.WriterGroupId |
                                              UadpNetworkMessageContentMask.GroupVersion |
                                              UadpNetworkMessageContentMask.NetworkMessageNumber |
                                              UadpNetworkMessageContentMask.SequenceNumber)) !=
                UadpNetworkMessageContentMask.None)
            {
                // UADPFlags: Bit 5: GroupHeader enabled
                UADPFlags |= UADPFlagsEncodingMask.GroupHeader;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                // GroupFlags: Bit 0: WriterGroupId enabled
                GroupFlags |= GroupFlagsEncodingMask.WriterGroupId;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                // GroupFlags: Bit 1: GroupVersion enabled
                GroupFlags |= GroupFlagsEncodingMask.GroupVersion;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                // GroupFlags: Bit 2: NetworkMessageNumber enabled
                GroupFlags |= GroupFlagsEncodingMask.NetworkMessageNumber;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                // GroupFlags: Bit 3: SequenceNumber enabled
                GroupFlags |= GroupFlagsEncodingMask.SequenceNumber;
            }

            #endregion

            #region Extended network message header

            if ((NetworkMessageContentMask & (UadpNetworkMessageContentMask.Timestamp |
                                              UadpNetworkMessageContentMask.PicoSeconds |
                                              UadpNetworkMessageContentMask.PromotedFields)) != 0)
            {
                // Enable ExtendedFlags1 usage
                UADPFlags |= UADPFlagsEncodingMask.ExtendedFlags1;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                // ExtendedFlags1: Bit 5: Timestamp enabled
                ExtendedFlags1 |= ExtendedFlags1EncodingMask.Timestamp;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
            {
                // ExtendedFlags1: Bit 6: PicoSeconds enabled
                ExtendedFlags1 |= ExtendedFlags1EncodingMask.PicoSeconds;
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PromotedFields) != 0)
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

            #endregion

            #region PayLoad Header

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // UADPFlag: Bit 6: PayloadHeader enabled
                UADPFlags |= UADPFlagsEncodingMask.PayloadHeader;
            }

            #endregion

            #region Security footer (not implemented yet)

            // ExtendedFlags1: Bit 4: Security enabled
            // Disable security for now
            ExtendedFlags1 &= ~(ExtendedFlags1EncodingMask.Security);

            // The security footer size shall be omitted if bit 2 of the SecurityFlags is false.
            SecurityFlags &= ~(SecurityFlagsEncodingMask.SecurityFooter);

            #endregion
        }


        /// <summary>
        /// Set All flags before encode/decode for a NetworkMessage that contains A DiscoveryResponse containing data set metadata
        /// </summary>
        private void SetFlagsDiscoveryResponseMetaData()
        {
            /* DiscoveryResponse:
             * UADPFlags bits 5 and 6 shall be false, bits 4 and 7 shall be true
             * ExtendedFlags1 bits 3, 5 and 6 shall be false, bit 7 shall be true (erata 9):Bit 4 of ExtendedFlags1 shall be true
             * ExtendedFlags2 bit 1 shall be false and the NetworkMessage type shall be discovery response
             * */
            UADPFlags = UADPFlagsEncodingMask.PublisherId | UADPFlagsEncodingMask.ExtendedFlags1;
            ExtendedFlags1 = ExtendedFlags1EncodingMask.Security | ExtendedFlags1EncodingMask.ExtendedFlags2;
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
            ExtendedFlags1 = ExtendedFlags1EncodingMask.Security | ExtendedFlags1EncodingMask.ExtendedFlags2;
            ExtendedFlags2 = ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest;
        }




        /// <summary>
        /// Decode the stream from decoder parameter and produce a Dataset 
        /// </summary> 
        /// <param name="binaryDecoder"></param>
        /// <param name="dataSetReaders"></param>
        /// <returns></returns>
        public void DecodeSubscribedDataSets(BinaryDecoder binaryDecoder, IList<DataSetReaderDataType> dataSetReaders)
        {
            if (dataSetReaders == null || dataSetReaders.Count == 0)
            {
                return;
            }

            try
            {
                List<DataSetReaderDataType> dataSetReadersFiltered = new List<DataSetReaderDataType>();

                /* 6.2.8.1 PublisherId
                 The parameter PublisherId defines the Publisher to receive NetworkMessages from.
                 If the value is null, the parameter shall be ignored and all received NetworkMessages pass the PublisherId filter. */
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    //check Enabled & publisher id
                    if (dataSetReader.PublisherId.Value == null
                        || (PublisherId != null && PublisherId.Equals(dataSetReader.PublisherId.Value)))
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
                dataSetReadersFiltered = new List<DataSetReaderDataType>();

                // 2. decode WriterGroupId
                DecodeGroupMessageHeader(binaryDecoder);
                /* 6.2.8.2 WriterGroupId
                The parameter WriterGroupId with DataType UInt16 defines the identifier of the corresponding WriterGroup.
                The default value 0 is defined as null value, and means this parameter shall be ignored.*/
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    //check WriterGroupId id
                    if (dataSetReader.WriterGroupId == 0 || dataSetReader.WriterGroupId == WriterGroupId)
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
                List<UaDataSetMessage> dataSetMessages = new List<UaDataSetMessage>();

                /* 6.2.8.3 DataSetWriterId
                The parameter DataSetWriterId with DataType UInt16 defines the DataSet selected in the Publisher for the DataSetReader.
                If the value is 0 (null), the parameter shall be ignored and all received DataSetMessages pass the DataSetWriterId filter.*/
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    List<UaDataSetMessage> uadpDataSetMessages = new List<UaDataSetMessage>(DataSetMessages);
                    //if there is no information regarding dataSet in network message, add dummy datasetMessage to try decoding
                    if (uadpDataSetMessages.Count == 0)
                    {
                        uadpDataSetMessages.Add(new UadpDataSetMessage());
                    }

                    // 6.2 Decode payload into DataSets 
                    // Restore the encoded fields (into dataset for now) for each possible dataset reader
                    foreach (UadpDataSetMessage uadpDataSetMessage in uadpDataSetMessages)
                    {
                        if (uadpDataSetMessage.DataSet != null)
                        {
                            continue; // this dataset message was already decoded
                        }

                        if (dataSetReader.DataSetWriterId == 0 || uadpDataSetMessage.DataSetWriterId == dataSetReader.DataSetWriterId)
                        {
                            //atempt to decode dataset message using the reader
                            uadpDataSetMessage.DecodePossibleDataSetReader(binaryDecoder, dataSetReader);
                            if (uadpDataSetMessage.DataSet != null)
                            {
                                dataSetMessages.Add(uadpDataSetMessage);
                            }
                            else if (uadpDataSetMessage.IsMetadataMajorVersionChange)
                            {
                                OnDataSetDecodeErrorOccurred(new DataSetDecodeErrorEventArgs(DataSetDecodeErrorReason.MetadataMajorVersion, this, dataSetReader));
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
                    dataSetMessages = new List<UaDataSetMessage>();
                    // check if DataSets are decoded into the existing dataSetMessages
                    foreach (var dataSetMessage in m_uaDataSetMessages)
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
                Utils.Trace(ex, "UadpNetworkMessage.DecodeSubscribedDataSets");
            }
        }

        /// <summary>
        /// Decode the binaryDecoder content as a MetaData message
        /// </summary>
        /// <param name="binaryDecoder"></param>
        private void DecodeMetaDataMessage(BinaryDecoder binaryDecoder)
        {
            DataSetWriterId = binaryDecoder.ReadUInt16("DataSetWriterId");
            m_metadata = binaryDecoder.ReadEncodeable("MetaData", typeof(DataSetMetaDataType)) as DataSetMetaDataType;

            // temporary write StatusCode.Good 
            StatusCode statusCode = binaryDecoder.ReadStatusCode("StatusCode");
            Utils.Trace("DecodeMetaDataMessage returned: ", statusCode);

        }

        /// <summary>
        ///  Encode Network Message Header
        /// </summary>
        /// <param name="encoder"></param>
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
                    Utils.Trace(TraceMasks.Error, "NetworkMessageHeader cannot be encoded. PublisherId is null but it is expected to be encoded.");
                }
                else
                {
                    PublisherIdTypeEncodingMask publisherIdType = (PublisherIdTypeEncodingMask)((byte)ExtendedFlags1 & kPublishedIdTypeUsedBits);
                    switch (publisherIdType)
                    {
                        case PublisherIdTypeEncodingMask.Byte:
                            encoder.WriteByte("PublisherId", Convert.ToByte(PublisherId));
                            break;
                        case PublisherIdTypeEncodingMask.UInt16:
                            encoder.WriteUInt16("PublisherId", Convert.ToUInt16(PublisherId));
                            break;
                        case PublisherIdTypeEncodingMask.UInt32:
                            encoder.WriteUInt32("PublisherId", Convert.ToUInt32(PublisherId));
                            break;
                        case PublisherIdTypeEncodingMask.UInt64:
                            encoder.WriteUInt64("PublisherId", Convert.ToUInt64(PublisherId));
                            break;
                        case PublisherIdTypeEncodingMask.String:
                            encoder.WriteString("PublisherId", Convert.ToString(PublisherId));
                            break;
                        default:
                            // Reserved - no type provided
                            break;
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
        /// <param name="encoder"></param>
        private void EncodeGroupMessageHeader(BinaryEncoder encoder)
        {
            if ((NetworkMessageContentMask & (UadpNetworkMessageContentMask.GroupHeader |
                                              UadpNetworkMessageContentMask.WriterGroupId |
                                              UadpNetworkMessageContentMask.GroupVersion |
                                              UadpNetworkMessageContentMask.NetworkMessageNumber |
                                              UadpNetworkMessageContentMask.SequenceNumber)) != UadpNetworkMessageContentMask.None)
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
            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
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
        /// <param name="encoder"></param>
        private void EncodePayloadHeader(BinaryEncoder encoder)
        {
            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                encoder.WriteByte("Count", (byte)DataSetMessages.Count);

                // Collect DataSetSetMessages headers
                for (int index = 0; index < DataSetMessages.Count; index++)
                {
                    UadpDataSetMessage uadpDataSetMessage = DataSetMessages[index] as UadpDataSetMessage;
                    if (uadpDataSetMessage != null && uadpDataSetMessage.DataSet != null)
                    {
                        encoder.WriteUInt16("DataSetWriterId", uadpDataSetMessage.DataSetWriterId);
                    }
                }
            }
        }

        /// <summary>
        ///  Encode Extended network message header
        /// </summary>
        /// <param name="encoder"></param>
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
        /// <param name="encoder"></param>
        private void EncodePromotedFields(BinaryEncoder encoder)
        {
            // todo: Promoted fields not supported
        }

        /// <summary>
        /// Encode security header 
        /// </summary>
        /// <param name="encoder"></param>
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
        /// <param name="encoder"></param>
        private void EncodePayload(BinaryEncoder encoder)
        {
            int payloadStartPositionInStream = encoder.Position;
            if (DataSetMessages.Count > 1
                && (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                //skip 2 * dataset count for each dataset payload size 
                encoder.Position += 2 * DataSetMessages.Count;
            }
            //encode dataset message payload
            foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages)
            {
                uadpDataSetMessage.Encode(encoder);
            }

            if (DataSetMessages.Count > 1 && (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                int payloadEndPositionInStream = encoder.Position;
                encoder.Position = payloadStartPositionInStream;
                foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages)
                {
                    encoder.WriteUInt16("Size", uadpDataSetMessage.PayloadSizeInStream);
                }
                encoder.Position = payloadEndPositionInStream;
            }
        }

        /// <summary>
        /// Encode security footer
        /// </summary>
        /// <param name="encoder"></param>
        private void EncodeSecurityFooter(BinaryEncoder encoder)
        {
            if ((SecurityFlags & SecurityFlagsEncodingMask.SecurityFooter) != 0)
            {
                encoder.WriteByteArray("SecurityFooter", SecurityFooter);
            }
        }

        /// <summary>
        /// Encode signature
        /// </summary>
        /// <param name="encoder"></param>
        private void EncodeSignature(BinaryEncoder encoder)
        {
            // encoder.WriteByteArray("Signature", Signature);
        }

        #endregion

        #region Private Methods - Decoding 

        /// <summary>
        /// Encode Network Message Header
        /// </summary>
        /// <param name="decoder"></param>
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
            if ((ExtendedFlags2 & ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest) != 0)
            {
                m_uadpNetworkMessageType = UADPNetworkMessageType.DiscoveryRequest;
            }
            else if ((ExtendedFlags2 & ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse) != 0)
            {
                m_uadpNetworkMessageType = UADPNetworkMessageType.DiscoveryResponse;
            }
            else
            {
                m_uadpNetworkMessageType = UADPNetworkMessageType.DataSetMessage;
            }

            // Decode PublisherId
            if ((UADPFlags & UADPFlagsEncodingMask.PublisherId) != 0)
            {
                PublisherIdTypeEncodingMask publishedIdTypeType = (PublisherIdTypeEncodingMask)((byte)ExtendedFlags1 & kPublishedIdTypeUsedBits);

                switch (publishedIdTypeType)
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
        /// <param name="decoder"></param>
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
        /// <param name="decoder"></param>
        private void DecodePayloadHeader(BinaryDecoder decoder)
        {
            // Decode PayloadHeader
            if ((UADPFlags & UADPFlagsEncodingMask.PayloadHeader) != 0)
            {
                byte count = decoder.ReadByte("Count");
                for (int idx = 0; idx < count; idx++)
                {
                    m_uaDataSetMessages.Add(new UadpDataSetMessage());
                }

                // collect DataSetSetMessages headers
                foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages)
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
        /// <param name="decoder"></param>
        private void DecodePromotedFields(BinaryDecoder decoder)
        {
            // todo: Promoted fields not supported
        }

        /// <summary>
        /// Decode  payload size and prepare for decoding payload
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodePayloadSize(BinaryDecoder decoder)
        {
            if (DataSetMessages.Count > 1)
            {
                // Decode PayloadHeader Size
                if ((UADPFlags & UADPFlagsEncodingMask.PayloadHeader) != 0)
                {
                    foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages)
                    {
                        // Save the size
                        uadpDataSetMessage.PayloadSizeInStream = decoder.ReadUInt16("Size");
                    }
                }
            }
            BinaryDecoder binaryDecoder = decoder as BinaryDecoder;
            if (binaryDecoder != null)
            {
                int offset = 0;
                // set start position of dataset message in binary stream 
                foreach (UadpDataSetMessage uadpDataSetMessage in DataSetMessages)
                {
                    uadpDataSetMessage.StartPositionInStream = binaryDecoder.Position + offset;
                    offset += uadpDataSetMessage.PayloadSizeInStream;
                }
            }
        }

        /// <summary>
        /// Decode security header 
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodeSecurityHeader(BinaryDecoder decoder)
        {
            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.Security) != 0)
            {
                SecurityFlags = (SecurityFlagsEncodingMask)decoder.ReadByte("SecurityFlags");

                SecurityTokenId = decoder.ReadUInt32("SecurityTokenId");
                NonceLength = decoder.ReadByte("NonceLength");
                MessageNonce = decoder.ReadByteArray("MessageNonce").ToArray();

                if ((SecurityFlags & SecurityFlagsEncodingMask.SecurityFooter) != 0)
                {
                    SecurityFooterSize = decoder.ReadUInt16("SecurityFooterSize");
                }
            }
        }

        /// <summary>
        /// Decode security footer
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodeSecurityFooter(BinaryDecoder decoder)
        {
            if ((SecurityFlags & SecurityFlagsEncodingMask.SecurityFooter) != 0)
            {
                SecurityFooter = decoder.ReadByteArray("SecurityFooter").ToArray();
            }
        }

        /// <summary>
        /// Decode signature
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodeSignature(BinaryDecoder decoder)
        {
            // Signature = decoder.ReadByteArray("Signature").ToArray();
        }
        #endregion
    }
}
