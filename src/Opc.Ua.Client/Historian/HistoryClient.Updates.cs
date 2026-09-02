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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Historian
{
    public sealed partial class HistoryClient
    {
        /// <summary>
        /// Inserts, replaces, updates, or removes structured historical values.
        /// </summary>
        /// <remarks>
        /// <see cref="PerformUpdateType.Remove"/> is valid for structured
        /// history such as annotations. Raw values must be deleted with
        /// <see cref="DeleteRawAsync"/> or <see cref="DeleteAtTimeAsync"/>.
        /// </remarks>
        public async ValueTask<ArrayOf<StatusCode>> UpdateStructureDataAsync(
            NodeId nodeId,
            PerformUpdateType performUpdate,
            ArrayOf<DataValue> values,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (values.IsNull)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var details = new UpdateStructureDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = performUpdate,
                UpdateValues = values
            };

            HistoryUpdateResult? result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return GetOperationResults(result, values.Count, useStatusFallback: true);
        }

        private async ValueTask<HistoryUpdateResult?> SendHistoryUpdateAsync(
            ExtensionObject details,
            CancellationToken cancellationToken)
        {
            HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                null,
                [details],
                cancellationToken).ConfigureAwait(false);

            return response.Results.Count > 0 ? response.Results[0] : null;
        }

        private static ArrayOf<StatusCode> GetOperationResults(
            HistoryUpdateResult? result,
            int expectedCount,
            bool useStatusFallback)
        {
            if (result == null)
            {
                return ArrayOf<StatusCode>.Empty;
            }
            if (result.OperationResults.Count > 0)
            {
                return result.OperationResults;
            }
            if (!useStatusFallback || expectedCount == 0)
            {
                return ArrayOf<StatusCode>.Empty;
            }

            var statuses = new StatusCode[expectedCount];
            for (int i = 0; i < statuses.Length; i++)
            {
                statuses[i] = result.StatusCode;
            }
            return statuses.ToArrayOf();
        }

        private static StatusCode[] ToStatusList(ArrayOf<StatusCode> statuses)
        {
            var result = new StatusCode[statuses.Count];
            for (int i = 0; i < statuses.Count; i++)
            {
                result[i] = statuses[i];
            }
            return result;
        }
    }
}
