/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace TestData
{
    #region ScalarValueDataType Class
    #if (!OPCUA_EXCLUDE_ScalarValueDataType)
    /// <summary>
    /// A description for the ScalarValueDataType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = TestData.Namespaces.TestData)]
    public partial class ScalarValueDataType : IEncodeable
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ScalarValueDataType()
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_booleanValue = true;
            m_sByteValue = (sbyte)0;
            m_byteValue = (byte)0;
            m_int16Value = (short)0;
            m_uInt16Value = (ushort)0;
            m_int32Value = (int)0;
            m_uInt32Value = (uint)0;
            m_int64Value = (long)0;
            m_uInt64Value = (ulong)0;
            m_floatValue = (float)0;
            m_doubleValue = (double)0;
            m_stringValue = null;
            m_dateTimeValue = DateTime.MinValue;
            m_guidValue = Uuid.Empty;
            m_byteStringValue = null;
            m_xmlElementValue = null;
            m_nodeIdValue = null;
            m_expandedNodeIdValue = null;
            m_qualifiedNameValue = null;
            m_localizedTextValue = null;
            m_statusCodeValue = StatusCodes.Good;
            m_variantValue = Variant.Null;
            m_enumerationValue = 0;
            m_structureValue = null;
            m_number = (double)0;
            m_integer = (long)0;
            m_uInteger = (ulong)0;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the BooleanValue field.
        /// </summary>
        [DataMember(Name = "BooleanValue", IsRequired = false, Order = 1)]
        public bool BooleanValue
        {
            get { return m_booleanValue;  }
            set { m_booleanValue = value; }
        }

        /// <summary>
        /// A description for the SByteValue field.
        /// </summary>
        [DataMember(Name = "SByteValue", IsRequired = false, Order = 2)]
        public sbyte SByteValue
        {
            get { return m_sByteValue;  }
            set { m_sByteValue = value; }
        }

        /// <summary>
        /// A description for the ByteValue field.
        /// </summary>
        [DataMember(Name = "ByteValue", IsRequired = false, Order = 3)]
        public byte ByteValue
        {
            get { return m_byteValue;  }
            set { m_byteValue = value; }
        }

        /// <summary>
        /// A description for the Int16Value field.
        /// </summary>
        [DataMember(Name = "Int16Value", IsRequired = false, Order = 4)]
        public short Int16Value
        {
            get { return m_int16Value;  }
            set { m_int16Value = value; }
        }

        /// <summary>
        /// A description for the UInt16Value field.
        /// </summary>
        [DataMember(Name = "UInt16Value", IsRequired = false, Order = 5)]
        public ushort UInt16Value
        {
            get { return m_uInt16Value;  }
            set { m_uInt16Value = value; }
        }

        /// <summary>
        /// A description for the Int32Value field.
        /// </summary>
        [DataMember(Name = "Int32Value", IsRequired = false, Order = 6)]
        public int Int32Value
        {
            get { return m_int32Value;  }
            set { m_int32Value = value; }
        }

        /// <summary>
        /// A description for the UInt32Value field.
        /// </summary>
        [DataMember(Name = "UInt32Value", IsRequired = false, Order = 7)]
        public uint UInt32Value
        {
            get { return m_uInt32Value;  }
            set { m_uInt32Value = value; }
        }

        /// <summary>
        /// A description for the Int64Value field.
        /// </summary>
        [DataMember(Name = "Int64Value", IsRequired = false, Order = 8)]
        public long Int64Value
        {
            get { return m_int64Value;  }
            set { m_int64Value = value; }
        }

        /// <summary>
        /// A description for the UInt64Value field.
        /// </summary>
        [DataMember(Name = "UInt64Value", IsRequired = false, Order = 9)]
        public ulong UInt64Value
        {
            get { return m_uInt64Value;  }
            set { m_uInt64Value = value; }
        }

        /// <summary>
        /// A description for the FloatValue field.
        /// </summary>
        [DataMember(Name = "FloatValue", IsRequired = false, Order = 10)]
        public float FloatValue
        {
            get { return m_floatValue;  }
            set { m_floatValue = value; }
        }

        /// <summary>
        /// A description for the DoubleValue field.
        /// </summary>
        [DataMember(Name = "DoubleValue", IsRequired = false, Order = 11)]
        public double DoubleValue
        {
            get { return m_doubleValue;  }
            set { m_doubleValue = value; }
        }

        /// <summary>
        /// A description for the StringValue field.
        /// </summary>
        [DataMember(Name = "StringValue", IsRequired = false, Order = 12)]
        public string StringValue
        {
            get { return m_stringValue;  }
            set { m_stringValue = value; }
        }

        /// <summary>
        /// A description for the DateTimeValue field.
        /// </summary>
        [DataMember(Name = "DateTimeValue", IsRequired = false, Order = 13)]
        public DateTime DateTimeValue
        {
            get { return m_dateTimeValue;  }
            set { m_dateTimeValue = value; }
        }

        /// <summary>
        /// A description for the GuidValue field.
        /// </summary>
        [DataMember(Name = "GuidValue", IsRequired = false, Order = 14)]
        public Uuid GuidValue
        {
            get { return m_guidValue;  }
            set { m_guidValue = value; }
        }

        /// <summary>
        /// A description for the ByteStringValue field.
        /// </summary>
        [DataMember(Name = "ByteStringValue", IsRequired = false, Order = 15)]
        public byte[] ByteStringValue
        {
            get { return m_byteStringValue;  }
            set { m_byteStringValue = value; }
        }

        /// <summary>
        /// A description for the XmlElementValue field.
        /// </summary>
        [DataMember(Name = "XmlElementValue", IsRequired = false, Order = 16)]
        public XmlElement XmlElementValue
        {
            get { return m_xmlElementValue;  }
            set { m_xmlElementValue = value; }
        }

        /// <summary>
        /// A description for the NodeIdValue field.
        /// </summary>
        [DataMember(Name = "NodeIdValue", IsRequired = false, Order = 17)]
        public NodeId NodeIdValue
        {
            get { return m_nodeIdValue;  }
            set { m_nodeIdValue = value; }
        }

        /// <summary>
        /// A description for the ExpandedNodeIdValue field.
        /// </summary>
        [DataMember(Name = "ExpandedNodeIdValue", IsRequired = false, Order = 18)]
        public ExpandedNodeId ExpandedNodeIdValue
        {
            get { return m_expandedNodeIdValue;  }
            set { m_expandedNodeIdValue = value; }
        }

        /// <summary>
        /// A description for the QualifiedNameValue field.
        /// </summary>
        [DataMember(Name = "QualifiedNameValue", IsRequired = false, Order = 19)]
        public QualifiedName QualifiedNameValue
        {
            get { return m_qualifiedNameValue;  }
            set { m_qualifiedNameValue = value; }
        }

        /// <summary>
        /// A description for the LocalizedTextValue field.
        /// </summary>
        [DataMember(Name = "LocalizedTextValue", IsRequired = false, Order = 20)]
        public LocalizedText LocalizedTextValue
        {
            get { return m_localizedTextValue;  }
            set { m_localizedTextValue = value; }
        }

        /// <summary>
        /// A description for the StatusCodeValue field.
        /// </summary>
        [DataMember(Name = "StatusCodeValue", IsRequired = false, Order = 21)]
        public StatusCode StatusCodeValue
        {
            get { return m_statusCodeValue;  }
            set { m_statusCodeValue = value; }
        }

        /// <summary>
        /// A description for the VariantValue field.
        /// </summary>
        [DataMember(Name = "VariantValue", IsRequired = false, Order = 22)]
        public Variant VariantValue
        {
            get { return m_variantValue;  }
            set { m_variantValue = value; }
        }

        /// <summary>
        /// A description for the EnumerationValue field.
        /// </summary>
        [DataMember(Name = "EnumerationValue", IsRequired = false, Order = 23)]
        public int EnumerationValue
        {
            get { return m_enumerationValue;  }
            set { m_enumerationValue = value; }
        }

        /// <summary>
        /// A description for the StructureValue field.
        /// </summary>
        [DataMember(Name = "StructureValue", IsRequired = false, Order = 24)]
        public ExtensionObject StructureValue
        {
            get { return m_structureValue;  }
            set { m_structureValue = value; }
        }

        /// <summary>
        /// A description for the Number field.
        /// </summary>
        [DataMember(Name = "Number", IsRequired = false, Order = 25)]
        public Variant Number
        {
            get { return m_number;  }
            set { m_number = value; }
        }

        /// <summary>
        /// A description for the Integer field.
        /// </summary>
        [DataMember(Name = "Integer", IsRequired = false, Order = 26)]
        public Variant Integer
        {
            get { return m_integer;  }
            set { m_integer = value; }
        }

        /// <summary>
        /// A description for the UInteger field.
        /// </summary>
        [DataMember(Name = "UInteger", IsRequired = false, Order = 27)]
        public Variant UInteger
        {
            get { return m_uInteger;  }
            set { m_uInteger = value; }
        }
        #endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public virtual ExpandedNodeId TypeId
        {
            get { return DataTypeIds.ScalarValueDataType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public virtual ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.ScalarValueDataType_Encoding_DefaultBinary; }
        }

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public virtual ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.ScalarValueDataType_Encoding_DefaultXml; }
        }

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(TestData.Namespaces.TestData);

            encoder.WriteBoolean("BooleanValue", BooleanValue);
            encoder.WriteSByte("SByteValue", SByteValue);
            encoder.WriteByte("ByteValue", ByteValue);
            encoder.WriteInt16("Int16Value", Int16Value);
            encoder.WriteUInt16("UInt16Value", UInt16Value);
            encoder.WriteInt32("Int32Value", Int32Value);
            encoder.WriteUInt32("UInt32Value", UInt32Value);
            encoder.WriteInt64("Int64Value", Int64Value);
            encoder.WriteUInt64("UInt64Value", UInt64Value);
            encoder.WriteFloat("FloatValue", FloatValue);
            encoder.WriteDouble("DoubleValue", DoubleValue);
            encoder.WriteString("StringValue", StringValue);
            encoder.WriteDateTime("DateTimeValue", DateTimeValue);
            encoder.WriteGuid("GuidValue", GuidValue);
            encoder.WriteByteString("ByteStringValue", ByteStringValue);
            encoder.WriteXmlElement("XmlElementValue", XmlElementValue);
            encoder.WriteNodeId("NodeIdValue", NodeIdValue);
            encoder.WriteExpandedNodeId("ExpandedNodeIdValue", ExpandedNodeIdValue);
            encoder.WriteQualifiedName("QualifiedNameValue", QualifiedNameValue);
            encoder.WriteLocalizedText("LocalizedTextValue", LocalizedTextValue);
            encoder.WriteStatusCode("StatusCodeValue", StatusCodeValue);
            encoder.WriteVariant("VariantValue", VariantValue);
            encoder.WriteInt32("EnumerationValue", EnumerationValue);
            encoder.WriteExtensionObject("StructureValue", StructureValue);
            encoder.WriteVariant("Number", Number);
            encoder.WriteVariant("Integer", Integer);
            encoder.WriteVariant("UInteger", UInteger);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(TestData.Namespaces.TestData);

            BooleanValue = decoder.ReadBoolean("BooleanValue");
            SByteValue = decoder.ReadSByte("SByteValue");
            ByteValue = decoder.ReadByte("ByteValue");
            Int16Value = decoder.ReadInt16("Int16Value");
            UInt16Value = decoder.ReadUInt16("UInt16Value");
            Int32Value = decoder.ReadInt32("Int32Value");
            UInt32Value = decoder.ReadUInt32("UInt32Value");
            Int64Value = decoder.ReadInt64("Int64Value");
            UInt64Value = decoder.ReadUInt64("UInt64Value");
            FloatValue = decoder.ReadFloat("FloatValue");
            DoubleValue = decoder.ReadDouble("DoubleValue");
            StringValue = decoder.ReadString("StringValue");
            DateTimeValue = decoder.ReadDateTime("DateTimeValue");
            GuidValue = decoder.ReadGuid("GuidValue");
            ByteStringValue = decoder.ReadByteString("ByteStringValue");
            XmlElementValue = decoder.ReadXmlElement("XmlElementValue");
            NodeIdValue = decoder.ReadNodeId("NodeIdValue");
            ExpandedNodeIdValue = decoder.ReadExpandedNodeId("ExpandedNodeIdValue");
            QualifiedNameValue = decoder.ReadQualifiedName("QualifiedNameValue");
            LocalizedTextValue = decoder.ReadLocalizedText("LocalizedTextValue");
            StatusCodeValue = decoder.ReadStatusCode("StatusCodeValue");
            VariantValue = decoder.ReadVariant("VariantValue");
            EnumerationValue = decoder.ReadInt32("EnumerationValue");
            StructureValue = decoder.ReadExtensionObject("StructureValue");
            Number = decoder.ReadVariant("Number");
            Integer = decoder.ReadVariant("Integer");
            UInteger = decoder.ReadVariant("UInteger");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            ScalarValueDataType value = encodeable as ScalarValueDataType;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_booleanValue, value.m_booleanValue)) return false;
            if (!Utils.IsEqual(m_sByteValue, value.m_sByteValue)) return false;
            if (!Utils.IsEqual(m_byteValue, value.m_byteValue)) return false;
            if (!Utils.IsEqual(m_int16Value, value.m_int16Value)) return false;
            if (!Utils.IsEqual(m_uInt16Value, value.m_uInt16Value)) return false;
            if (!Utils.IsEqual(m_int32Value, value.m_int32Value)) return false;
            if (!Utils.IsEqual(m_uInt32Value, value.m_uInt32Value)) return false;
            if (!Utils.IsEqual(m_int64Value, value.m_int64Value)) return false;
            if (!Utils.IsEqual(m_uInt64Value, value.m_uInt64Value)) return false;
            if (!Utils.IsEqual(m_floatValue, value.m_floatValue)) return false;
            if (!Utils.IsEqual(m_doubleValue, value.m_doubleValue)) return false;
            if (!Utils.IsEqual(m_stringValue, value.m_stringValue)) return false;
            if (!Utils.IsEqual(m_dateTimeValue, value.m_dateTimeValue)) return false;
            if (!Utils.IsEqual(m_guidValue, value.m_guidValue)) return false;
            if (!Utils.IsEqual(m_byteStringValue, value.m_byteStringValue)) return false;
            if (!Utils.IsEqual(m_xmlElementValue, value.m_xmlElementValue)) return false;
            if (!Utils.IsEqual(m_nodeIdValue, value.m_nodeIdValue)) return false;
            if (!Utils.IsEqual(m_expandedNodeIdValue, value.m_expandedNodeIdValue)) return false;
            if (!Utils.IsEqual(m_qualifiedNameValue, value.m_qualifiedNameValue)) return false;
            if (!Utils.IsEqual(m_localizedTextValue, value.m_localizedTextValue)) return false;
            if (!Utils.IsEqual(m_statusCodeValue, value.m_statusCodeValue)) return false;
            if (!Utils.IsEqual(m_variantValue, value.m_variantValue)) return false;
            if (!Utils.IsEqual(m_enumerationValue, value.m_enumerationValue)) return false;
            if (!Utils.IsEqual(m_structureValue, value.m_structureValue)) return false;
            if (!Utils.IsEqual(m_number, value.m_number)) return false;
            if (!Utils.IsEqual(m_integer, value.m_integer)) return false;
            if (!Utils.IsEqual(m_uInteger, value.m_uInteger)) return false;

            return true;
        }
        
        public new object MemberwiseClone()
        {
            ScalarValueDataType clone = new ScalarValueDataType();

            clone.m_booleanValue = (bool)Utils.Clone(this.m_booleanValue);
            clone.m_sByteValue = (sbyte)Utils.Clone(this.m_sByteValue);
            clone.m_byteValue = (byte)Utils.Clone(this.m_byteValue);
            clone.m_int16Value = (short)Utils.Clone(this.m_int16Value);
            clone.m_uInt16Value = (ushort)Utils.Clone(this.m_uInt16Value);
            clone.m_int32Value = (int)Utils.Clone(this.m_int32Value);
            clone.m_uInt32Value = (uint)Utils.Clone(this.m_uInt32Value);
            clone.m_int64Value = (long)Utils.Clone(this.m_int64Value);
            clone.m_uInt64Value = (ulong)Utils.Clone(this.m_uInt64Value);
            clone.m_floatValue = (float)Utils.Clone(this.m_floatValue);
            clone.m_doubleValue = (double)Utils.Clone(this.m_doubleValue);
            clone.m_stringValue = (string)Utils.Clone(this.m_stringValue);
            clone.m_dateTimeValue = (DateTime)Utils.Clone(this.m_dateTimeValue);
            clone.m_guidValue = (Uuid)Utils.Clone(this.m_guidValue);
            clone.m_byteStringValue = (byte[])Utils.Clone(this.m_byteStringValue);
            clone.m_xmlElementValue = (XmlElement)Utils.Clone(this.m_xmlElementValue);
            clone.m_nodeIdValue = (NodeId)Utils.Clone(this.m_nodeIdValue);
            clone.m_expandedNodeIdValue = (ExpandedNodeId)Utils.Clone(this.m_expandedNodeIdValue);
            clone.m_qualifiedNameValue = (QualifiedName)Utils.Clone(this.m_qualifiedNameValue);
            clone.m_localizedTextValue = (LocalizedText)Utils.Clone(this.m_localizedTextValue);
            clone.m_statusCodeValue = (StatusCode)Utils.Clone(this.m_statusCodeValue);
            clone.m_variantValue = (Variant)Utils.Clone(this.m_variantValue);
            clone.m_enumerationValue = (int)Utils.Clone(this.m_enumerationValue);
            clone.m_structureValue = (ExtensionObject)Utils.Clone(this.m_structureValue);
            clone.m_number = (Variant)Utils.Clone(this.m_number);
            clone.m_integer = (Variant)Utils.Clone(this.m_integer);
            clone.m_uInteger = (Variant)Utils.Clone(this.m_uInteger);

            return clone;
        }
        #endregion

        #region Private Fields
        private bool m_booleanValue;
        private sbyte m_sByteValue;
        private byte m_byteValue;
        private short m_int16Value;
        private ushort m_uInt16Value;
        private int m_int32Value;
        private uint m_uInt32Value;
        private long m_int64Value;
        private ulong m_uInt64Value;
        private float m_floatValue;
        private double m_doubleValue;
        private string m_stringValue;
        private DateTime m_dateTimeValue;
        private Uuid m_guidValue;
        private byte[] m_byteStringValue;
        private XmlElement m_xmlElementValue;
        private NodeId m_nodeIdValue;
        private ExpandedNodeId m_expandedNodeIdValue;
        private QualifiedName m_qualifiedNameValue;
        private LocalizedText m_localizedTextValue;
        private StatusCode m_statusCodeValue;
        private Variant m_variantValue;
        private int m_enumerationValue;
        private ExtensionObject m_structureValue;
        private Variant m_number;
        private Variant m_integer;
        private Variant m_uInteger;
        #endregion
    }

    #region ScalarValueDataTypeCollection Class
    /// <summary>
    /// A collection of ScalarValueDataType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfScalarValueDataType", Namespace = TestData.Namespaces.TestData, ItemName = "ScalarValueDataType")]
    public partial class ScalarValueDataTypeCollection : List<ScalarValueDataType>
    {
        #region Constructors
        /// <summary>
        /// Initializes the collection with default values.
        /// </summary>
        public ScalarValueDataTypeCollection() {}

        /// <summary>
        /// Initializes the collection with an initial capacity.
        /// </summary>
        public ScalarValueDataTypeCollection(int capacity) : base(capacity) {}

        /// <summary>
        /// Initializes the collection with another collection.
        /// </summary>
        public ScalarValueDataTypeCollection(IEnumerable<ScalarValueDataType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator ScalarValueDataTypeCollection(ScalarValueDataType[] values)
        {
            if (values != null)
            {
                return new ScalarValueDataTypeCollection(values);
            }

            return new ScalarValueDataTypeCollection();
        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator ScalarValueDataType[](ScalarValueDataTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            ScalarValueDataTypeCollection clone = new ScalarValueDataTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((ScalarValueDataType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region ArrayValueDataType Class
    #if (!OPCUA_EXCLUDE_ArrayValueDataType)
    /// <summary>
    /// A description for the ArrayValueDataType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = TestData.Namespaces.TestData)]
    public partial class ArrayValueDataType : IEncodeable
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ArrayValueDataType()
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_booleanValue = new BooleanCollection();
            m_sByteValue = new SByteCollection();
            m_byteValue = new ByteCollection();
            m_int16Value = new Int16Collection();
            m_uInt16Value = new UInt16Collection();
            m_int32Value = new Int32Collection();
            m_uInt32Value = new UInt32Collection();
            m_int64Value = new Int64Collection();
            m_uInt64Value = new UInt64Collection();
            m_floatValue = new FloatCollection();
            m_doubleValue = new DoubleCollection();
            m_stringValue = new StringCollection();
            m_dateTimeValue = new DateTimeCollection();
            m_guidValue = new UuidCollection();
            m_byteStringValue = new ByteStringCollection();
            m_xmlElementValue = new XmlElementCollection();
            m_nodeIdValue = new NodeIdCollection();
            m_expandedNodeIdValue = new ExpandedNodeIdCollection();
            m_qualifiedNameValue = new QualifiedNameCollection();
            m_localizedTextValue = new LocalizedTextCollection();
            m_statusCodeValue = new StatusCodeCollection();
            m_variantValue = new VariantCollection();
            m_enumerationValue = new Int32Collection();
            m_structureValue = new ExtensionObjectCollection();
            m_number = new VariantCollection();
            m_integer = new VariantCollection();
            m_uInteger = new VariantCollection();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the BooleanValue field.
        /// </summary>
        [DataMember(Name = "BooleanValue", IsRequired = false, Order = 1)]
        public BooleanCollection BooleanValue
        {
            get
            {
                return m_booleanValue;
            }

            set
            {
                m_booleanValue = value;

                if (value == null)
                {
                    m_booleanValue = new BooleanCollection();
                }
            }
        }

        /// <summary>
        /// A description for the SByteValue field.
        /// </summary>
        [DataMember(Name = "SByteValue", IsRequired = false, Order = 2)]
        public SByteCollection SByteValue
        {
            get
            {
                return m_sByteValue;
            }

            set
            {
                m_sByteValue = value;

                if (value == null)
                {
                    m_sByteValue = new SByteCollection();
                }
            }
        }

        /// <summary>
        /// A description for the ByteValue field.
        /// </summary>
        [DataMember(Name = "ByteValue", IsRequired = false, Order = 3)]
        public ByteCollection ByteValue
        {
            get
            {
                return m_byteValue;
            }

            set
            {
                m_byteValue = value;

                if (value == null)
                {
                    m_byteValue = new ByteCollection();
                }
            }
        }

        /// <summary>
        /// A description for the Int16Value field.
        /// </summary>
        [DataMember(Name = "Int16Value", IsRequired = false, Order = 4)]
        public Int16Collection Int16Value
        {
            get
            {
                return m_int16Value;
            }

            set
            {
                m_int16Value = value;

                if (value == null)
                {
                    m_int16Value = new Int16Collection();
                }
            }
        }

        /// <summary>
        /// A description for the UInt16Value field.
        /// </summary>
        [DataMember(Name = "UInt16Value", IsRequired = false, Order = 5)]
        public UInt16Collection UInt16Value
        {
            get
            {
                return m_uInt16Value;
            }

            set
            {
                m_uInt16Value = value;

                if (value == null)
                {
                    m_uInt16Value = new UInt16Collection();
                }
            }
        }

        /// <summary>
        /// A description for the Int32Value field.
        /// </summary>
        [DataMember(Name = "Int32Value", IsRequired = false, Order = 6)]
        public Int32Collection Int32Value
        {
            get
            {
                return m_int32Value;
            }

            set
            {
                m_int32Value = value;

                if (value == null)
                {
                    m_int32Value = new Int32Collection();
                }
            }
        }

        /// <summary>
        /// A description for the UInt32Value field.
        /// </summary>
        [DataMember(Name = "UInt32Value", IsRequired = false, Order = 7)]
        public UInt32Collection UInt32Value
        {
            get
            {
                return m_uInt32Value;
            }

            set
            {
                m_uInt32Value = value;

                if (value == null)
                {
                    m_uInt32Value = new UInt32Collection();
                }
            }
        }

        /// <summary>
        /// A description for the Int64Value field.
        /// </summary>
        [DataMember(Name = "Int64Value", IsRequired = false, Order = 8)]
        public Int64Collection Int64Value
        {
            get
            {
                return m_int64Value;
            }

            set
            {
                m_int64Value = value;

                if (value == null)
                {
                    m_int64Value = new Int64Collection();
                }
            }
        }

        /// <summary>
        /// A description for the UInt64Value field.
        /// </summary>
        [DataMember(Name = "UInt64Value", IsRequired = false, Order = 9)]
        public UInt64Collection UInt64Value
        {
            get
            {
                return m_uInt64Value;
            }

            set
            {
                m_uInt64Value = value;

                if (value == null)
                {
                    m_uInt64Value = new UInt64Collection();
                }
            }
        }

        /// <summary>
        /// A description for the FloatValue field.
        /// </summary>
        [DataMember(Name = "FloatValue", IsRequired = false, Order = 10)]
        public FloatCollection FloatValue
        {
            get
            {
                return m_floatValue;
            }

            set
            {
                m_floatValue = value;

                if (value == null)
                {
                    m_floatValue = new FloatCollection();
                }
            }
        }

        /// <summary>
        /// A description for the DoubleValue field.
        /// </summary>
        [DataMember(Name = "DoubleValue", IsRequired = false, Order = 11)]
        public DoubleCollection DoubleValue
        {
            get
            {
                return m_doubleValue;
            }

            set
            {
                m_doubleValue = value;

                if (value == null)
                {
                    m_doubleValue = new DoubleCollection();
                }
            }
        }

        /// <summary>
        /// A description for the StringValue field.
        /// </summary>
        [DataMember(Name = "StringValue", IsRequired = false, Order = 12)]
        public StringCollection StringValue
        {
            get
            {
                return m_stringValue;
            }

            set
            {
                m_stringValue = value;

                if (value == null)
                {
                    m_stringValue = new StringCollection();
                }
            }
        }

        /// <summary>
        /// A description for the DateTimeValue field.
        /// </summary>
        [DataMember(Name = "DateTimeValue", IsRequired = false, Order = 13)]
        public DateTimeCollection DateTimeValue
        {
            get
            {
                return m_dateTimeValue;
            }

            set
            {
                m_dateTimeValue = value;

                if (value == null)
                {
                    m_dateTimeValue = new DateTimeCollection();
                }
            }
        }

        /// <summary>
        /// A description for the GuidValue field.
        /// </summary>
        [DataMember(Name = "GuidValue", IsRequired = false, Order = 14)]
        public UuidCollection GuidValue
        {
            get
            {
                return m_guidValue;
            }

            set
            {
                m_guidValue = value;

                if (value == null)
                {
                    m_guidValue = new UuidCollection();
                }
            }
        }

        /// <summary>
        /// A description for the ByteStringValue field.
        /// </summary>
        [DataMember(Name = "ByteStringValue", IsRequired = false, Order = 15)]
        public ByteStringCollection ByteStringValue
        {
            get
            {
                return m_byteStringValue;
            }

            set
            {
                m_byteStringValue = value;

                if (value == null)
                {
                    m_byteStringValue = new ByteStringCollection();
                }
            }
        }

        /// <summary>
        /// A description for the XmlElementValue field.
        /// </summary>
        [DataMember(Name = "XmlElementValue", IsRequired = false, Order = 16)]
        public XmlElementCollection XmlElementValue
        {
            get
            {
                return m_xmlElementValue;
            }

            set
            {
                m_xmlElementValue = value;

                if (value == null)
                {
                    m_xmlElementValue = new XmlElementCollection();
                }
            }
        }

        /// <summary>
        /// A description for the NodeIdValue field.
        /// </summary>
        [DataMember(Name = "NodeIdValue", IsRequired = false, Order = 17)]
        public NodeIdCollection NodeIdValue
        {
            get
            {
                return m_nodeIdValue;
            }

            set
            {
                m_nodeIdValue = value;

                if (value == null)
                {
                    m_nodeIdValue = new NodeIdCollection();
                }
            }
        }

        /// <summary>
        /// A description for the ExpandedNodeIdValue field.
        /// </summary>
        [DataMember(Name = "ExpandedNodeIdValue", IsRequired = false, Order = 18)]
        public ExpandedNodeIdCollection ExpandedNodeIdValue
        {
            get
            {
                return m_expandedNodeIdValue;
            }

            set
            {
                m_expandedNodeIdValue = value;

                if (value == null)
                {
                    m_expandedNodeIdValue = new ExpandedNodeIdCollection();
                }
            }
        }

        /// <summary>
        /// A description for the QualifiedNameValue field.
        /// </summary>
        [DataMember(Name = "QualifiedNameValue", IsRequired = false, Order = 19)]
        public QualifiedNameCollection QualifiedNameValue
        {
            get
            {
                return m_qualifiedNameValue;
            }

            set
            {
                m_qualifiedNameValue = value;

                if (value == null)
                {
                    m_qualifiedNameValue = new QualifiedNameCollection();
                }
            }
        }

        /// <summary>
        /// A description for the LocalizedTextValue field.
        /// </summary>
        [DataMember(Name = "LocalizedTextValue", IsRequired = false, Order = 20)]
        public LocalizedTextCollection LocalizedTextValue
        {
            get
            {
                return m_localizedTextValue;
            }

            set
            {
                m_localizedTextValue = value;

                if (value == null)
                {
                    m_localizedTextValue = new LocalizedTextCollection();
                }
            }
        }

        /// <summary>
        /// A description for the StatusCodeValue field.
        /// </summary>
        [DataMember(Name = "StatusCodeValue", IsRequired = false, Order = 21)]
        public StatusCodeCollection StatusCodeValue
        {
            get
            {
                return m_statusCodeValue;
            }

            set
            {
                m_statusCodeValue = value;

                if (value == null)
                {
                    m_statusCodeValue = new StatusCodeCollection();
                }
            }
        }

        /// <summary>
        /// A description for the VariantValue field.
        /// </summary>
        [DataMember(Name = "VariantValue", IsRequired = false, Order = 22)]
        public VariantCollection VariantValue
        {
            get
            {
                return m_variantValue;
            }

            set
            {
                m_variantValue = value;

                if (value == null)
                {
                    m_variantValue = new VariantCollection();
                }
            }
        }

        /// <summary>
        /// A description for the EnumerationValue field.
        /// </summary>
        [DataMember(Name = "EnumerationValue", IsRequired = false, Order = 23)]
        public Int32Collection EnumerationValue
        {
            get
            {
                return m_enumerationValue;
            }

            set
            {
                m_enumerationValue = value;

                if (value == null)
                {
                    m_enumerationValue = new Int32Collection();
                }
            }
        }

        /// <summary>
        /// A description for the StructureValue field.
        /// </summary>
        [DataMember(Name = "StructureValue", IsRequired = false, Order = 24)]
        public ExtensionObjectCollection StructureValue
        {
            get
            {
                return m_structureValue;
            }

            set
            {
                m_structureValue = value;

                if (value == null)
                {
                    m_structureValue = new ExtensionObjectCollection();
                }
            }
        }

        /// <summary>
        /// A description for the Number field.
        /// </summary>
        [DataMember(Name = "Number", IsRequired = false, Order = 25)]
        public VariantCollection Number
        {
            get
            {
                return m_number;
            }

            set
            {
                m_number = value;

                if (value == null)
                {
                    m_number = new VariantCollection();
                }
            }
        }

        /// <summary>
        /// A description for the Integer field.
        /// </summary>
        [DataMember(Name = "Integer", IsRequired = false, Order = 26)]
        public VariantCollection Integer
        {
            get
            {
                return m_integer;
            }

            set
            {
                m_integer = value;

                if (value == null)
                {
                    m_integer = new VariantCollection();
                }
            }
        }

        /// <summary>
        /// A description for the UInteger field.
        /// </summary>
        [DataMember(Name = "UInteger", IsRequired = false, Order = 27)]
        public VariantCollection UInteger
        {
            get
            {
                return m_uInteger;
            }

            set
            {
                m_uInteger = value;

                if (value == null)
                {
                    m_uInteger = new VariantCollection();
                }
            }
        }
        #endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public virtual ExpandedNodeId TypeId
        {
            get { return DataTypeIds.ArrayValueDataType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public virtual ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.ArrayValueDataType_Encoding_DefaultBinary; }
        }

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public virtual ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.ArrayValueDataType_Encoding_DefaultXml; }
        }

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(TestData.Namespaces.TestData);

            encoder.WriteBooleanArray("BooleanValue", BooleanValue);
            encoder.WriteSByteArray("SByteValue", SByteValue);
            encoder.WriteByteArray("ByteValue", ByteValue);
            encoder.WriteInt16Array("Int16Value", Int16Value);
            encoder.WriteUInt16Array("UInt16Value", UInt16Value);
            encoder.WriteInt32Array("Int32Value", Int32Value);
            encoder.WriteUInt32Array("UInt32Value", UInt32Value);
            encoder.WriteInt64Array("Int64Value", Int64Value);
            encoder.WriteUInt64Array("UInt64Value", UInt64Value);
            encoder.WriteFloatArray("FloatValue", FloatValue);
            encoder.WriteDoubleArray("DoubleValue", DoubleValue);
            encoder.WriteStringArray("StringValue", StringValue);
            encoder.WriteDateTimeArray("DateTimeValue", DateTimeValue);
            encoder.WriteGuidArray("GuidValue", GuidValue);
            encoder.WriteByteStringArray("ByteStringValue", ByteStringValue);
            encoder.WriteXmlElementArray("XmlElementValue", XmlElementValue);
            encoder.WriteNodeIdArray("NodeIdValue", NodeIdValue);
            encoder.WriteExpandedNodeIdArray("ExpandedNodeIdValue", ExpandedNodeIdValue);
            encoder.WriteQualifiedNameArray("QualifiedNameValue", QualifiedNameValue);
            encoder.WriteLocalizedTextArray("LocalizedTextValue", LocalizedTextValue);
            encoder.WriteStatusCodeArray("StatusCodeValue", StatusCodeValue);
            encoder.WriteVariantArray("VariantValue", VariantValue);
            encoder.WriteInt32Array("EnumerationValue", EnumerationValue);
            encoder.WriteExtensionObjectArray("StructureValue", StructureValue);
            encoder.WriteVariantArray("Number", Number);
            encoder.WriteVariantArray("Integer", Integer);
            encoder.WriteVariantArray("UInteger", UInteger);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(TestData.Namespaces.TestData);

            BooleanValue = decoder.ReadBooleanArray("BooleanValue");
            SByteValue = decoder.ReadSByteArray("SByteValue");
            ByteValue = decoder.ReadByteArray("ByteValue");
            Int16Value = decoder.ReadInt16Array("Int16Value");
            UInt16Value = decoder.ReadUInt16Array("UInt16Value");
            Int32Value = decoder.ReadInt32Array("Int32Value");
            UInt32Value = decoder.ReadUInt32Array("UInt32Value");
            Int64Value = decoder.ReadInt64Array("Int64Value");
            UInt64Value = decoder.ReadUInt64Array("UInt64Value");
            FloatValue = decoder.ReadFloatArray("FloatValue");
            DoubleValue = decoder.ReadDoubleArray("DoubleValue");
            StringValue = decoder.ReadStringArray("StringValue");
            DateTimeValue = decoder.ReadDateTimeArray("DateTimeValue");
            GuidValue = decoder.ReadGuidArray("GuidValue");
            ByteStringValue = decoder.ReadByteStringArray("ByteStringValue");
            XmlElementValue = decoder.ReadXmlElementArray("XmlElementValue");
            NodeIdValue = decoder.ReadNodeIdArray("NodeIdValue");
            ExpandedNodeIdValue = decoder.ReadExpandedNodeIdArray("ExpandedNodeIdValue");
            QualifiedNameValue = decoder.ReadQualifiedNameArray("QualifiedNameValue");
            LocalizedTextValue = decoder.ReadLocalizedTextArray("LocalizedTextValue");
            StatusCodeValue = decoder.ReadStatusCodeArray("StatusCodeValue");
            VariantValue = decoder.ReadVariantArray("VariantValue");
            EnumerationValue = decoder.ReadInt32Array("EnumerationValue");
            StructureValue = decoder.ReadExtensionObjectArray("StructureValue");
            Number = decoder.ReadVariantArray("Number");
            Integer = decoder.ReadVariantArray("Integer");
            UInteger = decoder.ReadVariantArray("UInteger");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            ArrayValueDataType value = encodeable as ArrayValueDataType;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_booleanValue, value.m_booleanValue)) return false;
            if (!Utils.IsEqual(m_sByteValue, value.m_sByteValue)) return false;
            if (!Utils.IsEqual(m_byteValue, value.m_byteValue)) return false;
            if (!Utils.IsEqual(m_int16Value, value.m_int16Value)) return false;
            if (!Utils.IsEqual(m_uInt16Value, value.m_uInt16Value)) return false;
            if (!Utils.IsEqual(m_int32Value, value.m_int32Value)) return false;
            if (!Utils.IsEqual(m_uInt32Value, value.m_uInt32Value)) return false;
            if (!Utils.IsEqual(m_int64Value, value.m_int64Value)) return false;
            if (!Utils.IsEqual(m_uInt64Value, value.m_uInt64Value)) return false;
            if (!Utils.IsEqual(m_floatValue, value.m_floatValue)) return false;
            if (!Utils.IsEqual(m_doubleValue, value.m_doubleValue)) return false;
            if (!Utils.IsEqual(m_stringValue, value.m_stringValue)) return false;
            if (!Utils.IsEqual(m_dateTimeValue, value.m_dateTimeValue)) return false;
            if (!Utils.IsEqual(m_guidValue, value.m_guidValue)) return false;
            if (!Utils.IsEqual(m_byteStringValue, value.m_byteStringValue)) return false;
            if (!Utils.IsEqual(m_xmlElementValue, value.m_xmlElementValue)) return false;
            if (!Utils.IsEqual(m_nodeIdValue, value.m_nodeIdValue)) return false;
            if (!Utils.IsEqual(m_expandedNodeIdValue, value.m_expandedNodeIdValue)) return false;
            if (!Utils.IsEqual(m_qualifiedNameValue, value.m_qualifiedNameValue)) return false;
            if (!Utils.IsEqual(m_localizedTextValue, value.m_localizedTextValue)) return false;
            if (!Utils.IsEqual(m_statusCodeValue, value.m_statusCodeValue)) return false;
            if (!Utils.IsEqual(m_variantValue, value.m_variantValue)) return false;
            if (!Utils.IsEqual(m_enumerationValue, value.m_enumerationValue)) return false;
            if (!Utils.IsEqual(m_structureValue, value.m_structureValue)) return false;
            if (!Utils.IsEqual(m_number, value.m_number)) return false;
            if (!Utils.IsEqual(m_integer, value.m_integer)) return false;
            if (!Utils.IsEqual(m_uInteger, value.m_uInteger)) return false;

            return true;
        }
        
        public new object MemberwiseClone()
        {
            ArrayValueDataType clone = new ArrayValueDataType();

            clone.m_booleanValue = (BooleanCollection)Utils.Clone(this.m_booleanValue);
            clone.m_sByteValue = (SByteCollection)Utils.Clone(this.m_sByteValue);
            clone.m_byteValue = (ByteCollection)Utils.Clone(this.m_byteValue);
            clone.m_int16Value = (Int16Collection)Utils.Clone(this.m_int16Value);
            clone.m_uInt16Value = (UInt16Collection)Utils.Clone(this.m_uInt16Value);
            clone.m_int32Value = (Int32Collection)Utils.Clone(this.m_int32Value);
            clone.m_uInt32Value = (UInt32Collection)Utils.Clone(this.m_uInt32Value);
            clone.m_int64Value = (Int64Collection)Utils.Clone(this.m_int64Value);
            clone.m_uInt64Value = (UInt64Collection)Utils.Clone(this.m_uInt64Value);
            clone.m_floatValue = (FloatCollection)Utils.Clone(this.m_floatValue);
            clone.m_doubleValue = (DoubleCollection)Utils.Clone(this.m_doubleValue);
            clone.m_stringValue = (StringCollection)Utils.Clone(this.m_stringValue);
            clone.m_dateTimeValue = (DateTimeCollection)Utils.Clone(this.m_dateTimeValue);
            clone.m_guidValue = (UuidCollection)Utils.Clone(this.m_guidValue);
            clone.m_byteStringValue = (ByteStringCollection)Utils.Clone(this.m_byteStringValue);
            clone.m_xmlElementValue = (XmlElementCollection)Utils.Clone(this.m_xmlElementValue);
            clone.m_nodeIdValue = (NodeIdCollection)Utils.Clone(this.m_nodeIdValue);
            clone.m_expandedNodeIdValue = (ExpandedNodeIdCollection)Utils.Clone(this.m_expandedNodeIdValue);
            clone.m_qualifiedNameValue = (QualifiedNameCollection)Utils.Clone(this.m_qualifiedNameValue);
            clone.m_localizedTextValue = (LocalizedTextCollection)Utils.Clone(this.m_localizedTextValue);
            clone.m_statusCodeValue = (StatusCodeCollection)Utils.Clone(this.m_statusCodeValue);
            clone.m_variantValue = (VariantCollection)Utils.Clone(this.m_variantValue);
            clone.m_enumerationValue = (Int32Collection)Utils.Clone(this.m_enumerationValue);
            clone.m_structureValue = (ExtensionObjectCollection)Utils.Clone(this.m_structureValue);
            clone.m_number = (VariantCollection)Utils.Clone(this.m_number);
            clone.m_integer = (VariantCollection)Utils.Clone(this.m_integer);
            clone.m_uInteger = (VariantCollection)Utils.Clone(this.m_uInteger);

            return clone;
        }
        #endregion

        #region Private Fields
        private BooleanCollection m_booleanValue;
        private SByteCollection m_sByteValue;
        private ByteCollection m_byteValue;
        private Int16Collection m_int16Value;
        private UInt16Collection m_uInt16Value;
        private Int32Collection m_int32Value;
        private UInt32Collection m_uInt32Value;
        private Int64Collection m_int64Value;
        private UInt64Collection m_uInt64Value;
        private FloatCollection m_floatValue;
        private DoubleCollection m_doubleValue;
        private StringCollection m_stringValue;
        private DateTimeCollection m_dateTimeValue;
        private UuidCollection m_guidValue;
        private ByteStringCollection m_byteStringValue;
        private XmlElementCollection m_xmlElementValue;
        private NodeIdCollection m_nodeIdValue;
        private ExpandedNodeIdCollection m_expandedNodeIdValue;
        private QualifiedNameCollection m_qualifiedNameValue;
        private LocalizedTextCollection m_localizedTextValue;
        private StatusCodeCollection m_statusCodeValue;
        private VariantCollection m_variantValue;
        private Int32Collection m_enumerationValue;
        private ExtensionObjectCollection m_structureValue;
        private VariantCollection m_number;
        private VariantCollection m_integer;
        private VariantCollection m_uInteger;
        #endregion
    }

    #region ArrayValueDataTypeCollection Class
    /// <summary>
    /// A collection of ArrayValueDataType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfArrayValueDataType", Namespace = TestData.Namespaces.TestData, ItemName = "ArrayValueDataType")]
    public partial class ArrayValueDataTypeCollection : List<ArrayValueDataType>
    {
        #region Constructors
        /// <summary>
        /// Initializes the collection with default values.
        /// </summary>
        public ArrayValueDataTypeCollection() {}

        /// <summary>
        /// Initializes the collection with an initial capacity.
        /// </summary>
        public ArrayValueDataTypeCollection(int capacity) : base(capacity) {}

        /// <summary>
        /// Initializes the collection with another collection.
        /// </summary>
        public ArrayValueDataTypeCollection(IEnumerable<ArrayValueDataType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator ArrayValueDataTypeCollection(ArrayValueDataType[] values)
        {
            if (values != null)
            {
                return new ArrayValueDataTypeCollection(values);
            }

            return new ArrayValueDataTypeCollection();
        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator ArrayValueDataType[](ArrayValueDataTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            ArrayValueDataTypeCollection clone = new ArrayValueDataTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((ArrayValueDataType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region UserScalarValueDataType Class
    #if (!OPCUA_EXCLUDE_UserScalarValueDataType)
    /// <summary>
    /// A description for the UserScalarValueDataType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = TestData.Namespaces.TestData)]
    public partial class UserScalarValueDataType : IEncodeable
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public UserScalarValueDataType()
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_booleanDataType = true;
            m_sByteDataType = (sbyte)0;
            m_byteDataType = (byte)0;
            m_int16DataType = (short)0;
            m_uInt16DataType = (ushort)0;
            m_int32DataType = (int)0;
            m_uInt32DataType = (uint)0;
            m_int64DataType = (long)0;
            m_uInt64DataType = (ulong)0;
            m_floatDataType = (float)0;
            m_doubleDataType = (double)0;
            m_stringDataType = null;
            m_dateTimeDataType = DateTime.MinValue;
            m_guidDataType = Uuid.Empty;
            m_byteStringDataType = null;
            m_xmlElementDataType = null;
            m_nodeIdDataType = null;
            m_expandedNodeIdDataType = null;
            m_qualifiedNameDataType = null;
            m_localizedTextDataType = null;
            m_statusCodeDataType = StatusCodes.Good;
            m_variantDataType = Variant.Null;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the BooleanDataType field.
        /// </summary>
        [DataMember(Name = "BooleanDataType", IsRequired = false, Order = 1)]
        public bool BooleanDataType
        {
            get { return m_booleanDataType;  }
            set { m_booleanDataType = value; }
        }

        /// <summary>
        /// A description for the SByteDataType field.
        /// </summary>
        [DataMember(Name = "SByteDataType", IsRequired = false, Order = 2)]
        public sbyte SByteDataType
        {
            get { return m_sByteDataType;  }
            set { m_sByteDataType = value; }
        }

        /// <summary>
        /// A description for the ByteDataType field.
        /// </summary>
        [DataMember(Name = "ByteDataType", IsRequired = false, Order = 3)]
        public byte ByteDataType
        {
            get { return m_byteDataType;  }
            set { m_byteDataType = value; }
        }

        /// <summary>
        /// A description for the Int16DataType field.
        /// </summary>
        [DataMember(Name = "Int16DataType", IsRequired = false, Order = 4)]
        public short Int16DataType
        {
            get { return m_int16DataType;  }
            set { m_int16DataType = value; }
        }

        /// <summary>
        /// A description for the UInt16DataType field.
        /// </summary>
        [DataMember(Name = "UInt16DataType", IsRequired = false, Order = 5)]
        public ushort UInt16DataType
        {
            get { return m_uInt16DataType;  }
            set { m_uInt16DataType = value; }
        }

        /// <summary>
        /// A description for the Int32DataType field.
        /// </summary>
        [DataMember(Name = "Int32DataType", IsRequired = false, Order = 6)]
        public int Int32DataType
        {
            get { return m_int32DataType;  }
            set { m_int32DataType = value; }
        }

        /// <summary>
        /// A description for the UInt32DataType field.
        /// </summary>
        [DataMember(Name = "UInt32DataType", IsRequired = false, Order = 7)]
        public uint UInt32DataType
        {
            get { return m_uInt32DataType;  }
            set { m_uInt32DataType = value; }
        }

        /// <summary>
        /// A description for the Int64DataType field.
        /// </summary>
        [DataMember(Name = "Int64DataType", IsRequired = false, Order = 8)]
        public long Int64DataType
        {
            get { return m_int64DataType;  }
            set { m_int64DataType = value; }
        }

        /// <summary>
        /// A description for the UInt64DataType field.
        /// </summary>
        [DataMember(Name = "UInt64DataType", IsRequired = false, Order = 9)]
        public ulong UInt64DataType
        {
            get { return m_uInt64DataType;  }
            set { m_uInt64DataType = value; }
        }

        /// <summary>
        /// A description for the FloatDataType field.
        /// </summary>
        [DataMember(Name = "FloatDataType", IsRequired = false, Order = 10)]
        public float FloatDataType
        {
            get { return m_floatDataType;  }
            set { m_floatDataType = value; }
        }

        /// <summary>
        /// A description for the DoubleDataType field.
        /// </summary>
        [DataMember(Name = "DoubleDataType", IsRequired = false, Order = 11)]
        public double DoubleDataType
        {
            get { return m_doubleDataType;  }
            set { m_doubleDataType = value; }
        }

        /// <summary>
        /// A description for the StringDataType field.
        /// </summary>
        [DataMember(Name = "StringDataType", IsRequired = false, Order = 12)]
        public string StringDataType
        {
            get { return m_stringDataType;  }
            set { m_stringDataType = value; }
        }

        /// <summary>
        /// A description for the DateTimeDataType field.
        /// </summary>
        [DataMember(Name = "DateTimeDataType", IsRequired = false, Order = 13)]
        public DateTime DateTimeDataType
        {
            get { return m_dateTimeDataType;  }
            set { m_dateTimeDataType = value; }
        }

        /// <summary>
        /// A description for the GuidDataType field.
        /// </summary>
        [DataMember(Name = "GuidDataType", IsRequired = false, Order = 14)]
        public Uuid GuidDataType
        {
            get { return m_guidDataType;  }
            set { m_guidDataType = value; }
        }

        /// <summary>
        /// A description for the ByteStringDataType field.
        /// </summary>
        [DataMember(Name = "ByteStringDataType", IsRequired = false, Order = 15)]
        public byte[] ByteStringDataType
        {
            get { return m_byteStringDataType;  }
            set { m_byteStringDataType = value; }
        }

        /// <summary>
        /// A description for the XmlElementDataType field.
        /// </summary>
        [DataMember(Name = "XmlElementDataType", IsRequired = false, Order = 16)]
        public XmlElement XmlElementDataType
        {
            get { return m_xmlElementDataType;  }
            set { m_xmlElementDataType = value; }
        }

        /// <summary>
        /// A description for the NodeIdDataType field.
        /// </summary>
        [DataMember(Name = "NodeIdDataType", IsRequired = false, Order = 17)]
        public NodeId NodeIdDataType
        {
            get { return m_nodeIdDataType;  }
            set { m_nodeIdDataType = value; }
        }

        /// <summary>
        /// A description for the ExpandedNodeIdDataType field.
        /// </summary>
        [DataMember(Name = "ExpandedNodeIdDataType", IsRequired = false, Order = 18)]
        public ExpandedNodeId ExpandedNodeIdDataType
        {
            get { return m_expandedNodeIdDataType;  }
            set { m_expandedNodeIdDataType = value; }
        }

        /// <summary>
        /// A description for the QualifiedNameDataType field.
        /// </summary>
        [DataMember(Name = "QualifiedNameDataType", IsRequired = false, Order = 19)]
        public QualifiedName QualifiedNameDataType
        {
            get { return m_qualifiedNameDataType;  }
            set { m_qualifiedNameDataType = value; }
        }

        /// <summary>
        /// A description for the LocalizedTextDataType field.
        /// </summary>
        [DataMember(Name = "LocalizedTextDataType", IsRequired = false, Order = 20)]
        public LocalizedText LocalizedTextDataType
        {
            get { return m_localizedTextDataType;  }
            set { m_localizedTextDataType = value; }
        }

        /// <summary>
        /// A description for the StatusCodeDataType field.
        /// </summary>
        [DataMember(Name = "StatusCodeDataType", IsRequired = false, Order = 21)]
        public StatusCode StatusCodeDataType
        {
            get { return m_statusCodeDataType;  }
            set { m_statusCodeDataType = value; }
        }

        /// <summary>
        /// A description for the VariantDataType field.
        /// </summary>
        [DataMember(Name = "VariantDataType", IsRequired = false, Order = 22)]
        public Variant VariantDataType
        {
            get { return m_variantDataType;  }
            set { m_variantDataType = value; }
        }
        #endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public virtual ExpandedNodeId TypeId
        {
            get { return DataTypeIds.UserScalarValueDataType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public virtual ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.UserScalarValueDataType_Encoding_DefaultBinary; }
        }

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public virtual ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.UserScalarValueDataType_Encoding_DefaultXml; }
        }

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(TestData.Namespaces.TestData);

            encoder.WriteBoolean("BooleanDataType", BooleanDataType);
            encoder.WriteSByte("SByteDataType", SByteDataType);
            encoder.WriteByte("ByteDataType", ByteDataType);
            encoder.WriteInt16("Int16DataType", Int16DataType);
            encoder.WriteUInt16("UInt16DataType", UInt16DataType);
            encoder.WriteInt32("Int32DataType", Int32DataType);
            encoder.WriteUInt32("UInt32DataType", UInt32DataType);
            encoder.WriteInt64("Int64DataType", Int64DataType);
            encoder.WriteUInt64("UInt64DataType", UInt64DataType);
            encoder.WriteFloat("FloatDataType", FloatDataType);
            encoder.WriteDouble("DoubleDataType", DoubleDataType);
            encoder.WriteString("StringDataType", StringDataType);
            encoder.WriteDateTime("DateTimeDataType", DateTimeDataType);
            encoder.WriteGuid("GuidDataType", GuidDataType);
            encoder.WriteByteString("ByteStringDataType", ByteStringDataType);
            encoder.WriteXmlElement("XmlElementDataType", XmlElementDataType);
            encoder.WriteNodeId("NodeIdDataType", NodeIdDataType);
            encoder.WriteExpandedNodeId("ExpandedNodeIdDataType", ExpandedNodeIdDataType);
            encoder.WriteQualifiedName("QualifiedNameDataType", QualifiedNameDataType);
            encoder.WriteLocalizedText("LocalizedTextDataType", LocalizedTextDataType);
            encoder.WriteStatusCode("StatusCodeDataType", StatusCodeDataType);
            encoder.WriteVariant("VariantDataType", VariantDataType);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(TestData.Namespaces.TestData);

            BooleanDataType = decoder.ReadBoolean("BooleanDataType");
            SByteDataType = decoder.ReadSByte("SByteDataType");
            ByteDataType = decoder.ReadByte("ByteDataType");
            Int16DataType = decoder.ReadInt16("Int16DataType");
            UInt16DataType = decoder.ReadUInt16("UInt16DataType");
            Int32DataType = decoder.ReadInt32("Int32DataType");
            UInt32DataType = decoder.ReadUInt32("UInt32DataType");
            Int64DataType = decoder.ReadInt64("Int64DataType");
            UInt64DataType = decoder.ReadUInt64("UInt64DataType");
            FloatDataType = decoder.ReadFloat("FloatDataType");
            DoubleDataType = decoder.ReadDouble("DoubleDataType");
            StringDataType = decoder.ReadString("StringDataType");
            DateTimeDataType = decoder.ReadDateTime("DateTimeDataType");
            GuidDataType = decoder.ReadGuid("GuidDataType");
            ByteStringDataType = decoder.ReadByteString("ByteStringDataType");
            XmlElementDataType = decoder.ReadXmlElement("XmlElementDataType");
            NodeIdDataType = decoder.ReadNodeId("NodeIdDataType");
            ExpandedNodeIdDataType = decoder.ReadExpandedNodeId("ExpandedNodeIdDataType");
            QualifiedNameDataType = decoder.ReadQualifiedName("QualifiedNameDataType");
            LocalizedTextDataType = decoder.ReadLocalizedText("LocalizedTextDataType");
            StatusCodeDataType = decoder.ReadStatusCode("StatusCodeDataType");
            VariantDataType = decoder.ReadVariant("VariantDataType");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            UserScalarValueDataType value = encodeable as UserScalarValueDataType;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_booleanDataType, value.m_booleanDataType)) return false;
            if (!Utils.IsEqual(m_sByteDataType, value.m_sByteDataType)) return false;
            if (!Utils.IsEqual(m_byteDataType, value.m_byteDataType)) return false;
            if (!Utils.IsEqual(m_int16DataType, value.m_int16DataType)) return false;
            if (!Utils.IsEqual(m_uInt16DataType, value.m_uInt16DataType)) return false;
            if (!Utils.IsEqual(m_int32DataType, value.m_int32DataType)) return false;
            if (!Utils.IsEqual(m_uInt32DataType, value.m_uInt32DataType)) return false;
            if (!Utils.IsEqual(m_int64DataType, value.m_int64DataType)) return false;
            if (!Utils.IsEqual(m_uInt64DataType, value.m_uInt64DataType)) return false;
            if (!Utils.IsEqual(m_floatDataType, value.m_floatDataType)) return false;
            if (!Utils.IsEqual(m_doubleDataType, value.m_doubleDataType)) return false;
            if (!Utils.IsEqual(m_stringDataType, value.m_stringDataType)) return false;
            if (!Utils.IsEqual(m_dateTimeDataType, value.m_dateTimeDataType)) return false;
            if (!Utils.IsEqual(m_guidDataType, value.m_guidDataType)) return false;
            if (!Utils.IsEqual(m_byteStringDataType, value.m_byteStringDataType)) return false;
            if (!Utils.IsEqual(m_xmlElementDataType, value.m_xmlElementDataType)) return false;
            if (!Utils.IsEqual(m_nodeIdDataType, value.m_nodeIdDataType)) return false;
            if (!Utils.IsEqual(m_expandedNodeIdDataType, value.m_expandedNodeIdDataType)) return false;
            if (!Utils.IsEqual(m_qualifiedNameDataType, value.m_qualifiedNameDataType)) return false;
            if (!Utils.IsEqual(m_localizedTextDataType, value.m_localizedTextDataType)) return false;
            if (!Utils.IsEqual(m_statusCodeDataType, value.m_statusCodeDataType)) return false;
            if (!Utils.IsEqual(m_variantDataType, value.m_variantDataType)) return false;

            return true;
        }

        public new object MemberwiseClone()
        {
            UserScalarValueDataType clone = new UserScalarValueDataType();

            clone.m_booleanDataType = (bool)Utils.Clone(this.m_booleanDataType);
            clone.m_sByteDataType = (sbyte)Utils.Clone(this.m_sByteDataType);
            clone.m_byteDataType = (byte)Utils.Clone(this.m_byteDataType);
            clone.m_int16DataType = (short)Utils.Clone(this.m_int16DataType);
            clone.m_uInt16DataType = (ushort)Utils.Clone(this.m_uInt16DataType);
            clone.m_int32DataType = (int)Utils.Clone(this.m_int32DataType);
            clone.m_uInt32DataType = (uint)Utils.Clone(this.m_uInt32DataType);
            clone.m_int64DataType = (long)Utils.Clone(this.m_int64DataType);
            clone.m_uInt64DataType = (ulong)Utils.Clone(this.m_uInt64DataType);
            clone.m_floatDataType = (float)Utils.Clone(this.m_floatDataType);
            clone.m_doubleDataType = (double)Utils.Clone(this.m_doubleDataType);
            clone.m_stringDataType = (string)Utils.Clone(this.m_stringDataType);
            clone.m_dateTimeDataType = (DateTime)Utils.Clone(this.m_dateTimeDataType);
            clone.m_guidDataType = (Uuid)Utils.Clone(this.m_guidDataType);
            clone.m_byteStringDataType = (byte[])Utils.Clone(this.m_byteStringDataType);
            clone.m_xmlElementDataType = (XmlElement)Utils.Clone(this.m_xmlElementDataType);
            clone.m_nodeIdDataType = (NodeId)Utils.Clone(this.m_nodeIdDataType);
            clone.m_expandedNodeIdDataType = (ExpandedNodeId)Utils.Clone(this.m_expandedNodeIdDataType);
            clone.m_qualifiedNameDataType = (QualifiedName)Utils.Clone(this.m_qualifiedNameDataType);
            clone.m_localizedTextDataType = (LocalizedText)Utils.Clone(this.m_localizedTextDataType);
            clone.m_statusCodeDataType = (StatusCode)Utils.Clone(this.m_statusCodeDataType);
            clone.m_variantDataType = (Variant)Utils.Clone(this.m_variantDataType);

            return clone;
        }
        #endregion

        #region Private Fields
        private bool m_booleanDataType;
        private sbyte m_sByteDataType;
        private byte m_byteDataType;
        private short m_int16DataType;
        private ushort m_uInt16DataType;
        private int m_int32DataType;
        private uint m_uInt32DataType;
        private long m_int64DataType;
        private ulong m_uInt64DataType;
        private float m_floatDataType;
        private double m_doubleDataType;
        private string m_stringDataType;
        private DateTime m_dateTimeDataType;
        private Uuid m_guidDataType;
        private byte[] m_byteStringDataType;
        private XmlElement m_xmlElementDataType;
        private NodeId m_nodeIdDataType;
        private ExpandedNodeId m_expandedNodeIdDataType;
        private QualifiedName m_qualifiedNameDataType;
        private LocalizedText m_localizedTextDataType;
        private StatusCode m_statusCodeDataType;
        private Variant m_variantDataType;
        #endregion
    }

    #region UserScalarValueDataTypeCollection Class
    /// <summary>
    /// A collection of UserScalarValueDataType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfUserScalarValueDataType", Namespace = TestData.Namespaces.TestData, ItemName = "UserScalarValueDataType")]
    public partial class UserScalarValueDataTypeCollection : List<UserScalarValueDataType>
    {
        #region Constructors
        /// <summary>
        /// Initializes the collection with default values.
        /// </summary>
        public UserScalarValueDataTypeCollection() {}

        /// <summary>
        /// Initializes the collection with an initial capacity.
        /// </summary>
        public UserScalarValueDataTypeCollection(int capacity) : base(capacity) {}

        /// <summary>
        /// Initializes the collection with another collection.
        /// </summary>
        public UserScalarValueDataTypeCollection(IEnumerable<UserScalarValueDataType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator UserScalarValueDataTypeCollection(UserScalarValueDataType[] values)
        {
            if (values != null)
            {
                return new UserScalarValueDataTypeCollection(values);
            }

            return new UserScalarValueDataTypeCollection();
        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator UserScalarValueDataType[](UserScalarValueDataTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            UserScalarValueDataTypeCollection clone = new UserScalarValueDataTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((UserScalarValueDataType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region UserArrayValueDataType Class
    #if (!OPCUA_EXCLUDE_UserArrayValueDataType)
    /// <summary>
    /// A description for the UserArrayValueDataType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = TestData.Namespaces.TestData)]
    public partial class UserArrayValueDataType : IEncodeable
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public UserArrayValueDataType()
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_booleanDataType = new BooleanCollection();
            m_sByteDataType = new SByteCollection();
            m_byteDataType = new ByteCollection();
            m_int16DataType = new Int16Collection();
            m_uInt16DataType = new UInt16Collection();
            m_int32DataType = new Int32Collection();
            m_uInt32DataType = new UInt32Collection();
            m_int64DataType = new Int64Collection();
            m_uInt64DataType = new UInt64Collection();
            m_floatDataType = new FloatCollection();
            m_doubleDataType = new DoubleCollection();
            m_stringDataType = new StringCollection();
            m_dateTimeDataType = new DateTimeCollection();
            m_guidDataType = new UuidCollection();
            m_byteStringDataType = new ByteStringCollection();
            m_xmlElementDataType = new XmlElementCollection();
            m_nodeIdDataType = new NodeIdCollection();
            m_expandedNodeIdDataType = new ExpandedNodeIdCollection();
            m_qualifiedNameDataType = new QualifiedNameCollection();
            m_localizedTextDataType = new LocalizedTextCollection();
            m_statusCodeDataType = new StatusCodeCollection();
            m_variantDataType = new VariantCollection();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the BooleanDataType field.
        /// </summary>
        [DataMember(Name = "BooleanDataType", IsRequired = false, Order = 1)]
        public BooleanCollection BooleanDataType
        {
            get
            {
                return m_booleanDataType;
            }

            set
            {
                m_booleanDataType = value;

                if (value == null)
                {
                    m_booleanDataType = new BooleanCollection();
                }
            }
        }

        /// <summary>
        /// A description for the SByteDataType field.
        /// </summary>
        [DataMember(Name = "SByteDataType", IsRequired = false, Order = 2)]
        public SByteCollection SByteDataType
        {
            get
            {
                return m_sByteDataType;
            }

            set
            {
                m_sByteDataType = value;

                if (value == null)
                {
                    m_sByteDataType = new SByteCollection();
                }
            }
        }

        /// <summary>
        /// A description for the ByteDataType field.
        /// </summary>
        [DataMember(Name = "ByteDataType", IsRequired = false, Order = 3)]
        public ByteCollection ByteDataType
        {
            get
            {
                return m_byteDataType;
            }

            set
            {
                m_byteDataType = value;

                if (value == null)
                {
                    m_byteDataType = new ByteCollection();
                }
            }
        }

        /// <summary>
        /// A description for the Int16DataType field.
        /// </summary>
        [DataMember(Name = "Int16DataType", IsRequired = false, Order = 4)]
        public Int16Collection Int16DataType
        {
            get
            {
                return m_int16DataType;
            }

            set
            {
                m_int16DataType = value;

                if (value == null)
                {
                    m_int16DataType = new Int16Collection();
                }
            }
        }

        /// <summary>
        /// A description for the UInt16DataType field.
        /// </summary>
        [DataMember(Name = "UInt16DataType", IsRequired = false, Order = 5)]
        public UInt16Collection UInt16DataType
        {
            get
            {
                return m_uInt16DataType;
            }

            set
            {
                m_uInt16DataType = value;

                if (value == null)
                {
                    m_uInt16DataType = new UInt16Collection();
                }
            }
        }

        /// <summary>
        /// A description for the Int32DataType field.
        /// </summary>
        [DataMember(Name = "Int32DataType", IsRequired = false, Order = 6)]
        public Int32Collection Int32DataType
        {
            get
            {
                return m_int32DataType;
            }

            set
            {
                m_int32DataType = value;

                if (value == null)
                {
                    m_int32DataType = new Int32Collection();
                }
            }
        }

        /// <summary>
        /// A description for the UInt32DataType field.
        /// </summary>
        [DataMember(Name = "UInt32DataType", IsRequired = false, Order = 7)]
        public UInt32Collection UInt32DataType
        {
            get
            {
                return m_uInt32DataType;
            }

            set
            {
                m_uInt32DataType = value;

                if (value == null)
                {
                    m_uInt32DataType = new UInt32Collection();
                }
            }
        }

        /// <summary>
        /// A description for the Int64DataType field.
        /// </summary>
        [DataMember(Name = "Int64DataType", IsRequired = false, Order = 8)]
        public Int64Collection Int64DataType
        {
            get
            {
                return m_int64DataType;
            }

            set
            {
                m_int64DataType = value;

                if (value == null)
                {
                    m_int64DataType = new Int64Collection();
                }
            }
        }

        /// <summary>
        /// A description for the UInt64DataType field.
        /// </summary>
        [DataMember(Name = "UInt64DataType", IsRequired = false, Order = 9)]
        public UInt64Collection UInt64DataType
        {
            get
            {
                return m_uInt64DataType;
            }

            set
            {
                m_uInt64DataType = value;

                if (value == null)
                {
                    m_uInt64DataType = new UInt64Collection();
                }
            }
        }

        /// <summary>
        /// A description for the FloatDataType field.
        /// </summary>
        [DataMember(Name = "FloatDataType", IsRequired = false, Order = 10)]
        public FloatCollection FloatDataType
        {
            get
            {
                return m_floatDataType;
            }

            set
            {
                m_floatDataType = value;

                if (value == null)
                {
                    m_floatDataType = new FloatCollection();
                }
            }
        }

        /// <summary>
        /// A description for the DoubleDataType field.
        /// </summary>
        [DataMember(Name = "DoubleDataType", IsRequired = false, Order = 11)]
        public DoubleCollection DoubleDataType
        {
            get
            {
                return m_doubleDataType;
            }

            set
            {
                m_doubleDataType = value;

                if (value == null)
                {
                    m_doubleDataType = new DoubleCollection();
                }
            }
        }

        /// <summary>
        /// A description for the StringDataType field.
        /// </summary>
        [DataMember(Name = "StringDataType", IsRequired = false, Order = 12)]
        public StringCollection StringDataType
        {
            get
            {
                return m_stringDataType;
            }

            set
            {
                m_stringDataType = value;

                if (value == null)
                {
                    m_stringDataType = new StringCollection();
                }
            }
        }

        /// <summary>
        /// A description for the DateTimeDataType field.
        /// </summary>
        [DataMember(Name = "DateTimeDataType", IsRequired = false, Order = 13)]
        public DateTimeCollection DateTimeDataType
        {
            get
            {
                return m_dateTimeDataType;
            }

            set
            {
                m_dateTimeDataType = value;

                if (value == null)
                {
                    m_dateTimeDataType = new DateTimeCollection();
                }
            }
        }

        /// <summary>
        /// A description for the GuidDataType field.
        /// </summary>
        [DataMember(Name = "GuidDataType", IsRequired = false, Order = 14)]
        public UuidCollection GuidDataType
        {
            get
            {
                return m_guidDataType;
            }

            set
            {
                m_guidDataType = value;

                if (value == null)
                {
                    m_guidDataType = new UuidCollection();
                }
            }
        }

        /// <summary>
        /// A description for the ByteStringDataType field.
        /// </summary>
        [DataMember(Name = "ByteStringDataType", IsRequired = false, Order = 15)]
        public ByteStringCollection ByteStringDataType
        {
            get
            {
                return m_byteStringDataType;
            }

            set
            {
                m_byteStringDataType = value;

                if (value == null)
                {
                    m_byteStringDataType = new ByteStringCollection();
                }
            }
        }

        /// <summary>
        /// A description for the XmlElementDataType field.
        /// </summary>
        [DataMember(Name = "XmlElementDataType", IsRequired = false, Order = 16)]
        public XmlElementCollection XmlElementDataType
        {
            get
            {
                return m_xmlElementDataType;
            }

            set
            {
                m_xmlElementDataType = value;

                if (value == null)
                {
                    m_xmlElementDataType = new XmlElementCollection();
                }
            }
        }

        /// <summary>
        /// A description for the NodeIdDataType field.
        /// </summary>
        [DataMember(Name = "NodeIdDataType", IsRequired = false, Order = 17)]
        public NodeIdCollection NodeIdDataType
        {
            get
            {
                return m_nodeIdDataType;
            }

            set
            {
                m_nodeIdDataType = value;

                if (value == null)
                {
                    m_nodeIdDataType = new NodeIdCollection();
                }
            }
        }

        /// <summary>
        /// A description for the ExpandedNodeIdDataType field.
        /// </summary>
        [DataMember(Name = "ExpandedNodeIdDataType", IsRequired = false, Order = 18)]
        public ExpandedNodeIdCollection ExpandedNodeIdDataType
        {
            get
            {
                return m_expandedNodeIdDataType;
            }

            set
            {
                m_expandedNodeIdDataType = value;

                if (value == null)
                {
                    m_expandedNodeIdDataType = new ExpandedNodeIdCollection();
                }
            }
        }

        /// <summary>
        /// A description for the QualifiedNameDataType field.
        /// </summary>
        [DataMember(Name = "QualifiedNameDataType", IsRequired = false, Order = 19)]
        public QualifiedNameCollection QualifiedNameDataType
        {
            get
            {
                return m_qualifiedNameDataType;
            }

            set
            {
                m_qualifiedNameDataType = value;

                if (value == null)
                {
                    m_qualifiedNameDataType = new QualifiedNameCollection();
                }
            }
        }

        /// <summary>
        /// A description for the LocalizedTextDataType field.
        /// </summary>
        [DataMember(Name = "LocalizedTextDataType", IsRequired = false, Order = 20)]
        public LocalizedTextCollection LocalizedTextDataType
        {
            get
            {
                return m_localizedTextDataType;
            }

            set
            {
                m_localizedTextDataType = value;

                if (value == null)
                {
                    m_localizedTextDataType = new LocalizedTextCollection();
                }
            }
        }

        /// <summary>
        /// A description for the StatusCodeDataType field.
        /// </summary>
        [DataMember(Name = "StatusCodeDataType", IsRequired = false, Order = 21)]
        public StatusCodeCollection StatusCodeDataType
        {
            get
            {
                return m_statusCodeDataType;
            }

            set
            {
                m_statusCodeDataType = value;

                if (value == null)
                {
                    m_statusCodeDataType = new StatusCodeCollection();
                }
            }
        }

        /// <summary>
        /// A description for the VariantDataType field.
        /// </summary>
        [DataMember(Name = "VariantDataType", IsRequired = false, Order = 22)]
        public VariantCollection VariantDataType
        {
            get
            {
                return m_variantDataType;
            }

            set
            {
                m_variantDataType = value;

                if (value == null)
                {
                    m_variantDataType = new VariantCollection();
                }
            }
        }
        #endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public virtual ExpandedNodeId TypeId
        {
            get { return DataTypeIds.UserArrayValueDataType; }
        }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public virtual ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.UserArrayValueDataType_Encoding_DefaultBinary; }
        }

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public virtual ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.UserArrayValueDataType_Encoding_DefaultXml; }
        }

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(TestData.Namespaces.TestData);

            encoder.WriteBooleanArray("BooleanDataType", BooleanDataType);
            encoder.WriteSByteArray("SByteDataType", SByteDataType);
            encoder.WriteByteArray("ByteDataType", ByteDataType);
            encoder.WriteInt16Array("Int16DataType", Int16DataType);
            encoder.WriteUInt16Array("UInt16DataType", UInt16DataType);
            encoder.WriteInt32Array("Int32DataType", Int32DataType);
            encoder.WriteUInt32Array("UInt32DataType", UInt32DataType);
            encoder.WriteInt64Array("Int64DataType", Int64DataType);
            encoder.WriteUInt64Array("UInt64DataType", UInt64DataType);
            encoder.WriteFloatArray("FloatDataType", FloatDataType);
            encoder.WriteDoubleArray("DoubleDataType", DoubleDataType);
            encoder.WriteStringArray("StringDataType", StringDataType);
            encoder.WriteDateTimeArray("DateTimeDataType", DateTimeDataType);
            encoder.WriteGuidArray("GuidDataType", GuidDataType);
            encoder.WriteByteStringArray("ByteStringDataType", ByteStringDataType);
            encoder.WriteXmlElementArray("XmlElementDataType", XmlElementDataType);
            encoder.WriteNodeIdArray("NodeIdDataType", NodeIdDataType);
            encoder.WriteExpandedNodeIdArray("ExpandedNodeIdDataType", ExpandedNodeIdDataType);
            encoder.WriteQualifiedNameArray("QualifiedNameDataType", QualifiedNameDataType);
            encoder.WriteLocalizedTextArray("LocalizedTextDataType", LocalizedTextDataType);
            encoder.WriteStatusCodeArray("StatusCodeDataType", StatusCodeDataType);
            encoder.WriteVariantArray("VariantDataType", VariantDataType);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(TestData.Namespaces.TestData);

            BooleanDataType = decoder.ReadBooleanArray("BooleanDataType");
            SByteDataType = decoder.ReadSByteArray("SByteDataType");
            ByteDataType = decoder.ReadByteArray("ByteDataType");
            Int16DataType = decoder.ReadInt16Array("Int16DataType");
            UInt16DataType = decoder.ReadUInt16Array("UInt16DataType");
            Int32DataType = decoder.ReadInt32Array("Int32DataType");
            UInt32DataType = decoder.ReadUInt32Array("UInt32DataType");
            Int64DataType = decoder.ReadInt64Array("Int64DataType");
            UInt64DataType = decoder.ReadUInt64Array("UInt64DataType");
            FloatDataType = decoder.ReadFloatArray("FloatDataType");
            DoubleDataType = decoder.ReadDoubleArray("DoubleDataType");
            StringDataType = decoder.ReadStringArray("StringDataType");
            DateTimeDataType = decoder.ReadDateTimeArray("DateTimeDataType");
            GuidDataType = decoder.ReadGuidArray("GuidDataType");
            ByteStringDataType = decoder.ReadByteStringArray("ByteStringDataType");
            XmlElementDataType = decoder.ReadXmlElementArray("XmlElementDataType");
            NodeIdDataType = decoder.ReadNodeIdArray("NodeIdDataType");
            ExpandedNodeIdDataType = decoder.ReadExpandedNodeIdArray("ExpandedNodeIdDataType");
            QualifiedNameDataType = decoder.ReadQualifiedNameArray("QualifiedNameDataType");
            LocalizedTextDataType = decoder.ReadLocalizedTextArray("LocalizedTextDataType");
            StatusCodeDataType = decoder.ReadStatusCodeArray("StatusCodeDataType");
            VariantDataType = decoder.ReadVariantArray("VariantDataType");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            UserArrayValueDataType value = encodeable as UserArrayValueDataType;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_booleanDataType, value.m_booleanDataType)) return false;
            if (!Utils.IsEqual(m_sByteDataType, value.m_sByteDataType)) return false;
            if (!Utils.IsEqual(m_byteDataType, value.m_byteDataType)) return false;
            if (!Utils.IsEqual(m_int16DataType, value.m_int16DataType)) return false;
            if (!Utils.IsEqual(m_uInt16DataType, value.m_uInt16DataType)) return false;
            if (!Utils.IsEqual(m_int32DataType, value.m_int32DataType)) return false;
            if (!Utils.IsEqual(m_uInt32DataType, value.m_uInt32DataType)) return false;
            if (!Utils.IsEqual(m_int64DataType, value.m_int64DataType)) return false;
            if (!Utils.IsEqual(m_uInt64DataType, value.m_uInt64DataType)) return false;
            if (!Utils.IsEqual(m_floatDataType, value.m_floatDataType)) return false;
            if (!Utils.IsEqual(m_doubleDataType, value.m_doubleDataType)) return false;
            if (!Utils.IsEqual(m_stringDataType, value.m_stringDataType)) return false;
            if (!Utils.IsEqual(m_dateTimeDataType, value.m_dateTimeDataType)) return false;
            if (!Utils.IsEqual(m_guidDataType, value.m_guidDataType)) return false;
            if (!Utils.IsEqual(m_byteStringDataType, value.m_byteStringDataType)) return false;
            if (!Utils.IsEqual(m_xmlElementDataType, value.m_xmlElementDataType)) return false;
            if (!Utils.IsEqual(m_nodeIdDataType, value.m_nodeIdDataType)) return false;
            if (!Utils.IsEqual(m_expandedNodeIdDataType, value.m_expandedNodeIdDataType)) return false;
            if (!Utils.IsEqual(m_qualifiedNameDataType, value.m_qualifiedNameDataType)) return false;
            if (!Utils.IsEqual(m_localizedTextDataType, value.m_localizedTextDataType)) return false;
            if (!Utils.IsEqual(m_statusCodeDataType, value.m_statusCodeDataType)) return false;
            if (!Utils.IsEqual(m_variantDataType, value.m_variantDataType)) return false;

            return true;
        }
        
        public new object MemberwiseClone()
        {
            UserArrayValueDataType clone = new UserArrayValueDataType();

            clone.m_booleanDataType = (BooleanCollection)Utils.Clone(this.m_booleanDataType);
            clone.m_sByteDataType = (SByteCollection)Utils.Clone(this.m_sByteDataType);
            clone.m_byteDataType = (ByteCollection)Utils.Clone(this.m_byteDataType);
            clone.m_int16DataType = (Int16Collection)Utils.Clone(this.m_int16DataType);
            clone.m_uInt16DataType = (UInt16Collection)Utils.Clone(this.m_uInt16DataType);
            clone.m_int32DataType = (Int32Collection)Utils.Clone(this.m_int32DataType);
            clone.m_uInt32DataType = (UInt32Collection)Utils.Clone(this.m_uInt32DataType);
            clone.m_int64DataType = (Int64Collection)Utils.Clone(this.m_int64DataType);
            clone.m_uInt64DataType = (UInt64Collection)Utils.Clone(this.m_uInt64DataType);
            clone.m_floatDataType = (FloatCollection)Utils.Clone(this.m_floatDataType);
            clone.m_doubleDataType = (DoubleCollection)Utils.Clone(this.m_doubleDataType);
            clone.m_stringDataType = (StringCollection)Utils.Clone(this.m_stringDataType);
            clone.m_dateTimeDataType = (DateTimeCollection)Utils.Clone(this.m_dateTimeDataType);
            clone.m_guidDataType = (UuidCollection)Utils.Clone(this.m_guidDataType);
            clone.m_byteStringDataType = (ByteStringCollection)Utils.Clone(this.m_byteStringDataType);
            clone.m_xmlElementDataType = (XmlElementCollection)Utils.Clone(this.m_xmlElementDataType);
            clone.m_nodeIdDataType = (NodeIdCollection)Utils.Clone(this.m_nodeIdDataType);
            clone.m_expandedNodeIdDataType = (ExpandedNodeIdCollection)Utils.Clone(this.m_expandedNodeIdDataType);
            clone.m_qualifiedNameDataType = (QualifiedNameCollection)Utils.Clone(this.m_qualifiedNameDataType);
            clone.m_localizedTextDataType = (LocalizedTextCollection)Utils.Clone(this.m_localizedTextDataType);
            clone.m_statusCodeDataType = (StatusCodeCollection)Utils.Clone(this.m_statusCodeDataType);
            clone.m_variantDataType = (VariantCollection)Utils.Clone(this.m_variantDataType);

            return clone;
        }
        #endregion

        #region Private Fields
        private BooleanCollection m_booleanDataType;
        private SByteCollection m_sByteDataType;
        private ByteCollection m_byteDataType;
        private Int16Collection m_int16DataType;
        private UInt16Collection m_uInt16DataType;
        private Int32Collection m_int32DataType;
        private UInt32Collection m_uInt32DataType;
        private Int64Collection m_int64DataType;
        private UInt64Collection m_uInt64DataType;
        private FloatCollection m_floatDataType;
        private DoubleCollection m_doubleDataType;
        private StringCollection m_stringDataType;
        private DateTimeCollection m_dateTimeDataType;
        private UuidCollection m_guidDataType;
        private ByteStringCollection m_byteStringDataType;
        private XmlElementCollection m_xmlElementDataType;
        private NodeIdCollection m_nodeIdDataType;
        private ExpandedNodeIdCollection m_expandedNodeIdDataType;
        private QualifiedNameCollection m_qualifiedNameDataType;
        private LocalizedTextCollection m_localizedTextDataType;
        private StatusCodeCollection m_statusCodeDataType;
        private VariantCollection m_variantDataType;
        #endregion
    }

    #region UserArrayValueDataTypeCollection Class
    /// <summary>
    /// A collection of UserArrayValueDataType objects.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfUserArrayValueDataType", Namespace = TestData.Namespaces.TestData, ItemName = "UserArrayValueDataType")]
    public partial class UserArrayValueDataTypeCollection : List<UserArrayValueDataType>
    {
        #region Constructors
        /// <summary>
        /// Initializes the collection with default values.
        /// </summary>
        public UserArrayValueDataTypeCollection() {}

        /// <summary>
        /// Initializes the collection with an initial capacity.
        /// </summary>
        public UserArrayValueDataTypeCollection(int capacity) : base(capacity) {}

        /// <summary>
        /// Initializes the collection with another collection.
        /// </summary>
        public UserArrayValueDataTypeCollection(IEnumerable<UserArrayValueDataType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator UserArrayValueDataTypeCollection(UserArrayValueDataType[] values)
        {
            if (values != null)
            {
                return new UserArrayValueDataTypeCollection(values);
            }

            return new UserArrayValueDataTypeCollection();
        }

        /// <summary>
        /// Converts a collection to an array.
        /// </summary>
        public static explicit operator UserArrayValueDataType[](UserArrayValueDataTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            UserArrayValueDataTypeCollection clone = new UserArrayValueDataTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((UserArrayValueDataType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion
}