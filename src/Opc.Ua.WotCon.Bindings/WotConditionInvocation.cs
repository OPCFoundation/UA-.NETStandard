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
using System.Linq;
using System.Text.Json;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Compiled Section 13 occurrence-action semantics. Optional WoT Comment
    /// omission never removes the standard OPC UA Method's second argument.
    /// </summary>
    public sealed class WotConditionInvocation
    {
        /// <summary>
        /// Initializes the occurrence action and its optional-Comment policy.
        /// </summary>
        public WotConditionInvocation(string action, bool commentOptional)
        {
            if (action is not ("Acknowledge" or "Confirm" or "AddComment"))
            {
                throw new ArgumentOutOfRangeException(nameof(action));
            }
            Action = action;
            CommentOptional = commentOptional;
        }

        /// <summary>
        /// Gets the standard occurrence action.
        /// </summary>
        public string Action { get; }

        /// <summary>
        /// Gets whether an omitted trailing WoT Comment is supplied as a null
        /// LocalizedText in the OPC UA invocation.
        /// </summary>
        public bool CommentOptional { get; }

        /// <summary>
        /// Completes the standard UA signature without replacing supplied
        /// values or masking missing EventId, excess arguments or required Comment.
        /// </summary>
        public ArrayOf<Variant> NormalizeInputs(ArrayOf<Variant> inputs)
        {
            return CommentOptional && inputs.Count == 1
                ? inputs.AddItem(new Variant(LocalizedText.Null))
                : inputs;
        }

        internal static WotConditionInvocation? FromAffordance(WotAffordanceForm form)
        {
            JsonElement affordance = form.AffordanceElement;
            if (form.Kind != WotAffordanceKind.Action || affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty("uav:conditionAction", out JsonElement actionElement) ||
                actionElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            string? action = actionElement.GetString();
            if (action is not ("Acknowledge" or "Confirm" or "AddComment"))
            {
                return null;
            }
            bool optional = false;
            if (affordance.TryGetProperty("input", out JsonElement input) &&
                input.ValueKind == JsonValueKind.Object && HasCanonicalInputOrder(input))
            {
                optional = !input.TryGetProperty("required", out JsonElement required) ||
                    (required.ValueKind == JsonValueKind.Array &&
                        !required.EnumerateArray().Any(member =>
                            member.ValueKind == JsonValueKind.String && member.GetString() == "Comment"));
            }
            return new WotConditionInvocation(action, optional);
        }

        private static bool HasCanonicalInputOrder(JsonElement input)
        {
            if (!input.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object ||
                !properties.TryGetProperty("EventId", out _))
            {
                return false;
            }
            int count = properties.EnumerateObject().Count();
            if (count == 1)
            {
                return true;
            }
            return count == 2 && properties.TryGetProperty("Comment", out _) &&
                input.TryGetProperty("uav:fieldOrder", out JsonElement order) &&
                order.ValueKind == JsonValueKind.Array && order.GetArrayLength() == 2 &&
                order[0].ValueKind == JsonValueKind.String && order[0].GetString() == "EventId" &&
                order[1].ValueKind == JsonValueKind.String && order[1].GetString() == "Comment";
        }
    }
}
