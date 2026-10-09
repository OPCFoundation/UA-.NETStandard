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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Result;
using ResultBrowseNames = Opc.Ua.Machinery.Result.BrowseNames;

namespace Opc.Ua.Machinery.Server.Results
{
    /// <summary>
    /// Evaluates the <c>filter</c> and <c>orderedBy</c> arguments of the
    /// OPC 40001-101 <c>GetResultIdListFiltered</c> Method (§7.1.3) against a
    /// result store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §7.1.3 says the BrowsePaths a filter uses "can be built from the fields
    /// of the <c>ResultReadyEventType</c>, the <c>ResultType</c> VariableType
    /// or the <c>ResultDataType</c>". All three carry the same structure — the
    /// event wraps it in a <c>Result</c> variable, the variable type exposes
    /// its fields as structured components, the data type as fields — so one
    /// resolver answers all three spellings: an optional leading
    /// <c>Result</c>, then <c>ResultMetaData</c> or <c>ResultContent</c>, then
    /// the field. Names are matched without their namespace index, because a
    /// DataType field has none and the three spellings would otherwise
    /// disagree.
    /// </para>
    /// <para>
    /// A filter path that names nothing evaluates to null, which is how an
    /// event filter treats an unknown field, and an unset optional field is
    /// null too; <see cref="MachineryResultFilterEvaluator"/> applies the
    /// OPC 10000-4 NULL rules to both. An <c>orderedBy</c> path that
    /// names nothing is refused instead: a sort key that sorts by nothing is
    /// always a client mistake. §7.1.3 does not fix the direction; the order
    /// is ascending, results that leave a key unset come last, and results
    /// that tie keep the store's newest-first order. An empty
    /// <c>orderedBy</c> executes no ordering, as §7.1.3 requires, so the
    /// store's own order stands.
    /// </para>
    /// <para>
    /// The store is asked for the candidate identifiers and each candidate's
    /// data through the two <see cref="IMachineryResultStore"/> reads every
    /// store already answers, so a custom store gains filtering and ordering
    /// without implementing anything new.
    /// </para>
    /// </remarks>
    internal static class MachineryResultQuery
    {
        /// <summary>
        /// Returns whether the call asks for neither a filter nor an order,
        /// in which case the store's own listing answers it directly.
        /// </summary>
        public static bool IsUnrestricted(ContentFilter? filter, ArrayOf<RelativePath> orderedBy)
        {
            return !HasFilter(filter) && orderedBy.Count == 0;
        }

        /// <summary>
        /// Validates the two arguments before any result is read.
        /// </summary>
        /// <param name="filter">The content filter; may be null.</param>
        /// <param name="orderedBy">The ordering criteria; may be empty.</param>
        /// <param name="context">The filter context.</param>
        public static ServiceResult Validate(
            ContentFilter? filter,
            ArrayOf<RelativePath> orderedBy,
            IFilterContext context)
        {
            if (HasFilter(filter))
            {
                ContentFilter.Result result = filter!.Validate(context);
                if (ServiceResult.IsBad(result.Status))
                {
                    return result.Status;
                }
            }
            for (int ii = 0; ii < orderedBy.Count; ii++)
            {
                if (!TryResolve(s_emptyResult, ToBrowsePath(orderedBy[ii]), out _))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "orderedBy[{0}] does not identify a field of the ResultDataType, " +
                        "the ResultType VariableType or the ResultReadyEventType.",
                        ii);
                }
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns the identifiers of the results matching
        /// <paramref name="filter"/>, ordered by <paramref name="orderedBy"/>
        /// and capped at <paramref name="maxResults"/>.
        /// </summary>
        /// <param name="store">The store to query.</param>
        /// <param name="filter">The validated content filter; may be null.</param>
        /// <param name="orderedBy">The validated ordering criteria.</param>
        /// <param name="maxResults">The cap; zero means none.</param>
        /// <param name="context">The filter context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static async ValueTask<ArrayOf<string>> ExecuteAsync(
            IMachineryResultStore store,
            ContentFilter? filter,
            ArrayOf<RelativePath> orderedBy,
            uint maxResults,
            IFilterContext context,
            CancellationToken cancellationToken)
        {
            ArrayOf<string> ids = await store
                .GetResultIdsAsync(0, cancellationToken)
                .ConfigureAwait(false);

            bool filtering = HasFilter(filter);
            bool ordering = orderedBy.Count > 0;
            var keys = new ArrayOf<QualifiedName>[orderedBy.Count];
            for (int ii = 0; ii < keys.Length; ii++)
            {
                keys[ii] = ToBrowsePath(orderedBy[ii]);
            }

            var matches = new List<Candidate>();
            for (int ii = 0; ii < ids.Count; ii++)
            {
                // Without an order the store's newest-first listing already is
                // the answer, so the scan can stop as soon as the cap is met.
                if (!ordering && maxResults > 0 && matches.Count >= maxResults)
                {
                    break;
                }

                MachineryResult? result = await store
                    .GetResultByIdAsync(ids[ii], cancellationToken)
                    .ConfigureAwait(false);
                if (result == null)
                {
                    // Dropped by the store between the listing and the read.
                    continue;
                }
                if (filtering &&
                    !MachineryResultFilterEvaluator.Evaluate(
                        filter!,
                        context,
                        new MachineryResultFilterTarget(result.Data)))
                {
                    continue;
                }

                var sortKeys = new Variant[keys.Length];
                for (int jj = 0; jj < keys.Length; jj++)
                {
                    TryResolve(result.Data, keys[jj], out sortKeys[jj]);
                }
                matches.Add(new Candidate(result.ResultId, matches.Count, sortKeys));
            }

            if (ordering)
            {
                matches.Sort(CompareCandidates);
            }

            int count = maxResults > 0 && maxResults < (uint)matches.Count
                ? (int)maxResults
                : matches.Count;
            var resultIds = new string[count];
            for (int ii = 0; ii < count; ii++)
            {
                resultIds[ii] = matches[ii].ResultId;
            }
            return resultIds.ToArrayOf();
        }

        /// <summary>
        /// Resolves a browse path to the value it names in a result.
        /// </summary>
        /// <param name="data">The result data.</param>
        /// <param name="path">The browse path, relative to the result.</param>
        /// <param name="value">
        /// The value, or <see cref="Variant.Null"/> when the field is known but
        /// the result leaves it unset.
        /// </param>
        /// <returns>
        /// <see langword="false"/> when the path names no field of the result.
        /// </returns>
        internal static bool TryResolve(
            ResultDataType data,
            ArrayOf<QualifiedName> path,
            out Variant value)
        {
            value = Variant.Null;
            int index = 0;
            if (path.Count > 0 && path[0].Name == ResultBrowseNames.Result)
            {
                // The ResultReadyEventType spelling wraps the structure in its
                // Result variable.
                index++;
            }
            if (index == path.Count)
            {
                value = Variant.FromStructure(data);
                return true;
            }

            string? name = path[index].Name;
            if (name == ResultBrowseNames.ResultContent)
            {
                if (index + 1 != path.Count)
                {
                    return false;
                }
                // Mandatory in ResultDataType: an empty array, never absent.
                value = Variant.From(data.ResultContent);
                return true;
            }
            if (name != ResultBrowseNames.ResultMetaData)
            {
                return false;
            }

            ResultMetaDataType metaData = data.ResultMetaData ?? s_emptyMetaData;
            index++;
            if (index == path.Count)
            {
                value = data.ResultMetaData == null
                    ? Variant.Null
                    : Variant.FromStructure(metaData);
                return true;
            }

            string? field = path[index].Name;
            if (index + 1 == path.Count)
            {
                return TryResolveMetaData(metaData, field, out value);
            }
            if (field == nameof(ResultMetaDataType.ProcessingTimes) && index + 2 == path.Count)
            {
                return TryResolveProcessingTimes(
                    HasProcessingTimes(metaData) ? metaData.ProcessingTimes : null,
                    path[index + 1].Name,
                    out value);
            }
            return false;
        }

        private static bool TryResolveMetaData(
            ResultMetaDataType metaData,
            string? field,
            out Variant value)
        {
            if (field == null || !s_metaDataFields.Contains(field))
            {
                value = Variant.Null;
                return false;
            }

            uint mask = metaData.EncodingMask;
            value = field switch
            {
                nameof(ResultMetaDataType.ResultId) => From(metaData.ResultId),
                nameof(ResultMetaDataType.HasTransferableDataOnFile) => From(
                    metaData.HasTransferableDataOnFile,
                    mask,
                    ResultMetaDataTypeFields.HasTransferableDataOnFile),
                nameof(ResultMetaDataType.IsPartial) => From(
                    metaData.IsPartial,
                    mask,
                    ResultMetaDataTypeFields.IsPartial),
                nameof(ResultMetaDataType.IsSimulated) => From(
                    metaData.IsSimulated,
                    mask,
                    ResultMetaDataTypeFields.IsSimulated),
                nameof(ResultMetaDataType.ResultState) =>
                    IsSet(mask, ResultMetaDataTypeFields.ResultState) || metaData.ResultState != 0
                        ? Variant.From(metaData.ResultState)
                        : Variant.Null,
                nameof(ResultMetaDataType.StepId) => From(metaData.StepId),
                nameof(ResultMetaDataType.PartId) => From(metaData.PartId),
                nameof(ResultMetaDataType.ExternalRecipeId) => From(metaData.ExternalRecipeId),
                nameof(ResultMetaDataType.InternalRecipeId) => From(metaData.InternalRecipeId),
                nameof(ResultMetaDataType.ProductId) => From(metaData.ProductId),
                nameof(ResultMetaDataType.ExternalConfigurationId) =>
                    From(metaData.ExternalConfigurationId),
                nameof(ResultMetaDataType.InternalConfigurationId) =>
                    From(metaData.InternalConfigurationId),
                nameof(ResultMetaDataType.JobId) => From(metaData.JobId),
                nameof(ResultMetaDataType.CreationTime) => From(
                    metaData.CreationTime,
                    IsSet(mask, ResultMetaDataTypeFields.CreationTime)),
                nameof(ResultMetaDataType.ProcessingTimes) => HasProcessingTimes(metaData)
                    ? Variant.FromStructure(metaData.ProcessingTimes)
                    : Variant.Null,
                nameof(ResultMetaDataType.ResultUri) =>
                    IsSet(mask, ResultMetaDataTypeFields.ResultUri) || metaData.ResultUri.Count > 0
                        ? Variant.From(metaData.ResultUri)
                        : Variant.Null,
                // Enumerations travel as Int32 on the wire, which is what a
                // client's LiteralOperand carries.
                nameof(ResultMetaDataType.ResultEvaluation) =>
                    IsSet(mask, ResultMetaDataTypeFields.ResultEvaluation) ||
                    metaData.ResultEvaluation != ResultEvaluationEnum.Undefined
                        ? Variant.From((int)metaData.ResultEvaluation)
                        : Variant.Null,
                nameof(ResultMetaDataType.ResultEvaluationCode) =>
                    IsSet(mask, ResultMetaDataTypeFields.ResultEvaluationCode) ||
                    metaData.ResultEvaluationCode != 0
                        ? Variant.From(metaData.ResultEvaluationCode)
                        : Variant.Null,
                nameof(ResultMetaDataType.ResultEvaluationDetails) =>
                    IsSet(mask, ResultMetaDataTypeFields.ResultEvaluationDetails) ||
                    !metaData.ResultEvaluationDetails.IsNullOrEmpty
                        ? Variant.From(metaData.ResultEvaluationDetails)
                        : Variant.Null,
                nameof(ResultMetaDataType.FileFormat) =>
                    IsSet(mask, ResultMetaDataTypeFields.FileFormat) || metaData.FileFormat.Count > 0
                        ? Variant.From(metaData.FileFormat)
                        : Variant.Null,
                _ => Variant.Null
            };
            return true;
        }

        private static bool TryResolveProcessingTimes(
            ProcessingTimesDataType? times,
            string? field,
            out Variant value)
        {
            ProcessingTimesDataType effective = times ?? s_emptyProcessingTimes;
            uint mask = effective.EncodingMask;
            switch (field)
            {
                case nameof(ProcessingTimesDataType.StartTime):
                    value = From(effective.StartTime, isSet: false);
                    return true;
                case nameof(ProcessingTimesDataType.EndTime):
                    value = From(effective.EndTime, isSet: false);
                    return true;
                case nameof(ProcessingTimesDataType.AcquisitionDuration):
                    value = IsSet(mask, ProcessingTimesDataTypeFields.AcquisitionDuration) ||
                        effective.AcquisitionDuration != 0
                        ? Variant.From(effective.AcquisitionDuration)
                        : Variant.Null;
                    return true;
                case nameof(ProcessingTimesDataType.ProcessingDuration):
                    value = IsSet(mask, ProcessingTimesDataTypeFields.ProcessingDuration) ||
                        effective.ProcessingDuration != 0
                        ? Variant.From(effective.ProcessingDuration)
                        : Variant.Null;
                    return true;
                default:
                    value = Variant.Null;
                    return false;
            }
        }

        /// <summary>
        /// Orders two candidates by their sort keys, falling back to the
        /// store's order so equal keys keep newest first.
        /// </summary>
        private static int CompareCandidates(Candidate left, Candidate right)
        {
            for (int ii = 0; ii < left.Keys.Length; ii++)
            {
                int result = CompareKeys(left.Keys[ii], right.Keys[ii]);
                if (result != 0)
                {
                    return result;
                }
            }
            return left.Index.CompareTo(right.Index);
        }

        /// <summary>
        /// Compares two sort keys ascending, with an unset key last.
        /// </summary>
        internal static int CompareKeys(Variant left, Variant right)
        {
            if (left.IsNull || right.IsNull)
            {
                return left.IsNull == right.IsNull ? 0 : left.IsNull ? 1 : -1;
            }
            // Values that have no order to offer are treated as equal, so the
            // store's order decides between them.
            return MachineryResultFilterEvaluator.TryCompare(left, right, out int result)
                ? result
                : 0;
        }

        private static bool HasFilter(ContentFilter? filter)
        {
            return filter != null && filter.Elements.Count > 0;
        }

        private static bool HasProcessingTimes(ResultMetaDataType metaData)
        {
            return metaData.ProcessingTimes != null &&
                (IsSet(metaData.EncodingMask, ResultMetaDataTypeFields.ProcessingTimes) ||
                    metaData.ProcessingTimes.StartTime != DateTimeUtc.MinValue ||
                    metaData.ProcessingTimes.EndTime != DateTimeUtc.MinValue);
        }

        private static ArrayOf<QualifiedName> ToBrowsePath(RelativePath? relativePath)
        {
            if (relativePath == null || relativePath.Elements.Count == 0)
            {
                return ArrayOf<QualifiedName>.Empty;
            }
            var names = new QualifiedName[relativePath.Elements.Count];
            for (int ii = 0; ii < names.Length; ii++)
            {
                names[ii] = relativePath.Elements[ii]?.TargetName ?? QualifiedName.Null;
            }
            return names.ToArrayOf();
        }

        // An optional field counts as set when the encoding mask says so or
        // when the application filled it in without touching the mask, which
        // is what a result built in process usually does.
        private static bool IsSet(uint mask, ResultMetaDataTypeFields field)
        {
            return (mask & (uint)field) != 0;
        }

        private static bool IsSet(uint mask, ProcessingTimesDataTypeFields field)
        {
            return (mask & (uint)field) != 0;
        }

        private static Variant From(string? value)
        {
            return value == null ? Variant.Null : Variant.From(value);
        }

        private static Variant From(bool value, uint mask, ResultMetaDataTypeFields field)
        {
            return value || IsSet(mask, field) ? Variant.From(value) : Variant.Null;
        }

        private static Variant From(DateTimeUtc value, bool isSet)
        {
            return isSet || value != DateTimeUtc.MinValue ? Variant.From(value) : Variant.Null;
        }

        private static readonly ResultDataType s_emptyResult = new();
        private static readonly ResultMetaDataType s_emptyMetaData = new();
        private static readonly ProcessingTimesDataType s_emptyProcessingTimes = new();

        private static readonly HashSet<string> s_metaDataFields = new(StringComparer.Ordinal)
        {
            nameof(ResultMetaDataType.ResultId),
            nameof(ResultMetaDataType.HasTransferableDataOnFile),
            nameof(ResultMetaDataType.IsPartial),
            nameof(ResultMetaDataType.IsSimulated),
            nameof(ResultMetaDataType.ResultState),
            nameof(ResultMetaDataType.StepId),
            nameof(ResultMetaDataType.PartId),
            nameof(ResultMetaDataType.ExternalRecipeId),
            nameof(ResultMetaDataType.InternalRecipeId),
            nameof(ResultMetaDataType.ProductId),
            nameof(ResultMetaDataType.ExternalConfigurationId),
            nameof(ResultMetaDataType.InternalConfigurationId),
            nameof(ResultMetaDataType.JobId),
            nameof(ResultMetaDataType.CreationTime),
            nameof(ResultMetaDataType.ProcessingTimes),
            nameof(ResultMetaDataType.ResultUri),
            nameof(ResultMetaDataType.ResultEvaluation),
            nameof(ResultMetaDataType.ResultEvaluationCode),
            nameof(ResultMetaDataType.ResultEvaluationDetails),
            nameof(ResultMetaDataType.FileFormat)
        };

        private sealed record Candidate(string ResultId, int Index, Variant[] Keys);
    }

    /// <summary>
    /// Presents one stored result to <see cref="FilterEvaluator"/> the way a
    /// <c>ResultReadyEventType</c> event, a <c>ResultType</c> variable or a
    /// <c>ResultDataType</c> value would present it.
    /// </summary>
    internal sealed class MachineryResultFilterTarget : IFilterTarget
    {
        public MachineryResultFilterTarget(ResultDataType data)
        {
            m_data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A stored result is of each of the three types §7.1.3 lets a filter
        /// build its paths from, and therefore of every one of their
        /// supertypes.
        /// </remarks>
        public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
        {
            if (typeDefinitionId.IsNull)
            {
                return false;
            }
            NodeId[] anchors =
            [
                NodeId.Create(
                    Opc.Ua.Machinery.Result.ObjectTypes.ResultReadyEventType,
                    Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                    context.NamespaceUris),
                NodeId.Create(
                    Opc.Ua.Machinery.Result.VariableTypes.ResultType,
                    Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                    context.NamespaceUris),
                NodeId.Create(
                    Opc.Ua.Machinery.Result.DataTypes.ResultDataType,
                    Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                    context.NamespaceUris)
            ];
            for (int ii = 0; ii < anchors.Length; ii++)
            {
                if (anchors[ii] == typeDefinitionId ||
                    context.TypeTree.IsTypeOf(anchors[ii], typeDefinitionId))
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public Variant GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            ArrayOf<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            if (attributeId != Attributes.Value ||
                !MachineryResultQuery.TryResolve(m_data, relativePath, out Variant value))
            {
                return Variant.Null;
            }
            if (!indexRange.IsNull &&
                !value.IsNull &&
                StatusCode.IsBad(indexRange.ApplyRange(ref value)))
            {
                return Variant.Null;
            }
            return value;
        }

        private readonly ResultDataType m_data;
    }
}
