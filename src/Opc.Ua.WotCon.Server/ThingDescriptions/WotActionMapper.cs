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

using System.Collections.Generic;
using System.Text;

namespace Opc.Ua.WotCon.Server.ThingDescriptions
{
    /// <summary>
    /// Maps a WoT action JSON schema to an OPC UA <see cref="Argument"/>
    /// list per OPC 10100-1 §6.3.9.
    /// </summary>
    /// <remarks>
    /// Only the <c>type: object</c> shape (a flat <c>properties</c> bag) is
    /// mapped to individual arguments. Other shapes — including nested
    /// objects and arrays-of-objects — collapse to a single argument typed
    /// as <c>BaseDataType</c> with the schema preserved in the description.
    /// </remarks>
    public static class WotActionMapper
    {
        /// <summary>
        /// Builds the input/output arguments for an action.
        /// </summary>
        public static IReadOnlyList<Argument> BuildArguments(WotActionSchema? schema)
        {
            if (schema == null)
            {
                return [];
            }

            if (!string.Equals(schema.Type, "object", System.StringComparison.OrdinalIgnoreCase) ||
                schema.Properties == null ||
                schema.Properties.Count == 0)
            {
                return
                [
                    new Argument
                    {
                        Name = schema.Title ?? "value",
                        DataType = Ua.DataTypeIds.BaseDataType,
                        ValueRank = ValueRanks.Scalar,
                        Description = BuildSchemaDescription(schema)
                    }
                ];
            }

            var arguments = new List<Argument>(schema.Properties.Count);
            foreach (KeyValuePair<string, WotActionMember> member in schema.Properties)
            {
                arguments.Add(BuildMemberArgument(member.Key, member.Value));
            }
            return arguments;
        }

        private static Argument BuildMemberArgument(string name, WotActionMember member)
        {
            int valueRank = ValueRanks.Scalar;
            string? jsonType = member.Type;
            if (string.Equals(jsonType, "array", System.StringComparison.OrdinalIgnoreCase))
            {
                valueRank = ValueRanks.OneDimension;
                jsonType = member.Items?.Type;
            }

            if (!WotPropertyMapper.TryMapPrimitive(jsonType, out NodeId dataType))
            {
                dataType = Ua.DataTypeIds.BaseDataType;
            }

            return new Argument
            {
                Name = name,
                DataType = dataType,
                ValueRank = valueRank,
                Description = BuildMemberDescription(member)
            };
        }

        private static LocalizedText BuildMemberDescription(WotActionMember member)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(member.Description))
            {
                sb.Append(member.Description);
            }
            if (!string.IsNullOrEmpty(member.Unit))
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append('[').Append(member.Unit).Append(']');
            }
            if (member.Minimum.HasValue || member.Maximum.HasValue)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append('(');
                if (member.Minimum.HasValue)
                {
                    sb.Append("min=").Append(member.Minimum.Value);
                }
                if (member.Minimum.HasValue && member.Maximum.HasValue)
                {
                    sb.Append(", ");
                }
                if (member.Maximum.HasValue)
                {
                    sb.Append("max=").Append(member.Maximum.Value);
                }
                sb.Append(')');
            }
            return sb.Length == 0 ? LocalizedText.Null : new LocalizedText(sb.ToString());
        }

        private static LocalizedText BuildSchemaDescription(WotActionSchema schema)
        {
            return string.IsNullOrEmpty(schema.Description)
                ? LocalizedText.Null
                : new LocalizedText(schema.Description);
        }
    }
}
