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
using System.Collections.Generic;
using System.IO;
using Opc.Ua;

namespace UaLens.StructuredValues;

/// <summary>
/// An isolated field snapshot. Inclusion is independent of a typed null value.
/// </summary>
internal sealed record StructuredValueField(
    StructureField Definition,
    TypeInfo TypeInfo,
    Variant Value,
    bool IsIncluded);

/// <summary>
/// One proposed field replacement; an omitted optional or union field has IsIncluded=false.
/// </summary>
internal sealed record StructuredFieldEdit(string Name, Variant Value, bool IsIncluded = true);

/// <summary>
/// An edit transaction over native structured accessors. Validation and setters run
/// on a new copy; a failed commit never publishes a partially changed body.
/// </summary>
internal sealed class StructuredValueDraft
{
    private StructuredValueDraft(
        NodeId dataTypeId,
        DataTypeDefinition definition,
        Variant initialValue,
        ArrayOf<StructuredValueField> fields,
        Action ensureCurrent,
        IEncodeable? adapter = null,
        IServiceMessageContext? context = null,
        IEnumeratedType? enumeration = null)
    {
        DataTypeId = dataTypeId;
        Definition = CoreUtils.Clone(definition)!;
        InitialValue = initialValue.Copy();
        Fields = fields;
        m_ensureCurrent = ensureCurrent;
        m_adapter = adapter is null ? null : CoreUtils.Clone(adapter);
        m_context = context;
        m_enumeration = enumeration;
    }

    public NodeId DataTypeId { get; }
    public DataTypeDefinition Definition { get; }
    public Variant InitialValue { get; }
    public ArrayOf<StructuredValueField> Fields { get; }

    public static StructuredValueDraft ForEnumeration(
        NodeId dataTypeId,
        EnumDefinition definition,
        Variant initialValue,
        IEnumeratedType enumeration,
        Action ensureCurrent)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(enumeration);
        ArgumentNullException.ThrowIfNull(ensureCurrent);
        if (definition.IsOptionSet)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "OptionSets require the bit-field editor, not an enumeration selection.");
        }
        if (!initialValue.IsNull &&
            (!initialValue.TypeInfo.IsScalar || !initialValue.TryGetValue(out int _)))
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "An enumeration must be a scalar Int32.");
        }
        return new StructuredValueDraft(
            dataTypeId, definition, initialValue, [], ensureCurrent, enumeration: enumeration);
    }

    public static StructuredValueDraft ForStructure(
        NodeId dataTypeId,
        StructureDefinition definition,
        IEncodeable source,
        IEncodeable adapter,
        IServiceMessageContext context,
        Action ensureCurrent)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ensureCurrent);
        if (adapter is not IStructure structure)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported, "The native adapter has no field accessors.");
        }
        if ((IsUnion(definition) && adapter is not Opc.Ua.Encoders.Union) ||
            (definition.StructureType == StructureType.StructureWithOptionalFields &&
                adapter is not Opc.Ua.Encoders.StructureWithOptionalFields))
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "The native adapter has no presence accessors.");
        }
        IReadOnlyList<IStructureField> accessors = structure.GetFields();
        if (accessors.Count != definition.Fields.Count)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "The structure's accessors do not match the current definition.");
        }
        var snapshots = new StructuredValueField[definition.Fields.Count];
        int optionalIndex = 0;
        for (int i = 0; i < snapshots.Length; i++)
        {
            StructureField field = definition.Fields[i];
            IStructureField? accessor = null;
            foreach (IStructureField candidate in accessors)
            {
                if (string.Equals(candidate.Name, field.Name, StringComparison.Ordinal))
                {
                    accessor = candidate;
                    break;
                }
            }
            if (string.IsNullOrEmpty(field.Name) || accessor is null ||
                accessor.TypeInfo.ValueRank != field.ValueRank || accessor.IsOptional != field.IsOptional)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, $"Field '{field.Name}' does not match its native accessor.");
            }
            Variant fieldValue = structure[field.Name];
            bool included = true;
            if (IsUnion(definition))
            {
                if (adapter is not Opc.Ua.Encoders.Union union)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "The union has no presence adapter.");
                }
                included = union.SwitchField == i + 1;
            }
            else if (definition.StructureType == StructureType.StructureWithOptionalFields && field.IsOptional)
            {
                if (adapter is not Opc.Ua.Encoders.StructureWithOptionalFields optional || optionalIndex >= 32)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "The optional fields have no supported presence adapter.");
                }
                included = (optional.EncodingMask & (1u << optionalIndex++)) != 0;
            }
            snapshots[i] = new StructuredValueField(
                CoreUtils.Clone(field)!, accessor.TypeInfo, fieldValue.Copy(), included);
        }
        return new StructuredValueDraft(
            dataTypeId, definition, Variant.FromStructure(source), snapshots, ensureCurrent, adapter, context);
    }

    public bool TryCommitEnum(int value, out Variant committed, out string? error)
    {
        committed = Variant.Null;
        error = null;
        try
        {
            m_ensureCurrent();
            if (m_enumeration is null || Definition is not EnumDefinition definition)
            {
                error = "The value is not an enumeration.";
                return false;
            }
            bool known = false;
            foreach (EnumField field in definition.Fields)
            {
                known |= field.Value == value;
            }
            if (!known && (!InitialValue.TryGetValue(out int original) || original != value))
            {
                error = "Select a defined enumeration value.";
                return false;
            }
            committed = Variant.From(new EnumValue(value, m_enumeration));
            return true;
        }
        catch (ServiceResultException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryCommit(ArrayOf<StructuredFieldEdit> edits, out Variant committed, out string? error)
    {
        committed = Variant.Null;
        error = null;
        try
        {
            m_ensureCurrent();
            if (Definition is not StructureDefinition definition || m_adapter is null || m_context is null)
            {
                error = "The value is not a structure.";
                return false;
            }
            if (edits.Count != Fields.Count)
            {
                error = "Every field must have exactly one edit.";
                return false;
            }

            var byName = new Dictionary<string, StructuredFieldEdit>(StringComparer.Ordinal);
            int selected = 0;
            foreach (StructuredFieldEdit edit in edits)
            {
                if (!byName.TryAdd(edit.Name, edit))
                {
                    error = $"Field '{edit.Name}' occurs more than once.";
                    return false;
                }
                if (edit.IsIncluded)
                {
                    selected++;
                }
            }
            bool union = IsUnion(definition);
            if (union && selected > 1)
            {
                error = "A union can contain at most one selected field.";
                return false;
            }

            foreach (StructuredValueField field in Fields)
            {
                string name = field.Definition.Name!;
                if (!byName.TryGetValue(name, out StructuredFieldEdit? edit))
                {
                    error = $"Field '{name}' is missing.";
                    return false;
                }
                bool optional = definition.StructureType == StructureType.StructureWithOptionalFields &&
                    field.Definition.IsOptional;
                if (!edit.IsIncluded)
                {
                    if (!union && !optional)
                    {
                        error = $"Field '{name}' is required.";
                        return false;
                    }
                }
                else if (!ValidateValue(field, edit.Value, definition, m_context, out string? fieldError))
                {
                    error = $"Field '{name}': {fieldError}";
                    return false;
                }
            }

            IEncodeable working = CoreUtils.Clone(m_adapter)!;
            if (working is not IStructure structure)
            {
                error = "The native adapter did not clone its field accessors.";
                return false;
            }
            // Clear union selectors before setting the selected arm. Setting a
            // later omitted arm after it would reset the default adapter's switch.
            foreach (StructuredValueField field in Fields)
            {
                StructuredFieldEdit edit = byName[field.Definition.Name!];
                if (union || !edit.IsIncluded)
                {
                    structure[edit.Name] = Variant.Null;
                }
            }
            foreach (StructuredValueField field in Fields)
            {
                StructuredFieldEdit edit = byName[field.Definition.Name!];
                if (edit.IsIncluded)
                {
                    structure[edit.Name] = edit.Value.Copy();
                }
            }

            Variant candidate = InitialValue.Copy();
            if (!candidate.TryGetValue(out ExtensionObject extension) ||
                !extension.TryGetValue(out IEncodeable? destination))
            {
                error = "The original native codec is unavailable.";
                return false;
            }
            if (union || definition.StructureType == StructureType.StructureWithOptionalFields)
            {
                // A present Variant.Null cannot be expressed by the default
                // indexers: their null setters clear the mask/switch. Encode
                // presence explicitly and let a plain Core.Schema adapter
                // encode the selected fields using its native field codecs.
                (IEncodeable body, uint presence) = CreatePresentFields(definition, byName);
                CopyBody(body, destination, m_context, definition.StructureType, presence);
            }
            else
            {
                CopyBody(working, destination, m_context);
            }
            m_ensureCurrent();
            committed = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ServiceResultException or ArgumentException or
            InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or IOException)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool SameType(ExpandedNodeId actual, NodeId expected, NamespaceTable namespaceUris)
    {
        return !actual.IsNull && ExpandedNodeId.ToNodeId(actual, namespaceUris) == expected;
    }

    internal static void CopyBody(
        IEncodeable source,
        IEncodeable destination,
        IServiceMessageContext context,
        StructureType presenceType = StructureType.Structure,
        uint presence = 0)
    {
        using var stream = new MemoryStream();
        using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
        {
            if (presenceType == StructureType.StructureWithOptionalFields)
            {
                encoder.WriteEncodingMask(presence);
            }
            else if (presenceType is StructureType.Union or StructureType.UnionWithSubtypedValues)
            {
                encoder.WriteSwitchField(presence, out _);
            }
            encoder.WriteEncodeable(null, source, source.TypeId);
        }
        stream.Position = 0;
        using var decoder = new BinaryDecoder(stream, context, leaveOpen: true);
        destination.Decode(decoder);
        if (decoder.Position != stream.Length)
        {
            throw new ServiceResultException(
                StatusCodes.BadDecodingError, "The structure codec and definition disagree about the encoded fields.");
        }
    }

    private static bool IsUnion(StructureDefinition definition)
    {
        return definition.StructureType is StructureType.Union or StructureType.UnionWithSubtypedValues;
    }

    private (IEncodeable Body, uint Presence) CreatePresentFields(
        StructureDefinition definition,
        Dictionary<string, StructuredFieldEdit> edits)
    {
        var fields = new List<StructureField>();
        var types = new Dictionary<string, BuiltInType>(StringComparer.Ordinal);
        uint presence = 0;
        int optionalIndex = 0;
        for (int i = 0; i < Fields.Count; i++)
        {
            StructuredValueField field = Fields[i];
            bool included = edits[field.Definition.Name!].IsIncluded;
            if (field.Definition.IsOptional)
            {
                if (included)
                {
                    presence |= 1u << optionalIndex;
                }
                optionalIndex++;
            }
            if (!included)
            {
                continue;
            }
            fields.Add(field.Definition);
            types.Add(field.Definition.Name!, field.TypeInfo.BuiltInType);
            if (IsUnion(definition))
            {
                presence = (uint)i + 1;
            }
        }
        var native = (Opc.Ua.Encoders.Structure)m_adapter!;
        var body = new Opc.Ua.Encoders.Structure(
            native.XmlName, native.TypeId, native.BinaryEncodingId, native.XmlEncodingId,
            new StructureDefinition { Fields = fields.ToArray() }, types);
        foreach (StructureField field in fields)
        {
            body[field.Name!] = edits[field.Name!].Value.Copy();
        }
        return (body, presence);
    }

    private static bool ValidateValue(
        StructuredValueField field,
        Variant value,
        StructureDefinition definition,
        IServiceMessageContext context,
        out string? error)
    {
        error = null;
        if (value.IsNull)
        {
            if (field.TypeInfo.BuiltInType == BuiltInType.Variant &&
                field.Definition.ValueRank is ValueRanks.Scalar or ValueRanks.Any or ValueRanks.ScalarOrOneDimension)
            {
                return true;
            }
            error = "Use a typed null value, or omit an optional field.";
            return false;
        }
        int rank = field.Definition.ValueRank;
        bool rankMatches = rank switch
        {
            ValueRanks.Any => true,
            ValueRanks.ScalarOrOneDimension => value.TypeInfo.IsScalar || value.TypeInfo.IsArray,
            ValueRanks.OneOrMoreDimensions => !value.TypeInfo.IsScalar,
            _ => rank == value.TypeInfo.ValueRank
        };
        BuiltInType expected = field.TypeInfo.BuiltInType;
        if (expected == BuiltInType.Null)
        {
            expected = BuiltInType.ExtensionObject;
        }
        bool typeMatches = expected switch
        {
            BuiltInType.Variant => true,
            BuiltInType.Enumeration => value.TypeInfo.BuiltInType is BuiltInType.Enumeration or BuiltInType.Int32,
            _ => expected == value.TypeInfo.BuiltInType
        };
        if (!rankMatches || !typeMatches)
        {
            error = $"Expected {expected} (rank {rank}), not {value.TypeInfo}.";
            return false;
        }
        if (field.TypeInfo.BuiltInType == BuiltInType.Null)
        {
            bool allowSubtypes = field.Definition.IsOptional && definition.StructureType is
                StructureType.StructureWithSubtypedValues or StructureType.UnionWithSubtypedValues;
            ArrayOf<Variant> elements = value.TypeInfo.IsScalar
                ? [value]
                : StructuredArrayValue.Read(value, context).Elements;
            foreach (Variant element in elements)
            {
                if (!element.TryGetValue(out ExtensionObject extension) ||
                    !extension.TryGetValue(out IEncodeable? body, context) ||
                    (!allowSubtypes && !SameType(body.TypeId, field.Definition.DataType, context.NamespaceUris)))
                {
                    error = "The nested structure does not match the field's DataType.";
                    return false;
                }
            }
        }
        return true;
    }

    private readonly Action m_ensureCurrent;
    private readonly IEncodeable? m_adapter;
    private readonly IServiceMessageContext? m_context;
    private readonly IEnumeratedType? m_enumeration;
}
