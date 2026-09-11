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
using System.Text.Json;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using UaLens.StructuredValues;

namespace UaLens.Plugins.PubSub;

internal sealed record PubSubActionInputDraft(string Name, BuiltInType Type, string Text);

internal static class PubSubActionInputs
{
    public static ArrayOf<DataSetField> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxInputCharacters)
        {
            throw new JsonException("Action inputs are limited to 8192 characters.");
        }
        List<PubSubActionInputDraft> inputs = JsonSerializer.Deserialize(
            json, PubSubJsonContext.Default.ListPubSubActionInputDraft)
            ?? throw new JsonException("An Action input array is required.");
        return Create([.. inputs]);
    }

    public static ArrayOf<DataSetField> Create(ArrayOf<PubSubActionInputDraft> inputs)
    {
        if (inputs.IsNull || inputs.Count > MaxInputs)
        {
            throw new JsonException("At most 16 scalar Action inputs are supported.");
        }
        var fields = new DataSetField[inputs.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        int characters = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            PubSubActionInputDraft input = inputs[i];
            if (input is null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 64 ||
                PubSubConfigurationValidation.HasControlCharacters(input.Name) ||
                !names.Add(input.Name) || input.Text is null || input.Text.Length > 512)
            {
                throw new JsonException("Use unique names and bounded scalar input text.");
            }
            characters += input.Name.Length + input.Text.Length;
            if (characters > MaxInputCharacters || !IsSupportedType(input.Type) ||
                !StructuredScalarValue.TryParse(input.Type, input.Text, Variant.Null, out Variant value, out _) ||
                !IsSupportedValue(value))
            {
                throw new JsonException("An Action input is invalid, nonfinite, too large or not a supported scalar.");
            }
            fields[i] = new DataSetField
            {
                Name = input.Name,
                Value = value,
                Encoding = PubSubFieldEncoding.Variant
            };
        }
        return fields;
    }

    public static void Validate(ArrayOf<DataSetField> fields, int maximum = MaxInputs)
    {
        if (fields.IsNull || fields.Count > maximum)
        {
            throw new ArgumentException("A bounded Action field vector is required.", nameof(fields));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataSetField field in fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 64 ||
                PubSubConfigurationValidation.HasControlCharacters(field.Name) || !names.Add(field.Name) ||
                field.FieldIndex != -1 || !Enum.IsDefined(field.Encoding) || !IsSupportedValue(field.Value) ||
                PubSubValueDisplay.Create(field).Truncated)
            {
                throw new ArgumentException("Action fields must have unique names and bounded scalar values.",
                    nameof(fields));
            }
        }
    }

    public static bool IsSupportedType(BuiltInType type)
    {
        return type is >= BuiltInType.Boolean and <= BuiltInType.ByteString or
            BuiltInType.NodeId or BuiltInType.QualifiedName or BuiltInType.LocalizedText;
    }

    public static bool IsSupportedValue(Variant value)
    {
        return !value.IsNull && value.TypeInfo.IsScalar && IsSupportedType(value.TypeInfo.BuiltInType) &&
            (!value.TryGetValue(out float single) || float.IsFinite(single)) &&
            (!value.TryGetValue(out double number) || double.IsFinite(number)) &&
            (!value.TryGetValue(out string text) || text.Length <= PubSubConfigurationValidation.MaxValueCharacters) &&
            (!value.TryGetValue(out ByteString bytes) || bytes.Length <= 128) &&
            (!value.TryGetValue(out NodeId node) || node.NamespaceIndex == 0) &&
            (!value.TryGetValue(out QualifiedName name) || name.NamespaceIndex == 0);
    }

    public const int MaxInputs = 16;
    public const int MaxInputCharacters = 8192;
}
