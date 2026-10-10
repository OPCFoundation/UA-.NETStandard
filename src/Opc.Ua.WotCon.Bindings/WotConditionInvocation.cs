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
using Opc.Ua.Wot;

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

        internal void ValidateLayout(WotPayloadDescriptor payload)
        {
            if (payload.InputLayout is not null &&
                (!TryGetCommentPolicy(payload, out bool optional) || optional != CommentOptional))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The Condition invocation policy disagrees with its resolved native signature.");
            }
        }

        internal static bool TryCreate(
            WotAffordanceForm form,
            WotPayloadDescriptor payload,
            out WotConditionInvocation? invocation,
            out string? error)
        {
            invocation = null;
            error = null;
            JsonElement affordance = payload.Schema?.Definition ?? form.AffordanceElement;
            if (form.Kind != WotAffordanceKind.Action ||
                affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty("uav:conditionAction", out JsonElement actionElement))
            {
                return true;
            }
            string? action = actionElement.ValueKind == JsonValueKind.String ? actionElement.GetString() : null;
            if (action is "Enable" or "Disable")
            {
                if (payload.InputLayout?.ArgumentCount == 0 && payload.OutputLayout?.ArgumentCount == 0)
                {
                    return true;
                }
                error = "Enable and Disable retain their zero-input, zero-output native signatures.";
                return false;
            }
            if (action is not ("Acknowledge" or "Confirm" or "AddComment") ||
                !TryGetCommentPolicy(payload, out bool optional))
            {
                error = "A Condition occurrence action requires the ordered scalar ByteString EventId " +
                    "and LocalizedText Comment signature, with required EventId and no outputs.";
                return false;
            }
            invocation = new WotConditionInvocation(action, optional);
            return true;
        }

        private static bool TryGetCommentPolicy(WotPayloadDescriptor payload, out bool optional)
        {
            optional = false;
            WotMethodArgumentLayout? input = payload.InputLayout;
            if (input is null ||
                input.Kind != WotMethodArgumentLayoutKind.Named ||
                input.ArgumentCount != 2 ||
                input.FieldOrder[0] != "EventId" ||
                input.FieldOrder[1] != "Comment" ||
                payload.OutputLayout?.ArgumentCount != 0 ||
                !input.Schema.TryGetProperty("required", out JsonElement required) ||
                required.ValueKind != JsonValueKind.Array ||
                !required.EnumerateArray().Any(member =>
                    member.ValueKind == JsonValueKind.String && member.GetString() == "EventId"))
            {
                return false;
            }
            WotPayloadSchema schema = payload.GetActionSchema();
            if (!HasNativeType(schema, "EventId", Ua.DataTypeIds.ByteString) ||
                !HasNativeType(schema, "Comment", Ua.DataTypeIds.LocalizedText))
            {
                return false;
            }
            optional = !required.EnumerateArray().Any(member =>
                member.ValueKind == JsonValueKind.String && member.GetString() == "Comment");
            return true;
        }

        private static bool HasNativeType(WotPayloadSchema schema, string name, NodeId expected)
        {
            return schema.TryGetTypeBinding("/input/properties/" + name, out WotPayloadTypeBinding? binding) &&
                binding.TypeInfo.ValueRank == ValueRanks.Scalar &&
                binding.DataTypeId.ServerIndex == 0 &&
                (string.IsNullOrEmpty(binding.DataTypeId.NamespaceUri) ||
                    binding.DataTypeId.NamespaceUri == Ua.Namespaces.OpcUa) &&
                binding.DataTypeId.InnerNodeId == expected;
        }
    }
}
