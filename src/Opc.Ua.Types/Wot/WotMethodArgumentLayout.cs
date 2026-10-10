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
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The relationship between an action DataSchema and native argument positions.
    /// </summary>
    public enum WotMethodArgumentLayoutKind
    {
        /// <summary>
        /// No native arguments are declared.
        /// </summary>
        None,

        /// <summary>
        /// The complete DataSchema describes one native argument.
        /// </summary>
        Single,

        /// <summary>
        /// The schema's named properties describe ordered native arguments.
        /// </summary>
        Named
    }

    /// <summary>
    /// An immutable action input or output layout resolved by the converter's
    /// argument mapping, shared by transport planners and payload adapters.
    /// </summary>
    public sealed class WotMethodArgumentLayout
    {
        internal WotMethodArgumentLayout(
            WotMethodArgumentLayoutKind kind,
            JsonElement schema,
            ArrayOf<string> fieldOrder,
            string defaultName)
        {
            Kind = kind;
            Schema = schema.ValueKind == JsonValueKind.Undefined ? default : schema.Clone();
            FieldOrder = fieldOrder;
            m_defaultName = defaultName;
        }

        /// <summary>
        /// Gets whether the schema declares zero, one whole value, or named arguments.
        /// </summary>
        public WotMethodArgumentLayoutKind Kind { get; }

        /// <summary>
        /// Gets the complete DataSchema, or an undefined element when it is absent.
        /// </summary>
        public JsonElement Schema { get; }

        /// <summary>
        /// Gets the positional property names for a named argument layout.
        /// A single value's own Structure fields are not argument positions.
        /// </summary>
        public ArrayOf<string> FieldOrder { get; }

        /// <summary>
        /// Gets the number of declared native argument positions.
        /// </summary>
        public int ArgumentCount => Kind switch
        {
            WotMethodArgumentLayoutKind.Single => 1,
            WotMethodArgumentLayoutKind.Named => FieldOrder.Count,
            _ => 0
        };

        /// <summary>
        /// Gets the DataSchema for one native argument position.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public JsonElement GetArgumentSchema(int index)
        {
            return GetArgumentSchema(Schema, index);
        }

        /// <summary>
        /// Gets the native argument name, including the converter's default for a single value.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public string GetArgumentName(int index)
        {
            JsonElement schema = GetArgumentSchema(index);
            return Kind == WotMethodArgumentLayoutKind.Single
                ? WotNodeSetConverter.ReadArgumentName(schema, m_defaultName)
                : FieldOrder[index];
        }

        internal JsonElement GetArgumentSchema(JsonElement sourceSchema, int index)
        {
            if (index < 0 || index >= ArgumentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return Kind == WotMethodArgumentLayoutKind.Single
                ? sourceSchema
                : sourceSchema.GetProperty("properties").GetProperty(FieldOrder[index]);
        }

        private readonly string m_defaultName;
    }
}
