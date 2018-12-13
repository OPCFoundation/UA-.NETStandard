using Opc.Ua;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace Opc.Ua
{
    public class UadpNetworkMessage : BinaryEncoder
    {
        //UadpDataSetMessage uadpDataSetMessage = new UadpDataSetMessage();

        public UadpNetworkMessage(ServiceMessageContext context) : base(context)
        {

        }

        byte UADPVersionFlags { get; set; }
        byte SecurityFlags { get; set; }

        byte ExtendedFlags1 { get; set; }

        byte GroupFlags { get; set; }
        byte ExtendedFlags2 { get; set; }
         
        public uint NetworkContentMask { get; set; }
        public object PublisherId { get; set; }
        public Guid DataSetClassId { get; set; }
        public MessageSecurityMode SecurityMode { get; set; }
        public UInt16 WriterGroupId { get; set; }
        public UInt16 NetworkMessageNumber { get; set; }
        public uint GroupVersion { get; set; }
        public UInt16 NetworkMessageSequenceNumber { get; set; }
        public Int32 MessageCount { get; set; }
        public List<UInt16> LstDataSetWriterId = new List<ushort>();
        public UInt32 SecurityGroupId { get; set; } 
        public bool IsChunkMessage { get; set; }
   
        public FieldMetaDataCollection FieldMetaDataCollection { get; set; }


        public void EncodeNetworkMessageHeader()
        {
            Type publisherIDType=null;
            #region UADPVersion
            // UADpo version set the flag to 1
            UADPVersionFlags = SetBit(UADPVersionFlags, 0);
            #endregion

            #region UADPFlags
            
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.PublisherId) != 0)
            {
                UADPVersionFlags= SetBit(UADPVersionFlags, 4);
                  publisherIDType = PublisherId.GetType();
                switch (publisherIDType.FullName)
                {
                    case "System.String":
                        ExtendedFlags1= SetBit(ExtendedFlags1, 2);
                        break;
                    
                    case "System.UInt16":
                        ExtendedFlags1= SetBit(ExtendedFlags1, 0);
                        break;
                    case "System.UInt32":
                        ExtendedFlags1= SetBit(ExtendedFlags1, 1);
                        break;
                    case "System.UInt64":
                        ExtendedFlags1= SetBit(ExtendedFlags1, 0);
                        ExtendedFlags1= SetBit(ExtendedFlags1, 1);
                        break;
                       
                }
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.GroupHeader) != 0)
            {
                UADPVersionFlags= SetBit(UADPVersionFlags, 5);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                UADPVersionFlags= SetBit(UADPVersionFlags, 6);
            }
            #endregion

            #region Extended Flag1
            //if ((NetworkContentMask & UadpNetworkMessageContentMask.PublisherId) != 0)
            //{
                 
            //    Type publisherIDType = PublisherId.GetType();
            //    switch (publisherIDType.FullName)
            //    {
            //        case "System.String":
            //            ExtendedFlags1= SetBit(ExtendedFlags1, 2);
            //            break;

            //        case "System.UInt16":
            //            ExtendedFlags1= SetBit(ExtendedFlags1, 0);
            //            break;
            //        case "System.UInt32":
            //            ExtendedFlags1= SetBit(ExtendedFlags1, 1);
            //            break;
            //        case "System.UInt64":
            //            ExtendedFlags1= SetBit(ExtendedFlags1, 0);
            //            ExtendedFlags1= SetBit(ExtendedFlags1, 1);
            //            break;

            //    }
            //}
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                ExtendedFlags1= SetBit(ExtendedFlags1, 3);
            }
            if (SecurityMode == MessageSecurityMode.Sign || SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                ExtendedFlags1= SetBit(ExtendedFlags1, 4);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                ExtendedFlags1=SetBit(ExtendedFlags1, 5);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.Picoseconds) != 0)
            {
                ExtendedFlags1=SetBit(ExtendedFlags1, 6);
            }

            for (int ii = 0; ii < 7; ii++) // validate whether ExtendedFlags1 is set
            {
                bool isExtendedFlags1Set = IsBitSet(ExtendedFlags1, ii);
                if (isExtendedFlags1Set)
                {
                    UADPVersionFlags= SetBit(UADPVersionFlags, 7); 
                    break;
                }

            }

            #endregion
            #region Extended Flag2
            if (IsChunkMessage)
            {
                ExtendedFlags2= SetBit(ExtendedFlags2, 0);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.PromotedFields) != 0)
            {
                ExtendedFlags2 = SetBit(ExtendedFlags2, 1);
            }
            for (int ii = 0; ii < 7; ii++) // validate whether ExtendedFlags2 is set
            {
                bool isExtendedFlags2Set = IsBitSet(ExtendedFlags2, ii);
                if (isExtendedFlags2Set)
                {
                    ExtendedFlags1= SetBit(ExtendedFlags1, 7);
                    
                    break;
                }
                 
            }
            #endregion

            WriteByte("UADPVersionFlags", UADPVersionFlags);
            if (IsBitSet(UADPVersionFlags, 7))
            {
                WriteByte("ExtendedFlags1", ExtendedFlags1);
            }
            if (IsBitSet(ExtendedFlags1, 7))
            {
                WriteByte("ExtendedFlags2", ExtendedFlags2);
            }
            if (IsBitSet(UADPVersionFlags, 4))
            {
                if(publisherIDType!=null)
                {
                    switch (publisherIDType.FullName)
                    {
                        case "System.String":
                            WriteString("PublisherId", Convert.ToString(PublisherId));
                            break;

                        case "System.UInt16":
                            WriteUInt16("PublisherId", Convert.ToUInt16(PublisherId));
                            break;
                        case "System.UInt32":
                            WriteUInt32("PublisherId", Convert.ToUInt32(PublisherId));
                            break;
                        case "System.UInt64":
                            WriteUInt64("PublisherId", Convert.ToUInt64(PublisherId));
                            break;

                    }
                }
                
            }
            if (IsBitSet(ExtendedFlags1, 3))
            { 
               WriteGuid("DataSetClassId", DataSetClassId);
                
            }
            
        }
        public void EncodeGroupMessageHeader()
        {
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                GroupFlags= SetBit(GroupFlags, 0);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                GroupFlags= SetBit(GroupFlags, 1);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                GroupFlags= SetBit(GroupFlags, 2);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                GroupFlags=SetBit(GroupFlags, 3);
            }
            if (IsBitSet(UADPVersionFlags, 5))
            {
                WriteByte("GroupFlags", GroupFlags);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
               WriteUInt16("WriterGroupId", WriterGroupId);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                //The type should be version time
                WriteUInt32("GroupVersion", GroupVersion);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                WriteUInt16("NetworkMessageNumber", (UInt16)NetworkMessageNumber);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                WriteUInt16("SequenceNumber", NetworkMessageSequenceNumber); //ToDo: Thilak do we need to maintain Sequence number?
            }
        }

        
        public void EncodePayloadHeader()
        {
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                WriteInt32("MessageCount", MessageCount);
                foreach (UInt16 datasetWriterSetId in LstDataSetWriterId)
                {
                    WriteUInt16("DataSetWriterId", datasetWriterSetId);
                }
            }
        }

        public void EncodeExtendedNetworkMessageHeader()
        {
            DateTime dt = DateTime.UtcNow;
            if (IsBitSet(ExtendedFlags1, 5))
            {
                WriteDateTime("Timestamp", dt);
            }
            if (IsBitSet(ExtendedFlags1, 6))
            { 
                WriteUInt16("Picoseconds", (UInt16)dt.Add(new TimeSpan((long)1e-11)).Ticks);
            }
            if ((NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.PromotedFields) != 0)
            {
                EncodePromotedFields();
            }
                //if (IsBitSet(ExtendedFlags1, 4))  //Discard
                //{
                // WriteUInt16("PromotedFields", (UInt16)writerGroupState.PromotedFields);
                // }
            }
       
        public void EncodeSecurityHeader()
        {
            if (IsBitSet(ExtendedFlags1, 4))
            {
                if (SecurityMode == MessageSecurityMode.Sign)
                {
                    SecurityFlags = SetBit(SecurityFlags, 0);
                }
                else if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                {
                    SecurityFlags = SetBit(SecurityFlags, 1);
                }
                WriteByte("SecurityFlags", SecurityFlags);

                WriteUInt32("SecurityTokenId", SecurityGroupId);
                byte NonceLength = 0;
                //ToDo: Thilak
                WriteByte("NonceLength", NonceLength);
                byte[] MessageNonce = new byte[NonceLength]; 
                WriteByteArray("MessageNonce", MessageNonce);
                if(IsBitSet(SecurityFlags,2))
                {
                    //ToDo: Thilak
                    WriteUInt16("SecurityFooterSize", 0);
                }
            }
        }

        public void Encode()
        {
            EncodeNetworkMessageHeader();
            EncodeGroupMessageHeader();
            EncodePayloadHeader();
            EncodeExtendedNetworkMessageHeader();
            EncodeSecurityHeader();

        }

        public void EncodePromotedFields()
        {
            int count = FieldMetaDataCollection.Count;
            MemoryStream memoryStream = new MemoryStream();
            for (ushort ii = 0; ii < count; ii++)
            {
                var field = FieldMetaDataCollection[ii];
                PromotedFields promotedfields = new PromotedFields();
                promotedfields.Order = ii;
                promotedfields.DataType = field.DataType;
                promotedfields.Encode();

                var stream = new BinaryWriter(memoryStream);
                {
                    stream.Write((UInt16)(promotedfields.BaseStream as MemoryStream).Length);
                    stream.Write((promotedfields.BaseStream as MemoryStream).ToArray());
                }
            }
            WriteUInt16("Length", (UInt16)memoryStream.Length);
            //  WriteByteArray("promotedFields", memoryStream.ToArray());
            int offset = (int)BaseStream.Length;
           // (BaseStream as BinaryWriter).Write(memoryStream.ToArray());
            var stream1 = new BinaryWriter(BaseStream);
            {

                stream1.Write(memoryStream.ToArray());
            }
            offset = (int)BaseStream.Length;
        }
        private byte SetBit(byte b, int pos)
        {
            if (pos < 0 || pos > 7)
            {
                throw new ArgumentOutOfRangeException("pos", "Index must be in the range of 0-7.");
            }

             return (byte)(b | (1 << pos));

        }
        private bool IsBitSet(byte value, int bitNumber)
        {
            if ((bitNumber < 0) || (bitNumber > 7))
            {
                throw new ArgumentOutOfRangeException("bitNumber", bitNumber, "bitNumber must be 0..7");
            }
            return ((value & (1 << bitNumber)) != 0);
        }

    }

    public class UadpDataSetMessage : BinaryEncoder
    {
        public UadpDataSetMessage(ServiceMessageContext context) : base(context)
        {
             
        }
        public uint FieldContentMask { get; set; }
        public uint MessageContentMask { get; set; }
          byte DataSetFlags1 { get; set; }
          byte DataSetFlags2 { get; set; }
        public List<DataValue> FieldDatas = new List<DataValue>();
        public uint DataSetMessageSequenceNumber { get; set; }
 

        public UInt32 ConfigurationMajorVersion = 1;
        public UInt32 ConfigurationMinorVersion = 1;
        public void EncodeDataSetMessageHeader()
        {
            DataSetFlags1= SetBit(DataSetFlags1, 0);

            if ((FieldContentMask & (UInt32)DataSetFieldContentMask.RawDataEncoding) != 0)
            {
                DataSetFlags1= SetBit(DataSetFlags1, 1);
            }
            else 
            {
                DataSetFlags1= SetBit(DataSetFlags1, 2); // datavalue field encoding
            }
            if ((MessageContentMask & (UInt32)UadpDataSetMessageContentMask.SequenceNumber) != 0)
            {
                DataSetFlags1= SetBit(DataSetFlags1, 3);
            }
            if ((MessageContentMask & (UInt32)UadpDataSetMessageContentMask.Status) != 0)
            {
                DataSetFlags1= SetBit(DataSetFlags1, 4);
            }
            if ((MessageContentMask & (UInt32)UadpDataSetMessageContentMask.MajorVersion) != 0)
            {
                DataSetFlags1= SetBit(DataSetFlags1, 5);
            }
            if ((MessageContentMask & (UInt32)UadpDataSetMessageContentMask.MinorVersion) != 0)
            {
                DataSetFlags1= SetBit(DataSetFlags1, 6);
            }

             //Always Key frame is sent.

            if ((MessageContentMask & (UInt32)UadpDataSetMessageContentMask.Timestamp) != 0)
            {
                DataSetFlags2 = SetBit(DataSetFlags2, 4);
            }
            if ((MessageContentMask & (UInt32)UadpDataSetMessageContentMask.PicoSeconds) != 0)
            {
                DataSetFlags2 = SetBit(DataSetFlags2, 5);
            }
            for (int ii = 0; ii < 7; ii++)
            {
                bool isDataSetFlag2Set = IsBitSet(DataSetFlags2, ii);
                if (isDataSetFlag2Set)
                {
                    DataSetFlags1=SetBit(DataSetFlags1, 7);
                    break;
                }
            }
             
            WriteByte("DataSetFlags1", DataSetFlags1);
            if (IsBitSet(DataSetFlags1, 7))
            {
                WriteByte("DataSetFlags2", (byte)DataSetFlags2);
            }
            WriteUInt16("DataSetMessageSequenceNumber",(UInt16)DataSetMessageSequenceNumber);

            DateTime dt = DateTime.UtcNow;
            if (IsBitSet(DataSetFlags2, 4))
            {
                WriteDateTime("Timestamp", dt);
            }
            if (IsBitSet(DataSetFlags2, 5))
            {
                WriteUInt16("Picoseconds", (UInt16)dt.Add(new TimeSpan((long)1e-11)).Ticks);
            }
            if (IsBitSet(DataSetFlags1, 4))
            {
                WriteUInt16("Status", (UInt16)(EventSeverity.High | StatusCodes.Good));
            }
            if (IsBitSet(DataSetFlags1, 5))
            {
                WriteUInt32("ConfigurationMajorVersion", ConfigurationMajorVersion);
            }
            if (IsBitSet(DataSetFlags1, 6))
            {
                WriteUInt32("ConfigurationMinorVersion", ConfigurationMinorVersion);
            }
        } 
        public void EncodePayload()
        {
            WriteInt32("DataSetFieldCount", FieldDatas.Count);
            foreach (DataValue dataValue in FieldDatas)
            {
                if (IsBitSet(DataSetFlags1, 1))
                {
                    var variant = dataValue.WrappedValue;

                    if (variant.TypeInfo == null || variant.TypeInfo.BuiltInType == BuiltInType.Null)
                    {
                        return;
                    }

                    if (variant.TypeInfo.ValueRank == ValueRanks.Scalar)
                    {
                        switch (variant.TypeInfo.BuiltInType)
                        {
                            case BuiltInType.Boolean: { WriteBoolean("Bool", (bool)variant.Value); break; }
                            case BuiltInType.SByte: { WriteSByte("SByte", (sbyte)variant.Value); break; }
                            case BuiltInType.Byte: { WriteByte("Byte", (byte)variant.Value); break; }
                            case BuiltInType.Int16: { WriteInt16("Int16", (short)variant.Value); break; }
                            case BuiltInType.UInt16: { WriteUInt16("UInt16", (ushort)variant.Value); break; }
                            case BuiltInType.Int32: { WriteInt32("Int32", (int)variant.Value); break; }
                            case BuiltInType.UInt32: { WriteUInt32("UInt32", (uint)variant.Value); break; }
                            case BuiltInType.Int64: { WriteInt64("Int64", (long)variant.Value); break; }
                            case BuiltInType.UInt64: { WriteUInt64("UInt64", (ulong)variant.Value); break; }
                            case BuiltInType.Float: { WriteFloat("Float", (float)variant.Value); break; }
                            case BuiltInType.Double: { WriteDouble("Double", (double)variant.Value); break; }
                            case BuiltInType.DateTime: { WriteDateTime("DateTime", (DateTime)variant.Value); break; }
                            case BuiltInType.Guid: { WriteGuid("GUID", (Uuid)variant.Value); break; }
                            case BuiltInType.String: { WriteString("String", (string)variant.Value); break; }
                            case BuiltInType.ByteString: { WriteByteString("ByteString", (byte[])variant.Value); break; }
                            case BuiltInType.QualifiedName: { WriteQualifiedName("QualifiedName", (QualifiedName)variant.Value); break; }
                            case BuiltInType.LocalizedText: { WriteLocalizedText("LocalizedText", (LocalizedText)variant.Value); break; }
                            case BuiltInType.NodeId: { WriteNodeId("NodeId", (NodeId)variant.Value); break; }
                            case BuiltInType.ExpandedNodeId: { WriteExpandedNodeId("ExpandedNodeId", (ExpandedNodeId)variant.Value); break; }
                            case BuiltInType.StatusCode: { WriteStatusCode("StatusCode", (StatusCode)variant.Value); break; }
                            case BuiltInType.XmlElement: { WriteXmlElement("XmlElement", (System.Xml.XmlElement)variant.Value); break; }
                            case BuiltInType.ExtensionObject: { WriteExtensionObject("ExtensionObject", (ExtensionObject)variant.Value); break; }
                        }
                    }
                    else
                    {
                        switch (variant.TypeInfo.BuiltInType)
                        {
                            case BuiltInType.Boolean: { WriteBooleanArray("BooleanArray", (bool[])variant.Value); break; }
                            case BuiltInType.SByte: { WriteSByteArray("SByteArray", (sbyte[])variant.Value); break; }
                            case BuiltInType.Byte: { WriteByteArray("ByteArray", (byte[])variant.Value); break; }
                            case BuiltInType.Int16: { WriteInt16Array("ByteArray", (short[])variant.Value); break; }
                            case BuiltInType.UInt16: { WriteUInt16Array("UInt16Array", (ushort[])variant.Value); break; }
                            case BuiltInType.Int32: { WriteInt32Array("Int32Array", (int[])variant.Value); break; }
                            case BuiltInType.UInt32: { WriteUInt32Array("UInt32Array", (uint[])variant.Value); break; }
                            case BuiltInType.Int64: { WriteInt64Array("Int64Array", (long[])variant.Value); break; }
                            case BuiltInType.UInt64: { WriteUInt64Array("UInt64Array", (ulong[])variant.Value); break; }
                            case BuiltInType.Float: { WriteFloatArray("FloatArray", (float[])variant.Value); break; }
                            case BuiltInType.Double: { WriteDoubleArray("DoubleArray", (double[])variant.Value); break; }
                            case BuiltInType.DateTime: { WriteDateTimeArray("DateTimeArray", (DateTime[])variant.Value); break; }
                            case BuiltInType.Guid: { WriteGuidArray("GuidArray", (Uuid[])variant.Value); break; }
                            case BuiltInType.String: { WriteStringArray("StringArray", (string[])variant.Value); break; }
                            case BuiltInType.ByteString: { WriteByteStringArray("StringArray", (byte[][])variant.Value); break; }
                            case BuiltInType.QualifiedName: { WriteQualifiedNameArray("QualifiedNameArray", (QualifiedName[])variant.Value); break; }
                            case BuiltInType.LocalizedText: { WriteLocalizedTextArray("LocalizedTextArray", (LocalizedText[])variant.Value); break; }
                            case BuiltInType.NodeId: { WriteNodeIdArray("NodeIdArray", (NodeId[])variant.Value); break; }
                            case BuiltInType.ExpandedNodeId: { WriteExpandedNodeIdArray("ExpandedNodeIdArray", (ExpandedNodeId[])variant.Value); break; }
                            case BuiltInType.StatusCode: { WriteStatusCodeArray("StatusCodeArray", (StatusCode[])variant.Value); break; }
                            case BuiltInType.XmlElement: { WriteXmlElementArray("XmlElementArray", (System.Xml.XmlElement[])variant.Value); break; }
                            case BuiltInType.ExtensionObject: { WriteExtensionObjectArray("ExtensionObjectArray", (ExtensionObject[])variant.Value); break; }
                            case BuiltInType.Variant: { WriteVariantArray("VariantArray", (Variant[])variant.Value); break; }
                        }
                    }

                    return;
                }
                else if(IsBitSet(DataSetFlags1,2))
                { 
                    WriteDataValue("DataValue",dataValue);
                }
                else
                {
                    WriteVariant("Variant", dataValue.WrappedValue);
                }
                //FieldEncoding is pending
            }
        }

        public void Encode()
        {
            EncodeDataSetMessageHeader();
            EncodePayload();
        }
        public byte SetBit(byte b, int pos)
        {
            if (pos < 0 || pos > 7)
            {
                throw new ArgumentOutOfRangeException("pos", "Index must be in the range of 0-7.");
            }

           return (byte)(b | (1 << pos));
        }

        private bool IsBitSet(byte value, int bitNumber)
        {
            if ((bitNumber < 0) || (bitNumber > 7))
            {
                throw new ArgumentOutOfRangeException("bitNumber", bitNumber, "bitNumber must be 0..7");
            }
            return ((value & (1 << bitNumber)) != 0);
        }

    }

    public class UadpNetworkMessageDecoder: BinaryDecoder
    {
        public bool IsPublishedEnabled = false;
        bool IsGroupHeaderEnabled = false;
        bool IsPayloadHeaderEnabled = false;
        bool IsExtensionFlag1Enabled = false;

        byte ExtendedFlag1 = 0;
        bool IsDataSetClassIdEnabled = false;
        bool IsSecurityEnabled = false;
        bool IsTimestampEnabled = false;
        bool IsPicoSecondEnabled = false;
        bool IsExtensionFlag2Enabled = false;
        bool IsChuckMessage = false;
        bool IsPromotedFieldsEnabled = false;
        bool IsDataSetMessageType = false;
        bool IsWriterGroupEnabled = false;
        bool IsGroupVersionEnabled = false;
        bool IsNetworkMessageNumberEnabled = false;
        bool IsSequenceNumberEnabled = false;
        bool IsNetworkMessageSigned = false;
        bool IsNetworkMessageEncrypted = false;
        bool IsSecurityFooterEnabled = false;
         Int32 MessageCount = 0;
        public List<UInt16> DataSetWriterIds = new List<ushort>();
       public object PublisherId;
        Guid DataSetClassId;
        UInt16 PublishedIdType = 0;
        UInt16 WriterGroupId = 0;
        UInt32 GroupVersion = 0;
        UInt16 NetworkMessageNumber = 0;
        UInt16 SequenceNumber = 0;
        UInt32 SecurityTokenId = 0;
        byte NonceLength = 0;
        byte[] NonceMessage = new byte[] { };
        UInt16 SecurityFootersize = 0;
        List<UInt64> DataSetMessageSizes = new List<UInt64>();
        
        public Dictionary<UInt64, UadpDataSetMessageDecoder> DicDataSetWiter_Message = new Dictionary<UInt64, UadpDataSetMessageDecoder>();
        public UadpNetworkMessageDecoder(byte[] data): base(data,new ServiceMessageContext())
        {

        }
        public void Decode()
        {
            byte UADPVersionFlag = ReadByte("UADPVersionFlag");
            IsPublishedEnabled = IsBitSet(UADPVersionFlag, 4);
            IsGroupHeaderEnabled = IsBitSet(UADPVersionFlag, 5);
            IsPayloadHeaderEnabled = IsBitSet(UADPVersionFlag, 6);
            IsExtensionFlag1Enabled = IsBitSet(UADPVersionFlag, 7);
            byte ExtendedFlag1 = 0;
            if (IsExtensionFlag1Enabled)
            {
                ExtendedFlag1 = ReadByte("ExtendedFlag1");
            }
            if (IsBitSet(ExtendedFlag1, 0) && IsBitSet(ExtendedFlag1, 1))
            {
                PublishedIdType = 3; //UINT64
            }
            else if (IsBitSet(ExtendedFlag1, 0))
            {
                PublishedIdType = 1; //UINT16
            }
            else if (IsBitSet(ExtendedFlag1, 1))
            {
                PublishedIdType = 2; //UINT32
            }
            else if (IsBitSet(ExtendedFlag1, 2))
            {
                PublishedIdType = 4; //String
            }

            IsDataSetClassIdEnabled = IsBitSet(ExtendedFlag1, 3);
            IsSecurityEnabled = IsBitSet(ExtendedFlag1, 4);
            IsTimestampEnabled = IsBitSet(ExtendedFlag1, 5);
            IsPicoSecondEnabled = IsBitSet(ExtendedFlag1, 6);
            IsExtensionFlag2Enabled = IsBitSet(ExtendedFlag1, 7);
            if (IsExtensionFlag2Enabled)
            {
                byte ExtendedFlag2 = ReadByte("ExtendedFlag2");
                IsChuckMessage = IsBitSet(ExtendedFlag2, 0);
                IsPromotedFieldsEnabled = IsBitSet(ExtendedFlag2, 1);
                if (!IsBitSet(ExtendedFlag2, 2) && !IsBitSet(ExtendedFlag2, 3) && !IsBitSet(ExtendedFlag2, 4))
                {
                    IsDataSetMessageType = true;
                }
            }
            else
            {
                IsDataSetMessageType = true;
            }
            if (IsPublishedEnabled)
            {
                switch (PublishedIdType)
                {
                    case 0:
                        PublisherId = ReadByte("PublisherId");
                        break;
                    case 1: PublisherId = ReadUInt16("PublisherId"); break;
                    case 2: PublisherId = ReadUInt32("PublisherId"); break;
                    case 3: PublisherId = ReadUInt64("PublisherId"); break;
                    case 4: PublisherId = ReadString("PublisherId"); break;

                }
            }
            if (IsDataSetClassIdEnabled)
            {
                DataSetClassId = ReadGuid("IsDataSetClassIdEnabled");
            }

            if (IsGroupHeaderEnabled)
            {
                byte GroupFlags = ReadByte("GroupFlags");
                IsWriterGroupEnabled = IsBitSet(GroupFlags, 0);
                IsGroupVersionEnabled = IsBitSet(GroupFlags, 1);
                IsNetworkMessageNumberEnabled = IsBitSet(GroupFlags, 2);
                IsSequenceNumberEnabled = IsBitSet(GroupFlags, 3);

                if (IsWriterGroupEnabled)
                {
                    WriterGroupId = ReadUInt16("WriterGroupId");
                }
                if (IsGroupVersionEnabled)
                {
                    GroupVersion = ReadUInt32("GroupVersion");
                }
                if (IsNetworkMessageNumberEnabled)
                {
                    NetworkMessageNumber = ReadUInt16("NetworkMessageNumber");
                }
                if (IsSequenceNumberEnabled)
                {
                    SequenceNumber = ReadUInt16("SequenceNumber");
                }

            }

            #region Payload Header
            if (IsPayloadHeaderEnabled)
            {
                MessageCount = ReadInt32("MessageCount");
                for (int i = 1; i <= MessageCount; i++)
                {
                    DataSetWriterIds.Add(ReadUInt16("DataSetWriterId"));
                }
            }
            #endregion

            #region Extended Network Message Header
            if (IsTimestampEnabled)
            {
                DateTime Timestamp = ReadDateTime("Timestamp");
            }
            if (IsPicoSecondEnabled)
            {
                UInt16 PicoSeconds = ReadUInt16("Picoseconds");
            }
            if (IsPromotedFieldsEnabled)
            {
                UInt16 PromotedFieldsSize = ReadUInt16("PromotedFieldsSize");
                if(PromotedFieldsSize > 0)
                {
                    byte[] PromotedFields = new byte[PromotedFieldsSize];
                    BaseStream.Read(PromotedFields, 0, PromotedFieldsSize);
                    UInt16 RemainingBytes = PromotedFieldsSize;
                    PromotedFieldDecoder decoder = new PromotedFieldDecoder(PromotedFields);
                    do
                    {
                        
                        int usedLength = decoder.Decode();
                         RemainingBytes = (UInt16)(RemainingBytes - usedLength);

                    } while (RemainingBytes > 0);

                }
               
            }

            #endregion

            #region Security Header
            if (IsSecurityEnabled)
            {
                byte SecurityFlags = ReadByte("Security");
                IsNetworkMessageSigned = IsBitSet(SecurityFlags, 0);
                IsNetworkMessageEncrypted = IsBitSet(SecurityFlags, 1);
                IsSecurityFooterEnabled = IsBitSet(SecurityFlags, 2);

                SecurityTokenId = ReadUInt32("SecurityTokenId");
                NonceLength = ReadByte("Noncelength");
                NonceMessage = new byte[NonceLength];
                for (int i = 1; i <= NonceLength; i++)
                {
                    NonceMessage[i - 1] = ReadByte("NonceMessage");
                }
                SecurityFootersize = ReadUInt16("SecurityFootersize");
            }
            #endregion

            #region Payload Size
            for (int i = 1; i <= MessageCount; i++)
            {
                DataSetMessageSizes.Add(ReadUInt64("DataSetMessageSize"));
            }
             
            #endregion
        }


        private bool IsBitSet(byte value, int bitNumber)
        {
            if ((bitNumber < 0) || (bitNumber > 7))
            {
                throw new ArgumentOutOfRangeException("bitNumber", bitNumber, "bitNumber must be 0..7");
            }
            return ((value & (1 << bitNumber)) != 0);
        }
    }

    public class UadpDataSetMessageDecoder : BinaryDecoder
    {
        byte DataSetFlags1 = 0;
        byte DataSetFlags2 = 0;
        UInt16 DataSetMessageSequenceNumber = 0;
        DateTime TimeStamp { get; set; }
        UInt16 PicoSeconds;
        UInt16 Status;
        UInt32 ConfigurationMajorVersion = 1;
        UInt32 ConfigurationMinorVersion = 1;
        Int32 FieldCount = 0;
        public List<DataValue> LstFieldMessageData = new List<DataValue>();

        public DataSetMetaDataType m_dataSetMetaDataType { get; set; }
        public UadpDataSetMessageDecoder(Stream stream, DataSetMetaDataType metaDataType) : base(stream, new ServiceMessageContext())
        {
            m_dataSetMetaDataType = metaDataType;
        }
        public void Decode()
        {
            DecodeDataSetMessageHeader();
            DecodeFieldMessageData();

        }
        private void DecodeDataSetMessageHeader()
        {
            DataSetFlags1 = ReadByte("DataSetFlags1");
            if (IsBitSet(DataSetFlags1, 7)) // IsDataSetFlag2 Enabled
            {
                DataSetFlags2 = ReadByte("DataSetFlags2");
            }
            DataSetMessageSequenceNumber = ReadUInt16("DataSetMessageSequenceNumber");

            if (IsBitSet(DataSetFlags2, 4))
            {
                TimeStamp = ReadDateTime("Timestamp");
            }
            if (IsBitSet(DataSetFlags2, 5))
            {
                PicoSeconds = ReadUInt16("Picoseconds");
            }
            if (IsBitSet(DataSetFlags1, 4))
            {
                Status = ReadUInt16("Status");
            }
            if (IsBitSet(DataSetFlags1, 5))
            {
                ConfigurationMajorVersion = ReadUInt32("ConfigurationMajorVersion");
            }
            if (IsBitSet(DataSetFlags1, 6))
            {
                ConfigurationMinorVersion = ReadUInt32("ConfigurationMinorVersion");
            }
        }

        private void DecodeFieldMessageData()
        {
            FieldCount = ReadInt32("DataSetFieldCount");
            bool isVariant = false;
            bool isRawData = false;
            bool isDataValue = false;
            if (IsBitSet(DataSetFlags1, 1) && IsBitSet(DataSetFlags1, 2)) // Variant Data
            {
                isVariant = true;
            }
            else if (IsBitSet(DataSetFlags1, 1) && !IsBitSet(DataSetFlags1, 2)) //Raw Data
            {
                isRawData = true;
            }
            else if (!IsBitSet(DataSetFlags1, 1) && IsBitSet(DataSetFlags1, 2)) //DataValue
            {
                isDataValue = true;
            }
            for (int i = 1; i <= FieldCount; i++)
            {
                DataValue dataValue = new DataValue();
                if (isVariant)
                {
                    
                    dataValue.SourceTimestamp = DateTime.UtcNow;
                    dataValue.Value = ReadVariant("FieldValue");
                    
                     
                }
                else if (isRawData)
                {
                    FieldMetaData metaData = m_dataSetMetaDataType.Fields[i-1];
                     
                    dataValue.SourceTimestamp = DateTime.UtcNow;
                    dataValue.Value = ReadRawData(metaData.DataType);
                     
                }
                else if (isDataValue)
                {
                      dataValue = ReadDataValue("FieldValue");
                     
                }
                LstFieldMessageData.Add(dataValue);
            }
        }

        private bool IsBitSet(byte value, int bitNumber)
        {
            if ((bitNumber < 0) || (bitNumber > 7))
            {
                throw new ArgumentOutOfRangeException("bitNumber", bitNumber, "bitNumber must be 0..7");
            }
            return ((value & (1 << bitNumber)) != 0);
        }

        private object ReadRawData(NodeId DataTypeId)
        {
            object value = null;
            switch ((uint)DataTypeId.Identifier)
            {

                case Opc.Ua.DataTypes.Boolean:
                    {
                        value = (ReadBoolean(null));
                        break;
                    }

                case Opc.Ua.DataTypes.SByte:
                    {
                        value = (ReadSByte(null));
                        break;
                    }

                case Opc.Ua.DataTypes.Byte:
                    {
                        value = (ReadByte(null));
                        break;
                    }

                case Opc.Ua.DataTypes.Int16:
                    {
                        value = (ReadInt16(null));
                        break;
                    }

                case Opc.Ua.DataTypes.UInt16:
                    {
                        value = (ReadUInt16(null));
                        break;
                    }

                case Opc.Ua.DataTypes.Int32:
                case Opc.Ua.DataTypes.Enumeration:
                    {
                        value = (ReadInt32(null));
                        break;
                    }

                case Opc.Ua.DataTypes.UInt32:
                    {
                        value = (ReadUInt32(null));
                        break;
                    }

                case Opc.Ua.DataTypes.Int64:
                    {
                        value = (ReadInt64(null));
                        break;
                    }

                case Opc.Ua.DataTypes.UInt64:
                    {
                        value = (ReadUInt64(null));
                        break;
                    }

                case Opc.Ua.DataTypes.Float:
                    {
                        value = (ReadFloat(null));
                        break;
                    }

                case Opc.Ua.DataTypes.Double:
                    {
                        value = (ReadDouble(null));
                        break;
                    }

                case Opc.Ua.DataTypes.String:
                    {
                        value = (ReadString(null));
                        break;
                    }

                case Opc.Ua.DataTypes.DateTime:
                    {
                        value = (ReadDateTime(null));
                        break;
                    }

                case Opc.Ua.DataTypes.Guid:
                    {
                        value = (ReadGuid(null));
                        break;
                    }

                case Opc.Ua.DataTypes.ByteString:
                    {
                        value = (ReadByteString(null));
                        break;
                    }

                case Opc.Ua.DataTypes.XmlElement:
                    {
                        try
                        {
                            value = (ReadXmlElement(null));
                        }
                        catch (Exception ex)
                        {
                            Utils.Trace(ex, "Error reading xml element for variant.");
                            value = (StatusCodes.BadEncodingError);
                        }
                        break;
                    }

                case Opc.Ua.DataTypes.NodeId:
                    {
                        value = (ReadNodeId(null));
                        break;
                    }

                case Opc.Ua.DataTypes.ExpandedNodeId:
                    {
                        value = (ReadExpandedNodeId(null));
                        break;
                    }

                case Opc.Ua.DataTypes.StatusCode:
                    {
                        value = (ReadStatusCode(null));
                        break;
                    }

                case Opc.Ua.DataTypes.QualifiedName:
                    {
                        value = (ReadQualifiedName(null));
                        break;
                    }

                case Opc.Ua.DataTypes.LocalizedText:
                    {
                        value = (ReadLocalizedText(null));
                        break;
                    }
                case Opc.Ua.DataTypes.DataValue:
                    {
                        value = (ReadDataValue(null));
                        break;
                    }


            }
            return value;
        }
    }

    class PromotedFields : BinaryEncoder
    {
        public PromotedFields() : base(new ServiceMessageContext())
        {

        }
        public UInt16 Order { get; set; }
        public NodeId DataType { get; set; }
        
        public void Encode()
        {
            WriteUInt16("Order", Order);
            WriteNodeId("DataType", DataType);
        }

        
    }

    public class PromotedFieldDecoder:BinaryDecoder
    {
        public UInt16 Order { get; set; }
        public NodeId DataType { get; set; }

        public UInt16 Size { get; set; }

        public PromotedFieldDecoder(byte[] length) : base(length, new ServiceMessageContext())
        {
        }
        public UInt16 Decode()
        {
            Size = ReadUInt16("Size");
            Order= ReadUInt16("Order");
            DataType= ReadNodeId("DataType");

            return 6;

        }
    }
}


 