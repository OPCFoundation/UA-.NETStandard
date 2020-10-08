/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A structure that could contain value with any of the UA built-in data types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Variant is described in <b>Part 6 - Mappings, Section 6.2.2.15</b>, titled <b>Variant</b>
    /// <br/></para>
    /// <para>
    /// Variant is a data type in COM, but not within the .NET Framework. Therefore OPC UA has its own
    /// Variant type that supports all of the OPC UA data-types.
    /// <br/></para>
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public partial struct Variant : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <remarks>
        /// Creates a new Variant instance, while deep-copying the contents of the specified Variant
        /// </remarks>
        /// <param name="value">The Variant value to copy.</param>
        public Variant(Variant value)
        {
            m_value = Utils.Clone(value.m_value);
            m_typeInfo = value.m_typeInfo;
        }

        /// <summary>
        /// Constructs a Variant
        /// </summary>
        /// <param name="value">The value to store.</param>
        /// <param name="typeInfo">The type information for the value.</param>
        public Variant(object value, TypeInfo typeInfo)
        {
            m_value = null;
            m_typeInfo = typeInfo;
            Set(value, typeInfo);

#if DEBUG

            TypeInfo sanityCheck = TypeInfo.Construct(m_value);

            // except special case byte array vs. bytestring
            if (sanityCheck.BuiltInType == BuiltInType.ByteString &&
                sanityCheck.ValueRank == ValueRanks.Scalar &&
                typeInfo.BuiltInType == BuiltInType.Byte &&
                typeInfo.ValueRank == ValueRanks.OneDimension)
            {
                return;
            }

            // An enumeration can contain Int32
            if (sanityCheck.BuiltInType == BuiltInType.Int32 &&
                typeInfo.BuiltInType == BuiltInType.Enumeration)
            {
                return;
            }

            System.Diagnostics.Debug.Assert(
                sanityCheck.BuiltInType == m_typeInfo.BuiltInType,
                Utils.Format("{0} != {1}",
                sanityCheck.BuiltInType,
                typeInfo.BuiltInType));

            System.Diagnostics.Debug.Assert(
                sanityCheck.ValueRank == m_typeInfo.ValueRank,
                Utils.Format("{0} != {1}",
                sanityCheck.ValueRank,
                typeInfo.ValueRank));

#endif
        }

        /// <summary>
        /// Initializes the object with an object value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant instance while specifying the value.
        /// </remarks>
        /// <param name="value">The value to encode within the variant</param>
        public Variant(object value)
        {
            m_value = null;
            m_typeInfo = TypeInfo.Construct(value);
            Set(value, m_typeInfo);
        }

        /// <summary>
        /// Initializes the variant with matrix.
        /// </summary>
        /// <param name="value">The value to store within the variant</param>
        public Variant(Matrix value)
        {
            m_value = value;
            m_typeInfo = value.TypeInfo;
        }

        /// <summary>
        /// Initializes the object with a bool value.
        /// </summary>
        /// <remarks>
        /// Creates a new Variant with a Boolean value.
        /// </remarks>
        /// <param name="value">The value of the variant</param>
        public Variant(bool value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Boolean;
        }

        /// <summary>
        /// Initializes the object with a sbyte value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="sbyte"/> value
        /// </remarks>
        /// <param name="value">The <see cref="sbyte"/> value of the Variant</param>
        public Variant(sbyte value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.SByte;
        }

        /// <summary>
        /// Initializes the object with a byte value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="byte"/> value
        /// </remarks>
        /// <param name="value">The <see cref="byte"/> value of the Variant</param>
        public Variant(byte value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Byte;
        }

        /// <summary>
        /// Initializes the object with a short value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="short"/> value
        /// </remarks>
        /// <param name="value">The <see cref="short"/> value of the Variant</param>
        public Variant(short value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Int16;
        }

        /// <summary>
        /// Initializes the object with a ushort value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ushort"/> value
        /// </remarks>
        /// <param name="value">The <see cref="ushort"/> value of the Variant</param>
        public Variant(ushort value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.UInt16;
        }

        /// <summary>
        /// Initializes the object with an int value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="int"/> value
        /// </remarks>
        /// <param name="value">The <see cref="int"/> value of the Variant</param>
        public Variant(int value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Int32;
        }

        /// <summary>
        /// Initializes the object with a uint value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="uint"/> value
        /// </remarks>
        /// <param name="value">The <see cref="uint"/> value of the Variant</param>
        public Variant(uint value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.UInt32;
        }

        /// <summary>
        /// Initializes the object with a long value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="long"/> value
        /// </remarks>
        /// <param name="value">The <see cref="long"/> value of the Variant</param>
        public Variant(long value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Int64;
        }

        /// <summary>
        /// Initializes the object with a ulong value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ulong"/> value
        /// </remarks>
        /// <param name="value">The <see cref="ulong"/> value of the Variant</param>
        public Variant(ulong value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.UInt64;
        }

        /// <summary>
        /// Initializes the object with a float value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="float"/> value
        /// </remarks>
        /// <param name="value">The <see cref="float"/> value of the Variant</param>
        public Variant(float value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Float;
        }

        /// <summary>
        /// Initializes the object with a double value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="double"/> value
        /// </remarks>
        /// <param name="value">The <see cref="double"/> value of the Variant</param>
        public Variant(double value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Double;
        }

        /// <summary>
        /// Initializes the object with a string value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="string"/> value
        /// </remarks>
        /// <param name="value">The <see cref="string"/> value of the Variant</param>
        public Variant(string value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.String;
        }

        /// <summary>
        /// Initializes the object with a DateTime value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="DateTime"/> value
        /// </remarks>
        /// <param name="value">The <see cref="DateTime"/> value of the Variant</param>
        public Variant(DateTime value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.DateTime;
        }

        /// <summary>
        /// Initializes the object with a Guid value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="Guid"/> value
        /// </remarks>
        /// <param name="value">The <see cref="Guid"/> value of the Variant</param>
        public Variant(Guid value)
        {
            m_value = new Uuid(value);
            m_typeInfo = TypeInfo.Scalars.Guid;
        }

        /// <summary>
        /// Initializes the object with a Uuid value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="Uuid"/> value
        /// </remarks>
        /// <param name="value">The <see cref="Uuid"/> value of the Variant</param>
        public Variant(Uuid value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Guid;
        }

        /// <summary>
        /// Initializes the object with a byte[] value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="byte"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="byte"/>-array value of the Variant</param>
        public Variant(byte[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ByteString;
        }

        /// <summary>
        /// Initializes the object with a XmlElement value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="XmlElement"/> value
        /// </remarks>
        /// <param name="value">The <see cref="XmlElement"/> value of the Variant</param>
        public Variant(XmlElement value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.XmlElement;
        }

        /// <summary>
        /// Initializes the object with a NodeId value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="NodeId"/> value
        /// </remarks>
        /// <param name="value">The <see cref="NodeId"/> value of the Variant</param>
        public Variant(NodeId value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.NodeId;
        }

        /// <summary>
        /// Initializes the object with a ExpandedNodeId value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ExpandedNodeId"/> value
        /// </remarks>
        /// <param name="value">The <see cref="ExpandedNodeId"/> value of the Variant</param>
        public Variant(ExpandedNodeId value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ExpandedNodeId;
        }

        /// <summary>
        /// Initializes the object with a StatusCode value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="StatusCode"/> value
        /// </remarks>
        /// <param name="value">The <see cref="StatusCode"/> value of the Variant</param>
        public Variant(StatusCode value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.StatusCode;
        }

        /// <summary>
        /// Initializes the object with a QualifiedName value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="QualifiedName"/> value
        /// </remarks>
        /// <param name="value">The <see cref="QualifiedName"/> value of the Variant</param>
        public Variant(QualifiedName value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.QualifiedName;
        }

        /// <summary>
        /// Initializes the object with a LocalizedText value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="LocalizedText"/> value
        /// </remarks>
        /// <param name="value">The <see cref="LocalizedText"/> value of the Variant</param>
        public Variant(LocalizedText value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.LocalizedText;
        }

        /// <summary>
        /// Initializes the object with a ExtensionObject value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ExtensionObject"/> value
        /// </remarks>
        /// <param name="value">The <see cref="ExtensionObject"/> value of the Variant</param>
        public Variant(ExtensionObject value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ExtensionObject;
        }

        /// <summary>
        /// Initializes the object with a DataValue value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="DataValue"/> value
        /// </remarks>
        /// <param name="value">The <see cref="DataValue"/> value of the Variant</param>
        public Variant(DataValue value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.DataValue;
        }

        /// <summary>
        /// Initializes the object with a bool array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="bool"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="bool"/>-array value of the Variant</param>
        public Variant(bool[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Boolean;
        }

        /// <summary>
        /// Initializes the object with a sbyte array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="sbyte"/>-arrat value
        /// </remarks>
        /// <param name="value">The <see cref="sbyte"/>-array value of the Variant</param>
        public Variant(sbyte[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.SByte;
        }

        /// <summary>
        /// Initializes the object with a short array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="short"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="short"/>-array value of the Variant</param>
        public Variant(short[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int16;
        }

        /// <summary>
        /// Initializes the object with a ushort array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ushort"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="ushort"/>-array value of the Variant</param>
        public Variant(ushort[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt16;
        }

        /// <summary>
        /// Initializes the object with an int array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="int"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="int"/>-array value of the Variant</param>
        public Variant(int[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int32;
        }

        /// <summary>
        /// Initializes the object with a uint array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="uint"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="uint"/>-array value of the Variant</param>
        public Variant(uint[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt32;
        }

        /// <summary>
        /// Initializes the object with a long array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="long"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="long"/>-array value of the Variant</param>
        public Variant(long[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int64;
        }

        /// <summary>
        /// Initializes the object with a ulong array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ulong"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="ulong"/>-array value of the Variant</param>
        public Variant(ulong[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt64;
        }

        /// <summary>
        /// Initializes the object with a float array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="float"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="float"/>-array value of the Variant</param>
        public Variant(float[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Float;
        }

        /// <summary>
        /// Initializes the object with a double array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="double"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="double"/>-array value of the Variant</param>
        public Variant(double[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Double;
        }

        /// <summary>
        /// Initializes the object with a string array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="string"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="string"/>-array value of the Variant</param>
        public Variant(string[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.String;
        }

        /// <summary>
        /// Initializes the object with a DateTime array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="DateTime"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="DateTime"/>-array value of the Variant</param>
        public Variant(DateTime[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.DateTime;
        }

        /// <summary>
        /// Initializes the object with a Guid array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="Guid"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="Guid"/>-array value of the Variant</param>
        public Variant(Guid[] value)
        {
            m_value = null;
            m_typeInfo = TypeInfo.Arrays.Guid;
            Set(value);
        }

        /// <summary>
        /// Initializes the object with a Uuid array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="Uuid"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="Uuid"/>-array value of the Variant</param>
        public Variant(Uuid[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Guid;
        }

        /// <summary>
        /// Initializes the object with a byte[] array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a 2-d <see cref="byte"/>-array value
        /// </remarks>
        /// <param name="value">The 2-d <see cref="byte"/>-array value of the Variant</param>
        public Variant(byte[][] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ByteString;
        }

        /// <summary>
        /// Initializes the object with a XmlElement array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="XmlElement"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="XmlElement"/>-array value of the Variant</param>
        public Variant(XmlElement[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.XmlElement;
        }

        /// <summary>
        /// Initializes the object with a NodeId array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="NodeId"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="NodeId"/>-array value of the Variant</param>
        public Variant(NodeId[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.NodeId;
        }

        /// <summary>
        /// Initializes the object with a ExpandedNodeId array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ExpandedNodeId"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-array value of the Variant</param>
        public Variant(ExpandedNodeId[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ExpandedNodeId;
        }

        /// <summary>
        /// Initializes the object with a StatusCode array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="StatusCode"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="StatusCode"/>-array value of the Variant</param>
        public Variant(StatusCode[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.StatusCode;
        }

        /// <summary>
        /// Initializes the object with a QualifiedName array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="QualifiedName"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="QualifiedName"/>-array value of the Variant</param>
        public Variant(QualifiedName[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.QualifiedName;
        }

        /// <summary>
        /// Initializes the object with a LocalizedText array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="LocalizedText"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="LocalizedText"/>-array value of the Variant</param>
        public Variant(LocalizedText[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.LocalizedText;
        }

        /// <summary>
        /// Initializes the object with a ExtensionObject array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="ExtensionObject"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="ExtensionObject"/>-array value of the Variant</param>
        public Variant(ExtensionObject[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ExtensionObject;
        }

        /// <summary>
        /// Initializes the object with a DataValue array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="DataValue"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="DataValue"/>-array value of the Variant</param>
        public Variant(DataValue[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.DataValue;
        }

        /// <summary>
        /// Initializes the object with a Variant array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="Variant"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="Variant"/>-array value of the Variant</param>
        public Variant(Variant[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Variant;
        }

        /// <summary>
        /// Initializes the object with a object array value.
        /// </summary>
        /// <remarks>
        /// Creates a new variant with a <see cref="object"/>-array value
        /// </remarks>
        /// <param name="value">The <see cref="object"/>-array value of the Variant</param>
        public Variant(object[] value)
        {
            m_value = null;
            m_typeInfo = TypeInfo.Arrays.Variant;
            Set(value);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The value stored in the object.
        /// </summary>
        /// <remarks>
        /// The value stored within the Variant object.
        /// </remarks>
        [DataMember(Name = "Value", Order = 1)]
        private XmlElement XmlEncodedValue
        {
            get
            {
                // create encoder.
                XmlEncoder encoder = new XmlEncoder(MessageContextExtension.CurrentContext);

                // write value.
                encoder.WriteVariantContents(m_value, m_typeInfo);

                // create document from encoder.
                XmlDocument document = new XmlDocument();
                document.InnerXml = encoder.Close();

                // return element.
                return document.DocumentElement;
            }

            set
            {
                // check for null values.
                if (value == null)
                {
                    m_value = null;
                    return;
                }

                TypeInfo typeInfo = null;

                // create decoder.
                XmlDecoder decoder = new XmlDecoder(value, MessageContextExtension.CurrentContext);

                try
                {
                    // read value.
                    object body = decoder.ReadVariantContents(out typeInfo);
                    Set(body, typeInfo);
                }
                catch (Exception e)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        e,
                        "Error decoding Variant value.");
                }
                finally
                {
                    // close decoder.
                    decoder.Close();
                }
            }
        }

        /// <summary>
        /// The value stored in the object.
        /// </summary>
        /// <remarks>
        /// The value stored -as <see cref="Object"/>- within the Variant object.
        /// </remarks>
        public object Value
        {
            get { return m_value; }
            set { Set(value, TypeInfo.Construct(value)); }
        }

        /// <summary>
        /// The type information for the matrix.
        /// </summary>
        public TypeInfo TypeInfo => m_typeInfo;
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <exception cref="FormatException">Thrown when the 'format' argument is NOT null.</exception>
        /// <param name="format">(Unused) Always pass a NULL value</param>
        /// <param name="formatProvider">The format-provider to use. If unsure, pass an empty string or null</param>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder buffer = new StringBuilder();
                AppendFormat(buffer, m_value, formatProvider);
                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Formats a value as a string.
        /// </summary>
        private void AppendFormat(StringBuilder buffer, object value, IFormatProvider formatProvider)
        {
            // check for null.
            if (value == null || m_typeInfo == null)
            {
                buffer.Append("(null)");
                return;
            }

            // convert byte string to hexstring.
            if (m_typeInfo.BuiltInType == BuiltInType.ByteString && m_typeInfo.ValueRank < 0)
            {
                byte[] bytes = (byte[])value;

                for (int ii = 0; ii < bytes.Length; ii++)
                {
                    buffer.AppendFormat(formatProvider, "{0:X2}", bytes[ii]);
                }

                return;
            }

            // convert XML element to string.
            if (m_typeInfo.BuiltInType == BuiltInType.XmlElement && m_typeInfo.ValueRank < 0)
            {
                XmlElement xml = (XmlElement)value;
                buffer.AppendFormat(formatProvider, "{0}", xml.OuterXml);
                return;
            }

            // recusrively write individual elements of an array.
            Array array = value as Array;

            if (array != null && m_typeInfo.ValueRank <= 1)
            {
                buffer.Append("{");

                if (array.Length > 0)
                {
                    AppendFormat(buffer, array.GetValue(0), formatProvider);
                }

                for (int ii = 1; ii < array.Length; ii++)
                {
                    buffer.Append(" |");
                    AppendFormat(buffer, array.GetValue(ii), formatProvider);
                }

                buffer.Append("}");
                return;
            }

            // let the object format itself.
            buffer.AppendFormat(formatProvider, "{0}", value);
        }
        #endregion

        #region ICloneable Members
        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <remarks>
        /// Makes a deep copy of the object.
        /// </remarks>
        public new object MemberwiseClone()
        {
            return new Variant(Utils.Clone(this.Value));
        }
        #endregion

        #region Static Operators
        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        public static bool operator ==(Variant a, Variant b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        public static bool operator !=(Variant a, Variant b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        /// Converts a bool value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a bool value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(bool value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a sbyte value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a sbyte value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(sbyte value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a byte value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a byte value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(byte value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a short value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a short value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(short value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ushort value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ushort value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ushort value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a int value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a int value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(int value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a uint value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a uint value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(uint value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a long value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a long value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(long value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ulong value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ulong value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ulong value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a float value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a float value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(float value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a double value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a double value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(double value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a string value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a string value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(string value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a DateTime value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a DateTime value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(DateTime value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a Guid value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a Guid value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(Guid value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a Uuid value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a Uuid value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(Uuid value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a byte[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a byte[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(byte[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a XmlElement value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a XmlElement value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(XmlElement value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a NodeId value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a NodeId value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(NodeId value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ExpandedNodeId value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ExpandedNodeId value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ExpandedNodeId value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a StatusCode value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a StatusCode value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(StatusCode value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a QualifiedName value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a QualifiedName value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(QualifiedName value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a LocalizedText value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a LocalizedText value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(LocalizedText value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ExtensionObject value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ExtensionObject value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ExtensionObject value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a DataValue value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a DataValue value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(DataValue value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a bool[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a bool[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(bool[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a sbyte[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a sbyte[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(sbyte[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a short[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a short[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(short[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ushort[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ushort[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ushort[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a int[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a int[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(int[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a uint[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a uint[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(uint[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a long[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a long[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(long[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ulong[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ulong[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ulong[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a float[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a float[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(float[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a double[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a double[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(double[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a string []value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a string []value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(string[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a DateTime[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a DateTime[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(DateTime[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a Guid[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a Guid[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(Guid[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a Uuid[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a Uuid[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(Uuid[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a byte[][] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a byte[][] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(byte[][] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a XmlElement[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a XmlElement[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(XmlElement[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a NodeId[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a NodeId[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(NodeId[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ExpandedNodeId[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ExpandedNodeId[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ExpandedNodeId[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a StatusCode[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a StatusCode[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(StatusCode[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a QualifiedName[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a QualifiedName[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(QualifiedName[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a LocalizedText[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a LocalizedText[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(LocalizedText[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a ExtensionObject[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a ExtensionObject[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(ExtensionObject[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a DataValue[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a DataValue[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(DataValue[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a Variant[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a Variant[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(Variant[] value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Converts a object[] value to an Variant object.
        /// </summary>
        /// <remarks>
        /// Converts a object[] value to an Variant object.
        /// </remarks>
        public static implicit operator Variant(object[] value)
        {
            return new Variant(value);
        }
        #endregion

        #region Static Fields
        /// <summary>
        /// An constant containing a null Variant structure.
        /// </summary>
        /// <remarks>
        /// An constant containing a null Variant structure.
        /// </remarks>
        public static readonly Variant Null = new Variant();
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            Variant? variant = obj as Variant?;

            if (variant != null)
            {
                return Utils.IsEqual(m_value, variant.Value.m_value);
            }

            return false;
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        public override int GetHashCode()
        {
            if (this.m_value != null)
            {
                return m_value.GetHashCode();
            }

            return 0;
        }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the object with a bool value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="bool"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="bool"/> value to set this Variant to</param>
        public void Set(bool value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Boolean;
        }

        /// <summary>
        /// Initializes the object with a sbyte value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="sbyte"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="sbyte"/> value to set this Variant to</param>
        public void Set(sbyte value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.SByte;
        }

        /// <summary>
        /// Initializes the object with a byte value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="byte"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="byte"/> value to set this Variant to</param>
        public void Set(byte value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Byte;
        }

        /// <summary>
        /// Initializes the object with a short value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="short"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="short"/> value to set this Variant to</param>
        public void Set(short value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Int16;
        }

        /// <summary>
        /// Initializes the object with a ushort value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ushort"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="ushort"/> value to set this Variant to</param>
        public void Set(ushort value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.UInt16;
        }

        /// <summary>
        /// Initializes the object with an int value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="int"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="int"/> value to set this Variant to</param>
        public void Set(int value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Int32;
        }

        /// <summary>
        /// Initializes the object with a uint value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="uint"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="uint"/> value to set this Variant to</param>
        public void Set(uint value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.UInt32;
        }

        /// <summary>
        /// Initializes the object with a long value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="long"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="long"/> value to set this Variant to</param>
        public void Set(long value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Int64;
        }

        /// <summary>
        /// Initializes the object with a ulong value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ulong"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="ulong"/> value to set this Variant to</param>
        public void Set(ulong value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.UInt64;
        }

        /// <summary>
        /// Initializes the object with a float value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="float"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="float"/> value to set this Variant to</param>
        public void Set(float value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Float;
        }

        /// <summary>
        /// Initializes the object with a double value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="double"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="double"/> value to set this Variant to</param>
        public void Set(double value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Double;
        }

        /// <summary>
        /// Initializes the object with a string value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="string"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="string"/> value to set this Variant to</param>
        public void Set(string value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.String;
        }

        /// <summary>
        /// Initializes the object with a DateTime value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="DateTime"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="DateTime"/> value to set this Variant to</param>
        public void Set(DateTime value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.DateTime;
        }

        /// <summary>
        /// Initializes the object with a Guid value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="Guid"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="Guid"/> value to set this Variant to</param>
        public void Set(Guid value)
        {
            m_value = new Uuid(value);
            m_typeInfo = TypeInfo.Scalars.Guid;
        }

        /// <summary>
        /// Initializes the object with a Uuid value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="Uuid"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="Uuid"/> value to set this Variant to</param>
        public void Set(Uuid value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Guid;
        }

        /// <summary>
        /// Initializes the object with a byte[] value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="byte"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="byte"/>-array value to set this Variant to</param>
        public void Set(byte[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ByteString;
        }

        /// <summary>
        /// Initializes the object with a XmlElement value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="XmlElement"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="XmlElement"/> value to set this Variant to</param>
        public void Set(XmlElement value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.XmlElement;
        }

        /// <summary>
        /// Initializes the object with a NodeId value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="NodeId"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="NodeId"/> value to set this Variant to</param>
        public void Set(NodeId value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.NodeId;
        }

        /// <summary>
        /// Initializes the object with a ExpandedNodeId value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ExpandedNodeId"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="ExpandedNodeId"/> value to set this Variant to</param>
        public void Set(ExpandedNodeId value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ExpandedNodeId;
        }

        /// <summary>
        /// Initializes the object with a StatusCode value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="StatusCode"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="StatusCode"/> value to set this Variant to</param>
        public void Set(StatusCode value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.StatusCode;
        }

        /// <summary>
        /// Initializes the object with a QualifiedName value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="QualifiedName"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="QualifiedName"/> value to set this Variant to</param>
        public void Set(QualifiedName value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.QualifiedName;
        }

        /// <summary>
        /// Initializes the object with a LocalizedText value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="LocalizedText"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="LocalizedText"/> value to set this Variant to</param>
        public void Set(LocalizedText value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.LocalizedText;
        }

        /// <summary>
        /// Initializes the object with a ExtensionObject value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ExtensionObject"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="ExtensionObject"/> value to set this Variant to</param>
        public void Set(ExtensionObject value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ExtensionObject;
        }

        /// <summary>
        /// Initializes the object with a DataValue value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="DataValue"/> value.
        /// </remarks>
        /// <param name="value">The <see cref="DataValue"/> value to set this Variant to</param>
        public void Set(DataValue value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.DataValue;
        }

        /// <summary>
        /// Initializes the object with a bool array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="bool"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="bool"/>-array value to set this Variant to</param>
        public void Set(bool[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Boolean;
        }

        /// <summary>
        /// Initializes the object with a sbyte array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="sbyte"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="sbyte"/>-array value to set this Variant to</param>
        public void Set(sbyte[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.SByte;
        }

        /// <summary>
        /// Initializes the object with a short array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="short"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="short"/>-array value to set this Variant to</param>
        public void Set(short[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int16;
        }

        /// <summary>
        /// Initializes the object with a ushort array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ushort"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="ushort"/>-array value to set this Variant to</param>
        public void Set(ushort[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt16;
        }

        /// <summary>
        /// Initializes the object with an int array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="int"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="int"/>-array value to set this Variant to</param>
        public void Set(int[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int32;
        }

        /// <summary>
        /// Initializes the object with a uint array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="uint"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="uint"/>-array value to set this Variant to</param>
        public void Set(uint[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt32;
        }

        /// <summary>
        /// Initializes the object with a long array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="long"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="long"/>-array value to set this Variant to</param>
        public void Set(long[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int64;
        }

        /// <summary>
        /// Initializes the object with a ulong array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ulong"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="ulong"/>-array value to set this Variant to</param>
        public void Set(ulong[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt64;
        }

        /// <summary>
        /// Initializes the object with a float array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="float"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="float"/>-array value to set this Variant to</param>
        public void Set(float[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Float;
        }

        /// <summary>
        /// Initializes the object with a double array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="double"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="double"/>-array value to set this Variant to</param>
        public void Set(double[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Double;
        }

        /// <summary>
        /// Initializes the object with a string array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="string"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="string"/>-array value to set this Variant to</param>
        public void Set(string[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.String;
        }

        /// <summary>
        /// Initializes the object with a DateTime array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="DateTime"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="DateTime"/>-array value to set this Variant to</param>
        public void Set(DateTime[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.DateTime;
        }

        /// <summary>
        /// Initializes the object with a Guid array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="Guid"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="Guid"/>-array value to set this Variant to</param>
        public void Set(Guid[] value)
        {
            m_value = null;

            if (value != null)
            {
                Uuid[] uuids = new Uuid[value.Length];

                for (int ii = 0; ii < value.Length; ii++)
                {
                    uuids[ii] = new Uuid(value[ii]);
                }

                m_value = uuids;
            }

            m_typeInfo = TypeInfo.Arrays.Guid;
        }

        /// <summary>
        /// Initializes the object with a Uuid array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="Uuid"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="Uuid"/>-array value to set this Variant to</param>
        public void Set(Uuid[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Guid;
        }

        /// <summary>
        /// Initializes the object with a byte[] array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a 2-d <see cref="byte"/>-array value.
        /// </remarks>
        /// <param name="value">The 2-d <see cref="byte"/>-array value to set this Variant to</param>
        public void Set(byte[][] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ByteString;
        }

        /// <summary>
        /// Initializes the object with a XmlElement array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="XmlElement"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="XmlElement"/>-array value to set this Variant to</param>
        public void Set(XmlElement[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.XmlElement;
        }

        /// <summary>
        /// Initializes the object with a NodeId array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="NodeId"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="NodeId"/>-array value to set this Variant to</param>
        public void Set(NodeId[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.NodeId;
        }

        /// <summary>
        /// Initializes the object with a ExpandedNodeId array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ExpandedNodeId"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-array value to set this Variant to</param>
        public void Set(ExpandedNodeId[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ExpandedNodeId;
        }

        /// <summary>
        /// Initializes the object with a StatusCode array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="StatusCode"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="StatusCode"/>-array value to set this Variant to</param>
        public void Set(StatusCode[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.StatusCode;
        }

        /// <summary>
        /// Initializes the object with a QualifiedName array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="QualifiedName"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="QualifiedName"/>-array value to set this Variant to</param>
        public void Set(QualifiedName[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.QualifiedName;
        }

        /// <summary>
        /// Initializes the object with a LocalizedText array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="LocalizedText"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="LocalizedText"/>-array value to set this Variant to</param>
        public void Set(LocalizedText[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.LocalizedText;
        }

        /// <summary>
        /// Initializes the object with a ExtensionObject array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="ExtensionObject"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="ExtensionObject"/>-array value to set this Variant to</param>
        public void Set(ExtensionObject[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ExtensionObject;
        }

        /// <summary>
        /// Initializes the object with a DataValue array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="DataValue"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="DataValue"/>-array value to set this Variant to</param>
        public void Set(DataValue[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.DataValue;
        }

        /// <summary>
        /// Initializes the object with a Variant array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="Variant"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="Variant"/>-array value to set this Variant to</param>
        public void Set(Variant[] value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Variant;
        }

        /// <summary>
        /// Initializes the object with a object array value.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a <see cref="object"/>-array value.
        /// </remarks>
        /// <param name="value">The <see cref="object"/>-array value to set this Variant to</param>
        public void Set(object[] value)
        {
            m_value = null;

            if (value != null)
            {
                Variant[] anyValues = new Variant[value.Length];

                for (int ii = 0; ii < value.Length; ii++)
                {
                    anyValues[ii] = new Variant(value[ii]);
                }

                m_value = anyValues;
            }

            m_typeInfo = TypeInfo.Arrays.Variant;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Stores a scalar value in the variant.
        /// </summary>
        private void SetScalar(object value, TypeInfo typeInfo)
        {
            m_typeInfo = typeInfo;

            switch (typeInfo.BuiltInType)
            {
                // handle special types that can be converted to something the variant supports.
                case BuiltInType.Null:
                {
                    // check for enumerated value.
                    if (value.GetType().GetTypeInfo().IsEnum)
                    {
                        Set(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                        return;
                    }

                    // check for matrix
                    Matrix matrix = value as Matrix;

                    if (matrix != null)
                    {
                        m_value = matrix;
                        return;
                    }

                    // not supported.
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        Utils.Format("The type '{0}' cannot be stored in a Variant object.", value.GetType().FullName));
                }

                // convert Guids to Uuids.
                case BuiltInType.Guid:
                {
                    Guid? guid = value as Guid?;

                    if (guid != null)
                    {
                        m_value = new Uuid(guid.Value);
                        return;
                    }

                    m_value = value;
                    return;
                }

                // convert encodeables to extension objects.
                case BuiltInType.ExtensionObject:
                {
                    IEncodeable encodeable = value as IEncodeable;

                    if (encodeable != null)
                    {
                        m_value = new ExtensionObject(encodeable);
                        return;
                    }

                    m_value = value;
                    return;
                }

                // convert encodeables to extension objects.
                case BuiltInType.Variant:
                {
                    m_value = ((Variant)value).Value;
                    m_typeInfo = TypeInfo.Construct(m_value);
                    return;
                }

                // just save the value.
                default:
                {
                    m_value = value;
                    return;
                }
            }
        }

        /// <summary>
        /// Stores a on dimensional arrau value in the variant.
        /// </summary>
        private void SetArray(Array array, TypeInfo typeInfo)
        {
            m_typeInfo = typeInfo;

            switch (typeInfo.BuiltInType)
            {
                // handle special types that can be converted to something the variant supports.
                case BuiltInType.Null:
                {
                    // check for enumerated value.
                    if (array.GetType().GetElementType().GetTypeInfo().IsEnum)
                    {
                        int[] values = new int[array.Length];

                        for (int ii = 0; ii < array.Length; ii++)
                        {
                            values[ii] = Convert.ToInt32(array.GetValue(ii), CultureInfo.InvariantCulture);
                        }

                        m_value = values;
                        return;
                    }

                    // not supported.
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        Utils.Format("The type '{0}' cannot be stored in a Variant object.", array.GetType().FullName));
                }

                // convert Guids to Uuids.
                case BuiltInType.Guid:
                {
                    Guid[] guids = array as Guid[];

                    if (guids != null)
                    {
                        Set(guids);
                        return;
                    }

                    m_value = array;
                    return;
                }

                // convert encodeables to extension objects.
                case BuiltInType.ExtensionObject:
                {
                    IEncodeable[] encodeables = array as IEncodeable[];

                    if (encodeables != null)
                    {
                        ExtensionObject[] extensions = new ExtensionObject[encodeables.Length];

                        for (int ii = 0; ii < encodeables.Length; ii++)
                        {
                            extensions[ii] = new ExtensionObject(encodeables[ii]);
                        }

                        m_value = extensions;
                        return;
                    }

                    m_value = array;
                    return;
                }

                // convert objects to variants objects.
                case BuiltInType.Variant:
                {
                    object[] objects = array as object[];

                    if (objects != null)
                    {
                        Variant[] variants = new Variant[objects.Length];

                        for (int ii = 0; ii < objects.Length; ii++)
                        {
                            variants[ii] = new Variant(objects[ii]);
                        }

                        m_value = variants;
                        return;
                    }

                    m_value = array;
                    return;
                }

                // just save the value.
                default:
                {
                    m_value = array;
                    return;
                }
            }
        }

        /// <summary>
        /// Initializes the object with a collection.
        /// </summary>
        private void SetList(IList value, TypeInfo typeInfo)
        {
            m_typeInfo = typeInfo;

            Array array = TypeInfo.CreateArray(typeInfo.BuiltInType, value.Count);

            for (int ii = 0; ii < value.Count; ii++)
            {
                if (typeInfo.BuiltInType == BuiltInType.ExtensionObject)
                {
                    IEncodeable encodeable = value[ii] as IEncodeable;

                    if (encodeable != null)
                    {
                        array.SetValue(new ExtensionObject(encodeable), ii);
                        continue;
                    }
                }

                array.SetValue(value[ii], ii);
            }

            SetArray(array, typeInfo);
        }

        /// <summary>
        /// Initializes the object with an object.
        /// </summary>
        private void Set(object value, TypeInfo typeInfo)
        {
            // check for null values.
            if (value == null)
            {
                m_value = null;
                m_typeInfo = typeInfo;
                return;
            }

            // handle scalar values.
            if (typeInfo.ValueRank < 0)
            {
                SetScalar(value, typeInfo);
                return;
            }

            Array array = value as Array;

            // handle one dimensional arrays.
            if (typeInfo.ValueRank <= 1)
            {
                // handle arrays.
                if (array != null)
                {
                    SetArray(array, typeInfo);
                    return;
                }

                // handle lists.
                IList list = value as IList;

                if (list != null)
                {
                    SetList(list, typeInfo);
                    return;
                }
            }

            // handle multidimensional array.
            if (array != null)
            {
                m_value = new Matrix(array, typeInfo.BuiltInType);
                m_typeInfo = typeInfo;
                return;
            }

            // handle matrix.
            Matrix matrix = value as Matrix;

            if (matrix != null)
            {
                m_value = matrix;
                m_typeInfo = matrix.TypeInfo;
                return;
            }

            // not supported.
            throw new ServiceResultException(
                   StatusCodes.BadNotSupported,
                   Utils.Format("Arrays of the type '{0}' cannot be stored in a Variant object.", value.GetType().FullName));
        }
        #endregion

        #region Private Members
        private object m_value;
        private TypeInfo m_typeInfo;
        #endregion
    }

    #region VariantCollection Class
    /// <summary>
    /// A collection of Variant objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfVariant", Namespace = Namespaces.OpcUaXsd, ItemName = "Variant")]
    public partial class VariantCollection : List<Variant>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public VariantCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Provides a strongly-typed collection of <see cref="Variant"/> objects.
        /// </remarks>
        public VariantCollection(IEnumerable<Variant> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The capacity to constrain the collection to</param>
        public VariantCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array of <see cref="Variant"/> to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="Variant"/> to convert to a collection</param>
        public static VariantCollection ToVariantCollection(Variant[] values)
        {
            if (values != null)
            {
                return new VariantCollection(values);
            }

            return new VariantCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array of <see cref="Variant"/> to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="Variant"/> to convert to a collection</param>
        public static implicit operator VariantCollection(Variant[] values)
        {
            return ToVariantCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            VariantCollection clone = new VariantCollection(this.Count);

            foreach (Variant element in this)
            {
                clone.Add((Variant)Utils.Clone(element));
            }

            return clone;
        }
    }//class
    #endregion

    /// <summary>
    /// Wraps a multi-dimensional array for use within a Variant.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class Matrix : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Initializes the matrix with a multidimensional array.
        /// </summary>
        public Matrix(Array value, BuiltInType builtInType)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            m_elements = value;
            m_dimensions = new int[value.Rank];

            for (int ii = 0; ii < m_dimensions.Length; ii++)
            {
                m_dimensions[ii] = value.GetLength(ii);
            }

            m_elements = Utils.FlattenArray(value);
            m_typeInfo = new TypeInfo(builtInType, m_dimensions.Length);

#if DEBUG
            TypeInfo sanityCheck = TypeInfo.Construct(m_elements);
            System.Diagnostics.Debug.Assert(sanityCheck.BuiltInType == builtInType || (sanityCheck.BuiltInType == BuiltInType.ByteString && builtInType == BuiltInType.Byte));
#endif
        }

        /// <summary>
        /// Initializes the matrix with a one dimensional array and a list of dimensions.
        /// </summary>
        public Matrix(Array elements, BuiltInType builtInType, params int[] dimensions)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements));

            m_elements = elements;
            m_dimensions = dimensions;

            if (dimensions != null && dimensions.Length > 0)
            {
                int length = 1;

                for (int ii = 0; ii < dimensions.Length; ii++)
                {
                    length *= dimensions[ii];
                }

                if (length != elements.Length)
                {
                    throw new ArgumentException("The number of elements in the array does not match the dimensions.");
                }
            }
            else
            {
                m_dimensions = new int[] { elements.Length };
            }

            m_typeInfo = new TypeInfo(builtInType, m_dimensions.Length);

#if DEBUG
            TypeInfo sanityCheck = TypeInfo.Construct(m_elements);
            System.Diagnostics.Debug.Assert(sanityCheck.BuiltInType == builtInType ||
                (sanityCheck.BuiltInType == BuiltInType.Int32 && builtInType == BuiltInType.Enumeration) ||
                (sanityCheck.BuiltInType == BuiltInType.ByteString && builtInType == BuiltInType.Byte) ||
                (builtInType == BuiltInType.Variant));
#endif
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The elements of the matrix.
        /// </summary>
        /// <value>An array of elements.</value>
        public Array Elements => m_elements;

        /// <summary>
        /// The dimensions of the matrix.
        /// </summary>
        /// <value>The dimensions of the array.</value>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public int[] Dimensions => m_dimensions;

        /// <summary>
        /// The type information for the matrix.
        /// </summary>
        /// <value>The type information.</value>
        public TypeInfo TypeInfo => m_typeInfo;

        /// <summary>
        /// Returns the flattened array as a multi-dimensional array.
        /// </summary>
        public Array ToArray()
        {
            Array array = Array.CreateInstance(m_elements.GetType().GetElementType(), m_dimensions);

            int[] indexes = new int[m_dimensions.Length];

            for (int ii = 0; ii < m_elements.Length; ii++)
            {
                array.SetValue(m_elements.GetValue(ii), indexes);

                for (int jj = indexes.Length - 1; jj >= 0; jj--)
                {
                    indexes[jj]++;

                    if (indexes[jj] < m_dimensions[jj])
                    {
                        break;
                    }

                    indexes[jj] = 0;
                }
            }

            return array;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            Matrix matrix = obj as Matrix;

            if (matrix != null)
            {
                if (!m_typeInfo.Equals(matrix.TypeInfo))
                {
                    return false;
                }
                if (!Utils.IsEqual(m_dimensions, matrix.Dimensions))
                {
                    return false;
                }
                return Utils.IsEqual(m_elements, matrix.Elements);
            }

            return false;
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        public override int GetHashCode()
        {
            if (m_elements != null)
            {
                return m_elements.GetHashCode();
            }
            if (m_typeInfo != null)
            {
                return m_typeInfo.GetHashCode();
            }
            if (m_dimensions != null)
            {
                return m_dimensions.GetHashCode();
            }
            return base.GetHashCode();
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">(Unused) Always pass a NULL value</param>
        /// <param name="formatProvider">The format-provider to use. If unsure, pass an empty string or null</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <exception cref="FormatException">Thrown when the 'format' argument is NOT null.</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder buffer = new StringBuilder();

                buffer.AppendFormat("{0}[", m_elements.GetType().GetElementType().Name);

                for (int ii = 0; ii < m_dimensions.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(",");
                    }

                    buffer.AppendFormat(formatProvider, "{0}", m_dimensions[ii]);
                }

                buffer.AppendFormat(formatProvider, "]");

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region ICloneable Members
        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            return new Matrix((Array)Utils.Clone(m_elements), m_typeInfo.BuiltInType, (int[])Utils.Clone(m_dimensions));
        }
        #endregion

        #region Private Fields
        private Array m_elements;
        private int[] m_dimensions;
        private TypeInfo m_typeInfo;
        #endregion
    }

}//namespace
