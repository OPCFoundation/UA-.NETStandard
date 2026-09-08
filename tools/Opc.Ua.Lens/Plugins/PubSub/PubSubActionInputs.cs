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
using System.Globalization;
using System.Text.Json;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace UaLens.Plugins.PubSub;

internal sealed record PubSubActionInputDraft(string Name, BuiltInType Type, string Text);

internal static class PubSubActionInputs
{
    public static ArrayOf<DataSetField> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 8192)
        {
            throw new JsonException("Action inputs are limited to 8192 characters.");
        }
        List<PubSubActionInputDraft> inputs = JsonSerializer.Deserialize(
            json, PubSubJsonContext.Default.ListPubSubActionInputDraft)
            ?? throw new JsonException("An Action input array is required.");
        if (inputs.Count > 16)
        {
            throw new JsonException("At most 16 scalar Action inputs are supported.");
        }
        var fields = new DataSetField[inputs.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < fields.Length; i++)
        {
            PubSubActionInputDraft input = inputs[i];
            if (input is null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 64 ||
                !names.Add(input.Name) || input.Text is null || input.Text.Length > 512)
            {
                throw new JsonException("Use unique names and bounded scalar input text.");
            }
            Variant value = input.Type switch
            {
                BuiltInType.Boolean when bool.TryParse(input.Text, out bool result) => new Variant(result),
                BuiltInType.Int32 when int.TryParse(input.Text, CultureInfo.InvariantCulture, out int result) =>
                    new Variant(result),
                BuiltInType.UInt32 when uint.TryParse(input.Text, CultureInfo.InvariantCulture, out uint result) =>
                    new Variant(result),
                BuiltInType.Int64 when long.TryParse(input.Text, CultureInfo.InvariantCulture, out long result) =>
                    new Variant(result),
                BuiltInType.UInt64 when ulong.TryParse(input.Text, CultureInfo.InvariantCulture, out ulong result) =>
                    new Variant(result),
                BuiltInType.Double when double.TryParse(
                    input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) &&
                    double.IsFinite(result) =>
                    new Variant(result),
                BuiltInType.String => new Variant(input.Text),
                _ => throw new JsonException(
                    "Use Boolean, Int32, UInt32, Int64, UInt64, finite Double, or String input text.")
            };
            fields[i] = new DataSetField
            {
                Name = input.Name,
                Value = value,
                Encoding = PubSubFieldEncoding.Variant
            };
        }
        return fields;
    }
}
