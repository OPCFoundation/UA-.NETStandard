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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Opc.Ua.WotCon.V2;

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
            => index < inputArguments.Count ? inputArguments[index] : Variant.Null;

        private static ServiceResult TryDecodeSelection(
            Variant value,
            IServiceMessageContext context,
            out ImmutableArray<WoTResourceSelectorDataType> selectors)
        {
            selectors = ImmutableArray<WoTResourceSelectorDataType>.Empty;
            if (value.IsNull)
            {
                return ServiceResult.Good;
            }

            var builder = ImmutableArray.CreateBuilder<WoTResourceSelectorDataType>();
            foreach (object? element in Enumerate(value.AsBoxedObject(Variant.BoxingBehavior.Legacy)))
            {
                if (element is null)
                {
                    continue;
                }
                ServiceResult status = TryCoerce(
                    element, context, out WoTResourceSelectorDataType? selector);
                if (ServiceResult.IsBad(status) || selector is null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "The Selection argument must be an array of WoTResourceSelectorDataType.");
                }
                builder.Add(selector);
            }
            selectors = builder.ToImmutable();
            return ServiceResult.Good;
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
            ServiceResult status = TryCoerce(
                value.AsBoxedObject(Variant.BoxingBehavior.Legacy), context, out options);
            if (ServiceResult.IsBad(status) || options is null)
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
            switch (value.AsBoxedObject(Variant.BoxingBehavior.Legacy))
            {
                case uint u:
                    result = u;
                    return ServiceResult.Good;
                case int i when i >= 0:
                    result = (uint)i;
                    return ServiceResult.Good;
                case long l when l >= 0 && l <= uint.MaxValue:
                    result = (uint)l;
                    return ServiceResult.Good;
                case ushort us:
                    result = us;
                    return ServiceResult.Good;
                case byte b:
                    result = b;
                    return ServiceResult.Good;
                default:
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "The ExpectedGeneration argument must be a UInt32.");
            }
        }

        private static ServiceResult TryDecodeString(Variant value, out string? result)
        {
            result = null;
            if (value.IsNull)
            {
                return ServiceResult.Good;
            }
            if (value.AsBoxedObject(Variant.BoxingBehavior.Legacy) is string s)
            {
                result = s;
                return ServiceResult.Good;
            }
            return ServiceResult.Create(
                StatusCodes.BadInvalidArgument,
                "The RequestId argument must be a String.");
        }

        private static IEnumerable<object?> Enumerate(object? boxed)
        {
            switch (boxed)
            {
                case null:
                    yield break;
                case ExtensionObject single:
                    yield return single;
                    break;
                case IConvertableToArray convertible:
                    Array? array = convertible.ToArray();
                    if (array is not null)
                    {
                        foreach (object? item in array)
                        {
                            yield return item;
                        }
                    }
                    break;
                case IEnumerable enumerable when boxed is not string:
                    foreach (object? item in enumerable)
                    {
                        yield return item;
                    }
                    break;
                default:
                    yield return boxed;
                    break;
            }
        }

        private static ServiceResult TryCoerce<T>(
            object? element,
            IServiceMessageContext context,
            out T? value)
            where T : class, IEncodeable, new()
        {
            value = null;
            switch (element)
            {
                case T typed:
                    value = typed;
                    return ServiceResult.Good;
                case ExtensionObject extension:
                    return TryDecodeExtensionObject(extension, context, out value);
                default:
                    return StatusCodes.BadInvalidArgument;
            }
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
            if (extension.TryGetValue(out T? typed))
            {
                value = typed;
                return ServiceResult.Good;
            }
            if (extension.TryGetAsBinary(out ByteString binary) && !binary.IsNull)
            {
                try
                {
                    using var decoder = new BinaryDecoder(binary.ToArray(), context);
                    value = decoder.ReadEncodeable<T>(null);
                    return ServiceResult.Good;
                }
                catch (Exception ex) when (
                    ex is ServiceResultException or FormatException or InvalidOperationException)
                {
                    return ServiceResult.Create(
                        ex, StatusCodes.BadInvalidArgument,
                        "The encoded argument body could not be decoded.");
                }
            }
            return StatusCodes.BadInvalidArgument;
        }
    }
}
