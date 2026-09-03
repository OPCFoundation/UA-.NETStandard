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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Historian
{
    public sealed partial class HistoryClient
    {
        private async IAsyncEnumerable<T> ReadDetailsAsync<T>(
            NodeId nodeId,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            Func<HistoryReadResult, ArrayOf<T>> decodePage,
            HistoryReadNodeOptions? nodeOptions,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ByteString continuationPoint = ByteString.Empty;
            ByteString liveContinuationPoint = ByteString.Empty;
            uint pagesRead = 0;
            var elapsed = Stopwatch.StartNew();
            string indexRange = FormatIndexRange(
                nodeOptions?.IndexRange ?? default);
            try
            {
                while (true)
                {
                    if (Options.MaxPagesPerRead > 0 &&
                        pagesRead >= Options.MaxPagesPerRead)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTimeout,
                            "HistoryRead exceeded the configured page limit.");
                    }
                    if (Options.MaxReadDuration > TimeSpan.Zero &&
                        elapsed.Elapsed > Options.MaxReadDuration)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTimeout,
                            "HistoryRead exceeded the configured duration limit.");
                    }
                    var nodesToRead = new HistoryReadValueId[]
                    {
                        new()
                        {
                            NodeId = nodeId,
                            ContinuationPoint = continuationPoint,
                            IndexRange = indexRange,
                            DataEncoding = nodeOptions?.DataEncoding ?? QualifiedName.Null
                        }
                    };

                    HistoryReadResponse response = await Session.HistoryReadAsync(
                        null,
                        historyReadDetails,
                        timestampsToReturn,
                        releaseContinuationPoints: false,
                        nodesToRead,
                        cancellationToken).ConfigureAwait(false);

                    if (response.Results.Count != 1)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "HistoryRead returned a result count that does not match the request.");
                    }

                    HistoryReadResult result = response.Results[0];
                    liveContinuationPoint = result.ContinuationPoint;
                    pagesRead++;
                    if (Options.MaxReadDuration > TimeSpan.Zero &&
                        elapsed.Elapsed > Options.MaxReadDuration)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTimeout,
                            "HistoryRead exceeded the configured duration limit.");
                    }

                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        throw new ServiceResultException(
                            result.StatusCode,
                            "HistoryRead returned a bad status.");
                    }

                    ArrayOf<T> values = decodePage(result);
                    for (int i = 0; i < values.Count; i++)
                    {
                        yield return values[i];
                    }

                    if (result.ContinuationPoint.IsEmpty)
                    {
                        liveContinuationPoint = ByteString.Empty;
                        yield break;
                    }

                    continuationPoint = result.ContinuationPoint;
                }
            }
            finally
            {
                if (!liveContinuationPoint.IsEmpty)
                {
                    try
                    {
                        var releaseNodes = new HistoryReadValueId[]
                        {
                            new()
                            {
                                NodeId = nodeId,
                                ContinuationPoint = liveContinuationPoint,
                                IndexRange = indexRange,
                                DataEncoding = nodeOptions?.DataEncoding ?? QualifiedName.Null
                            }
                        };
                        _ = await Session.HistoryReadAsync(
                            null,
                            historyReadDetails,
                            timestampsToReturn,
                            releaseContinuationPoints: true,
                            releaseNodes,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // Best-effort cleanup.
                    }
                    catch (TaskCanceledException)
                    {
                        // Best-effort cleanup.
                    }
                    catch (OperationCanceledException)
                    {
                        // Best-effort cleanup.
                    }
                }
            }
        }

        private static string FormatIndexRange(NumericRange range)
        {
            if (range.IsNull)
            {
                return string.Empty;
            }
            NumericRange[]? subRanges = range.SubRanges;
            if (subRanges == null)
            {
                return range.ToString();
            }

            var builder = new StringBuilder();
            for (int i = 0; i < subRanges.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append(subRanges[i].ToString());
            }
            return builder.ToString();
        }

        private static ArrayOf<DataValue> DecodeHistoryData(HistoryReadResult result)
        {
            if (result.HistoryData.IsNull)
            {
                return [];
            }
            if (result.HistoryData.TryGetValue(out HistoryData? data) && data != null)
            {
                return data.DataValues;
            }
            throw CreateUnexpectedHistoryPayloadException(nameof(HistoryData));
        }

        private static ArrayOf<ModifiedHistoryValue> DecodeHistoryModifiedData(
            HistoryReadResult result)
        {
            if (result.HistoryData.IsNull)
            {
                return [];
            }
            if (!result.HistoryData.TryGetValue(out HistoryModifiedData? data) || data == null)
            {
                throw CreateUnexpectedHistoryPayloadException(nameof(HistoryModifiedData));
            }
            if (data.DataValues.Count != data.ModificationInfos.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "HistoryModifiedData returned different numbers of values and modification infos.");
            }

            var values = new ModifiedHistoryValue[data.DataValues.Count];
            for (int i = 0; i < values.Length; i++)
            {
                ModificationInfo info = data.ModificationInfos[i] ??
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "HistoryModifiedData returned a null ModificationInfo.");
                values[i] = new ModifiedHistoryValue(data.DataValues[i], info);
            }
            return values.ToArrayOf();
        }

        private static ArrayOf<HistoryEventFieldList> DecodeHistoryEvents(
            HistoryReadResult result)
        {
            if (result.HistoryData.IsNull)
            {
                return [];
            }
            if (result.HistoryData.TryGetValue(out HistoryEvent? data) && data != null)
            {
                return data.Events;
            }
            throw CreateUnexpectedHistoryPayloadException(nameof(HistoryEvent));
        }

        private static ServiceResultException CreateUnexpectedHistoryPayloadException(
            string expectedType)
        {
            return new ServiceResultException(
                StatusCodes.BadDecodingError,
                $"HistoryRead returned a payload other than {expectedType}.");
        }
    }
}
