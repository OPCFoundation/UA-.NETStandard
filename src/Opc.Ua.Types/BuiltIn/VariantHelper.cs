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
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Opc.Ua.Types;

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
                result = default!;
                return true;
            }
            switch (typeof(T))
            {
                // Cannot handle multi dim without reflection.
                case Type t when t == typeof(Variant):
                    result = AsT(value);
                    break;
                case Type t when t.IsEnum:
                {
                    // Reinterpreting the Int32 as T reads past the local
                    // for enums wider than 4 bytes; convert by value.
                    bool ok = value.TryGetValue(out int v);
                    result = EnumFromInt64(v);
                    return ok;
                }
                case Type t when t == typeof(bool):
                {
                    bool ok = value.TryGetValue(out bool v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(byte):
                {
                    bool ok = value.TryGetValue(out byte v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(sbyte):
                {
                    bool ok = value.TryGetValue(out sbyte v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ushort):
                {
                    bool ok = value.TryGetValue(out ushort v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(short):
                {
                    bool ok = value.TryGetValue(out short v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(uint):
                {
                    bool ok = value.TryGetValue(out uint v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(int):
                {
                    bool ok = value.TryGetValue(out int v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ulong):
                {
                    bool ok = value.TryGetValue(out ulong v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(long):
                {
                    bool ok = value.TryGetValue(out long v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(double):
                {
                    bool ok = value.TryGetValue(out double v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(float):
                {
                    bool ok = value.TryGetValue(out float v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(string):
                {
                    bool ok = value.TryGetValue(out string v);
                    result = AsRefT(v);
                    return ok;
                }
                case Type t when t == typeof(DateTimeUtc):
                {
                    bool ok = value.TryGetValue(out DateTimeUtc v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(DateTime):
                {
                    bool ok = value.TryGetValue(out DateTimeUtc v);
                    result = AsT((DateTime)v);
                    return ok;
                }
                case Type t when t == typeof(Guid):
                {
                    bool ok = value.TryGetValue(out Uuid v);
                    result = AsT(v.Guid);
                    return ok;
                }
                case Type t when t == typeof(Uuid):
                {
                    bool ok = value.TryGetValue(out Uuid v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ByteString):
                {
                    bool ok = value.TryGetValue(out ByteString v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(XmlElement):
                {
                    bool ok = value.TryGetValue(out XmlElement v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(NodeId):
                {
                    bool ok = value.TryGetValue(out NodeId v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ExpandedNodeId):
                {
                    bool ok = value.TryGetValue(out ExpandedNodeId v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(LocalizedText):
                {
                    bool ok = value.TryGetValue(out LocalizedText v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(QualifiedName):
                {
                    bool ok = value.TryGetValue(out QualifiedName v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(StatusCode):
                {
                    bool ok = value.TryGetValue(out StatusCode v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(DataValue):
                {
                    bool ok = value.TryGetValue(out DataValue v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ExtensionObject):
                {
                    bool ok = value.TryGetValue(out ExtensionObject v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when typeof(IEncodeable).IsAssignableFrom(t):
                    if (!value.TryGetValue(out ExtensionObject extensionObject) ||
                        !extensionObject.TryGetValue(out IEncodeable? encodeable) ||
                        encodeable is not T typedEncodeable)
                    {
                        result = default!;
                        return false;
                    }
                    result = typedEncodeable;
                    break;
                case Type t when t == typeof(ArrayOf<bool>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<bool> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<sbyte>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<sbyte> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<byte>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<byte> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<short>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<short> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<ushort>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ushort> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<int>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<int> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<uint>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<uint> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<long>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<long> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<ulong>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ulong> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<float>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<float> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<double>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<double> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<string>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<string> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<DateTimeUtc>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<DateTimeUtc> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<DateTime>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<DateTimeUtc> v);
                    result = AsT(v.ConvertAll(d => (DateTime)d));
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<Guid>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<Uuid> v);
                    result = AsT(v.ConvertAll(g => g.Guid));
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<Uuid>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<Uuid> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<ByteString>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ByteString> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<XmlElement>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<XmlElement> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<NodeId>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<NodeId> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<ExpandedNodeId>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ExpandedNodeId> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<LocalizedText>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<LocalizedText> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<QualifiedName>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<QualifiedName> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<StatusCode>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<StatusCode> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<DataValue>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<DataValue> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<Variant>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<Variant> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(ArrayOf<ExtensionObject>):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ExtensionObject> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<bool>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<bool> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<sbyte>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<sbyte> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<byte>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<byte> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<short>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<short> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<ushort>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<ushort> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<int>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<int> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<uint>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<uint> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<long>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<long> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<ulong>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<ulong> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<float>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<float> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<double>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<double> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<string>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<string> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<DateTimeUtc>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<DateTimeUtc> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<Guid>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<Uuid> v);
                    result = AsT(v.ConvertAll(g => g.Guid));
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<Uuid>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<Uuid> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<ByteString>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<ByteString> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<XmlElement>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<XmlElement> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<NodeId>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<NodeId> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<ExpandedNodeId>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<ExpandedNodeId> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<LocalizedText>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<LocalizedText> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<QualifiedName>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<QualifiedName> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<StatusCode>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<StatusCode> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<DataValue>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<DataValue> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<Variant>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<Variant> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(MatrixOf<ExtensionObject>):
                {
                    bool ok = value.TryGetValue(out MatrixOf<ExtensionObject> v);
                    result = AsT(v);
                    return ok;
                }
                case Type t when t == typeof(bool[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<bool> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(byte[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<byte> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(sbyte[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<sbyte> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(ushort[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ushort> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(short[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<short> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(uint[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<uint> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(int[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<int> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(ulong[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ulong> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(long[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<long> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(double[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<double> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(float[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<float> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(string[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<string> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(DateTimeUtc[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<DateTimeUtc> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(Uuid[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<Uuid> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(ByteString[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ByteString> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(XmlElement[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<XmlElement> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(NodeId[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<NodeId> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(ExpandedNodeId[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ExpandedNodeId> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(LocalizedText[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<LocalizedText> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(QualifiedName[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<QualifiedName> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(StatusCode[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<StatusCode> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(DataValue[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<DataValue> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(Variant[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<Variant> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(ExtensionObject[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<ExtensionObject> v);
                    result = AsRefT(v.ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(DateTime[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<DateTimeUtc> v);
                    result = AsRefT(v.ConvertAll(d => (DateTime)d).ToArray()!);
                    return ok;
                }
                case Type t when t == typeof(Guid[]):
                {
                    bool ok = value.TryGetValue(out ArrayOf<Uuid> v);
                    result = AsRefT(v.ConvertAll(g => g.Guid).ToArray()!);
                    return ok;
                }
                case Type t when typeof(IEncodeable[]).IsAssignableFrom(t):
                    if (!value.TryGetValue(out ArrayOf<ExtensionObject> extensionObjects))
                    {
                        result = default!;
                        return false;
                    }
                    // Allocate an array of T's element type (e.g. Argument[]),
                    // an IEncodeable[] cannot be cast to a derived array type.
                    Type encodeableType = t.GetElementType()!;
                    var encodeables = Array.CreateInstance(
                        encodeableType,
                        extensionObjects.Count);
                    for (int ii = 0; ii < encodeables.Length; ii++)
                    {
                        if (!extensionObjects[ii].TryGetValue(out IEncodeable? e) ||
                            !encodeableType.IsInstanceOfType(e))
                        {
                            result = default!;
                            return false;
                        }
                        encodeables.SetValue(e, ii);
                    }
                    result = AsRefT(encodeables);
                    break;
                case Type t when t.IsArray &&
                    t.GetArrayRank() == 1 &&
                    t.GetElementType() is Type et &&
                    et.IsEnum:
                    // An int[] can only be reinterpreted as an array of an
                    // enum whose underlying type is Int32.
                    if (Enum.GetUnderlyingType(et) != typeof(int) ||
                        !value.TryGetValue(out ArrayOf<int> enumValues))
                    {
                        result = default!;
                        return false;
                    }
                    result = AsRefT(enumValues.ToArray()!);
                    break;
                default:
                    result = default!;
                    return false;
            }
            return true;

            // Helper to treat U as T which are the same
            static T AsRefT<U>(U value) where U : class => (T)(object)value;

            // Helper to treat U as T which are the same
            static T AsT<U>(U value) where U : struct
            {
                Debug.Assert(typeof(U) == typeof(T));
                Debug.Assert(Unsafe.SizeOf<T>() == Unsafe.SizeOf<U>());
                return Unsafe.As<U, T>(ref value);
            }

            // Helper to convert to the enum T like Enum.ToObject without boxing:
            // keep the low bytes of the value that fit the underlying type.
            static T EnumFromInt64(long value)
            {
                switch (Unsafe.SizeOf<T>())
                {
                    case sizeof(byte):
                        byte b = unchecked((byte)value);
                        return Unsafe.As<byte, T>(ref b);
                    case sizeof(short):
                        short s = unchecked((short)value);
                        return Unsafe.As<short, T>(ref s);
                    case sizeof(int):
                        int i = unchecked((int)value);
                        return Unsafe.As<int, T>(ref i);
                    default:
                        return Unsafe.As<long, T>(ref value);
                }
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
        [RequiresDynamicCode("Uses reflection to cast types where not all may be available.")]
        [RequiresUnreferencedCode("Uses reflection to cast types where not all may be available.")]
        public static Variant CastFromWithReflectionFallback<T>(T value)
        {
            if (!TryCastFromWithReflectionFallback(value, out Variant variant))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Failed to cast type '{0}' to Variant (incl via reflection).",
                    value?.GetType().FullName ?? "null");
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
        [RequiresDynamicCode("Uses reflection to cast types where not all may be available.")]
        [RequiresUnreferencedCode("Uses reflection to cast types where not all may be available.")]
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
                    value?.GetType().FullName ?? "null");
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
                    variant = Variant.From(EnumValue.From(o, typeof(T)));
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
                case EnumValue o:
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
                case DateTimeUtc o:
                    variant = Variant.From(o);
                    break;
                case DateTime o:
                    variant = Variant.From((DateTimeUtc)o);
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
                case ArrayOf<EnumValue> o:
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
                case ArrayOf<DateTimeUtc> o:
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
                case MatrixOf<EnumValue> o:
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
                case MatrixOf<DateTimeUtc> o:
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
                    variant = FromEnums(o);
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
                case EnumValue[] o:
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
                case DateTimeUtc[] o:
                    variant = Variant.From(o.ToArrayOf());
                    break;
                case DateTime[] o:
                    variant = Variant.From(o.ToArrayOf(d => (DateTimeUtc)d));
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
                    variant = FromEnums(o);
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
                case IEnumerable<EnumValue> o:
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
                case IEnumerable<DateTimeUtc> o:
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
                    variant = ((DateTimeUtc[])matrix.Elements).ToMatrixOf(matrix.Dimensions);
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
        [RequiresDynamicCode("Uses reflection to cast types where not all may be available.")]
        [RequiresUnreferencedCode("Uses reflection to cast types where not all may be available.")]
        private static bool TryCastFromWithReflection<T>(T value, out Variant variant)
        {
            // Convert from ArrayOf<T> where T : IEncodeable or T : Enum
            Type valueType = value!.GetType();
            if (valueType.IsGenericType &&
                valueType.GetGenericTypeDefinition() == typeof(ArrayOf<>))
            {
                Type genericArg = valueType.GetGenericArguments()[0];
                bool isEncodeable = typeof(IEncodeable).IsAssignableFrom(genericArg);
                try
                {
                    MethodInfo? variantFrom = typeof(VariantHelper).GetMethod(
                        isEncodeable ?
                            nameof(FromArrayOfEncodeable) :
                            nameof(FromArrayOfEnumerated),
                        BindingFlags.Static | BindingFlags.NonPublic);
                    variant = (Variant)variantFrom!.MakeGenericMethod(genericArg)
                        .Invoke(null, [value])!;
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

            Type? elementType = valueType.GetElementType();
            if (TypeInfo.Construct(elementType!).IsUnknown)
            {
                variant = default;
                return false;
            }
            try
            {
                MethodInfo matrixFromArray = typeof(MatrixOf).GetMethod(
                    nameof(MatrixOf.From),
                    BindingFlags.Static | BindingFlags.Public)!
                    .MakeGenericMethod([elementType!]);
                Type matrixType = typeof(MatrixOf<>)
                    .MakeGenericType(elementType!);
                MethodInfo? variantFromMatrix = typeof(Variant).GetMethod(
                    nameof(Variant.From),
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    [matrixType],
                    null);
                variant = (Variant)variantFromMatrix!.Invoke(null,
                    [matrixFromArray.Invoke(null, [array])])!;
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
            // Keep the array shape (a one element or empty array stays an array).
            return Variant.Collapse(variants.ToArrayOf(), BuiltInType.Variant);
        }

        private static Variant FromEnums(IEnumerable<Enum> values)
        {
            var variants = new List<Variant>();
            foreach (Enum value in values)
            {
                variants.Add(Variant.From(EnumValue.From(value, value.GetType())));
            }
            // Keep the array shape (a one element or empty array stays an array).
            return Variant.Collapse(variants.ToArrayOf(), BuiltInType.Enumeration);
        }

        private static Variant FromArrayOfEncodeable<T>(ArrayOf<T> values)
            where T : IEncodeable
        {
            return Variant.FromStructure(values);
        }

        private static Variant FromArrayOfEnumerated<T>(ArrayOf<T> values)
            where T : struct, Enum
        {
            return Variant.From(values);
        }
    }
}
