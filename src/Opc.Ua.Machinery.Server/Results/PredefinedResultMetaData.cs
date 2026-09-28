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

namespace Opc.Ua.Machinery.Server.Results
{
    /// <summary>
    /// Enforces the OPC 40001-101
    /// <c>Machinery-Result PredefinedResultMetaData</c> conformance unit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unit requires that <em>every</em> exposed result carries
    /// <c>ExternalRecipeId</c>, <c>InternalRecipeId</c>, <c>JobId</c>,
    /// <c>ProductId</c>, <c>StepId</c> and <c>CreationTime</c>. All six are
    /// optional fields of <c>ResultMetaDataType</c>, so whether they are set is
    /// a property of the data a machine produces, not of the model — which is
    /// why the server may only advertise the unit when it is actually policing
    /// the results it hands out.
    /// </para>
    /// <para>
    /// Both directions are checked. Publishing through the built-in in-memory
    /// store is rejected at the seam, where the caller still has the context to
    /// fix it. A store the application brought itself owns its own ingestion
    /// path, so for that case the results are checked as they leave the store,
    /// which is the only moment the library sees them.
    /// </para>
    /// </remarks>
    internal static class PredefinedResultMetaData
    {
        /// <summary>
        /// Throws when the result is missing any of the six predefined fields.
        /// </summary>
        /// <param name="result">The result to check.</param>
        /// <param name="onIngestion">
        /// Whether the result is entering the store rather than leaving it;
        /// only the wording of the error depends on it.
        /// </param>
        public static void Validate(MachineryResult result, bool onIngestion)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            List<string>? missing = Missing(result);
            if (missing == null)
            {
                return;
            }

            throw ServiceResultException.Create(
                onIngestion ? StatusCodes.BadInvalidArgument : StatusCodes.BadInternalError,
                onIngestion
                    ? "Result '{0}' cannot be published: the server advertises " +
                        "'Machinery-Result PredefinedResultMetaData', which requires " +
                        "every exposed result to set {1}. Set them, or drop " +
                        "WithPredefinedResultMetaData()."
                    : "Result '{0}' cannot be exposed: the server advertises " +
                        "'Machinery-Result PredefinedResultMetaData', but the bound " +
                        "store returned a result missing {1}.",
                result.ResultId,
                string.Join(", ", missing));
        }

        /// <summary>
        /// Returns the predefined fields the result leaves unset, or
        /// <see langword="null"/> when it carries all of them.
        /// </summary>
        public static List<string>? Missing(MachineryResult result)
        {
            ResultMetaDataType? metaData = result?.Data?.ResultMetaData;
            if (metaData == null)
            {
                return [.. kRequiredFields];
            }

            List<string>? missing = null;
            AddIfBlank(ref missing, "ExternalRecipeId", metaData.ExternalRecipeId);
            AddIfBlank(ref missing, "InternalRecipeId", metaData.InternalRecipeId);
            AddIfBlank(ref missing, "JobId", metaData.JobId);
            AddIfBlank(ref missing, "ProductId", metaData.ProductId);
            AddIfBlank(ref missing, "StepId", metaData.StepId);

            // CreationTime is a UtcTime; default(DateTime) is how an unset
            // optional field arrives, and Min/Max are the two values OPC 10000-6
            // treats as "no time supplied".
            if (metaData.CreationTime == DateTime.MinValue ||
                metaData.CreationTime == DateTime.MaxValue)
            {
                (missing ??= []).Add("CreationTime");
            }

            return missing;
        }

        private static void AddIfBlank(ref List<string>? missing, string name, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                (missing ??= []).Add(name);
            }
        }

        private static readonly string[] kRequiredFields =
        [
            "ExternalRecipeId",
            "InternalRecipeId",
            "JobId",
            "ProductId",
            "StepId",
            "CreationTime"
        ];
    }

    /// <summary>
    /// Wraps a result store so every result it hands out is checked against the
    /// <c>Machinery-Result PredefinedResultMetaData</c> unit.
    /// </summary>
    /// <remarks>
    /// Applied only when the application opted into the unit. A store that
    /// already guarantees the metadata pays one field check per result read;
    /// one that does not is stopped from making the server advertise a unit it
    /// does not meet.
    /// </remarks>
    internal sealed class PredefinedResultMetaDataStore : IMachineryResultStore
    {
        public PredefinedResultMetaDataStore(IMachineryResultStore inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public async ValueTask<MachineryResult?> GetLatestResultAsync(
            CancellationToken cancellationToken = default)
        {
            return Checked(
                await m_inner.GetLatestResultAsync(cancellationToken).ConfigureAwait(false));
        }

        public async ValueTask<MachineryResult?> GetResultByIdAsync(
            string resultId,
            CancellationToken cancellationToken = default)
        {
            return Checked(
                await m_inner.GetResultByIdAsync(resultId, cancellationToken)
                    .ConfigureAwait(false));
        }

        public ValueTask<ArrayOf<string>> GetResultIdsAsync(
            uint maxResults,
            CancellationToken cancellationToken = default)
        {
            return m_inner.GetResultIdsAsync(maxResults, cancellationToken);
        }

        public ValueTask<ArrayOf<int>> AcknowledgeResultsAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default)
        {
            return m_inner.AcknowledgeResultsAsync(resultIds, cancellationToken);
        }

        private static MachineryResult? Checked(MachineryResult? result)
        {
            if (result != null)
            {
                PredefinedResultMetaData.Validate(result, onIngestion: false);
            }
            return result;
        }

        private readonly IMachineryResultStore m_inner;
    }
}
