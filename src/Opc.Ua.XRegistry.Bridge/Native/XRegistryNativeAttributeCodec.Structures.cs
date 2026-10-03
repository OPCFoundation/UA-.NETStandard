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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.Encoders;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public static partial class XRegistryNativeAttributeCodec
    {
        /// <summary>
        /// Decodes a value using a profile, including explicitly registered, field-accessible native structures.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        public static JsonElement Decode(
            in Variant value, JsonElement definition, XRegistryNativeAttributeMapping mapping)
        {
            mapping.ThrowIfNull(nameof(mapping));
            if (mapping.StructureType is null)
            {
                return Decode(value, definition, mapping.Encoding, mapping.NativeType);
            }
            RequireStructureMapping(mapping);
            if (!value.TryGetValue(out ExtensionObject extension) ||
                !extension.TryGetValue(out IEncodeable? body) ||
                body.TypeId != mapping.StructureTypeId ||
                body is not IStructure structure ||
                body is IStructureTypeInfo
                {
                    StructureType: StructureType.Union or StructureType.UnionWithSubtypedValues
                })
            {
                throw new InvalidDataException(
                    "A mapped structure must be decoded using the registered expected type.");
            }
            JsonObject result = DecodeStructure(structure, definition);
            using var document = JsonDocument.Parse(result.ToJsonString());
            XRegistryAttributeModel.Validate(document.RootElement, definition);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Encodes a value using a profile and an explicit native structure activator when configured.
        /// Unsupported fields or unknown logical members reject instead of being defaulted or dropped.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        public static Variant Encode(
            JsonElement value, JsonElement definition, XRegistryNativeAttributeMapping mapping)
        {
            mapping.ThrowIfNull(nameof(mapping));
            if (mapping.StructureType is not { } activator)
            {
                return Encode(value, definition, mapping.Encoding, mapping.NativeType);
            }
            RequireStructureMapping(mapping);
            XRegistryAttributeModel.Validate(value, definition);
            IEncodeable instance = activator.CreateInstance();
            if (instance.TypeId != mapping.StructureTypeId ||
                instance is not IStructure structure ||
                value.ValueKind != JsonValueKind.Object ||
                instance is IStructureTypeInfo
                {
                    StructureType: StructureType.Union or StructureType.UnionWithSubtypedValues
                })
            {
                throw new InvalidDataException("A mapped structured value requires explicit non-union field access.");
            }
            IReadOnlyList<IStructureField> fields = structure.GetFields();
            var names = new HashSet<string>();
            for (int index = 0; index < fields.Count; index++)
            {
                IStructureField field = fields[index];
                string name = field.Name ?? throw new InvalidDataException("A registered structure field has no name.");
                names.Add(name);
                if (!value.TryGetProperty(name, out JsonElement child))
                {
                    if (!field.IsOptional)
                    {
                        throw new InvalidDataException("A non-optional native structure field has no logical value.");
                    }
                    continue;
                }
                JsonElement rule = StructureFieldRule(definition, value, name);
                if (field.IsOptional && child.ValueKind == JsonValueKind.Null)
                {
                    throw new InvalidDataException(
                        "Optional native fields cannot distinguish explicit null from absence.");
                }
                structure[index] = Encode(
                    child, rule, XRegistryNativeAttributeEncoding.Typed, field.TypeInfo.BuiltInType);
                if (structure[index].TypeInfo.ValueRank != field.TypeInfo.ValueRank)
                {
                    throw new InvalidDataException("A native structure field has incompatible logical array rank.");
                }
            }
            if (value.EnumerateObject().Any(property => !names.Contains(property.Name)))
            {
                throw new InvalidDataException(
                    "A logical object contains fields not present in its registered native structure.");
            }
            return Variant.From(new ExtensionObject(instance));
        }

        private static JsonObject DecodeStructure(IStructure structure, JsonElement definition)
        {
            var result = new JsonObject();
            var pending = new List<(string Name, IStructureField Field, Variant Value)>();
            IReadOnlyList<IStructureField> fields = structure.GetFields();
            int optionalIndex = 0;
            for (int index = 0; index < fields.Count; index++)
            {
                IStructureField field = fields[index];
                string name = field.Name ?? throw new InvalidDataException("A registered structure field has no name.");
                Variant value = structure[index];
                if (field.IsOptional)
                {
                    if (optionalIndex >= 32)
                    {
                        throw new InvalidDataException(
                            "The registered optional-field mask exceeds its supported width.");
                    }
                    bool present = structure is StructureWithOptionalFields optional
                        ? (optional.EncodingMask & (1u << optionalIndex)) != 0
                        : !value.IsNull;
                    optionalIndex++;
                    if (!present)
                    {
                        if (structure is not StructureWithOptionalFields)
                        {
                            throw new InvalidDataException(
                                "The registered type does not expose optional-field presence.");
                        }
                        continue;
                    }
                }
                if (!value.IsNull && value.TypeInfo.ValueRank != field.TypeInfo.ValueRank)
                {
                    throw new InvalidDataException("A native structure value does not match its declared field rank.");
                }
                result.Add(name, ToNode(value));
                pending.Add((name, field, value));
            }
            using var metadata = JsonDocument.Parse(result.ToJsonString());
            foreach ((string name, IStructureField field, Variant value) in pending)
            {
                JsonElement rule = StructureFieldRule(definition, metadata.RootElement, name);
                _ = Decode(value, rule, XRegistryNativeAttributeEncoding.Typed, field.TypeInfo.BuiltInType);
            }
            return result;
        }

        private static JsonElement StructureFieldRule(JsonElement definition, JsonElement value, string name)
        {
            if (!TryStructureFieldRule(definition, value, name, out JsonElement rule))
            {
                throw new InvalidDataException("Every native structure field requires a matching logical model field.");
            }
            return rule;
        }

        private static bool TryStructureFieldRule(
            JsonElement definition, JsonElement value, string name, out JsonElement rule)
        {
            rule = default;
            return definition.GetProperty("type").GetString() == "object" &&
                definition.TryGetProperty("attributes", out JsonElement attributes) &&
                XRegistryAttributeModel.TryResolve(attributes, value, [name], out rule);
        }

        private static void RequireStructureMapping(XRegistryNativeAttributeMapping mapping)
        {
            if (mapping.Encoding != XRegistryNativeAttributeEncoding.Typed ||
                mapping.NativeType is not (BuiltInType.Null or BuiltInType.ExtensionObject))
            {
                throw new InvalidDataException("A registered structure requires typed ExtensionObject encoding.");
            }
        }
    }
}
