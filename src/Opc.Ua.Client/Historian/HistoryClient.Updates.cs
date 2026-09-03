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
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
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
            if (performUpdate is not PerformUpdateType.Insert and
                not PerformUpdateType.Replace and
                not PerformUpdateType.Update and
                not PerformUpdateType.Remove)
            {
                throw new ArgumentOutOfRangeException(nameof(performUpdate));
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

            return GetOperationResults(result, values.Count);
        }

        private async ValueTask<HistoryUpdateResult> SendHistoryUpdateAsync(
            ExtensionObject details,
            CancellationToken cancellationToken)
        {
            HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                null,
                [details],
                cancellationToken).ConfigureAwait(false);

            if (response.Results.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "HistoryUpdate returned a result count that does not match the request.");
            }
            return response.Results[0];
        }

        private static ArrayOf<StatusCode> GetOperationResults(
            HistoryUpdateResult result,
            int expectedCount)
        {
            if (expectedCount == 0)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new ServiceResultException(
                        result.StatusCode,
                        "HistoryUpdate returned a bad node status.");
                }
                if (result.OperationResults.Count != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "HistoryUpdate returned unexpected per-operation results.");
                }
                return [];
            }
            if (result.OperationResults.Count == 0)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    var statuses = new StatusCode[expectedCount];
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        statuses[i] = result.StatusCode;
                    }
                    return statuses.ToArrayOf();
                }
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "HistoryUpdate returned no per-operation results.");
            }
            if (result.OperationResults.Count != expectedCount)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "HistoryUpdate returned a per-operation result count that does not match the request.");
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                for (int i = 0; i < result.OperationResults.Count; i++)
                {
                    if (StatusCode.IsGood(result.OperationResults[i]))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "HistoryUpdate returned a bad node status with successful operation results.");
                    }
                }
            }
            return result.OperationResults;
        }
    }
}
