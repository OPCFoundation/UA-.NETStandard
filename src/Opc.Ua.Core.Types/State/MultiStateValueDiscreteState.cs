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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Enforces the MultiStateValueDiscreteType write contract from OPC UA Part 8
    /// section 5.3.3.4: the numeric value written must match one of the entries in the
    /// mandatory EnumValues array (which, unlike EnumStrings, may be sparse or not
    /// zero-based). Writes that do not match an entry are rejected with BadOutOfRange,
    /// and on a successful scalar write the ValueAsText property is updated with the
    /// matching entry's display name. Access level, data type and index range
    /// validation continue to be handled by the base class.
    /// </summary>
    public partial class MultiStateValueDiscreteState
    {
        /// <summary>
        /// Validates a scalar enumeration against its declared type, rank, values and optional dictionary matrix.
        /// </summary>
        public ServiceResult ValidateEnumValue(ISystemContext context, in Variant value)
        {
            return ValidateEnumValue(context, value, out _, out _, out _);
        }

        /// <summary>
        /// Publishes an acquired enumeration and its derived text/dictionary values without invoking a write handler.
        /// </summary>
        /// <remarks>
        /// Invalid samples leave the previous value and derived properties unchanged.
        /// A bad-quality null sample retains the last derived values with the new quality.
        /// </remarks>
        public ServiceResult UpdateEnumValue(ISystemContext context, in DataValue sample)
        {
            ServiceResult result = UpdateEnumValueCore(context, sample);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            ClearChangeMasks(context, includeChildren: true);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Asynchronously publishes an acquired enumeration and its derived text/dictionary values.
        /// </summary>
        /// <remarks>
        /// Invalid samples leave the previous value and derived properties unchanged.
        /// A bad-quality null sample retains the last derived values with the new quality.
        /// The cancellation token is checked before the sample is applied; notification sinks receive
        /// <see cref="CancellationToken.None"/> after commit.
        /// </remarks>
        public async ValueTask<ServiceResult> UpdateEnumValueAsync(
            ISystemContext context,
            DataValue sample,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ServiceResult result = UpdateEnumValueCore(context, sample);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            await ClearChangeMasksAsync(
                context, includeChildren: true, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates and applies an enum sample without dispatching state-change notifications.
        /// </summary>
        private ServiceResult UpdateEnumValueCore(ISystemContext context, in DataValue sample)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (sample.IsNull || (sample.WrappedValue.IsNull && !StatusCode.IsBad(sample.StatusCode)))
            {
                return StatusCodes.BadTypeMismatch;
            }
            LocalizedText text = LocalizedText.Null;
            ArrayOf<NodeId> dictionaryEntries = ArrayOf<NodeId>.Null;
            bool resolved = false;
            if (!sample.WrappedValue.IsNull)
            {
                ServiceResult validation = ValidateEnumValue(
                    context, sample.WrappedValue, out text, out dictionaryEntries, out resolved);
                if (ServiceResult.IsBad(validation))
                {
                    return validation;
                }
            }
            WrappedValue = sample.WrappedValue;
            StatusCode = sample.StatusCode;
            Timestamp = sample.SourceTimestamp;
            UpdateDerivedValues(context, sample, text, dictionaryEntries, resolved);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTimeUtc sourceTimestamp)
        {
            LocalizedText text = LocalizedText.Null;
            ArrayOf<NodeId> dictionaryEntries = ArrayOf<NodeId>.Null;
            bool resolved = false;
            bool matched = indexRange.IsNull && value.TypeInfo.IsScalar &&
                TryGetIntegerValue(value, out _) && TryGetEnumValues(context, out _);
            if (matched)
            {
                ServiceResult validation = ResolveEnumValue(
                    context, value, out text, out dictionaryEntries, out resolved);
                if (ServiceResult.IsBad(validation))
                {
                    return validation;
                }
            }

            ServiceResult result = base.WriteValueAttribute(
                context, indexRange, value, statusCode, sourceTimestamp);

            if (matched && ServiceResult.IsGood(result))
            {
                UpdateDerivedValues(
                    context, new DataValue(WrappedValue, StatusCode, Timestamp), text, dictionaryEntries, resolved);
            }

            return result;
        }

        /// <inheritdoc/>
        protected override async ValueTask<ServiceResult> WriteAttributeCoreAsync(
            ISystemContext context,
            uint attributeId,
            NumericRange indexRange,
            DataValue value,
            CancellationToken cancellationToken = default)
        {
            if (attributeId == Attributes.Value && indexRange.IsNull &&
                (value.WrappedValue.TypeInfo.IsScalar || ValueRank == ValueRanks.Scalar) &&
                (OnWriteValueAsync != null || OnSimpleWriteValueAsync != null))
            {
                ServiceResult validation = ValidateEnumValue(context, value.WrappedValue);
                if (ServiceResult.IsBad(validation))
                {
                    return validation;
                }
                AttributeWriteResult writeResult = await WriteAttributeWithResultAsync(
                    context, attributeId, indexRange, value, cancellationToken).ConfigureAwait(false);
                ServiceResult result = writeResult.Result;
                if (ServiceResult.IsGood(result) && !writeResult.SuppressCacheUpdate)
                {
                    var accepted = new DataValue(WrappedValue, StatusCode, Timestamp);
                    if (accepted.WrappedValue.IsNull && !StatusCode.IsBad(accepted.StatusCode))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }
                    LocalizedText text = LocalizedText.Null;
                    ArrayOf<NodeId> dictionaryEntries = ArrayOf<NodeId>.Null;
                    bool resolved = false;
                    if (!accepted.WrappedValue.IsNull)
                    {
                        ServiceResult acceptedValidation = ValidateEnumValue(
                            context, accepted.WrappedValue, out text, out dictionaryEntries, out resolved);
                        if (ServiceResult.IsBad(acceptedValidation))
                        {
                            return acceptedValidation;
                        }
                    }
                    UpdateDerivedValues(context, accepted, text, dictionaryEntries, resolved);
                    await ClearChangeMasksAsync(
                        context, includeChildren: true, cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
                }
                return result;
            }
            return await base.WriteAttributeCoreAsync(
                context, attributeId, indexRange, value, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override AttributeWriteResult ValidateAsyncValueWrite(
            ISystemContext context,
            NumericRange indexRange,
            Variant value)
        {
            LocalizedText matchedText = LocalizedText.Null;
            ArrayOf<NodeId> dictionaryEntries = ArrayOf<NodeId>.Null;
            bool resolved = false;
            bool matched = indexRange.IsNull &&
                value.TypeInfo.IsScalar &&
                TryGetIntegerValue(value, out _) &&
                TryGetEnumValues(context, out _);
            if (matched)
            {
                ServiceResult validation = ResolveEnumValue(
                    context, value, out matchedText, out dictionaryEntries, out resolved);
                if (ServiceResult.IsBad(validation))
                {
                    return new AttributeWriteResult(validation);
                }
            }

            return new AttributeWriteResult(ServiceResult.Good)
            {
                AfterOrdinaryCacheCommit = resolved
                    ? () => UpdateDerivedValues(
                        context,
                        new DataValue(WrappedValue, StatusCode, Timestamp),
                        matchedText,
                        dictionaryEntries,
                        resolved)
                    : null
            };
        }

        /// <summary>
        /// Validates and resolves derived values before publishing any part of an enumeration sample.
        /// </summary>
        private ServiceResult ValidateEnumValue(
            ISystemContext context,
            in Variant value,
            out LocalizedText text,
            out ArrayOf<NodeId> dictionaryEntries,
            out bool resolved)
        {
            text = LocalizedText.Null;
            dictionaryEntries = ArrayOf<NodeId>.Null;
            resolved = false;
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (TypeInfo.IsInstanceOfDataType(value, DataType, ValueRank,
                context.NamespaceUris, context.TypeTable).IsUnknown)
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (!value.TypeInfo.IsScalar)
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (!TryGetIntegerValue(value, out _))
            {
                return value.TypeInfo.BuiltInType == BuiltInType.UInt64
                    ? StatusCodes.BadOutOfRange
                    : StatusCodes.BadTypeMismatch;
            }
            return ResolveEnumValue(context, value, out text, out dictionaryEntries, out resolved);
        }

        /// <summary>
        /// Gets the mandatory EnumValues array. Returns false when the property is
        /// absent so the caller can defer to the base class; a present but empty array
        /// yields true so that no value matches and every write is rejected.
        /// </summary>
        private bool TryGetEnumValues(ISystemContext context, out ArrayOf<EnumValueType> enumValues)
        {
            if (EnumValues is { } property &&
                property.WrappedValue.TryGetValue(out enumValues, context.AsMessageContext()) &&
                !enumValues.IsNull)
            {
                return true;
            }

            enumValues = default;
            return false;
        }

        /// <summary>
        /// Resolves the selected enum row without changing the value or derived properties.
        /// </summary>
        private ServiceResult ResolveEnumValue(
            ISystemContext context,
            in Variant value,
            out LocalizedText text,
            out ArrayOf<NodeId> dictionaryEntries,
            out bool resolved)
        {
            text = LocalizedText.Null;
            dictionaryEntries = ArrayOf<NodeId>.Null;
            resolved = false;
            if (!value.TypeInfo.IsScalar || !TryGetIntegerValue(value, out long number) ||
                !TryGetEnumValues(context, out ArrayOf<EnumValueType> values))
            {
                return ServiceResult.Good;
            }
            for (int ii = 0; ii < values.Count; ii++)
            {
                if (values[ii].Value != number)
                {
                    continue;
                }
                text = values[ii].DisplayName;
                resolved = true;
                if (FindChild(context, new QualifiedName(BrowseNames.EnumDictionaryEntries)) is
                    BaseVariableState dictionary)
                {
                    if (!dictionary.WrappedValue.TryGetValue(out MatrixOf<NodeId> matrix))
                    {
                        return StatusCodes.BadConfigurationError;
                    }
                    int[] dimensions = matrix.Dimensions;
                    if (dimensions.Length != 2 || dimensions[0] != values.Count)
                    {
                        return StatusCodes.BadConfigurationError;
                    }
                    dictionaryEntries = matrix.Span.Slice(ii * dimensions[1], dimensions[1]).ToArray();
                }
                return ServiceResult.Good;
            }
            return StatusCodes.BadOutOfRange;
        }

        /// <summary>
        /// Keeps derived enum properties consistent with the currently published sample.
        /// </summary>
        private void UpdateDerivedValues(
            ISystemContext context,
            in DataValue sample,
            LocalizedText text,
            ArrayOf<NodeId> dictionaryEntries,
            bool resolved)
        {
            if (ValueAsText is { } property)
            {
                if (resolved)
                {
                    property.Value = text;
                }
                property.StatusCode = sample.StatusCode;
                property.Timestamp = sample.SourceTimestamp;
            }
            if (FindChild(context, new QualifiedName(BrowseNames.ValueAsDictionaryEntries)) is
                BaseVariableState entries)
            {
                if (!dictionaryEntries.IsNull)
                {
                    entries.WrappedValue = Variant.From(dictionaryEntries);
                }
                entries.StatusCode = sample.StatusCode;
                entries.Timestamp = sample.SourceTimestamp;
            }
        }
    }
}
