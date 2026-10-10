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
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    public sealed partial class WotPayloadDescriptor
    {
        /// <summary>
        /// Validates that both action layouts describe the captured interaction.
        /// A legacy transport-only descriptor makes no argument-layout claims.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ValidateArgumentLayouts()
        {
            if (InputLayout is null && OutputLayout is null && Schema is null)
            {
                return;
            }
            if (InputLayout is null || OutputLayout is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "An action requires both resolved argument layouts.");
            }
            WotPayloadSchema schema = GetActionSchema();
            foreach (WotDiagnostic diagnostic in schema.Diagnostics)
            {
                if (diagnostic.Severity == WotDiagnosticSeverity.Error)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError, diagnostic.Message);
                }
            }
            ValidateLayoutSchema(InputLayout, schema, "input");
            ValidateLayoutSchema(OutputLayout, schema, "output");
        }

        /// <summary>
        /// Validates every native input position before invocation. JSON optionality and
        /// defaults do not authorize omitting a position from the native signature.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public void ValidateInputs(ArrayOf<Variant> inputs, IServiceMessageContext context)
        {
            ValidateArgumentValues(inputs, InputLayout, "input", context);
        }

        /// <summary>
        /// Validates the complete native output list without accepting partial successful results.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public void ValidateOutputs(ArrayOf<Variant> outputs, IServiceMessageContext context)
        {
            try
            {
                ValidateArgumentValues(outputs, OutputLayout, "output", context);
            }
            catch (ServiceResultException exception) when (exception.StatusCode != StatusCodes.BadNotSupported)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, exception.Message, exception);
            }
        }

        /// <summary>
        /// Verifies the local Method's actual names, DataTypes and ranks against the
        /// compiled action before installing its invocation handler or opening a channel.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public void ValidateMethodSignature(
            ArrayOf<Argument> inputArguments,
            ArrayOf<Argument> outputArguments,
            NamespaceTable namespaceUris,
            ITypeTable? typeTable = null)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            ValidateArgumentLayouts();
            ValidateSignature(inputArguments, InputLayout, "input", namespaceUris, typeTable);
            ValidateSignature(outputArguments, OutputLayout, "output", namespaceUris, typeTable);
        }

        private void ValidateArgumentValues(
            ArrayOf<Variant> arguments,
            WotMethodArgumentLayout? layout,
            string member,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            ValidateArgumentLayouts();
            if (layout is null)
            {
                return;
            }
            if (arguments.Count != layout.ArgumentCount)
            {
                throw new ServiceResultException(
                    arguments.Count < layout.ArgumentCount
                        ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTooManyArguments,
                    $"The action declares {layout.ArgumentCount} native {member} arguments, " +
                    $"but {arguments.Count} were supplied.");
            }
            if (arguments.Count == 0)
            {
                return;
            }
            WotPayloadSchema schema = GetActionSchema();
            var validator = new WotPayloadValueValidator(context);
            for (int index = 0; index < arguments.Count; index++)
            {
                WotPayloadTypeBinding binding = GetArgumentBinding(schema, layout, member, index);
                validator.Validate(arguments[index], binding.DataTypeId, binding.TypeInfo);
            }
        }

        private void ValidateSignature(
            ArrayOf<Argument> arguments,
            WotMethodArgumentLayout? layout,
            string member,
            NamespaceTable namespaceUris,
            ITypeTable? typeTable)
        {
            if (layout is null)
            {
                return;
            }
            if (arguments.Count != layout.ArgumentCount)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    $"The local Method's {member} argument count disagrees with its compiled action.");
            }
            WotPayloadSchema schema = GetActionSchema();
            for (int index = 0; index < arguments.Count; index++)
            {
                WotPayloadTypeBinding binding = GetArgumentBinding(schema, layout, member, index);
                var expected = ExpandedNodeId.ToNodeId(binding.DataTypeId, namespaceUris);
                Argument argument = arguments[index];
                if (argument is null ||
                    !MatchesDataType(argument.DataType, expected, typeTable) ||
                    argument.ValueRank != binding.TypeInfo.ValueRank ||
                    !string.Equals(argument.Name, layout.GetArgumentName(index), StringComparison.Ordinal))
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        $"The local Method's {member} argument at position {index} " +
                        "disagrees with its compiled action.");
                }
            }
        }

        private static bool MatchesDataType(NodeId actual, NodeId expected, ITypeTable? typeTable)
        {
            if (actual.IsNull || expected.IsNull)
            {
                return false;
            }
            if (actual == expected || expected == Ua.DataTypeIds.BaseDataType)
            {
                return true;
            }
            if (expected != Ua.DataTypeIds.Number &&
                expected != Ua.DataTypeIds.Integer &&
                expected != Ua.DataTypeIds.UInteger &&
                expected != Ua.DataTypeIds.Structure &&
                expected != Ua.DataTypeIds.Enumeration)
            {
                return false;
            }
            if (typeTable?.IsTypeOf(actual, expected) == true)
            {
                return true;
            }
            BuiltInType actualType = typeTable is null
                ? TypeInfo.GetBuiltInType(actual) : TypeInfo.GetBuiltInType(actual, typeTable);
            if (expected == Ua.DataTypeIds.Number)
            {
                return actualType is >= BuiltInType.SByte and <= BuiltInType.Double;
            }
            if (expected == Ua.DataTypeIds.Integer)
            {
                return actualType is BuiltInType.SByte or BuiltInType.Int16 or BuiltInType.Int32 or BuiltInType.Int64;
            }
            return expected == Ua.DataTypeIds.UInteger &&
                actualType is BuiltInType.Byte or BuiltInType.UInt16 or BuiltInType.UInt32 or BuiltInType.UInt64;
        }

        private static void ValidateLayoutSchema(
            WotMethodArgumentLayout layout, WotPayloadSchema schema, string member)
        {
            if (schema.Definition.ValueKind != JsonValueKind.Object)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The captured interaction is not an action object.");
            }
            schema.Definition.TryGetProperty(member, out JsonElement declared);
            bool matches = layout.Schema.ValueKind == JsonValueKind.Undefined
                ? declared.ValueKind == JsonValueKind.Undefined
                : declared.ValueKind != JsonValueKind.Undefined && JsonElement.DeepEquals(layout.Schema, declared);
            if (!matches)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    $"The {member} layout disagrees with its captured payload schema.");
            }
        }

        private static WotPayloadTypeBinding GetArgumentBinding(
            WotPayloadSchema schema, WotMethodArgumentLayout layout, string member, int index)
        {
            string pointer = "/" + member;
            if (layout.Kind == WotMethodArgumentLayoutKind.Named)
            {
                pointer += "/properties/" + WotAffordanceForm.EscapePointerToken(layout.FieldOrder[index]);
            }
            if (!schema.TryGetTypeBinding(pointer, out WotPayloadTypeBinding? binding) || binding.DataTypeId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, $"The native argument type at '{pointer}' is unresolved.");
            }
            return binding;
        }
    }
}
