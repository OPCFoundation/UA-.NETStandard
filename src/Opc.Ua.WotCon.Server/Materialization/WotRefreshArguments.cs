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
using System.Collections.Immutable;
using Opc.Ua.Encoders;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Decodes the four <c>WoTRegistryType.Refresh</c> input arguments -
    /// <c>Selection</c> (<see cref="WoTResourceSelectorDataType"/><c>[]</c>),
    /// <c>Options</c> (<see cref="WoTRefreshOptionsDataType"/>),
    /// <c>ExpectedGeneration</c> (<see cref="uint"/>) and <c>RequestId</c>
    /// (<see cref="string"/>) - into a <see cref="WotRefreshRequest"/>. Structured
    /// arguments are accepted in every form a caller may present them: an already
    /// decoded encodeable, an <see cref="ExtensionObject"/> wrapping the
    /// encodeable, a binary-encoded ExtensionObject body, and both plain-array and
    /// <see cref="ArrayOf{T}"/> array containers. A value whose type does not match
    /// the argument's schema is rejected with
    /// <see cref="StatusCodes.BadInvalidArgument"/> rather than silently ignored.
    /// </summary>
    internal static class WotRefreshArguments
    {
        /// <summary>
        /// Decodes the Refresh input arguments into a <see cref="WotRefreshRequest"/>.
        /// </summary>
        /// <param name="inputArguments">The raw input argument variants.</param>
        /// <param name="context">The message context used to decode encoded bodies.</param>
        /// <param name="request">The decoded request on success.</param>
        /// <returns>
        /// <see cref="ServiceResult.Good"/> on success, or
        /// <see cref="StatusCodes.BadInvalidArgument"/> when an argument is present
        /// but has the wrong type.
        /// </returns>
        public static ServiceResult TryDecode(
            ArrayOf<Variant> inputArguments,
            IServiceMessageContext context,
            out WotRefreshRequest request)
        {
            request = new WotRefreshRequest();

            ServiceResult selection = TryDecodeSelection(
                ArgumentAt(inputArguments, 0), context,
                out ImmutableArray<WoTResourceSelectorDataType> selectors);
            if (ServiceResult.IsBad(selection))
            {
                return selection;
            }

            ServiceResult options = TryDecodeStructure(
                ArgumentAt(inputArguments, 1), context,
                out WoTRefreshOptionsDataType? decodedOptions);
            if (ServiceResult.IsBad(options))
            {
                return options;
            }

            ServiceResult generation = TryDecodeUInt32(
                ArgumentAt(inputArguments, 2), out uint expectedGeneration);
            if (ServiceResult.IsBad(generation))
            {
                return generation;
            }

            ServiceResult requestId = TryDecodeString(
                ArgumentAt(inputArguments, 3), out string? id);
            if (ServiceResult.IsBad(requestId))
            {
                return requestId;
            }

            request = new WotRefreshRequest
            {
                Selection = selectors,
                Options = decodedOptions ?? new WoTRefreshOptionsDataType(),
                ExpectedGeneration = expectedGeneration,
                RequestId = id ?? string.Empty
            };
            return ServiceResult.Good;
        }

        private static Variant ArgumentAt(ArrayOf<Variant> inputArguments, int index)
        {
            return index < inputArguments.Count ? inputArguments[index] : Variant.Null;
        }

        private static ServiceResult TryDecodeSelection(
            Variant value,
            IServiceMessageContext context,
            out ImmutableArray<WoTResourceSelectorDataType> selectors)
        {
            selectors = [];
            if (value.IsNull)
            {
                return ServiceResult.Good;
            }

            if (value.TryGetStructure<WoTResourceSelectorDataType>(
                context, out ArrayOf<WoTResourceSelectorDataType> decoded))
            {
                foreach (WoTResourceSelectorDataType selector in decoded)
                {
                    if (selector is null)
                    {
                        return InvalidSelection();
                    }
                }
                selectors = [.. decoded.ToList()];
                return ServiceResult.Good;
            }
            if (!value.TryGetValue(out ArrayOf<ExtensionObject> extensions))
            {
                if (!value.TryGetValue(out ExtensionObject single))
                {
                    return InvalidSelection();
                }
                extensions = [single];
            }
            ImmutableArray<WoTResourceSelectorDataType>.Builder builder =
                ImmutableArray.CreateBuilder<WoTResourceSelectorDataType>();
            foreach (ExtensionObject element in extensions)
            {
                ServiceResult status = TryDecodeExtensionObject(
                    element, context, out WoTResourceSelectorDataType? selector);
                if (ServiceResult.IsBad(status) || selector is null)
                {
                    return InvalidSelection();
                }
                builder.Add(selector);
            }
            selectors = builder.ToImmutable();
            return ServiceResult.Good;
        }

        private static ServiceResult InvalidSelection()
        {
            return ServiceResult.Create(
                StatusCodes.BadInvalidArgument,
                "The Selection argument must be an array of WoTResourceSelectorDataType.");
        }

        private static ServiceResult TryDecodeStructure(
            Variant value,
            IServiceMessageContext context,
            out WoTRefreshOptionsDataType? options)
        {
            options = null;
            if (value.IsNull)
            {
                return ServiceResult.Good;
            }
            if (value.TryGetStructure<WoTRefreshOptionsDataType>(context, out options))
            {
                return ServiceResult.Good;
            }
            if (!value.TryGetValue(out ExtensionObject extension) ||
                ServiceResult.IsBad(TryDecodeExtensionObject(extension, context, out options)) || options is null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The Options argument must be a single WoTRefreshOptionsDataType.");
            }
            return ServiceResult.Good;
        }

        private static ServiceResult TryDecodeUInt32(Variant value, out uint result)
        {
            result = 0;
            if (value.IsNull)
            {
                return ServiceResult.Good;
            }
            if (value.TryGetValue(out result))
            {
                return ServiceResult.Good;
            }
            if (value.TryGetValue(out int signed) && signed >= 0)
            {
                result = (uint)signed;
                return ServiceResult.Good;
            }
            if (value.TryGetValue(out long wide) && wide is >= 0 and <= uint.MaxValue)
            {
                result = (uint)wide;
                return ServiceResult.Good;
            }
            if (value.TryGetValue(out ushort narrow))
            {
                result = narrow;
                return ServiceResult.Good;
            }
            if (value.TryGetValue(out byte smallest))
            {
                result = smallest;
                return ServiceResult.Good;
            }
            return ServiceResult.Create(
                StatusCodes.BadInvalidArgument,
                "The ExpectedGeneration argument must be a UInt32.");
        }

        private static ServiceResult TryDecodeString(Variant value, out string? result)
        {
            result = null;
            if (value.IsNull)
            {
                return ServiceResult.Good;
            }
            if (value.TryGetValue(out string s))
            {
                result = s;
                return ServiceResult.Good;
            }
            return ServiceResult.Create(
                StatusCodes.BadInvalidArgument,
                "The RequestId argument must be a String.");
        }

        private static ServiceResult TryDecodeExtensionObject<T>(
            ExtensionObject extension,
            IServiceMessageContext context,
            out T? value)
            where T : class, IEncodeable, new()
        {
            value = null;
            if (extension.IsNull)
            {
                return StatusCodes.BadInvalidArgument;
            }
#pragma warning disable IDE0018, IDE0059 // must stay a separate, non-inlined out-var (see IDE0001 note below)
            T? typed = default;
#pragma warning disable IDE0001 // explicit <T> avoids CS8631/CS8634: inference from a nullable out-var picks 'T?'
            if (new Variant(extension).TryGetStructure<T>(context, out typed))
#pragma warning restore IDE0001, IDE0018, IDE0059
            {
                value = typed;
                return ServiceResult.Good;
            }
            if (extension.TryGetValue(out Structure? structure, context) && structure is not null)
            {
                if (typeof(T) == typeof(WoTRefreshOptionsDataType) &&
                    TryDecodeDynamicOptions(structure, out WoTRefreshOptionsDataType? options))
                {
                    value = (T)(IEncodeable)options!;
                    return ServiceResult.Good;
                }
                if (typeof(T) == typeof(WoTResourceSelectorDataType) &&
                    TryDecodeDynamicSelector(structure, out WoTResourceSelectorDataType? selector))
                {
                    value = (T)(IEncodeable)selector!;
                    return ServiceResult.Good;
                }
            }
            if (extension.TryGetAsBinary(out ByteString body, context) && !body.IsNull)
            {
                try
                {
                    using var decoder = new BinaryDecoder(body.Span.ToArray(), context);
                    value = new T();
                    value.Decode(decoder);
                    return ServiceResult.Good;
                }
                catch (Exception ex) when (ex is ServiceResultException or FormatException)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "The encoded argument body could not be decoded.");
                }
            }
            return ServiceResult.Create(
                StatusCodes.BadInvalidArgument,
                "The encoded argument body could not be decoded.");
        }

        private static bool TryDecodeDynamicSelector(
            Structure structure,
            out WoTResourceSelectorDataType? selector)
        {
            selector = null;
            if (structure.TypeId != DataTypeIds.WoTResourceSelectorDataType ||
                structure.BinaryEncodingId != ObjectIds.WoTResourceSelectorDataType_Encoding_DefaultBinary ||
                !structure["Kind"].TryGetValue(out WoTDocumentKindEnum kind) ||
                !TryGetSelectorString(structure, "Xid", out string xid) ||
                !TryGetSelectorString(structure, "GroupId", out string groupId) ||
                !TryGetSelectorString(structure, "ResourceId", out string resourceId) ||
                !TryGetSelectorString(structure, "VersionId", out string versionId))
            {
                return false;
            }
            selector = new WoTResourceSelectorDataType
            {
                Kind = kind,
                Xid = xid,
                GroupId = groupId,
                ResourceId = resourceId,
                VersionId = versionId
            };
            return true;
        }

        private static bool TryGetSelectorString(Structure structure, string name, out string value)
        {
            Variant field = structure[name];
            if (field.IsNull)
            {
                value = string.Empty;
                return true;
            }
            return field.TryGetValue(out value);
        }

        private static bool TryDecodeDynamicOptions(
            Structure structure,
            out WoTRefreshOptionsDataType? options)
        {
            options = null;
            if (structure.TypeId != DataTypeIds.WoTRefreshOptionsDataType ||
                structure.BinaryEncodingId != ObjectIds.WoTRefreshOptionsDataType_Encoding_DefaultBinary)
            {
                return false;
            }
            options = new WoTRefreshOptionsDataType
            {
                Atomicity = GetEnum<WoTAtomicityEnum>(structure, "Atomicity"),
                Force = GetBoolean(structure, "Force"),
                DryRun = GetBoolean(structure, "DryRun"),
                IncludeDependents = GetBoolean(structure, "IncludeDependents"),
                DeletePolicy = GetEnum<WoTDeletePolicyEnum>(structure, "DeletePolicy"),
                MaxParallelism = GetUInt32(structure, "MaxParallelism"),
                Timeout = GetDouble(structure, "Timeout")
            };
            return true;
        }

        private static bool GetBoolean(Structure structure, string fieldName)
            => structure[fieldName].TryGetValue(out bool value) && value;

        private static double GetDouble(Structure structure, string fieldName)
            => structure[fieldName].TryGetValue(out double value) ? value : 0;

        private static TEnum GetEnum<TEnum>(Structure structure, string fieldName)
            where TEnum : struct, Enum
        {
            return structure[fieldName].TryGetValue(out TEnum value)
                ? value
                : default;
        }

        private static uint GetUInt32(Structure structure, string fieldName)
            => structure[fieldName].TryGetValue(out uint value) ? value : 0;
    }
}
