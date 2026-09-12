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
using System.Globalization;
using System.IO;
using Opc.Ua;

namespace UaLens.StructuredValues;

/// <summary>
/// Session-bound array and matrix edits. Every operation returns an isolated
/// snapshot; resizing and discarding elements require an explicit choice.
/// </summary>
internal sealed class StructuredArrayDraft
{
    public StructuredArrayDraft(
        BuiltInType elementType,
        int valueRank,
        ArrayOf<uint> declaredDimensions,
        Variant initialValue,
        IServiceMessageContext context,
        Action ensureCurrent,
        bool isStructureField = false)
    {
        m_context = context ?? throw new ArgumentNullException(nameof(context));
        m_ensureCurrent = ensureCurrent ?? throw new ArgumentNullException(nameof(ensureCurrent));
        m_isStructureField = isStructureField;
        if (valueRank == ValueRanks.Scalar || valueRank < ValueRanks.ScalarOrOneDimension ||
            valueRank > MaximumRank)
        {
            throw new ServiceResultException(
                StatusCodes.BadOutOfRange, $"The array editor supports ranks 1 through {MaximumRank}.");
        }
        ValueRank = valueRank;
        DeclaredDimensions = CoreUtils.Clone(declaredDimensions);
        ElementCapacity = context.MaxArrayLength > 0
            ? Math.Min(MaximumElementCount, context.MaxArrayLength)
            : MaximumElementCount;
        int initialRank = valueRank > 0 ? valueRank : Math.Max(1, declaredDimensions.Count);
        if (initialRank > MaximumRank)
        {
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "The declared rank is too large.");
        }
        BuiltInType expected = elementType == BuiltInType.Null ? BuiltInType.ExtensionObject : elementType;
        InitialValue = initialValue.IsNull
            ? new StructuredArrayValue(expected, [],
                initialRank > 1 ? new ArrayOf<int>(new int[initialRank]) : default, isNull: true)
            : StructuredArrayValue.Read(initialValue, context);
        if (!initialValue.IsNull && initialValue.TypeInfo.ValueRank == ValueRanks.OneOrMoreDimensions &&
            InitialValue.IsNull && initialRank > 1)
        {
            InitialValue = new StructuredArrayValue(
                InitialValue.ElementType, [], new int[initialRank], isNull: true);
        }
        if (expected != BuiltInType.Variant && expected != InitialValue.ElementType &&
            !(expected == BuiltInType.Enumeration && InitialValue.ElementType == BuiltInType.Int32))
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, $"The array must contain {expected} elements.");
        }
        Validate(InitialValue);
        m_ensureCurrent();
    }

    public int ValueRank { get; }
    public ArrayOf<uint> DeclaredDimensions { get; }
    public int ElementCapacity { get; }
    public StructuredArrayValue InitialValue { get; }

    public static ArrayOf<int> ParseDimensions(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > MaximumRank)
        {
            throw new FormatException($"Enter 2 through {MaximumRank} comma-separated dimensions.");
        }
        var dimensions = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out dimensions[i]))
            {
                throw new FormatException("Every dimension must be a non-negative Int32 integer.");
            }
        }
        return dimensions;
    }

    public StructuredArrayValue Reshape(
        StructuredArrayValue source, ArrayOf<int> dimensions, bool allowResize = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        m_ensureCurrent();
        Validate(source);
        if (dimensions.Count is < 2 or > MaximumRank)
        {
            throw new ServiceResultException(
                StatusCodes.BadOutOfRange, $"A matrix requires 2 through {MaximumRank} dimensions.");
        }
        int count = StructuredArrayValue.GetElementCount(dimensions, ValueRank, DeclaredDimensions, m_context);
        if (count > ElementCapacity)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, $"The editor capacity is {ElementCapacity} elements.");
        }
        if (count != source.Elements.Count && !allowResize)
        {
            throw new ServiceResultException(
                StatusCodes.BadInvalidArgument,
                "Reshaping must retain the element count. Explicitly allow resizing to add or discard elements.");
        }
        var elements = new Variant[count];
        TypeInfo scalarType = TypeInfo.Create(source.ElementType, ValueRanks.Scalar);
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = i < source.Elements.Count
                ? source.Elements[i].Copy()
                : Variant.CreateDefault(scalarType);
        }
        var result = new StructuredArrayValue(source.ElementType, elements, dimensions);
        Validate(result);
        m_ensureCurrent();
        return result;
    }

    public StructuredArrayValue WithElements(StructuredArrayValue source, ArrayOf<Variant> elements)
    {
        ArgumentNullException.ThrowIfNull(source);
        m_ensureCurrent();
        Validate(source);
        if (elements.Count > ElementCapacity)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, $"The editor capacity is {ElementCapacity} elements.");
        }
        var result = new StructuredArrayValue(source.ElementType, elements, source.Dimensions);
        Validate(result);
        m_ensureCurrent();
        return result;
    }

    public StructuredArrayValue AsNull(StructuredArrayValue source, bool allowDiscard = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        m_ensureCurrent();
        Validate(source);
        if (!source.Elements.IsEmpty && !allowDiscard)
        {
            throw new ServiceResultException(
                StatusCodes.BadInvalidArgument, "Explicitly allow discarding elements before setting the array null.");
        }
        var result = new StructuredArrayValue(source.ElementType, [],
            source.Dimensions.IsEmpty ? default : new ArrayOf<int>(new int[source.Dimensions.Count]), isNull: true);
        Validate(result);
        m_ensureCurrent();
        return result;
    }

    public bool TryCommit(StructuredArrayValue value, out Variant committed, out string? error)
    {
        committed = Variant.Null;
        error = null;
        try
        {
            m_ensureCurrent();
            Validate(value);
            Variant candidate = value.ToVariant(m_context);
            using (var encoder = new BinaryEncoder(m_context))
            {
                if (m_isStructureField)
                {
                    encoder.WriteVariantValue(null, candidate);
                }
                else
                {
                    encoder.WriteVariant(null, candidate);
                }
            }
            m_ensureCurrent();
            committed = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ServiceResultException or ArgumentException or
            InvalidOperationException or OverflowException or IOException or OperationCanceledException)
        {
            error = ex.Message;
            return false;
        }
    }

    private void Validate(StructuredArrayValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ElementType != InitialValue.ElementType)
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The draft's element type cannot change.");
        }
        if (value.Elements.Count > ElementCapacity || value.Dimensions.Count > MaximumRank)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The value exceeds the array editor's capacity.");
        }
        value.Validate(ValueRank, DeclaredDimensions, m_context);
    }

    public const int MaximumRank = 32;
    public const int MaximumElementCount = 65536;

    private readonly IServiceMessageContext m_context;
    private readonly Action m_ensureCurrent;
    private readonly bool m_isStructureField;
}
