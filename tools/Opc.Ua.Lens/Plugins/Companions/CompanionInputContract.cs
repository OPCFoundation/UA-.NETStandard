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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Connection;
using UaLens.StructuredValues;

namespace UaLens.Plugins.Companions
{
    internal sealed record CompanionInputSchema(string Name, ByteString Digest);

    /// <summary>
    /// Shared typed-form validation for the desktop and direct preparation path.
    /// Definitions come from the active server and are pinned until execution.
    /// </summary>
    internal static class CompanionInputContract
    {
        public static void ValidateDefinition(CompanionInputDefinition input)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (string.IsNullOrWhiteSpace(input.Name) ||
                string.IsNullOrWhiteSpace(input.DisplayName) ||
                input.ValueRank < ValueRanks.ScalarOrOneDimension ||
                input.ValueRank > StructuredArrayDraft.MaximumRank ||
                input.ArrayDimensions.Count > StructuredArrayDraft.MaximumRank ||
                (input.ArrayDimensions.Count > 0 && input.ValueRank < ValueRanks.OneDimension) ||
                (input.ArrayDimensions.Count > 0 && input.ArrayDimensions.Count != input.ValueRank) ||
                (!IsScalarType(input.DataType) &&
                    input.DataType is not (BuiltInType.ExtensionObject or BuiltInType.Enumeration)) ||
                (input.DataType is BuiltInType.ExtensionObject or BuiltInType.Enumeration &&
                    input.DataTypeId.IsNull) ||
                input.DataTypeId.ServerIndex != 0 ||
                (input.DataTypeId.NamespaceIndex != 0 && string.IsNullOrEmpty(input.DataTypeId.NamespaceUri)) ||
                (input.IsFileSource && (input.RequiresEditor || input.DataType != BuiltInType.String)) ||
                (input.IsMultiline && (input.RequiresEditor || input.DataType != BuiltInType.String)))
            {
                throw new InvalidOperationException("The provider returned an invalid input definition.");
            }
        }

        public static bool IsScalarType(BuiltInType type)
        {
            return type is BuiltInType.Boolean or BuiltInType.SByte or BuiltInType.Byte or
                BuiltInType.Int16 or BuiltInType.UInt16 or BuiltInType.Int32 or BuiltInType.UInt32 or
                BuiltInType.Int64 or BuiltInType.UInt64 or BuiltInType.Float or BuiltInType.Double or
                BuiltInType.String or BuiltInType.DateTime or BuiltInType.Guid or BuiltInType.ByteString or
                BuiltInType.NodeId or BuiltInType.QualifiedName or BuiltInType.LocalizedText;
        }

        public static NodeId ResolveType(CompanionInputDefinition input, NamespaceTable namespaces)
        {
            NodeId id = input.DataTypeId.IsNull
                ? new NodeId((uint)input.DataType)
                : ExpandedNodeId.ToNodeId(input.DataTypeId, namespaces);
            if (id.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDataTypeIdUnknown, "The task input namespace is unavailable.");
            }
            return id;
        }

        public static ArrayOf<CompanionValue> Snapshot(
            ArrayOf<CompanionValue> inputs, IServiceMessageContext context)
        {
            return inputs.ConvertAll(value => value is null
                ? throw new ArgumentException("Task input fields cannot be null.", nameof(inputs))
                : new CompanionValue(value.Name, DataValueCodec.Snapshot(value.Value, context))
                {
                    InputSchemaDigest = value.InputSchemaDigest.Copy()
                });
        }

        public static ByteString SchemaDigest(DataTypeDefinition definition, IServiceMessageContext context)
        {
            ByteString encoded = CompanionInputSchemaGraph.EncodeDefinition(definition, context);
            return ByteString.From(SHA256.HashData(encoded.Span));
        }

        public static void ValidateShape(
            CompanionOperation operation,
            ArrayOf<CompanionValue> inputs)
        {
            if (!operation.HasTypedInput || inputs.Count != operation.Inputs.Count)
            {
                throw new ArgumentException("Supply the current task's exact typed input fields.", nameof(inputs));
            }
            int textLength = 0;
            for (int index = 0; index < inputs.Count; index++)
            {
                CompanionInputDefinition field = operation.Inputs[index];
                CompanionValue input = inputs[index] ??
                    throw new ArgumentException("Task input fields cannot be null.", nameof(inputs));
                ValidateDefinition(field);
                Variant value = input.Value;
                bool typeMatches = value.TypeInfo.BuiltInType == field.DataType ||
                    (field.DataType == BuiltInType.Enumeration &&
                        value.TypeInfo.BuiltInType == BuiltInType.Int32);
                bool rankMatches = field.ValueRank switch
                {
                    ValueRanks.Any => true,
                    ValueRanks.ScalarOrOneDimension => value.TypeInfo.ValueRank is -1 or 1,
                    ValueRanks.OneOrMoreDimensions => !value.TypeInfo.IsScalar,
                    _ => value.TypeInfo.ValueRank == field.ValueRank
                };
                if (input.Name != field.Name || !typeMatches || !rankMatches)
                {
                    throw new ArgumentException(
                        "The task input names, types or ranks do not match the offered form.", nameof(inputs));
                }
                if (value.TryGetValue(out string? text))
                {
                    if (field.Required && string.IsNullOrWhiteSpace(text))
                    {
                        throw new ArgumentException($"{field.DisplayName} is required.", nameof(inputs));
                    }
                    textLength = checked(textLength + (text?.Length ?? 0));
                }
                if (textLength > 65536)
                {
                    throw new ArgumentException("Task input is limited to 65536 characters.", nameof(inputs));
                }
            }
        }

        public static async Task<ArrayOf<CompanionInputSchema>> ValidateAsync(
            CompanionContext context,
            CompanionOperation operation,
            ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateShape(operation, inputs);
            using var values = new SessionStructuredValueService(context.Session);
            var schemas = new List<CompanionInputSchema>();
            int encodedLength = 0;
            for (int index = 0; index < inputs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CompanionInputDefinition field = operation.Inputs[index];
                CompanionValue input = inputs[index];
                Variant value = input.Value;
                NodeId typeId = ResolveType(field, context.Session.NamespaceUris);
                if (!field.DataTypeId.IsNull)
                {
                    DataTypeDefinition definition =
                        await values.ResolveAsync(typeId, cancellationToken).ConfigureAwait(false) ??
                        throw new ServiceResultException(
                            StatusCodes.BadDataTypeIdUnknown, "The task input definition is unavailable.");
                    CompanionInputSchemaGraph graph = await CompanionInputSchemaGraph.ResolveAsync(
                        typeId, definition, values, cancellationToken).ConfigureAwait(false);
                    await graph.ValidateAsync(typeId, value, field.ValueRank, field.ArrayDimensions, cancellationToken)
                        .ConfigureAwait(false);
                    ByteString digest = graph.GetDigest();
                    if (!input.InputSchemaDigest.IsNull && input.InputSchemaDigest != digest)
                    {
                        throw new InvalidOperationException("The typed input definition changed. Edit it again.");
                    }
                    schemas.Add(new CompanionInputSchema(field.Name, digest));
                }
                else if (!value.TypeInfo.IsScalar)
                {
                    var array = new StructuredArrayDraft(
                        field.DataType, field.ValueRank, field.ArrayDimensions, value, values.MessageContext,
                        cancellationToken.ThrowIfCancellationRequested);
                    if (array.InitialValue.Elements.Count > StructuredArrayDraft.MaximumElementCount)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                }
                encodedLength = checked(encodedLength +
                    DataValueCodec.EncodeVariant(value, EncodingFormat.Binary, values.MessageContext).Length);
                if (encodedLength > MaximumEncodedBytes)
                {
                    throw new ArgumentException(
                        "The prepared task exceeds its bounded input capacity.", nameof(inputs));
                }
            }
            return [.. schemas];
        }

        public const int MaximumEncodedBytes = 1024 * 1024;
    }
}
