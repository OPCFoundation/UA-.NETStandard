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

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Applies the Write semantics of the OptionSet Structure DataType (Part 3 8.40) to a
    /// Value written through the Write service.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The Value and ValidBits ByteStrings must have the same size, and the size
    /// must match the OptionSet currently stored in the Variable; otherwise
    /// Bad_OutOfRange.</item>
    /// <item>ValidBits must not select a bit the Server does not consider valid (the
    /// ValidBits of the stored value, or the bits defined by the OptionSet DataType);
    /// otherwise Bad_OutOfRange and no bit is changed.</item>
    /// <item>The written bits are merged into the stored value:
    /// new value = (value &amp; validBits) | (current value &amp; ~validBits).</item>
    /// </list>
    /// </remarks>
    internal static class OptionSetWriteMerge
    {
        /// <summary>
        /// Validates an OptionSet written to <paramref name="variable"/> and replaces
        /// <paramref name="value"/> with the merged OptionSet.
        /// </summary>
        /// <param name="variable">The Variable that is written.</param>
        /// <param name="indexRange">The index range of the write.</param>
        /// <param name="value">The written value; replaced by the merged value.</param>
        /// <returns><c>null</c> if the write may proceed; the error otherwise.</returns>
        public static ServiceResult? Apply(
            BaseVariableState variable,
            NumericRange indexRange,
            ref DataValue value)
        {
            // only a whole scalar OptionSet is merged; the access checks run first so the
            // caller still sees Bad_NotWritable for read-only variables.
            if (!indexRange.IsNull ||
                (variable.AccessLevelEx & AccessLevels.CurrentWrite) == 0 ||
                (variable.UserAccessLevel & AccessLevels.CurrentWrite) == 0 ||
                !value.WrappedValue.TypeInfo.IsScalar ||
                !value.WrappedValue.TryGetStructure<OptionSet>(out OptionSet? written) ||
                written == null)
            {
                return null;
            }

            ReadOnlySpan<byte> writtenValue = written.Value.Span;
            ReadOnlySpan<byte> writtenValid = written.ValidBits.Span;

            if (writtenValue.Length != writtenValid.Length)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The Value and ValidBits of the OptionSet have a different size.");
            }

            OptionSet? current = null;
            if (variable.Value.TryGetStructure<OptionSet>(out OptionSet? stored) &&
                stored != null &&
                !stored.Value.IsEmpty)
            {
                current = stored;
            }

            int expectedLength = current?.Value.Length ??
                (written is Encoders.OptionSet runtimeType ? runtimeType.ByteLength : -1);

            if (expectedLength > 0 && writtenValue.Length != expectedLength)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The size of the OptionSet does not match the OptionSet of the Variable.");
            }

            byte[]? allowed = GetValidBitMask(current, written, writtenValue.Length);

            if (allowed != null)
            {
                for (int ii = 0; ii < writtenValid.Length; ii++)
                {
                    if ((writtenValid[ii] & ~allowed[ii]) != 0)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadOutOfRange,
                            "The ValidBits of the OptionSet select a bit that is not valid.");
                    }
                }
            }

            if (current == null)
            {
                return null;
            }

            ReadOnlySpan<byte> currentValue = current.Value.Span;
            byte[] merged = new byte[writtenValue.Length];

            for (int ii = 0; ii < merged.Length; ii++)
            {
                merged[ii] = (byte)((writtenValue[ii] & writtenValid[ii]) |
                    (currentValue[ii] & ~writtenValid[ii]));
            }

            var result = (OptionSet)written.Clone();
            result.Value = ByteString.From(merged);

            // the ValidBits of the stored value describe which bits have a meaning.
            if (HasAnyBit(current.ValidBits.Span))
            {
                result.ValidBits = current.ValidBits;
            }

            value = new DataValue(
                new Variant(new ExtensionObject(result)),
                value.StatusCode,
                value.SourceTimestamp,
                value.ServerTimestamp);

            return null;
        }

        /// <summary>
        /// Returns the bits the Server considers valid, or <c>null</c> if they are unknown.
        /// </summary>
        private static byte[]? GetValidBitMask(OptionSet? current, OptionSet written, int length)
        {
            // an all-zero ValidBits of the stored value carries no information (for example a
            // value initialized without ValidBits), so fall back to the DataType definition.
            if (current != null &&
                current.ValidBits.Length == length &&
                length > 0 &&
                HasAnyBit(current.ValidBits.Span))
            {
                return current.ValidBits.ToArray();
            }

            EnumDefinition? definition = (written as Encoders.OptionSet)?.Definition ??
                (current as Encoders.OptionSet)?.Definition;

            if (definition == null || definition.Fields.IsEmpty)
            {
                return null;
            }

            byte[] mask = new byte[length];

            foreach (EnumField field in definition.Fields)
            {
                long bit = field.Value;
                if (bit >= 0 && bit < (long)length * 8)
                {
                    mask[bit >> 3] |= (byte)(1 << (int)(bit & 7));
                }
            }

            return mask;
        }

        private static bool HasAnyBit(ReadOnlySpan<byte> bytes)
        {
            foreach (byte b in bytes)
            {
                if (b != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
