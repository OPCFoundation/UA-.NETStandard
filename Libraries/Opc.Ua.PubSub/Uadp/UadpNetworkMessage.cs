/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.PubSub.PublishedData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Opc.Ua.PubSub.Uadp
{
    /// <summary>
    /// UADP Network Message
    /// </summary>
    internal class UadpNetworkMessage : UaNetworkMessage
    {
        #region Fields
        // The UADPVersion for this specification version is 1.
        private const byte UadpVersion = 1;
        private const byte PublishedIdTypeUsedBits = 0x07;
        private const byte UADPVersionBitMask = 0x0F;
        private const byte PublishedIdResetMask = 0xFC;
        private const byte UADPMessageTypeMask = 0x1C;
      
        private byte m_uadpVersion;
        private object m_publisherId;
        private UADPNetworkMessageType m_uadpNetworkMessageType;

        /// <summary>
        /// Uadp DataSet messages
        /// </summary>
        private readonly List<UadpDataSetMessage> m_uadpDataSetMessages;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of UadpNetworkMessage
        /// </summary>
        public UadpNetworkMessage()
        {
            UADPVersion = UadpVersion;
            DataSetClassId = Guid.Empty;
            Timestamp = DateTime.UtcNow;

            m_uadpDataSetMessages = new List<UadpDataSetMessage>();
        }
        /// <summary>
        /// Create new instance of UadpNetworkMessage
        /// </summary>
        /// <param name="uadpDataSetMessages">UadpDataSetMessage list as input</param>
        public UadpNetworkMessage(List<UadpDataSetMessage> uadpDataSetMessages) : this()
        {
            m_uadpDataSetMessages = uadpDataSetMessages;
        }
        #endregion

        #region Properties

        /// <summary>
        /// UadpDataSet messages
        /// </summary>
        public ReadOnlyCollection<UadpDataSetMessage> UadpDataSetMessages
        {
            get
            {
                return new ReadOnlyCollection<UadpDataSetMessage>(m_uadpDataSetMessages);
            }
        }

        /// <summary>
        /// NetworkMessageContentMask contains the mask that will be used to check NetworkMessage options selected for usage  
        /// </summary>
        public UadpNetworkMessageContentMask NetworkMessageContentMask { get; private set; }

        #region NetworkMessage Header

        /// <summary>
        /// Get and Set Uadp version
        /// </summary>
        public byte UADPVersion
        {
            get { return m_uadpVersion; }
            set { m_uadpVersion = Convert.ToByte(value & UADPVersionBitMask); }
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
                m_publisherId = value;

                // Remove previous PublisherId data type
                ExtendedFlags1 &= (ExtendedFlags1EncodingMask)PublishedIdResetMask;

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
        /// Get and Set WriterGroupId
        /// </summary>
        public UInt16 WriterGroupId { get; set; }

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

            SetFlags();
        }

        /// <summary>
        /// Encodes the object in a stream.
        /// </summary>
        public override void Encode(IEncoder encoder)
        {
            Encode(encoder as BinaryEncoder);
        }
        
        #endregion

        #region Private Methods - Encoding
        /// <summary>
        /// Encodes the object in a binary stream.
        /// </summary>
        /// <param name="binaryEncoder"></param>
        private void Encode(BinaryEncoder binaryEncoder)
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
        /// Set All flags before encode/decode
        /// </summary>
        private void SetFlags()
        {
            UADPFlags = 0;
            ExtendedFlags1 &= (ExtendedFlags1EncodingMask)PublishedIdTypeUsedBits;
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
                // todo:
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
        /// Decode the stream from decoder parameter and produce a Dataset 
        /// </summary> 
        /// <param name="binaryDecoder"></param>
        /// <param name="dataSetReaders"></param>
        /// <returns></returns>
        public List<DataSet> DecodeSubscribedDataSets(BinaryDecoder binaryDecoder, IEnumerable<DataSetReaderDataType> dataSetReaders)
        {
            List<DataSet> subscribedDataSets = new List<DataSet>();
            try
            {
                List<DataSetReaderDataType> dataSetReadersFiltered = new List<DataSetReaderDataType>();

                // 1. decode network message header (PublisherId & DataSetClassId)
                DecodeNetworkMessageHeader(binaryDecoder);

                //ignore network messages that are not dataSet messages
                if (m_uadpNetworkMessageType != UADPNetworkMessageType.DataSetMessage
                    || PublisherId == null)
                {
                    return subscribedDataSets;
                }

                /* 6.2.8.1 PublisherId
                 The parameter PublisherId defines the Publisher to receive NetworkMessages from.
                 If the value is null, the parameter shall be ignored and all received NetworkMessages pass the PublisherId filter. */
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    //check Enabled & publisher id
                    if (PublisherId.Equals(dataSetReader.PublisherId.Value))
                    {
                        dataSetReadersFiltered.Add(dataSetReader);
                    }
                }
                if (dataSetReadersFiltered.Count == 0)
                {
                    return subscribedDataSets;
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
                    return subscribedDataSets;
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
                
                /* 6.2.8.3 DataSetWriterId
                The parameter DataSetWriterId with DataType UInt16 defines the DataSet selected in the Publisher for the DataSetReader.
                If the value is 0 (null), the parameter shall be ignored and all received DataSetMessages pass the DataSetWriterId filter.*/
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    List<UadpDataSetMessage> uadpDataSetMessages = new List<UadpDataSetMessage>(m_uadpDataSetMessages);
                    //if there is no information regarding dataSet in network message, add dummy datasetMessage to try decoding
                    if (uadpDataSetMessages.Count == 0)
                    {
                        uadpDataSetMessages.Add(new UadpDataSetMessage());
                    }
                    // 6.2 Decode payload into DataSets 
                    // Restore the encoded fields (into dataset for now) for each possible dataset reader
                    foreach (UadpDataSetMessage uadpDataSetMessage in uadpDataSetMessages)
                    {
                        if (dataSetReader.DataSetWriterId == 0 || uadpDataSetMessage.DataSetWriterId == dataSetReader.DataSetWriterId)
                        {
                            //decode dataset message using the reader
                            DataSet dataSet = uadpDataSetMessage.DecodePossibleDataSetReader(binaryDecoder, dataSetReader);
                            if (dataSet != null)
                            {
                                subscribedDataSets.Add(dataSet);
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                // Unexpected exception in DecodeSubscribedDataSets
                Utils.Trace(ex, "UadpNetworkMessage.DecodeSubscribedDataSets");
            }
            return subscribedDataSets;
        }
        
        /// <summary>
        ///  Encode Network Message Header
        /// </summary>
        /// <param name="encoder"></param>
        private void EncodeNetworkMessageHeader(BinaryEncoder encoder)
        {
            // byte[0..3] UADPVersion value 1 (for now)
            // byte[4..7] UADPFlags
            encoder.WriteByte("VersionFlags", (byte) (UADPVersion | (byte)UADPFlags));

            if((UADPFlags & UADPFlagsEncodingMask.ExtendedFlags1) !=0)
            {
                encoder.WriteByte("ExtendedFlags1", (byte)ExtendedFlags1);
            }

            if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.ExtendedFlags2) != 0)
            {
                encoder.WriteByte("ExtendedFlags2", (byte)ExtendedFlags2);
            }

            if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PublisherId) != 0)
            {
                PublisherIdTypeEncodingMask publisherIdType = (PublisherIdTypeEncodingMask)((byte)ExtendedFlags1 & PublishedIdTypeUsedBits);
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
                encoder.WriteByte("Count", (byte) m_uadpDataSetMessages.Count);

                // Collect DataSetSetMessages headers
                for (int index = 0; index < m_uadpDataSetMessages.Count; index++)
                {
                    UadpDataSetMessage uadpDataSetMessage = m_uadpDataSetMessages[index];
                    if (uadpDataSetMessage.DataSet != null)
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
            // todo: Promnoted fields not supported
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
            if (m_uadpDataSetMessages.Count > 1
                && (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {                
                //skip 2 * dataset count for each dataset payload size 
                encoder.Position = encoder.Position + 2 * m_uadpDataSetMessages.Count;               
            }
            //encode dataset message payload
            foreach (UadpDataSetMessage uadpDataSetMessage in m_uadpDataSetMessages)
            {
                uadpDataSetMessage.Encode(encoder);
            }

            if (m_uadpDataSetMessages.Count > 1
                && (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                int payloadEndPositionInStream = encoder.Position;
                encoder.Position = payloadStartPositionInStream;
                foreach (UadpDataSetMessage uadpDataSetMessage in m_uadpDataSetMessages)
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
            UADPVersion = (byte)(versionFlags & UADPVersionBitMask);
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
                PublisherIdTypeEncodingMask publishedIdTypeType = (PublisherIdTypeEncodingMask)((byte)ExtendedFlags1 & PublishedIdTypeUsedBits);

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
                    default:
                        // 000 The PublisherId is of DataType Byte
                        // This is the default value if ExtendedFlags1 is omitted
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
                    m_uadpDataSetMessages.Add(new UadpDataSetMessage());
                }

                // collect DataSetSetMessages headers
                foreach (UadpDataSetMessage uadpDataSetMessage in m_uadpDataSetMessages)
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
            // todo:
        }

        /// <summary>
        /// Decode  payload size and prepare for decoding payload
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodePayloadSize(BinaryDecoder decoder)
        {
            if (m_uadpDataSetMessages.Count > 1)
            {                
                // Decode PayloadHeader Size
                if ((UADPFlags & UADPFlagsEncodingMask.PayloadHeader) != 0)
                {
                    foreach (UadpDataSetMessage uadpDataSetMessage in m_uadpDataSetMessages)
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
                foreach (UadpDataSetMessage uadpDataSetMessage in m_uadpDataSetMessages)
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
