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
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Opc.Ua.Types;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Variant helper methods
    /// </summary>
    public static class VariantHelper
    {
        /// <summary>
        /// Try cast to type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static bool TryCastTo<T>(this Variant value, out T result)
        {
            if (value.IsNull)
            {
                // Use default either null or default value of value type
                result = default;
                return true;
            }
            switch (typeof(T))
            {
                // Cannot handle multi dim without reflection.
                case Type t when t == typeof(Variant):
                    result = AsT(value);
                    break;
                case Type t when t.IsEnum:
                    result = AsT(value.GetInt32());
                    break;
                case Type t when t == typeof(bool):
                    result = AsT(value.GetBoolean());
                    break;
                case Type t when t == typeof(byte):
                    result = AsT(value.GetByte());
                    break;
                case Type t when t == typeof(sbyte):
                    result = AsT(value.GetSByte());
                    break;
                case Type t when t == typeof(ushort):
                    result = AsT(value.GetUInt16());
                    break;
                case Type t when t == typeof(short):
                    result = AsT(value.GetInt16());
                    break;
                case Type t when t == typeof(uint):
                    result = AsT(value.GetUInt32());
                    break;
                case Type t when t == typeof(int):
                    result = AsT(value.GetInt32());
                    break;
                case Type t when t == typeof(ulong):
                    result = AsT(value.GetUInt64());
                    break;
                case Type t when t == typeof(long):
                    result = AsT(value.GetInt64());
                    break;
                case Type t when t == typeof(double):
                    result = AsT(value.GetDouble());
                    break;
                case Type t when t == typeof(float):
                    result = AsT(value.GetFloat());
                    break;
                case Type t when t == typeof(string):
                    result = AsRefT(value.GetString());
                    break;
                case Type t when t == typeof(DateTime):
                    result = AsT(value.GetDateTime());
                    break;
                case Type t when t == typeof(Guid):
                    result = AsT<Uuid>(value.GetGuid().Guid);
                    break;
                case Type t when t == typeof(Uuid):
                    result = AsT(value.GetGuid());
                    break;
                case Type t when t == typeof(ByteString):
                    result = AsT(value.GetByteString());
                    break;
                case Type t when t == typeof(XmlElement):
                    result = AsT(value.GetXmlElement());
                    break;
                case Type t when t == typeof(NodeId):
                    result = AsT(value.GetNodeId());
                    break;
                case Type t when t == typeof(ExpandedNodeId):
                    result = AsT(value.GetExpandedNodeId());
                    break;
                case Type t when t == typeof(LocalizedText):
                    result = AsT(value.GetLocalizedText());
                    break;
                case Type t when t == typeof(QualifiedName):
                    result = AsT(value.GetQualifiedName());
                    break;
                case Type t when t == typeof(StatusCode):
                    result = AsT(value.GetStatusCode());
                    break;
                case Type t when t == typeof(DataValue):
                    result = AsRefT(value.GetDataValue());
                    break;
                case Type t when t == typeof(ExtensionObject):
                    result = AsT(value.GetExtensionObject());
                    break;
                case Type t when typeof(IEncodeable).IsAssignableFrom(t):
                    if (!value.GetExtensionObject()
                        .TryGetEncodeable(out IEncodeable encodeable))
                    {
                        result = default;
                        return false;
                    }
                    result = (T)(object)encodeable;
                    break;
                case Type t when t == typeof(ArrayOf<bool>):
                    result = AsT(value.GetBooleanArray());
                    break;
                case Type t when t == typeof(ArrayOf<sbyte>):
                    result = AsT(value.GetSByteArray());
                    break;
                case Type t when t == typeof(ArrayOf<byte>):
                    result = AsT(value.GetByteArray());
                    break;
                case Type t when t == typeof(ArrayOf<short>):
                    result = AsT(value.GetInt16Array());
                    break;
                case Type t when t == typeof(ArrayOf<ushort>):
                    result = AsT(value.GetUInt16Array());
                    break;
                case Type t when t == typeof(ArrayOf<int>):
                    result = AsT(value.GetInt32Array());
                    break;
                case Type t when t == typeof(ArrayOf<uint>):
                    result = AsT(value.GetUInt32Array());
                    break;
                case Type t when t == typeof(ArrayOf<long>):
                    result = AsT(value.GetInt64Array());
                    break;
                case Type t when t == typeof(ArrayOf<ulong>):
                    result = AsT(value.GetUInt64Array());
                    break;
                case Type t when t == typeof(ArrayOf<float>):
                    result = AsT(value.GetFloatArray());
                    break;
                case Type t when t == typeof(ArrayOf<double>):
                    result = AsT(value.GetDoubleArray());
                    break;
                case Type t when t == typeof(ArrayOf<string>):
                    result = AsT(value.GetStringArray());
                    break;
                case Type t when t == typeof(ArrayOf<DateTime>):
                    result = AsT(value.GetDateTimeArray());
                    break;
                case Type t when t == typeof(ArrayOf<Guid>):
                    result = AsT(value.GetGuidArray().ConvertAll(g => g.Guid));
                    break;
                case Type t when t == typeof(ArrayOf<Uuid>):
                    result = AsT(value.GetGuidArray());
                    break;
                case Type t when t == typeof(ArrayOf<ByteString>):
                    result = AsT(value.GetByteStringArray());
                    break;
                case Type t when t == typeof(ArrayOf<XmlElement>):
                    result = AsT(value.GetXmlElementArray());
                    break;
                case Type t when t == typeof(ArrayOf<NodeId>):
                    result = AsT(value.GetNodeIdArray());
                    break;
                case Type t when t == typeof(ArrayOf<ExpandedNodeId>):
                    result = AsT(value.GetExpandedNodeIdArray());
                    break;
                case Type t when t == typeof(ArrayOf<LocalizedText>):
                    result = AsT(value.GetLocalizedTextArray());
                    break;
                case Type t when t == typeof(ArrayOf<QualifiedName>):
                    result = AsT(value.GetQualifiedNameArray());
                    break;
                case Type t when t == typeof(ArrayOf<StatusCode>):
                    result = AsT(value.GetStatusCodeArray());
                    break;
                case Type t when t == typeof(ArrayOf<DataValue>):
                    result = AsT(value.GetDataValueArray());
                    break;
                case Type t when t == typeof(ArrayOf<Variant>):
                    result = AsT(value.GetVariantArray());
                    break;
                case Type t when t == typeof(ArrayOf<ExtensionObject>):
                    result = AsT(value.GetExtensionObjectArray());
                    break;
                case Type t when t == typeof(MatrixOf<bool>):
                    result = AsT(value.GetBooleanMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<sbyte>):
                    result = AsT(value.GetSByteMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<byte>):
                    result = AsT(value.GetByteMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<short>):
                    result = AsT(value.GetInt16Matrix());
                    break;
                case Type t when t == typeof(MatrixOf<ushort>):
                    result = AsT(value.GetUInt16Matrix());
                    break;
                case Type t when t == typeof(MatrixOf<int>):
                    result = AsT(value.GetInt32Matrix());
                    break;
                case Type t when t == typeof(MatrixOf<uint>):
                    result = AsT(value.GetUInt32Matrix());
                    break;
                case Type t when t == typeof(MatrixOf<long>):
                    result = AsT(value.GetInt64Matrix());
                    break;
                case Type t when t == typeof(MatrixOf<ulong>):
                    result = AsT(value.GetUInt64Matrix());
                    break;
                case Type t when t == typeof(MatrixOf<float>):
                    result = AsT(value.GetFloatMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<double>):
                    result = AsT(value.GetDoubleMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<string>):
                    result = AsT(value.GetStringMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<DateTime>):
                    result = AsT(value.GetDateTimeMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<Guid>):
                    result = AsT(value.GetGuidMatrix().ConvertAll(g => g.Guid));
                    break;
                case Type t when t == typeof(MatrixOf<Uuid>):
                    result = AsT(value.GetGuidMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<ByteString>):
                    result = AsT(value.GetByteStringMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<XmlElement>):
                    result = AsT(value.GetXmlElementMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<NodeId>):
                    result = AsT(value.GetNodeIdMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<ExpandedNodeId>):
                    result = AsT(value.GetExpandedNodeIdMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<LocalizedText>):
                    result = AsT(value.GetLocalizedTextMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<QualifiedName>):
                    result = AsT(value.GetQualifiedNameMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<StatusCode>):
                    result = AsT(value.GetStatusCodeMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<DataValue>):
                    result = AsT(value.GetDataValueMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<Variant>):
                    result = AsT(value.GetVariantMatrix());
                    break;
                case Type t when t == typeof(MatrixOf<ExtensionObject>):
                    result = AsT(value.GetExtensionObjectMatrix());
                    break;
                case Type t when t == typeof(bool[]):
                    result = AsRefT(value.GetBooleanArray().ToArray());
                    break;
                case Type t when t == typeof(byte[]):
                    result = AsRefT(value.GetByteArray().ToArray());
                    break;
                case Type t when t == typeof(sbyte[]):
                    result = AsRefT(value.GetSByteArray().ToArray());
                    break;
                case Type t when t == typeof(ushort[]):
                    result = AsRefT(value.GetUInt16Array().ToArray());
                    break;
                case Type t when t == typeof(short[]):
                    result = AsRefT(value.GetInt16Array().ToArray());
                    break;
                case Type t when t == typeof(uint[]):
                    result = AsRefT(value.GetUInt32Array().ToArray());
                    break;
                case Type t when t == typeof(int[]):
                    result = AsRefT(value.GetInt32Array().ToArray());
                    break;
                case Type t when t == typeof(ulong[]):
                    result = AsRefT(value.GetUInt64Array().ToArray());
                    break;
                case Type t when t == typeof(long[]):
                    result = AsRefT(value.GetInt64Array().ToArray());
                    break;
                case Type t when t == typeof(double[]):
                    result = AsRefT(value.GetDoubleArray().ToArray());
                    break;
                case Type t when t == typeof(float[]):
                    result = AsRefT(value.GetFloatArray().ToArray());
                    break;
                case Type t when t == typeof(string[]):
                    result = AsRefT(value.GetStringArray().ToArray());
                    break;
                case Type t when t == typeof(DateTime[]):
                    result = AsRefT(value.GetDateTimeArray().ToArray());
                    break;
                case Type t when t == typeof(Uuid[]):
                    result = AsRefT(value.GetGuidArray().ToArray());
                    break;
                case Type t when t == typeof(ByteString[]):
                    result = AsRefT(value.GetByteStringArray().ToArray());
                    break;
                case Type t when t == typeof(XmlElement[]):
                    result = AsRefT(value.GetXmlElementArray().ToArray());
                    break;
                case Type t when t == typeof(NodeId[]):
                    result = AsRefT(value.GetNodeIdArray().ToArray());
                    break;
                case Type t when t == typeof(ExpandedNodeId[]):
                    result = AsRefT(value.GetExpandedNodeIdArray().ToArray());
                    break;
                case Type t when t == typeof(LocalizedText[]):
                    result = AsRefT(value.GetLocalizedTextArray().ToArray());
                    break;
                case Type t when t == typeof(QualifiedName[]):
                    result = AsRefT(value.GetQualifiedNameArray().ToArray());
                    break;
                case Type t when t == typeof(StatusCode[]):
                    result = AsRefT(value.GetStatusCodeArray().ToArray());
                    break;
                case Type t when t == typeof(DataValue[]):
                    result = AsRefT(value.GetDataValueArray().ToArray());
                    break;
                case Type t when t == typeof(Variant[]):
                    result = AsRefT(value.GetVariantArray().ToArray());
                    break;
                case Type t when t == typeof(ExtensionObject[]):
                    result = AsRefT(value.GetExtensionObjectArray().ToArray());
                    break;
                case Type t when t == typeof(Guid[]):
                    result = AsRefT(value.GetGuidArray().ConvertAll(g => g.Guid).ToArray());
                    break;
                case Type t when typeof(IEncodeable[]).IsAssignableFrom(t):
                    ArrayOf<ExtensionObject> extensionObjects = value.GetExtensionObjectArray();
                    var encodeables = new IEncodeable[extensionObjects.Count];
                    for (int ii = 0; ii < encodeables.Length; ii++)
                    {
                        if (!extensionObjects[ii].TryGetEncodeable(out encodeables[ii]))
                        {
                            result = default;
                            return false;
                        }
                    }
                    result = AsRefT(encodeables);
                    break;
                case Type t when t.IsArray && t.GetArrayRank() == 1 && t.GetElementType().IsEnum:
                    ArrayOf<int> enumValues = value.GetInt32Array();
                    result = AsRefT(enumValues.ToArray());
                    break;
                default:
                    result = default;
                    return false;
            }
            return true;

            // Helper to treat U as T which are the same
            static T AsRefT<U>(U value) where U : class
            {
                // Debug.Assert(typeof(U) == typeof(T));
                return (T)(object)value;
            }

            // Helper to treat U as T which are the same
            static T AsT<U>(U value) where U : struct
            {
                Debug.Assert(typeof(U) == typeof(T));
#if NET8_0_OR_GREATER
                Debug.Assert(Unsafe.SizeOf<T>() == Unsafe.SizeOf<U>());
                return Unsafe.As<U, T>(ref value);
#else
                return (T)(object)value;
#endif
            }
        }

        /// <summary>
        /// Casts the variant to a <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type to cast to.</typeparam>
        /// <param name="value">The variant.</param>
        /// <param name="throwOnError"></param>
        /// <exception cref="ServiceResultException"></exception>
        public static T CastTo<T>(this Variant value, bool throwOnError = true)
        {
            if (!TryCastTo(value, out T result) && throwOnError)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Cannot cast '{0}' to a '{1}' type.",
                    value,
                    typeof(T).Name);
            }
            return result;
        }

        /// <summary>
        /// Convert with reflection fallback and throws if it cannot
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        public static Variant CastFromWithReflectionFallback<T>(T value)
        {
            if (!TryCastFromWithReflectionFallback(value, out Variant variant))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Failed to cast type '{0}' to Variant (incl via reflection).",
                    value?.GetType().FullName);
            }
            return variant;
        }

        /// <summary>
        /// Try cast to variant with reflection fallback
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="variant"></param>
        /// <returns></returns>
        public static bool TryCastFromWithReflectionFallback<T>(
            T value,
            out Variant variant)
        {
            if (TryCastFrom(value, out variant))
            {
                return true;
            }
            return TryCastFromWithReflection(value, out variant);
        }

        /// <summary>
        /// Convert and throws if it cannot
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        public static Variant CastFrom<T>(T value)
        {
            if (!TryCastFrom(value, out Variant variant))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Failed to cast type '{0}' to Variant.",
                    value?.GetType().FullName);
            }
            return variant;
        }

        /// <summary>
        /// Try convert object to variant
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <exception cref="ServiceResultException"></exception>
        public static bool TryCastFrom<T>(T value, out Variant variant)
        {
            if (value is ICloneable clonable)
            {
                value = (T)clonable.Clone();
            }
            switch (value)
            {
                case null:
                    variant = Variant.Null;
                    break;
                // Cannot handle multi dim without reflection.
                case Array o when o.Rank > 1:
                    variant = default;
                    return false;
                case Enum o:
                    variant = Variant.FromEnumeration(o, typeof(T));
                    break;
                case Variant o:
                    variant = o;
                    break;
                case bool o:
                    variant = Variant.From(o);
                    break;
                case byte o:
                    variant = Variant.From(o);
                    break;
                case sbyte o:
                    variant = Variant.From(o);
                    break;
                case ushort o:
                    variant = Variant.From(o);
                    break;
                case short o:
                    variant = Variant.From(o);
                    break;
                case uint o:
                    variant = Variant.From(o);
                    break;
                case int o:
                    variant = Variant.From(o);
                    break;
                case ulong o:
                    variant = Variant.From(o);
                    break;
                case long o:
                    variant = Variant.From(o);
                    break;
                case double o:
                    variant = Variant.From(o);
                    break;
                case float o:
                    variant = Variant.From(o);
                    break;
                case string o:
                    variant = Variant.From(o);
                    break;
                case DateTime o:
                    variant = Variant.From(o);
                    break;
                case Guid o:
                    variant = Variant.From(new Uuid(o));
                    break;
                case Uuid o:
                    variant = Variant.From(o);
                    break;
                case ByteString o:
                    variant = Variant.From(o);
                    break;
                case XmlElement o:
                    variant = Variant.From(o);
                    break;
                case NodeId o:
                    variant = Variant.From(o);
                    break;
                case ExpandedNodeId o:
                    variant = Variant.From(o);
                    break;
                case LocalizedText o:
                    variant = Variant.From(o);
                    break;
                case QualifiedName o:
                    variant = Variant.From(o);
                    break;
                case StatusCode o:
                    variant = Variant.From(o);
                    break;
                case DataValue o:
                    variant = Variant.From(o);
                    break;
                case ExtensionObject o:
                    variant = Variant.From(o);
                    break;
                case IEncodeable o:
                    variant = Variant.From(new ExtensionObject(o, true));
                    break;
                case ArrayOf<bool> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<sbyte> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<byte> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<short> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<ushort> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<int> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<uint> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<long> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<ulong> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<float> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<double> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<string> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<DateTime> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<Guid> o:
                    variant = Variant.From(o.ConvertAll(g => new Uuid(g)));
                    break;
                case ArrayOf<Uuid> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<ByteString> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<XmlElement> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<NodeId> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<ExpandedNodeId> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<LocalizedText> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<QualifiedName> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<StatusCode> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<DataValue> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<Variant> o:
                    variant = Variant.From(o);
                    break;
                case ArrayOf<ExtensionObject> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<bool> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<sbyte> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<byte> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<short> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<ushort> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<int> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<uint> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<long> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<ulong> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<float> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<double> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<string> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<DateTime> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<Guid> o:
                    variant = Variant.From(o.ConvertAll(g => new Uuid(g)));
                    break;
                case MatrixOf<Uuid> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<ByteString> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<XmlElement> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<NodeId> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<ExpandedNodeId> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<LocalizedText> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<QualifiedName> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<StatusCode> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<DataValue> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<Variant> o:
                    variant = Variant.From(o);
                    break;
                case MatrixOf<ExtensionObject> o:
                    variant = Variant.From(o);
                    break;
                case Enum[] o:
                    variant = FromEnumerations(o);
                    break;
                case bool[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case byte[] o when o.GetType() == typeof(byte[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case sbyte[] o when o.GetType() == typeof(sbyte[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case ushort[] o when o.GetType() == typeof(ushort[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case short[] o when o.GetType() == typeof(short[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case uint[] o when o.GetType() == typeof(uint[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case int[] o when o.GetType() == typeof(int[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case ulong[] o when o.GetType() == typeof(ulong[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case long[] o when o.GetType() == typeof(long[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case double[] o when o.GetType() == typeof(double[]):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case float[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case string[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case DateTime[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case Guid[] o:
                    variant = Variant.From(o.ToArrayOf(g => new Uuid(g)));
                    break;
                case Uuid[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case ByteString[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case XmlElement[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case NodeId[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case ExpandedNodeId[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case LocalizedText[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case QualifiedName[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case StatusCode[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case DataValue[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case Variant[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case ExtensionObject[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEncodeable[] o:
                    variant = Variant.From(o.ToArrayOf(o => new ExtensionObject(o)));
                    break;
                case object[] o:
                    variant = FromObjects(o);
                    break;
                case IEnumerable<Enum> o:
                    variant = FromEnumerations(o);
                    break;
                case IEnumerable<bool> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<byte> o when CheckType<byte>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<sbyte> o when CheckType<sbyte>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<ushort> o when CheckType<ushort>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<short> o when CheckType<short>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<uint> o when CheckType<uint>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<int> o when CheckType<int>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<ulong> o when CheckType<ulong>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<long> o when CheckType<long>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<double> o when CheckType<double>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<float> o when CheckType<float>(o):
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<string> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<DateTime> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<Guid> o:
                    variant = Variant.From(o.ToArrayOf(g => new Uuid(g)));
                    break;
                case IEnumerable<Uuid> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<ByteString> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<XmlElement> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<NodeId> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<ExpandedNodeId> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<LocalizedText> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<QualifiedName> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<StatusCode> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<DataValue> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<Variant> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<ExtensionObject> o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case IEnumerable<IEncodeable> o:
                    variant = Variant.From(o.ToArrayOf(o => new ExtensionObject(o)));
                    break;
                case IEnumerable<object> o:
                    variant = FromObjects(o);
                    break;
                case Matrix o:
                    variant = TryCastFrom(o, out Variant v) ? v : Variant.Null;
                    break;
                default:
                    // cannot handle such type
                    variant = default;
                    return false;
            }

            // Check the pattern match is not against a covariant
            static bool CheckType<TArg>(object o) =>
                o.GetType().GetGenericArguments()[0] == typeof(TArg);
            return true;
        }

        /// <summary>
        /// Cast old style matrix to variant
        /// </summary>
        /// <param name="matrix"></param>
        /// <param name="variant"></param>
        /// <returns></returns>
        public static bool TryCastFrom(Matrix matrix, out Variant variant)
        {
            switch (matrix.TypeInfo.BuiltInType)
            {
                case BuiltInType.Null:
                    variant = Variant.Null;
                    break;
                case BuiltInType.Boolean:
                    variant = ((bool[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.SByte:
                    variant = ((sbyte[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Byte:
                    variant = ((byte[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Int16:
                    variant = ((short[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.UInt16:
                    variant = ((ushort[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Int32:
                case BuiltInType.Enumeration:
                    variant = ((int[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.UInt32:
                    variant = ((uint[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Int64:
                    variant = ((long[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.UInt64:
                    variant = ((ulong[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Float:
                    variant = ((float[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Double:
                    variant = ((double[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.String:
                    variant = ((string[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.DateTime:
                    variant = ((DateTime[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Guid:
                    variant = ((Uuid[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.ByteString:
                    variant = ((ByteString[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.XmlElement:
                    variant = ((XmlElement[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.NodeId:
                    variant = ((NodeId[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.ExpandedNodeId:
                    variant = ((ExpandedNodeId[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.StatusCode:
                    variant = ((StatusCode[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.QualifiedName:
                    variant = ((QualifiedName[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.LocalizedText:
                    variant = ((LocalizedText[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.ExtensionObject:
                    variant = ((ExtensionObject[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.DataValue:
                    variant = ((DataValue[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                case BuiltInType.Variant:
                case BuiltInType.Number:
                case BuiltInType.Integer:
                case BuiltInType.UInteger:
                    variant = ((Variant[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
                    break;
                default:
                    variant = default;
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Use reflection to cast
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static bool TryCastFromWithReflection<T>(T value, out Variant variant)
        {
            // Convert from ArrayOf<T> where T : IEncodeable or T : Enum
            Type valueType = value.GetType();
            if (valueType.IsGenericType &&
                valueType.GetGenericTypeDefinition() == typeof(ArrayOf<>))
            {
                Type genericArg = valueType.GetGenericArguments()[0];
                bool isEncodeable = typeof(IEncodeable).IsAssignableFrom(genericArg);
                try
                {
                    MethodInfo variantFrom = typeof(VariantHelper).GetMethod(
                        isEncodeable ?
                            nameof(FromStructure) :
                            nameof(FromEnumeration),
                        BindingFlags.Static | BindingFlags.NonPublic);
                    variant = (Variant)variantFrom.MakeGenericMethod(genericArg)
                        .Invoke(null, [value]);
                    return !variant.IsNull;
                }
                catch
                {
                    variant = default;
                    return false;
                }
            }

            // All scalar types, typed arrays, matrices and lists are covered
            // so this must be a multi dimensional array. Use reflection to
            // bind the element type to the Create method in MatrixOf helper.
            // We do not multi dim structure
            if (value is not Array array)
            {
                variant = default;
                return false;
            }

            Type elementType = valueType.GetElementType();
            if (TypeInfo.Construct(elementType).IsUnknown)
            {
                variant = default;
                return false;
            }
            try
            {
                MethodInfo matrixFromArray = typeof(MatrixOf).GetMethod(
                    nameof(MatrixOf.From),
                    BindingFlags.Static | BindingFlags.Public)
                    .MakeGenericMethod([elementType]);
                Type matrixType = typeof(MatrixOf<>)
                    .MakeGenericType(elementType);
                MethodInfo variantFromMatrix = typeof(Variant).GetMethod(
                    nameof(Variant.From),
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    [matrixType],
                    null);
                variant = (Variant)variantFromMatrix.Invoke(null,
                    [matrixFromArray.Invoke(null, [array])]);
                return !variant.IsNull;
            }
            catch
            {
                variant = default;
                return false;
            }
        }

        private static Variant FromObjects(IEnumerable<object> values)
        {
            var variants = new List<Variant>();
            foreach (object value in values)
            {
                if (!TryCastFrom(value, out Variant variant))
                {
                    return Variant.Null;
                }
                variants.Add(variant);
            }
            return Variant.Collapse(variants.ToArrayOf());
        }

        private static Variant FromEnumerations(IEnumerable<Enum> values)
        {
            var variants = new List<Variant>();
            foreach (Enum value in values)
            {
                variants.Add(Variant.FromEnumeration(value, value.GetType()));
            }
            return Variant.Collapse(variants.ToArrayOf());
        }

        private static Variant FromStructure<T>(ArrayOf<T> values)
            where T : IEncodeable
        {
            return Variant.FromStructure(values);
        }

        private static Variant FromEnumeration<T>(ArrayOf<T> values)
            where T : Enum
        {
            return Variant.From(values);
        }
    }
}
