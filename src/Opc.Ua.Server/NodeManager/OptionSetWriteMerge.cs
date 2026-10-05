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
    /// ValidBits of the stored value, or the bits defined by the OptionSet DataType when
    /// the stored value has no ValidBits); otherwise Bad_OutOfRange and no bit is
    /// changed.</item>
    /// <item>The written bits are merged into the stored value:
    /// new value = (value &amp; validBits) | (current value &amp; ~validBits).</item>
    /// </list>
    /// The rules apply to a scalar OptionSet and to every element of an OptionSet array or
    /// matrix, including the elements addressed by an IndexRange; each element is merged
    /// with the stored element at the same position.
    /// </remarks>
    internal static class OptionSetWriteMerge
    {
        /// <summary>
        /// Validates an OptionSet written to <paramref name="variable"/> and replaces
        /// <paramref name="value"/> with the merged OptionSet(s).
        /// </summary>
        /// <param name="context">The context of the write (resolves the UserAccessLevel).</param>
        /// <param name="variable">The Variable that is written.</param>
        /// <param name="indexRange">The index range of the write.</param>
        /// <param name="value">The written value; replaced by the merged value.</param>
        /// <returns><c>null</c> if the write may proceed; the error otherwise.</returns>
        public static ServiceResult? Apply(
            ISystemContext context,
            BaseVariableState variable,
            NumericRange indexRange,
            ref DataValue value)
        {
            // the access checks run first (in WriteAttribute) so the caller still sees
            // Bad_NotWritable / Bad_UserAccessDenied for variables it cannot write.
            if ((variable.AccessLevelEx & AccessLevels.CurrentWrite) == 0)
            {
                return null;
            }

            // the effective UserAccessLevel, as resolved by the write path.
            byte userAccessLevel = variable.UserAccessLevel;
            variable.OnReadUserAccessLevel?.Invoke(context, variable, ref userAccessLevel);

            if ((userAccessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return null;
            }

            Variant written = value.WrappedValue;

            if (written.IsNull || written.TypeInfo.BuiltInType != BuiltInType.ExtensionObject)
            {
                return null;
            }

            if (written.TypeInfo.IsScalar)
            {
                // an IndexRange cannot address a part of a scalar Structure; WriteAttribute
                // reports that error.
                if (!indexRange.IsNull ||
                    !written.TryGetStructure<OptionSet>(out OptionSet? writtenScalar) ||
                    writtenScalar == null)
                {
                    return null;
                }

                OptionSet? stored = variable.Value.TypeInfo.IsScalar
                    ? GetOptionSet(variable.Value)
                    : null;

                ServiceResult? scalarResult = MergeElement(
                    writtenScalar,
                    stored,
                    stored,
                    out OptionSet? mergedScalar);

                if (scalarResult != null)
                {
                    return scalarResult;
                }

                if (mergedScalar != null)
                {
                    value = WithValue(value, new Variant(new ExtensionObject(mergedScalar)));
                }

                return null;
            }

            ExtensionObject[] elements;
            int[]? dimensions = null;

            if (written.TypeInfo.IsArray &&
                written.TryGetValue(out ArrayOf<ExtensionObject> writtenArray))
            {
                elements = writtenArray.ToArray() ?? [];
            }
            else if (written.TypeInfo.IsMatrix &&
                written.TryGetValue(out MatrixOf<ExtensionObject> writtenMatrix))
            {
                elements = writtenMatrix.Span.ToArray();
                dimensions = writtenMatrix.Dimensions;
            }
            else
            {
                return null;
            }

            ExtensionObject[] storedElements = GetStoredElements(variable.Value, indexRange);

            // the reference for the size and the valid bits of elements that have no stored
            // counterpart (for example an array that grows).
            OptionSet? reference = null;
            foreach (ExtensionObject storedElement in storedElements)
            {
                reference = GetOptionSet(new Variant(storedElement));
                if (reference != null)
                {
                    break;
                }
            }

            bool changed = false;

            for (int ii = 0; ii < elements.Length; ii++)
            {
                if (!new Variant(elements[ii]).TryGetStructure<OptionSet>(out OptionSet? element) ||
                    element == null)
                {
                    continue;
                }

                OptionSet? current = ii < storedElements.Length
                    ? GetOptionSet(new Variant(storedElements[ii]))
                    : null;

                ServiceResult? elementResult = MergeElement(
                    element,
                    current,
                    current ?? reference,
                    out OptionSet? merged);

                if (elementResult != null)
                {
                    return elementResult;
                }

                if (merged != null)
                {
                    elements[ii] = new ExtensionObject(merged);
                    changed = true;
                }
            }

            if (changed)
            {
                ArrayOf<ExtensionObject> mergedArray = elements;
                value = WithValue(
                    value,
                    dimensions == null
                        ? new Variant(mergedArray)
                        : new Variant(mergedArray.ToMatrix(dimensions)));
            }

            return null;
        }

        /// <summary>
        /// Validates one written OptionSet and merges it with the stored OptionSet.
        /// </summary>
        /// <param name="written">The written OptionSet.</param>
        /// <param name="current">The stored OptionSet at the same position, if any.</param>
        /// <param name="reference">The stored OptionSet that defines the expected size and
        /// the valid bits (the current one, or another element of the same Variable).</param>
        /// <param name="merged">The merged OptionSet, or <c>null</c> if the written value
        /// is used as is.</param>
        /// <returns><c>null</c> if the element may be written; the error otherwise.</returns>
        private static ServiceResult? MergeElement(
            OptionSet written,
            OptionSet? current,
            OptionSet? reference,
            out OptionSet? merged)
        {
            merged = null;

            ReadOnlySpan<byte> writtenValue = written.Value.Span;
            ReadOnlySpan<byte> writtenValid = written.ValidBits.Span;

            if (writtenValue.Length != writtenValid.Length)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The Value and ValidBits of the OptionSet have a different size.");
            }

            int expectedLength = reference?.Value.Length ??
                (written is Encoders.OptionSet runtimeType ? runtimeType.ByteLength : -1);

            if (expectedLength > 0 && writtenValue.Length != expectedLength)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The size of the OptionSet does not match the OptionSet of the Variable.");
            }

            byte[]? allowed = GetValidBitMask(reference, written, writtenValue.Length);

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
            byte[] mergedValue = new byte[writtenValue.Length];

            for (int ii = 0; ii < mergedValue.Length; ii++)
            {
                mergedValue[ii] = (byte)((writtenValue[ii] & writtenValid[ii]) |
                    (currentValue[ii] & ~writtenValid[ii]));
            }

            var result = (OptionSet)written.Clone();
            result.Value = ByteString.From(mergedValue);

            // the ValidBits of the stored value describe which bits have a meaning.
            if (HasValidBits(current, mergedValue.Length))
            {
                result.ValidBits = current.ValidBits;
            }

            merged = result;
            return null;
        }

        /// <summary>
        /// Returns the bits the Server considers valid, or <c>null</c> if they are unknown.
        /// </summary>
        private static byte[]? GetValidBitMask(OptionSet? stored, OptionSet written, int length)
        {
            // populated ValidBits of the stored value define the valid bits, even if no bit
            // is set (then no bit may be written). Only an absent mask falls back to the
            // DataType definition.
            if (stored != null && HasValidBits(stored, length))
            {
                return stored.ValidBits.ToArray();
            }

            EnumDefinition? definition = (written as Encoders.OptionSet)?.Definition ??
                (stored as Encoders.OptionSet)?.Definition;

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

        /// <summary>
        /// Returns <c>true</c> if the stored OptionSet carries a ValidBits mask of the
        /// expected size.
        /// </summary>
        private static bool HasValidBits(OptionSet stored, int length)
        {
            return length > 0 && stored.ValidBits.Length == length;
        }

        /// <summary>
        /// Returns the stored OptionSet, or <c>null</c> if the value is not a non-empty
        /// OptionSet.
        /// </summary>
        private static OptionSet? GetOptionSet(in Variant value)
        {
            if (value.TryGetStructure<OptionSet>(out OptionSet? stored) &&
                stored != null &&
                !stored.Value.IsEmpty)
            {
                return stored;
            }

            return null;
        }

        /// <summary>
        /// Returns the stored elements that correspond to the written elements: the whole
        /// stored array or matrix, or the elements addressed by the IndexRange. Returns an
        /// empty array if the stored value is not an array or matrix of Structures or the
        /// IndexRange does not address it.
        /// </summary>
        private static ExtensionObject[] GetStoredElements(Variant stored, NumericRange indexRange)
        {
            if (stored.IsNull ||
                stored.TypeInfo.BuiltInType != BuiltInType.ExtensionObject ||
                stored.TypeInfo.IsScalar)
            {
                return [];
            }

            if (!indexRange.IsNull && StatusCode.IsBad(indexRange.ApplyRange(ref stored)))
            {
                return [];
            }

            if (stored.TypeInfo.IsArray && stored.TryGetValue(out ArrayOf<ExtensionObject> array))
            {
                return array.ToArray() ?? [];
            }

            if (stored.TypeInfo.IsMatrix && stored.TryGetValue(out MatrixOf<ExtensionObject> matrix))
            {
                return matrix.Span.ToArray();
            }

            return [];
        }

        private static DataValue WithValue(DataValue value, Variant newValue)
        {
            return new DataValue(
                newValue,
                value.StatusCode,
                value.SourceTimestamp,
                value.ServerTimestamp);
        }
    }
}
