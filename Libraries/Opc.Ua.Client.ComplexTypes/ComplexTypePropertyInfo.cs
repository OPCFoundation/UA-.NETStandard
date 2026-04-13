/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using System.Runtime.Serialization;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Complex type property info.
    /// </summary>
    public class ComplexTypePropertyInfo
    {
        /// <inheritdoc cref="System.Reflection.PropertyInfo"/>
        public PropertyInfo PropertyInfo { get; }

        /// <summary>
        /// The structure field attributes of the complex type.
        /// </summary>
        public StructureFieldAttribute FieldAttribute { get; }

        /// <summary>
        /// The data attributes of the complex type.
        /// </summary>
        public DataMemberAttribute DataAttribute { get; }

        /// <summary>
        /// Type information object for the complex type property.
        /// </summary>
        public TypeInfo TypeInfo => TypeInfo.Create(
            (BuiltInType)FieldAttribute.BuiltInType,
            FieldAttribute.ValueRank);

        /// <summary>
        /// Create the infos for the complex type.
        /// </summary>
        public ComplexTypePropertyInfo(
            PropertyInfo propertyInfo,
            StructureFieldAttribute fieldAttribute,
            DataMemberAttribute dataAttribute)
        {
            PropertyInfo = propertyInfo;
            FieldAttribute = fieldAttribute;
            DataAttribute = dataAttribute;
            OptionalFieldMask = 0;
        }

        /// <summary>
        /// Get the name of the complex type.
        /// </summary>
        public string Name => PropertyInfo.Name;

        /// <summary>
        /// Get the value of a property.
        /// </summary>
        public Variant GetValue(object o)
        {
            object value = PropertyInfo.GetValue(o);
            if (value == null)
            {
                return Variant.CreateDefault(TypeInfo);
            }
            if (TypeInfo.IsScalar)
            {
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return Variant.From((bool)value);
                    case BuiltInType.SByte:
                        return Variant.From((sbyte)value);
                    case BuiltInType.Byte:
                        return Variant.From((byte)value);
                    case BuiltInType.Int16:
                        return Variant.From((short)value);
                    case BuiltInType.UInt16:
                        return Variant.From((ushort)value);
                    case BuiltInType.Int32:
                        return Variant.From((int)value);
                    case BuiltInType.Enumeration:
                        return Variant.FromEnumeration(value, PropertyInfo.PropertyType);
                    case BuiltInType.UInt32:
                        return Variant.From((uint)value);
                    case BuiltInType.Int64:
                        return Variant.From((long)value);
                    case BuiltInType.UInt64:
                        return Variant.From((ulong)value);
                    case BuiltInType.Float:
                        return Variant.From((float)value);
                    case BuiltInType.Double:
                        return Variant.From((double)value);
                    case BuiltInType.String:
                        return Variant.From((string)value);
                    case BuiltInType.DateTime:
                        return Variant.From((DateTimeUtc)value);
                    case BuiltInType.Guid:
                        return Variant.From((Uuid)value);
                    case BuiltInType.ByteString:
                        return Variant.From((ByteString)value);
                    case BuiltInType.XmlElement:
                        return Variant.From((XmlElement)value);
                    case BuiltInType.NodeId:
                        return Variant.From((NodeId)value);
                    case BuiltInType.ExpandedNodeId:
                        return Variant.From((ExpandedNodeId)value);
                    case BuiltInType.StatusCode:
                        return Variant.From((StatusCode)value);
                    case BuiltInType.QualifiedName:
                        return Variant.From((QualifiedName)value);
                    case BuiltInType.LocalizedText:
                        return Variant.From((LocalizedText)value);
                    case BuiltInType.ExtensionObject:
                        return
                            PropertyInfo.PropertyType == typeof(ExtensionObject) ?
                            Variant.From((ExtensionObject)value) :
                            Variant.FromStructure((IEncodeable)value);
                    case BuiltInType.DataValue:
                        return Variant.From((DataValue)value);
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        return (Variant)value;
                    default:
                        return default;
                }
            }
            else if (TypeInfo.IsArray)
            {
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return Variant.From((bool[])value);
                    case BuiltInType.SByte:
                        return Variant.From((sbyte[])value);
                    case BuiltInType.Byte:
                        return Variant.From((byte[])value);
                    case BuiltInType.Int16:
                        return Variant.From((short[])value);
                    case BuiltInType.UInt16:
                        return Variant.From((ushort[])value);
                    case BuiltInType.Int32:
                        return Variant.From((int[])value);
                    case BuiltInType.Enumeration:
                        return Variant.FromEnumeration(
                            EnumHelper.EnumArrayToInt32Array((Array)value),
                            PropertyInfo.PropertyType);
                    case BuiltInType.UInt32:
                        return Variant.From((uint[])value);
                    case BuiltInType.Int64:
                        return Variant.From((long[])value);
                    case BuiltInType.UInt64:
                        return Variant.From((ulong[])value);
                    case BuiltInType.Float:
                        return Variant.From((float[])value);
                    case BuiltInType.Double:
                        return Variant.From((double[])value);
                    case BuiltInType.String:
                        return Variant.From((string[])value);
                    case BuiltInType.DateTime:
                        return Variant.From((DateTimeUtc[])value);
                    case BuiltInType.Guid:
                        return Variant.From((Uuid[])value);
                    case BuiltInType.ByteString:
                        return Variant.From((ByteString[])value);
                    case BuiltInType.XmlElement:
                        return Variant.From((XmlElement[])value);
                    case BuiltInType.NodeId:
                        return Variant.From((NodeId[])value);
                    case BuiltInType.ExpandedNodeId:
                        return Variant.From((ExpandedNodeId[])value);
                    case BuiltInType.StatusCode:
                        return Variant.From((StatusCode[])value);
                    case BuiltInType.QualifiedName:
                        return Variant.From((QualifiedName[])value);
                    case BuiltInType.LocalizedText:
                        return Variant.From((LocalizedText[])value);
                    case BuiltInType.ExtensionObject:
                        return
                            PropertyInfo.PropertyType.GetElementType() == typeof(ExtensionObject) ?
                            Variant.From((ExtensionObject[])value) :
                            Variant.FromStructure(((IEncodeable[])value).ToArrayOf());
                    case BuiltInType.DataValue:
                        return Variant.From((DataValue[])value);
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        return Variant.From((Variant[])value);
                    default:
                        return default;
                }
            }
            else
            {
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return Variant.From(MatrixOf.From<bool>((Array)value));
                    case BuiltInType.SByte:
                        return Variant.From(MatrixOf.From<sbyte>((Array)value));
                    case BuiltInType.Byte:
                        return Variant.From(MatrixOf.From<byte>((Array)value));
                    case BuiltInType.Int16:
                        return Variant.From(MatrixOf.From<short>((Array)value));
                    case BuiltInType.UInt16:
                        return Variant.From(MatrixOf.From<ushort>((Array)value));
                    case BuiltInType.Int32:
                        return Variant.From(MatrixOf.From<int>((Array)value));
                    case BuiltInType.Enumeration:
                        return Variant.FromEnumeration(
                            EnumHelper.EnumArrayToInt32Matrix((Array)value),
                            PropertyInfo.PropertyType);
                    case BuiltInType.UInt32:
                        return Variant.From(MatrixOf.From<uint>((Array)value));
                    case BuiltInType.Int64:
                        return Variant.From(MatrixOf.From<long>((Array)value));
                    case BuiltInType.UInt64:
                        return Variant.From(MatrixOf.From<ulong>((Array)value));
                    case BuiltInType.Float:
                        return Variant.From(MatrixOf.From<float>((Array)value));
                    case BuiltInType.Double:
                        return Variant.From(MatrixOf.From<double>((Array)value));
                    case BuiltInType.String:
                        return Variant.From(MatrixOf.From<string>((Array)value));
                    case BuiltInType.DateTime:
                        return Variant.From(MatrixOf.From<DateTimeUtc>((Array)value));
                    case BuiltInType.Guid:
                        return Variant.From(MatrixOf.From<Uuid>((Array)value));
                    case BuiltInType.ByteString:
                        return Variant.From(MatrixOf.From<ByteString>((Array)value));
                    case BuiltInType.XmlElement:
                        return Variant.From(MatrixOf.From<XmlElement>((Array)value));
                    case BuiltInType.NodeId:
                        return Variant.From(MatrixOf.From<NodeId>((Array)value));
                    case BuiltInType.ExpandedNodeId:
                        return Variant.From(MatrixOf.From<ExpandedNodeId>((Array)value));
                    case BuiltInType.StatusCode:
                        return Variant.From(MatrixOf.From<StatusCode>((Array)value));
                    case BuiltInType.QualifiedName:
                        return Variant.From(MatrixOf.From<QualifiedName>((Array)value));
                    case BuiltInType.LocalizedText:
                        return Variant.From(MatrixOf.From<LocalizedText>((Array)value));
                    case BuiltInType.ExtensionObject:
                        return
                            PropertyInfo.PropertyType.GetElementType() == typeof(ExtensionObject) ?
                            Variant.From(MatrixOf.From<ExtensionObject>((Array)value)) :
                            Variant.FromStructure(MatrixOf.From<IEncodeable>((Array)value));
                    case BuiltInType.DataValue:
                        return Variant.From(MatrixOf.From<DataValue>((Array)value));
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        return Variant.From(MatrixOf.From<Variant>((Array)value));
                    default:
                        return default;
                }
            }
        }

        /// <summary>
        /// Set the value of a property.
        /// </summary>
        public void SetValue(object o, Variant v)
        {
            if (TypeInfo.IsScalar)
            {
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        PropertyInfo.SetValue(o, v.GetBoolean());
                        return;
                    case BuiltInType.SByte:
                        PropertyInfo.SetValue(o, v.GetSByte());
                        return;
                    case BuiltInType.Byte:
                        PropertyInfo.SetValue(o, v.GetByte());
                        return;
                    case BuiltInType.Int16:
                        PropertyInfo.SetValue(o, v.GetInt16());
                        return;
                    case BuiltInType.UInt16:
                        PropertyInfo.SetValue(o, v.GetUInt16());
                        return;
                    case BuiltInType.Int32:
                        PropertyInfo.SetValue(o, v.GetInt32());
                        return;
                    case BuiltInType.Enumeration:
                        PropertyInfo.SetValue(o, EnumHelper.Int32ToEnum(
                            v.GetInt32(),
                            PropertyInfo.PropertyType));
                        return;
                    case BuiltInType.UInt32:
                        PropertyInfo.SetValue(o, v.GetUInt32());
                        return;
                    case BuiltInType.Int64:
                        PropertyInfo.SetValue(o, v.GetInt64());
                        return;
                    case BuiltInType.UInt64:
                        PropertyInfo.SetValue(o, v.GetUInt64());
                        return;
                    case BuiltInType.Float:
                        PropertyInfo.SetValue(o, v.GetFloat());
                        return;
                    case BuiltInType.Double:
                        PropertyInfo.SetValue(o, v.GetDouble());
                        return;
                    case BuiltInType.String:
                        PropertyInfo.SetValue(o, v.GetString());
                        return;
                    case BuiltInType.DateTime:
                        PropertyInfo.SetValue(o, v.GetDateTime());
                        return;
                    case BuiltInType.Guid:
                        PropertyInfo.SetValue(o, v.GetGuid());
                        return;
                    case BuiltInType.ByteString:
                        PropertyInfo.SetValue(o, v.GetByteString());
                        return;
                    case BuiltInType.XmlElement:
                        PropertyInfo.SetValue(o, v.GetXmlElement());
                        return;
                    case BuiltInType.NodeId:
                        PropertyInfo.SetValue(o, v.GetNodeId());
                        return;
                    case BuiltInType.ExpandedNodeId:
                        PropertyInfo.SetValue(o, v.GetExpandedNodeId());
                        return;
                    case BuiltInType.StatusCode:
                        PropertyInfo.SetValue(o, v.GetStatusCode());
                        return;
                    case BuiltInType.QualifiedName:
                        PropertyInfo.SetValue(o, v.GetQualifiedName());
                        return;
                    case BuiltInType.LocalizedText:
                        PropertyInfo.SetValue(o, v.GetLocalizedText());
                        return;
                    case BuiltInType.ExtensionObject:
                        PropertyInfo.SetValue(o,
                            PropertyInfo.PropertyType == typeof(ExtensionObject) ?
                                v.GetExtensionObject() :
                                v.GetStructure<IEncodeable>());
                        return;
                    case BuiltInType.DataValue:
                        PropertyInfo.SetValue(o, v.GetDataValue());
                        return;
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        PropertyInfo.SetValue(o, v);
                        return;
                    case BuiltInType.DiagnosticInfo:
                        break;
                }
            }
            else if (TypeInfo.IsArray)
            {
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        PropertyInfo.SetValue(o, v.GetBooleanArray().ToArray());
                        return;
                    case BuiltInType.SByte:
                        PropertyInfo.SetValue(o, v.GetSByteArray().ToArray());
                        return;
                    case BuiltInType.Byte:
                        PropertyInfo.SetValue(o, v.GetByteArray().ToArray());
                        return;
                    case BuiltInType.Int16:
                        PropertyInfo.SetValue(o, v.GetInt16Array().ToArray());
                        return;
                    case BuiltInType.UInt16:
                        PropertyInfo.SetValue(o, v.GetUInt16Array().ToArray());
                        return;
                    case BuiltInType.Int32:
                        PropertyInfo.SetValue(o, v.GetInt32Array().ToArray());
                        return;
                    case BuiltInType.Enumeration:
                        PropertyInfo.SetValue(o, EnumHelper.Int32ArrayToEnumArray(
                            v.GetInt32Array(),
                            PropertyInfo.PropertyType.GetElementType()));
                        return;
                    case BuiltInType.UInt32:
                        PropertyInfo.SetValue(o, v.GetUInt32Array().ToArray());
                        return;
                    case BuiltInType.Int64:
                        PropertyInfo.SetValue(o, v.GetInt64Array().ToArray());
                        return;
                    case BuiltInType.UInt64:
                        PropertyInfo.SetValue(o, v.GetUInt64Array().ToArray());
                        return;
                    case BuiltInType.Float:
                        PropertyInfo.SetValue(o, v.GetFloatArray().ToArray());
                        return;
                    case BuiltInType.Double:
                        PropertyInfo.SetValue(o, v.GetDoubleArray().ToArray());
                        return;
                    case BuiltInType.String:
                        PropertyInfo.SetValue(o, v.GetStringArray().ToArray());
                        return;
                    case BuiltInType.DateTime:
                        PropertyInfo.SetValue(o, v.GetDateTimeArray().ToArray());
                        return;
                    case BuiltInType.Guid:
                        PropertyInfo.SetValue(o, v.GetGuidArray().ToArray());
                        return;
                    case BuiltInType.ByteString:
                        PropertyInfo.SetValue(o, v.GetByteStringArray().ToArray());
                        return;
                    case BuiltInType.XmlElement:
                        PropertyInfo.SetValue(o, v.GetXmlElementArray().ToArray());
                        return;
                    case BuiltInType.NodeId:
                        PropertyInfo.SetValue(o, v.GetNodeIdArray().ToArray());
                        return;
                    case BuiltInType.ExpandedNodeId:
                        PropertyInfo.SetValue(o, v.GetExpandedNodeIdArray().ToArray());
                        return;
                    case BuiltInType.StatusCode:
                        PropertyInfo.SetValue(o, v.GetStatusCodeArray().ToArray());
                        return;
                    case BuiltInType.QualifiedName:
                        PropertyInfo.SetValue(o, v.GetQualifiedNameArray().ToArray());
                        return;
                    case BuiltInType.LocalizedText:
                        PropertyInfo.SetValue(o, v.GetLocalizedTextArray().ToArray());
                        return;
                    case BuiltInType.ExtensionObject:
                        PropertyInfo.SetValue(o,
                            PropertyInfo.PropertyType.GetElementType() == typeof(ExtensionObject) ?
                                v.GetExtensionObjectArray().ToArray() :
                                v.GetStructureArray<IEncodeable>().ToArray());
                        return;
                    case BuiltInType.DataValue:
                        PropertyInfo.SetValue(o, v.GetDataValueArray().ToArray());
                        return;
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        PropertyInfo.SetValue(o, v.GetVariantArray().ToArray());
                        return;
                    case BuiltInType.DiagnosticInfo:
                        break;
                }
            }
            else
            {
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        PropertyInfo.SetValue(o, v.GetBooleanMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.SByte:
                        PropertyInfo.SetValue(o, v.GetSByteMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Byte:
                        PropertyInfo.SetValue(o, v.GetByteMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Int16:
                        PropertyInfo.SetValue(o, v.GetInt16Matrix().CreateArrayInstance());
                        return;
                    case BuiltInType.UInt16:
                        PropertyInfo.SetValue(o, v.GetUInt16Matrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Int32:
                        PropertyInfo.SetValue(o, v.GetInt32Matrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Enumeration:
                        PropertyInfo.SetValue(o, EnumHelper.Int32MatrixToEnumArray(
                           v.GetInt32Matrix(),
                           PropertyInfo.PropertyType.GetElementType()));
                        return;
                    case BuiltInType.UInt32:
                        PropertyInfo.SetValue(o, v.GetUInt32Matrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Int64:
                        PropertyInfo.SetValue(o, v.GetInt64Matrix().CreateArrayInstance());
                        return;
                    case BuiltInType.UInt64:
                        PropertyInfo.SetValue(o, v.GetUInt64Matrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Float:
                        PropertyInfo.SetValue(o, v.GetFloatMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Double:
                        PropertyInfo.SetValue(o, v.GetDoubleMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.String:
                        PropertyInfo.SetValue(o, v.GetStringMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.DateTime:
                        PropertyInfo.SetValue(o, v.GetDateTimeMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Guid:
                        PropertyInfo.SetValue(o, v.GetGuidMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.ByteString:
                        PropertyInfo.SetValue(o, v.GetByteStringMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.XmlElement:
                        PropertyInfo.SetValue(o, v.GetXmlElementMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.NodeId:
                        PropertyInfo.SetValue(o, v.GetNodeIdMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.ExpandedNodeId:
                        PropertyInfo.SetValue(o, v.GetExpandedNodeIdMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.StatusCode:
                        PropertyInfo.SetValue(o, v.GetStatusCodeMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.QualifiedName:
                        PropertyInfo.SetValue(o, v.GetQualifiedNameMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.LocalizedText:
                        PropertyInfo.SetValue(o, v.GetLocalizedTextMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.ExtensionObject:
                        PropertyInfo.SetValue(o,
                            PropertyInfo.PropertyType.GetElementType() == typeof(ExtensionObject) ?
                                v.GetExtensionObjectMatrix().CreateArrayInstance() :
                                v.GetStructureMatrix<IEncodeable>().CreateArrayInstance());
                        return;
                    case BuiltInType.DataValue:
                        PropertyInfo.SetValue(o, v.GetDataValueMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Variant:
                        PropertyInfo.SetValue(o, v.GetVariantMatrix().CreateArrayInstance());
                        return;
                    case BuiltInType.DiagnosticInfo:
                        break;
                }
            }
        }

        /// <inheritdoc cref="PropertyInfo.PropertyType"/>
        public Type PropertyType => PropertyInfo.PropertyType;

        /// <inheritdoc cref="StructureFieldAttribute.IsOptional"/>
        public bool IsOptional => FieldAttribute.IsOptional;

        /// <inheritdoc cref="StructureFieldAttribute.ValueRank"/>
        public int ValueRank => FieldAttribute.ValueRank;

        /// <inheritdoc cref="StructureFieldAttribute.BuiltInType"/>
        public BuiltInType BuiltInType => (BuiltInType)FieldAttribute.BuiltInType;

        /// <inheritdoc cref="DataMemberAttribute.Order"/>
        public int Order => DataAttribute.Order;

        /// <summary>
        /// Optional mask for the field in the property.
        /// </summary>
        public uint OptionalFieldMask { get; set; }

        /// <summary>
        /// Complex type identifier if field is a structure
        /// </summary>
        public ExpandedNodeId GetDataTypeId(NamespaceTable namespaceTable)
        {
            Type type = PropertyInfo.PropertyType.IsArray ?
                PropertyInfo.PropertyType.GetElementType() :
                PropertyInfo.PropertyType;
            StructureTypeIdAttribute typeAttribute = type
                .GetCustomAttribute<StructureTypeIdAttribute>();
            if (typeAttribute != null)
            {
                return ExpandedNodeId.Parse(typeAttribute.ComplexTypeId);
            }
            return TypeInfo.GetDataTypeId(type, namespaceTable);
        }
    }
}
