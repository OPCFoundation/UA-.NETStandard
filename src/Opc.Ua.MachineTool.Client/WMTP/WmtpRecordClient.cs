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
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Client
{
    /// <summary>
    /// Normalizes the two released generated record proxies without duplicating their OPC UA service protocol.
    /// Caller cancellation never sends the peripheral-scoped AbortOperation method.
    /// </summary>
    public sealed class WmtpRecordClient
    {
        /// <summary>
        /// Creates a workflow for one resolved, generated dataset proxy.
        /// </summary>
        internal WmtpRecordClient(
            MachineToolClient owner, WMTPWorkCycleDataTypeClient? work, WMTPServiceCycleDataTypeClient? service)
        {
            m_owner = owner;
            WorkProxy = work;
            ServiceProxy = service;
        }

        /// <summary>
        /// Gets the advanced generated work proxy, when this is the work dataset.
        /// </summary>
        public WMTPWorkCycleDataTypeClient? WorkProxy { get; }

        /// <summary>
        /// Gets the advanced generated service proxy, when this is the service dataset.
        /// </summary>
        public WMTPServiceCycleDataTypeClient? ServiceProxy { get; }

        /// <summary>
        /// Counts all records or an explicitly requested time interval without consuming data.
        /// </summary>
        public async ValueTask<uint> CountAsync(
            WmtpRecordQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(query);
            if (query.Selection is not WmtpRecordSelection.All and not WmtpRecordSelection.Time)
            {
                throw new ArgumentException("The released count methods select all records or a time interval.",
                    nameof(query));
            }
            if (WorkProxy != null)
            {
                return query.Selection == WmtpRecordSelection.All ?
                    await WorkProxy.ReportNumberOfStoredRecordsAsync(cancellationToken).ConfigureAwait(false) :
                    await WorkProxy.ReportNumberOfStoredRecordsTimeAsync(
                        query.FromTime, query.ToTime, cancellationToken).ConfigureAwait(false);
            }
            return query.Selection == WmtpRecordSelection.All ?
                await ServiceProxy!.ReportNumberOfStoredRecordsAsync(cancellationToken).ConfigureAwait(false) :
                await ServiceProxy!.ReportNumberOfStoredRecordsTimeAsync(
                    query.FromTime, query.ToTime, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies the complete selected report and rejects results beyond the client bound, never truncating them.
        /// </summary>
        public async ValueTask<ArrayOf<WMTPOutputDataType>> ReadAsync(
            WmtpRecordQuery query, int maximumRecords = 4096, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(query);
            if (maximumRecords is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRecords));
            }
            ArrayOf<WMTPOutputDataType> records;
            if (WorkProxy != null)
            {
                if (query.Selection == WmtpRecordSelection.First)
                {
                    records = [await WorkProxy.CombinedReportFirstValueAsync(cancellationToken).ConfigureAwait(false)];
                }
                else if (query.Selection == WmtpRecordSelection.Last)
                {
                    records = [await WorkProxy.CombinedReportLastValueAsync(cancellationToken).ConfigureAwait(false)];
                }
                else
                {
                    Variant result = query.Selection switch
                    {
                        WmtpRecordSelection.All => await WorkProxy.CombinedReportAllAsync(cancellationToken)
                            .ConfigureAwait(false),
                        WmtpRecordSelection.Index => await WorkProxy.CombinedReportIndexAsync(
                            query.FromIndex, query.ToIndex, cancellationToken).ConfigureAwait(false),
                        _ => await WorkProxy.CombinedReportTimeAsync(query.FromTime, query.ToTime, cancellationToken)
                            .ConfigureAwait(false)
                    };
                    if (!result.TryGetStructure(m_owner.Session.MessageContext, out records))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "The work report must contain the released WMTP output structure array.");
                    }
                }
            }
            else
            {
                records = query.Selection switch
                {
                    WmtpRecordSelection.All => await ServiceProxy!.CombinedReportAllAsync(cancellationToken)
                        .ConfigureAwait(false),
                    WmtpRecordSelection.Index => await ServiceProxy!.CombinedReportIndexAsync(
                        query.FromIndex, query.ToIndex, cancellationToken).ConfigureAwait(false),
                    WmtpRecordSelection.Time => await ServiceProxy!.CombinedReportTimeAsync(
                        query.FromTime, query.ToTime, cancellationToken).ConfigureAwait(false),
                    WmtpRecordSelection.First =>
                        [await ServiceProxy!.CombinedReportFirstValueAsync(cancellationToken).ConfigureAwait(false)],
                    _ => [await ServiceProxy!.CombinedReportLastValueAsync(cancellationToken).ConfigureAwait(false)]
                };
            }
            if (records.Count > maximumRecords)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The complete response exceeds the caller's record limit; use an explicit range.");
            }
            return records;
        }

        /// <summary>
        /// Deletes precisely the selected all/index/time range; the Boolean XML result is checked for refusal.
        /// </summary>
        public async ValueTask DeleteAsync(
            WmtpRecordQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(query);
            switch (query.Selection)
            {
                case WmtpRecordSelection.All:
                    bool deleted = WorkProxy != null ?
                        await WorkProxy.DeleteAllStoredRecordsAsync(cancellationToken).ConfigureAwait(false) :
                        await ServiceProxy!.DeleteAllStoredRecordsAsync(cancellationToken).ConfigureAwait(false);
                    if (!deleted)
                    {
                        throw new ServiceResultException(StatusCodes.BadRequestNotAllowed,
                            "The equipment refused to delete the complete dataset.");
                    }
                    break;
                case WmtpRecordSelection.Index:
                    if (WorkProxy != null)
                    {
                        await WorkProxy.DeleteStoredRecordsIndexAsync(query.FromIndex, query.ToIndex, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await ServiceProxy!.DeleteStoredRecordsIndexAsync(
                            query.FromIndex, query.ToIndex, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    break;
                case WmtpRecordSelection.Time:
                    if (WorkProxy != null)
                    {
                        await WorkProxy.DeleteStoredRecordsTimeAsync(query.FromTime, query.ToTime, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await ServiceProxy!.DeleteStoredRecordsTimeAsync(
                            query.FromTime, query.ToTime, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    break;
                default:
                    throw new ArgumentException("The released delete methods require all/index/time selection.",
                        nameof(query));
            }
        }

        /// <summary>
        /// Explicitly requests a peripheral-wide abort. It can affect another caller only if server policy permits it.
        /// </summary>
        public ValueTask AbortAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_owner.EnsureWmtpActive();
            return WorkProxy != null ? WorkProxy.AbortOperationAsync(cancellationToken) :
                ServiceProxy!.AbortOperationAsync(cancellationToken);
        }

        /// <summary>
        /// Checks local bounds without changing the meaning of the remote request.
        /// </summary>
        private void Validate(WmtpRecordQuery query)
        {
            m_owner.EnsureWmtpActive();
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }
            if (query.Selection is < WmtpRecordSelection.All or > WmtpRecordSelection.Last ||
                (query.Selection == WmtpRecordSelection.Index && query.FromIndex > query.ToIndex) ||
                (query.Selection == WmtpRecordSelection.Time &&
                    (query.FromTime.IsNull || query.ToTime.IsNull || query.FromTime > query.ToTime)))
            {
                throw new ArgumentOutOfRangeException(nameof(query));
            }
        }

        /// <summary>The owning facade and its existing session.</summary>
        private readonly MachineToolClient m_owner;
    }
}
