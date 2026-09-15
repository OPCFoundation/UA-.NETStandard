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
        Dimensions = CoreUtils.Clone(dimensions);
        IsNull = isNull;
    }

    public BuiltInType ElementType { get; }
    public ArrayOf<Variant> Elements { get; }
    public ArrayOf<int> Dimensions { get; }
    public bool IsNull { get; }

    public static bool RequiresEditor(int valueRank, Variant value)
    {
        return valueRank >= ValueRanks.OneOrMoreDimensions || (!value.IsNull && !value.TypeInfo.IsScalar);
    }

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
        if (count == -1 && dimensions.IsEmpty && value.TypeInfo.IsMatrix)
        {
            // Native default matrix fields may have no stored dimensions. Their
            // declared rank is still available even though they contain no data.
            dimensions = new int[value.TypeInfo.ValueRank];
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

    public void Validate(
        int valueRank, ArrayOf<uint> declaredDimensions, IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsNull && !Elements.IsEmpty)
        {
            throw new ServiceResultException(
                StatusCodes.BadInvalidArgument, "A null array cannot discard supplied elements.");
        }
        if (context.MaxArrayLength > 0 && Elements.Count > context.MaxArrayLength)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The array exceeds the session's element capacity.");
        }
        if (Dimensions.Count == 1)
        {
            throw new ServiceResultException(
                StatusCodes.BadOutOfRange, "A matrix must have at least two dimensions.");
        }
        ArrayOf<int> shape = Dimensions.IsEmpty ? [Elements.Count] : Dimensions;
        int count = GetElementCount(shape, valueRank, declaredDimensions, context);
        if (count != Elements.Count)
        {
            throw new ServiceResultException(
                StatusCodes.BadOutOfRange, "Changing a matrix's element count requires new dimensions.");
        }
    }

    public Variant ToVariant(IServiceMessageContext context)
    {
        Validate(ValueRanks.Any, [], context);
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

    internal static int GetElementCount(
        ArrayOf<int> dimensions,
        int valueRank,
        ArrayOf<uint> declaredDimensions,
        IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int rank = dimensions.Count;
        bool rankMatches = valueRank switch
        {
            ValueRanks.Any or ValueRanks.OneOrMoreDimensions => rank >= 1,
            ValueRanks.ScalarOrOneDimension => rank == 1,
            _ => valueRank >= 1 && valueRank == rank
        };
        if (!rankMatches)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, $"Array rank {rank} does not match the declared ValueRank {valueRank}.");
        }
        if (!declaredDimensions.IsEmpty && declaredDimensions.Count != rank)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "Declared ArrayDimensions must match the array's rank.");
        }
        int count = 1;
        for (int i = 0; i < rank; i++)
        {
            int dimension = dimensions[i];
            if (dimension < 0)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange, "Matrix dimensions cannot be negative.");
            }
            if (!declaredDimensions.IsEmpty && declaredDimensions[i] != 0 &&
                (uint)dimension > declaredDimensions[i])
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, $"Dimension {i + 1} exceeds its declared maximum.");
            }
            try
            {
                count = checked(count * dimension);
            }
            catch (OverflowException ex)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "The matrix element product exceeds Int32 capacity.", ex);
            }
        }
        if (context.MaxArrayLength > 0 && (count > context.MaxArrayLength || rank > context.MaxArrayLength))
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The matrix exceeds the session's array capacity.");
        }
        return count;
    }
}
