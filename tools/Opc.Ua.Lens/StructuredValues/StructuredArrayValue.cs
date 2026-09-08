/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using Opc.Ua;

namespace UaLens.StructuredValues;

/// <summary>
/// Typed array editing through the stack's native codecs. Retains matrix dimensions
/// and distinguishes a null array from an explicitly committed empty array.
/// </summary>
internal sealed class StructuredArrayValue
{
    public StructuredArrayValue(
        BuiltInType elementType,
        ArrayOf<Variant> elements,
        ArrayOf<int> dimensions = default,
        bool isNull = false)
    {
        ElementType = elementType == BuiltInType.Null ? BuiltInType.ExtensionObject : elementType;
        Elements = elements.ConvertAll(value => value.Copy());
        Dimensions = dimensions;
        IsNull = isNull;
    }

    public BuiltInType ElementType { get; }
    public ArrayOf<Variant> Elements { get; }
    public ArrayOf<int> Dimensions { get; }
    public bool IsNull { get; }

    public static StructuredArrayValue Read(Variant value, IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (value.IsNull || value.TypeInfo.IsScalar)
        {
            throw new ArgumentException("The value must be a typed array or matrix.", nameof(value));
        }
        using var stream = new MemoryStream();
        using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
        {
            encoder.WriteVariantValue(null, value);
        }
        stream.Position = 0;
        using var decoder = new BinaryDecoder(stream, context, leaveOpen: true);
        ArrayOf<int> dimensions = value.TypeInfo.IsMatrix ? decoder.ReadInt32Array(null) : default;
        int count = decoder.ReadInt32(null);
        if (count < -1 || (context.MaxArrayLength > 0 && count > context.MaxArrayLength))
        {
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
        }
        var elements = new Variant[Math.Max(count, 0)];
        TypeInfo scalar = TypeInfo.Create(value.TypeInfo.BuiltInType, ValueRanks.Scalar);
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = value.TypeInfo.BuiltInType == BuiltInType.Variant
                ? decoder.ReadVariant(null)
                : decoder.ReadVariantValue(null, scalar);
        }
        return new StructuredArrayValue(value.TypeInfo.BuiltInType, elements, dimensions, count == -1);
    }

    public Variant WithElements(ArrayOf<Variant> elements, IServiceMessageContext context)
    {
        return new StructuredArrayValue(ElementType, elements, Dimensions).ToVariant(context);
    }

    public Variant ToVariant(IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.MaxArrayLength > 0 && Elements.Count > context.MaxArrayLength)
        {
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
        }
        if (!Dimensions.IsEmpty)
        {
            if (Dimensions.Count < 2)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "A matrix must have at least two dimensions.");
            }
            long count = 1;
            foreach (int dimension in Dimensions)
            {
                if (dimension < 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadOutOfRange, "Matrix dimensions cannot be negative.");
                }
                count = checked(count * dimension);
            }
            if (count != Elements.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "Changing a matrix's element count requires new dimensions.");
            }
        }
        using var stream = new MemoryStream();
        using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
        {
            if (!Dimensions.IsEmpty)
            {
                encoder.WriteInt32Array(null, Dimensions);
            }
            encoder.WriteInt32(null, IsNull ? -1 : Elements.Count);
            if (!IsNull)
            {
                foreach (Variant element in Elements)
                {
                    if (ElementType == BuiltInType.Variant)
                    {
                        encoder.WriteVariant(null, element);
                        continue;
                    }
                    if (!element.TypeInfo.IsScalar ||
                        (element.TypeInfo.BuiltInType != ElementType &&
                            !(ElementType == BuiltInType.Enumeration &&
                                element.TypeInfo.BuiltInType == BuiltInType.Int32)))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch, $"Every array element must be a scalar {ElementType}.");
                    }
                    encoder.WriteVariantValue(null, element);
                }
            }
        }
        stream.Position = 0;
        using var decoder = new BinaryDecoder(stream, context, leaveOpen: true);
        return decoder.ReadVariantValue(null, TypeInfo.Create(
            ElementType, Dimensions.IsEmpty ? ValueRanks.OneDimension : Dimensions.Count));
    }
}
