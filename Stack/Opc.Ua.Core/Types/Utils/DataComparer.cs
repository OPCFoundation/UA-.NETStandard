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
using System.Collections.Generic;
using System.Reflection;
using System.Xml;

namespace Opc.Ua.Test
{
    /// <summary>
    /// Compares values taking into account semantically equivalent values that would fail the simple IsEqual test.
    /// </summary>
    public class DataComparer
    {
        #region Constructors
        /// <summary>
        /// Constructs an instance of the data comparer.
        /// </summary>
        public DataComparer(ServiceMessageContext context)
        {
            m_context = context;
            m_throwOnError = true;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or set a flag indicating whether an exception should be thrown on error.
        /// </summary>
        public bool ThrowOnError
        {
            get { return m_throwOnError; }
            set { m_throwOnError = value; }
        }
        #endregion

        #region Boolean Functions
        /// <summary>
        /// This method compares two Boolean values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareBoolean(bool value1, bool value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }
            return true;
        }
        #endregion

        #region SByte Functions
        /// <summary>
        /// This method compares two SByte values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareSByte(sbyte value1, sbyte value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region Byte Functions
        /// <summary>
        /// This method compares two Byte values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareByte(byte value1, byte value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region Int16 Functions
        /// <summary>
        /// This method compares two Int16 values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareInt16(short value1, short value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region UInt16 Functions
        /// <summary>
        /// This method compares two UInt16 values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareUInt16(ushort value1, ushort value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region Int32 Functions
        /// <summary>
        /// This method compares two Int32 values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareInt32(int value1, int value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region UInt32 Functions
        /// <summary>
        /// This method compares two UInt32 values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareUInt32(uint value1, uint value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region Int64 Functions
        /// <summary>
        /// This method compares two Int64 values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareInt64(long value1, long value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region UInt64 Functions
        /// <summary>
        /// This method compares two UInt64 values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareUInt64(ulong value1, ulong value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region Float Functions
        /// <summary>
        /// This method compares two Float values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareFloat(float value1, float value2)
        {
            if (value1 != value2)
            {
                if (Single.IsNaN(value1) && Single.IsNaN(value2))
                {
                    return true;
                }

                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region Double Functions
        /// <summary>
        /// This method compares two Double values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareDouble(double value1, double value2)
        {
            if (value1 != value2)
            {
                if (Double.IsNaN(value1) && Double.IsNaN(value2))
                {
                    return true;
                }

                double delta = Math.Abs(value1 - value2);

                if (delta < Math.Abs(value1 / 1e15))
                {
                    return true;
                }

                return ReportError(value1, delta);
            }

            return true;
        }
        #endregion

        #region String Functions
        /// <summary>
        /// This method compares two String values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareString(string value1, string value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region DateTime Functions
        /// <summary>
        /// This method compares two DateTime values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareDateTime(DateTime value1, DateTime value2)
        {
            if (value1.Kind != value2.Kind)
            {
                value1 = Utils.ToOpcUaUniversalTime(value1);
                value2 = Utils.ToOpcUaUniversalTime(value2);
            }

            if (value1 < Utils.TimeBase)
            {
                value1 = DateTime.MinValue;
            }

            if (value2 < Utils.TimeBase)
            {
                value2 = DateTime.MinValue;
            }

            if (value1 == value2)
            {
                return true;
            }

            // allow milliseconds to be truncated.
            return Math.Abs((value1 - value2).Ticks) < 10000;
        }
        #endregion

        #region Guid Functions
        /// <summary>
        /// This method compares two Guid values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareUuid(Uuid value1, Uuid value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region ByteString Functions
        /// <summary>
        /// This method compares two ByteString values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareByteString(byte[] value1, byte[] value2)
        {
            if (value1 == null || value2 == null)
            {
                if (value1 != value2)
                {
                    return ReportError(value1, value2);
                }

                return true;
            }

            if (value1.Length != value2.Length)
            {
                return ReportError(value1, value2);
            }

            for (int ii = 0; ii < value1.Length; ii++)
            {
                if (value1[ii] != value2[ii])
                {
                    return ReportError(value1[ii], value1[ii]);
                }
            }

            return true;
        }
        #endregion

        #region XmlElement Functions
        /// <summary>
        /// This method compares two XmlElement values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareXmlElement(XmlElement value1, XmlElement value2)
        {
            if (value1 == null || value2 == null)
            {
                if (value1 != value2)
                {
                    return ReportError(value1, value2);
                }

                return true;
            }

            if (value1.LocalName != value2.LocalName)
            {
                return ReportError(value1.LocalName, value2.LocalName);
            }

            if (value1.NamespaceURI != value2.NamespaceURI)
            {
                return ReportError(value1.NamespaceURI, value2.NamespaceURI);
            }

            foreach (XmlAttribute attribute1 in value1.Attributes)
            {
                XmlAttribute attribute2 = value2.GetAttributeNode(attribute1.Name);

                if (attribute2 == null)
                {
                    if (!attribute1.Name.StartsWith("xmlns", StringComparison.Ordinal))
                    {
                        return ReportError(attribute1, attribute2);
                    }

                    string prefix = (attribute1.Name.Length > 5) ? attribute1.Name.Substring(6) : String.Empty;

                    if (attribute1.Value != value2.GetNamespaceOfPrefix(prefix))
                    {
                        return ReportError(attribute1.Value, value2.GetNamespaceOfPrefix(prefix));
                    }
                }
                else
                {
                    if (attribute2.Value != attribute1.Value)
                    {
                        return ReportError(attribute2.Value, attribute1.Value);
                    }
                }
            }

            XmlNode child1 = value1.FirstChild;
            XmlNode child2 = value2.FirstChild;

            while (child1 != null && child2 != null)
            {
                while (child1 != null)
                {
                    if (child1.NodeType == XmlNodeType.Element)
                    {
                        break;
                    }

                    child1 = child1.NextSibling;
                }

                while (child2 != null)
                {
                    if (child2.NodeType == XmlNodeType.Element)
                    {
                        break;
                    }

                    child2 = child2.NextSibling;
                }

                if (!CompareXmlElement((XmlElement)child1, (XmlElement)child2))
                {
                    return false;
                }

                if (child1 != null)
                {
                    child1 = child1.NextSibling;
                }

                if (child2 != null)
                {
                    child2 = child2.NextSibling;
                }
            }

            if (child1 != child2)
            {
                return ReportError(child1, child2);
            }

            return true;
        }
        #endregion

        #region NodeId Functions
        /// <summary>
        /// This method compares two NodeId values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareNodeId(NodeId value1, NodeId value2)
        {
            if (NodeId.IsNull(value1) && NodeId.IsNull(value2))
            {
                return true;
            }

            if (value1 == null || value2 == null)
            {
                if (value1 != value2)
                {
                    return ReportError(value1, value2);
                }

                return true;
            }

            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region ExpandedNodeId Functions
        /// <summary>
        /// This method compares two ExpandedNodeId values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareExpandedNodeId(ExpandedNodeId value1, ExpandedNodeId value2)
        {
            if (NodeId.IsNull(value1) && NodeId.IsNull(value2))
            {
                return true;
            }

            if (value1 == null || value2 == null)
            {
                if (value1 != value2)
                {
                    return false;
                }

                return true;
            }

            if (value1 != value2)
            {
                NodeId nodeId1 = ExpandedNodeId.ToNodeId(value1, m_context.NamespaceUris);
                NodeId nodeId2 = ExpandedNodeId.ToNodeId(value2, m_context.NamespaceUris);

                if (nodeId1 != nodeId2)
                {
                    return ReportError(value1, value2);
                }
            }

            return true;
        }
        #endregion

        #region StatusCode Functions
        /// <summary>
        /// This method compares two StatusCode values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareStatusCode(StatusCode value1, StatusCode value2)
        {
            if (value1 != value2)
            {
                return ReportError(value1, value2);
            }

            return true;
        }
        #endregion

        #region DiagnosticInfo Functions
        /// <summary>
        /// This method compares two DiagnosticInfo values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareDiagnosticInfo(DiagnosticInfo value1, DiagnosticInfo value2)
        {
            if (value1 == null && value2 == null)
            {
                return true;
            }

            if (value1 == null)
            {
                value1 = new DiagnosticInfo();
            }

            if (value2 == null)
            {
                value2 = new DiagnosticInfo();
            }

            if (!CompareInt32(value1.SymbolicId, value2.SymbolicId))
            {
                return false;
            }

            if (!CompareInt32(value1.NamespaceUri, value2.NamespaceUri))
            {
                return false;
            }

            if (!CompareInt32(value1.Locale, value2.Locale))
            {
                return false;
            }

            if (!CompareInt32(value1.LocalizedText, value2.LocalizedText))
            {
                return false;
            }

            if (!CompareString(value1.AdditionalInfo, value2.AdditionalInfo))
            {
                return false;
            }

            if (!CompareStatusCode(value1.InnerStatusCode, value2.InnerStatusCode))
            {
                return false;
            }

            if (!CompareDiagnosticInfo(value1.InnerDiagnosticInfo, value2.InnerDiagnosticInfo))
            {
                return false;
            }

            return true;
        }
        #endregion

        #region QualifiedName Functions
        /// <summary>
        /// This method compares two QualifiedName values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareQualifiedName(QualifiedName value1, QualifiedName value2)
        {
            if (value1 == null)
            {
                if (value2 == null || value2 == QualifiedName.Null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (value2 == null)
            {
                if (value1 == null || value1 == QualifiedName.Null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (!value1.Equals(value2))
            {
                return ReportError(value1, value1);
            }
            return true;
        }
        #endregion

        #region LocalizedText Functions
        /// <summary>
        /// This method compares two LocalizedText values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareLocalizedText(LocalizedText value1, LocalizedText value2)
        {
            if (value1 == null)
            {
                if (value2 == null || value2 == LocalizedText.Null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (value2 == null)
            {
                if (value1 == null || value1 == LocalizedText.Null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (!value1.Equals(value2))
            {
                return ReportError(value1, value1);
            }
            return true;
        }
        #endregion

        #region Variant Functions
        /// <summary>
        /// This method compares two Variant values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public bool CompareVariant(Variant value1, Variant value2)
        {
            if (value1.Value == null || value2.Value == null)
            {
                if (value1.Value != value2.Value)
                {
                    return ReportError(value1.Value, value2.Value);
                }

                return true;
            }

            System.Type systemType = value1.Value.GetType();

            if (systemType != value2.Value.GetType())
            {
                return ReportError(value1.Value, value2.Value);
            }

            if (!systemType.IsArray || systemType == typeof(byte[]))
            {
                if (systemType == typeof(bool)) { return CompareBoolean((bool)value1.Value, (bool)value2.Value); }
                if (systemType == typeof(sbyte)) { return CompareSByte((sbyte)value1.Value, (sbyte)value2.Value); }
                if (systemType == typeof(byte)) { return CompareByte((byte)value1.Value, (byte)value2.Value); }
                if (systemType == typeof(short)) { return CompareInt16((short)value1.Value, (short)value2.Value); }
                if (systemType == typeof(ushort)) { return CompareUInt16((ushort)value1.Value, (ushort)value2.Value); }
                if (systemType == typeof(int)) { return CompareInt32((int)value1.Value, (int)value2.Value); }
                if (systemType == typeof(uint)) { return CompareUInt32((uint)value1.Value, (uint)value2.Value); }
                if (systemType == typeof(long)) { return CompareInt64((long)value1.Value, (long)value2.Value); }
                if (systemType == typeof(ulong)) { return CompareUInt64((ulong)value1.Value, (ulong)value2.Value); }
                if (systemType == typeof(float)) { return CompareFloat((float)value1.Value, (float)value2.Value); }
                if (systemType == typeof(double)) { return CompareDouble((double)value1.Value, (double)value2.Value); }
                if (systemType == typeof(string)) { return CompareString((string)value1.Value, (string)value2.Value); }
                if (systemType == typeof(DateTime)) { return CompareDateTime((DateTime)value1.Value, (DateTime)value2.Value); }
                if (systemType == typeof(Uuid)) { return CompareUuid((Uuid)value1.Value, (Uuid)value2.Value); }
                if (systemType == typeof(byte[])) { return CompareByteString((byte[])value1.Value, (byte[])value2.Value); }
                if (systemType == typeof(XmlElement)) { return CompareXmlElement((XmlElement)value1.Value, (XmlElement)value2.Value); }
                if (systemType == typeof(NodeId)) { return CompareNodeId((NodeId)value1.Value, (NodeId)value2.Value); }
                if (systemType == typeof(ExpandedNodeId)) { return CompareExpandedNodeId((ExpandedNodeId)value1.Value, (ExpandedNodeId)value2.Value); }
                if (systemType == typeof(StatusCode)) { return CompareStatusCode((StatusCode)value1.Value, (StatusCode)value2.Value); }
                if (systemType == typeof(DiagnosticInfo)) { return CompareDiagnosticInfo((DiagnosticInfo)value1.Value, (DiagnosticInfo)value2.Value); }
                if (systemType == typeof(QualifiedName)) { return CompareQualifiedName((QualifiedName)value1.Value, (QualifiedName)value2.Value); }
                if (systemType == typeof(LocalizedText)) { return CompareLocalizedText((LocalizedText)value1.Value, (LocalizedText)value2.Value); }
                if (systemType == typeof(ExtensionObject)) { return CompareExtensionObject((ExtensionObject)value1.Value, (ExtensionObject)value2.Value); }
                if (systemType == typeof(DataValue)) { return CompareDataValue((DataValue)value1.Value, (DataValue)value2.Value); }
                if (systemType == typeof(Variant)) { return CompareVariant((Variant)value1.Value, (Variant)value2.Value); }
                if (systemType == typeof(Matrix)) { return CompareMatrix((Matrix)value1.Value, (Matrix)value2.Value); }
            }
            else
            {
                if (systemType == typeof(bool[])) { return CompareArray<bool>((bool[])value1.Value, (bool[])value2.Value, CompareBoolean); }
                if (systemType == typeof(sbyte[])) { return CompareArray<sbyte>((sbyte[])value1.Value, (sbyte[])value2.Value, CompareSByte); }
                if (systemType == typeof(short[])) { return CompareArray<short>((short[])value1.Value, (short[])value2.Value, CompareInt16); }
                if (systemType == typeof(ushort[])) { return CompareArray<ushort>((ushort[])value1.Value, (ushort[])value2.Value, CompareUInt16); }
                if (systemType == typeof(int[])) { return CompareArray<int>((int[])value1.Value, (int[])value2.Value, CompareInt32); }
                if (systemType == typeof(uint[])) { return CompareArray<uint>((uint[])value1.Value, (uint[])value2.Value, CompareUInt32); }
                if (systemType == typeof(long[])) { return CompareArray<long>((long[])value1.Value, (long[])value2.Value, CompareInt64); }
                if (systemType == typeof(ulong[])) { return CompareArray<ulong>((ulong[])value1.Value, (ulong[])value2.Value, CompareUInt64); }
                if (systemType == typeof(float[])) { return CompareArray<float>((float[])value1.Value, (float[])value2.Value, CompareFloat); }
                if (systemType == typeof(double[])) { return CompareArray<double>((double[])value1.Value, (double[])value2.Value, CompareDouble); }
                if (systemType == typeof(string[])) { return CompareArray<string>((string[])value1.Value, (string[])value2.Value, CompareString); }
                if (systemType == typeof(DateTime[])) { return CompareArray<DateTime>((DateTime[])value1.Value, (DateTime[])value2.Value, CompareDateTime); }
                if (systemType == typeof(Uuid[])) { return CompareArray<Uuid>((Uuid[])value1.Value, (Uuid[])value2.Value, CompareUuid); }
                if (systemType == typeof(byte[][])) { return CompareArray<byte[]>((byte[][])value1.Value, (byte[][])value2.Value, CompareByteString); }
                if (systemType == typeof(XmlElement[])) { return CompareArray<XmlElement>((XmlElement[])value1.Value, (XmlElement[])value2.Value, CompareXmlElement); }
                if (systemType == typeof(NodeId[])) { return CompareArray<NodeId>((NodeId[])value1.Value, (NodeId[])value2.Value, CompareNodeId); }
                if (systemType == typeof(ExpandedNodeId[])) { return CompareArray<ExpandedNodeId>((ExpandedNodeId[])value1.Value, (ExpandedNodeId[])value2.Value, CompareExpandedNodeId); }
                if (systemType == typeof(StatusCode[])) { return CompareArray<StatusCode>((StatusCode[])value1.Value, (StatusCode[])value2.Value, CompareStatusCode); }
                if (systemType == typeof(DiagnosticInfo[])) { return CompareArray<DiagnosticInfo>((DiagnosticInfo[])value1.Value, (DiagnosticInfo[])value2.Value, CompareDiagnosticInfo); }
                if (systemType == typeof(QualifiedName[])) { return CompareArray<QualifiedName>((QualifiedName[])value1.Value, (QualifiedName[])value2.Value, CompareQualifiedName); }
                if (systemType == typeof(LocalizedText[])) { return CompareArray<LocalizedText>((LocalizedText[])value1.Value, (LocalizedText[])value2.Value, CompareLocalizedText); }
                if (systemType == typeof(ExtensionObject[])) { return CompareArray<ExtensionObject>((ExtensionObject[])value1.Value, (ExtensionObject[])value2.Value, CompareExtensionObject); }
                if (systemType == typeof(DataValue[])) { return CompareArray<DataValue>((DataValue[])value1.Value, (DataValue[])value2.Value, CompareDataValue); }
                if (systemType == typeof(Variant[])) { return CompareArray<Variant>((Variant[])value1.Value, (Variant[])value2.Value, CompareVariant); }
            }

            return ReportError(value1.Value, value2.Value);
        }
        #endregion

        #region DataValue Functions
        /// <summary>
        /// This method compares two DataValues.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns> 
        public bool CompareDataValue(DataValue value1, DataValue value2)
        {
            if (value1 == null || value2 == null)
            {
                if (value1 != value2)
                {
                    return ReportError(value1, value2);
                }

                return true;
            }

            if (!CompareVariant(value1.WrappedValue, value2.WrappedValue))
            {
                return false;
            }

            if (!CompareStatusCode(value1.StatusCode, value2.StatusCode))
            {
                return false;
            }

            if (!CompareDateTime(value1.SourceTimestamp, value2.SourceTimestamp))
            {
                return false;
            }

            if (!CompareUInt16(value1.SourcePicoseconds, value2.SourcePicoseconds))
            {
                return false;
            }

            if (!CompareDateTime(value1.ServerTimestamp, value2.ServerTimestamp))
            {
                return false;
            }

            if (!CompareUInt16(value1.ServerPicoseconds, value2.ServerPicoseconds))
            {
                return false;
            }

            return true;
        }
        #endregion

        #region Matrix Functions
        /// <summary>
        /// This method compares two DataValues.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns> 
        public bool CompareMatrix(Matrix value1, Matrix value2)
        {
            if (value1 == null || value2 == null)
            {
                if (value1 != value2)
                {
                    return ReportError(value1, value2);
                }

                return true;
            }

            if (!CompareVariant(new Variant(value1.Elements), new Variant(value2.Elements)))
            {
                return false;
            }

            if (!CompareArray<Int32>(value1.Dimensions, value2.Dimensions, CompareInt32))
            {
                return false;
            }

            return true;
        }
        #endregion

        #region ExtensionObject Functions
        /// <summary>
        /// The factory to use when decoding extension objects.
        /// </summary>        
        public static EncodeableFactory EncodeableFactory
        {
            get
            {
                if (s_Factory == null)
                {
                    s_Factory = new EncodeableFactory();
                    s_Factory.AddEncodeableTypes(typeof(DataComparer).GetTypeInfo().Assembly);
                }

                return s_Factory;
            }
        }

        // It stores encodable types of the executing assembly.       
        private static EncodeableFactory s_Factory = new EncodeableFactory();

        /// <summary>
        /// Extracts the extension object body.
        /// </summary>
        /// <param name="value">Extension object.</param>
        /// <returns>IEncodeable object</returns>
        public static object GetExtensionObjectBody(ExtensionObject value)
        {
            object body = value.Body;

            IEncodeable encodeable = body as IEncodeable;

            if (encodeable != null)
            {
                return encodeable;
            }

            Type expectedType = EncodeableFactory.GetSystemType(value.TypeId);

            if (expectedType == null)
            {
                return body;
            }

            ServiceMessageContext context = new ServiceMessageContext();
            context.Factory = EncodeableFactory;

            XmlElement xml = body as XmlElement;

            if (xml != null)
            {
                XmlQualifiedName xmlName = EncodeableFactory.GetXmlName(expectedType);
                XmlDecoder decoder = new XmlDecoder(xml, context);

                decoder.PushNamespace(xmlName.Namespace);
                body = decoder.ReadEncodeable(xmlName.Name, expectedType);
                decoder.PopNamespace();
                decoder.Close();

                return (IEncodeable)body;
            }

            byte[] bytes = body as byte[];

            if (bytes != null)
            {
                BinaryDecoder decoder = new BinaryDecoder(bytes, context);
                body = decoder.ReadEncodeable(null, expectedType);
                decoder.Close();

                return (IEncodeable)body;
            }

            return body;
        }

        /// <summary>
        /// This method compares two ExtensionObject values.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        public bool CompareExtensionObject(ExtensionObject value1, ExtensionObject value2)
        {
            if (value1 == null || value2 == null)
            {
                if (value1 != value2)
                {
                    return ReportError(value1, value2);
                }

                return true;
            }

            object body1 = value1.Body;
            object body2 = value2.Body;

            if (body1 == null || body2 == null)
            {
                return body1 == body2;
            }

            byte[] bytes1 = value1.Body as byte[];
            byte[] bytes2 = value2.Body as byte[];

            if (bytes1 != null && bytes2 != null)
            {
                if (!CompareExpandedNodeId(value1.TypeId, value2.TypeId))
                {
                    return ReportError(value1.TypeId, value2.TypeId);
                }

                return CompareByteString(bytes1, bytes2);
            }

            XmlElement xml1 = value1.Body as XmlElement;
            XmlElement xml2 = value2.Body as XmlElement;

            if (xml1 != null && xml2 != null)
            {
                if (!CompareExpandedNodeId(value1.TypeId, value2.TypeId))
                {
                    return ReportError(value1.TypeId, value2.TypeId);
                }

                return CompareXmlElement(xml1, xml2);
            }

            body1 = GetExtensionObjectBody(value1);
            body2 = GetExtensionObjectBody(value2);

            if (!CompareExtensionObjectBody(body1, body2))
            {
                return ReportError(value1, value2);
            }

            return true;
        }

        /// <summary>
        /// Compares the value of two extension object body.
        /// </summary>
        protected virtual bool CompareExtensionObjectBody(object value1, object value2)
        {
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            IEncodeable encodeable1 = value1 as IEncodeable;
            IEncodeable encodeable2 = value2 as IEncodeable;

            if (encodeable1 != null && encodeable2 != null)
            {
                if (encodeable1.IsEqual(encodeable1))
                {
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// The delegate used to compare two values.   
        /// </summary> 
        private delegate bool Comparator<T>(T value1, T value2);

        /// <summary>
        /// This method compares two arrays.
        /// </summary>      
        /// <param name="value1">IEnumerable object of type T</param>
        /// <param name="value2">IEnumerable object of type T</param>
        /// <param name="comparator">Method name to compare the arrays.</param>
        /// <returns>True in case of equal values.
        /// False or ServiceResultException in case of unequal values.</returns>
        private bool CompareArray<T>(IEnumerable<T> value1, IEnumerable<T> value2, Comparator<T> comparator)
        {
            if (value1 == null)
            {
                if (value2 == null || value2.GetEnumerator().MoveNext() == false)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (value2 == null)
            {
                if (value1 == null || value1.GetEnumerator().MoveNext() == false)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            if (value2 == null)
            {
                if (value1 == null || value1.GetEnumerator().MoveNext() == false)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            IEnumerator<T> enumerator1 = value1.GetEnumerator();
            IEnumerator<T> enumerator2 = value2.GetEnumerator();

            while (enumerator1.MoveNext())
            {
                if (!enumerator2.MoveNext())
                {
                    return ReportError(value1, value2);
                }

                if (!comparator(enumerator1.Current, enumerator2.Current))
                {
                    return false;
                }
            }

            if (enumerator2.MoveNext())
            {
                return ReportError(value1, value2);
            }

            return true;
        }

        /// <summary>
        /// In case of errors, throw exception.
        /// </summary>
        /// <param name="value1">First Value.</param>
        /// <param name="value2">Second Value.</param>
        /// <returns>Throws ServiceResultException in case of unequal values.</returns>
        private bool ReportError(object value1, object value2)
        {
            if (m_throwOnError)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "'{0}' is not equal to '{1}'.",
                    value1,
                    value2);
            }

            return false;
        }
        #endregion

        #region Private Fields
        private ServiceMessageContext m_context;
        private bool m_throwOnError;
        #endregion
    }
}
