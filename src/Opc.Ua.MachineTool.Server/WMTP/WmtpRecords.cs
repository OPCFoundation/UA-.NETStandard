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

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// One dataset's generated method bindings and bounded, non-consuming report operations.
    /// </summary>
    public sealed class WmtpRecords
    {
        /// <summary>
        /// Binds one independent store to the peripheral operation lifetime.
        /// </summary>
        internal WmtpRecords(
            WmtpPeripheral peripheral, IWmtpRecordStore store, WmtpRecordFeatures features, int maximumReportBytes)
        {
            m_peripheral = peripheral;
            m_store = store;
            Features = features;
            m_maximumReportBytes = maximumReportBytes;
        }

        /// <summary>
        /// Gets the selected optional record methods.
        /// </summary>
        public WmtpRecordFeatures Features { get; }

        /// <summary>
        /// Counts all records or a selected inclusive time interval without transferring or consuming them.
        /// </summary>
        public ValueTask<uint> CountAsync(
            WmtpRecordQuery query, WmtpRequest request, CancellationToken cancellationToken = default)
        {
            WmtpValidation.Query(query);
            Require(query.Selection == WmtpRecordSelection.All ||
                (query.Selection == WmtpRecordSelection.Time && Has(WmtpRecordFeatures.TimeCount)));
            return m_peripheral.RunAsync(request, WmtpOperation.ReadRecords,
                token => m_store.CountAsync(query, token), cancellationToken);
        }

        /// <summary>
        /// Returns every selected record or an explicit encoding-limit failure, never an implicit truncated page.
        /// </summary>
        public ValueTask<ArrayOf<WMTPOutputDataType>> ReadAsync(
            WmtpRecordQuery query, WmtpRequest request, CancellationToken cancellationToken = default)
        {
            WmtpValidation.Query(query);
            Require(query.Selection switch
            {
                WmtpRecordSelection.All => true,
                WmtpRecordSelection.Index => Has(WmtpRecordFeatures.ReportIndex),
                WmtpRecordSelection.Time => Has(WmtpRecordFeatures.ReportTime),
                WmtpRecordSelection.First => Has(WmtpRecordFeatures.ReportFirst),
                WmtpRecordSelection.Last => Has(WmtpRecordFeatures.ReportLast),
                _ => false
            });
            return m_peripheral.RunAsync(request, WmtpOperation.ReadRecords, async token =>
            {
                ArrayOf<WMTPOutputDataType> result = await m_store.ReadAsync(query, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Count > 65536)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                if (query.Selection is WmtpRecordSelection.First or WmtpRecordSelection.Last && result.Count != 1)
                {
                    throw new ServiceResultException(result.Count == 0 ?
                        StatusCodes.BadNoData : StatusCodes.BadUnexpectedError);
                }
                var copies = new WMTPOutputDataType[result.Count];
                using var encoder = new BinaryEncoder(m_peripheral.MessageContext);
                encoder.WriteInt32(null, result.Count);
                for (int index = 0; index < result.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    WmtpValidation.Record(result[index]);
                    copies[index] = (WMTPOutputDataType)result[index].Clone();
                    encoder.WriteExtensionObject(null, new ExtensionObject(copies[index]));
                    if (encoder.Position > m_maximumReportBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The complete selected report exceeds the message budget.");
                    }
                }
                return copies.ToArrayOf();
            }, cancellationToken);
        }

        /// <summary>
        /// Deletes exactly the explicitly selected records after authorization.
        /// </summary>
        public async ValueTask DeleteAsync(
            WmtpRecordQuery query, WmtpRequest request, CancellationToken cancellationToken = default)
        {
            WmtpValidation.Query(query);
            Require(query.Selection switch
            {
                WmtpRecordSelection.All => Has(WmtpRecordFeatures.DeleteAll),
                WmtpRecordSelection.Index => Has(WmtpRecordFeatures.DeleteIndex),
                WmtpRecordSelection.Time => Has(WmtpRecordFeatures.DeleteTime),
                _ => false
            });
            _ = await m_peripheral.RunAsync(request, WmtpOperation.DeleteRecords, async token =>
            {
                await m_store.DeleteAsync(query, token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Binds the actual work-cycle method declarations, whose report outputs are Variant in the released XML.
        /// </summary>
        internal void Bind(ISystemContext context, WMTPWorkCycleDataState state)
        {
            if (Has(WmtpRecordFeatures.DeleteAll))
            {
                state.AddDeleteAllStoredRecords(context);
            }
            if (Has(WmtpRecordFeatures.DeleteIndex))
            {
                state.AddDeleteStoredRecordsIndex(context);
            }
            if (Has(WmtpRecordFeatures.DeleteTime))
            {
                state.AddDeleteStoredRecordsTime(context);
            }
            if (Has(WmtpRecordFeatures.TimeCount))
            {
                state.AddReportNumberOfStoredRecordsTime(context);
            }
            if (Has(WmtpRecordFeatures.ReportFirst))
            {
                state.AddCombinedReportFirstValue(context);
            }
            if (Has(WmtpRecordFeatures.ReportLast))
            {
                state.AddCombinedReportLastValue(context);
            }
            BindCommon(state.AbortOperation!, state.ReportNumberOfStoredRecords!,
                state.ReportNumberOfStoredRecordsTime, state.DeleteAllStoredRecords,
                state.DeleteStoredRecordsIndex, state.DeleteStoredRecordsTime,
                state.CombinedReportFirstValue, state.CombinedReportLastValue);
            state.CombinedReportAll!.OnCallAsync = async (ctx, _, _, token) =>
                new CombinedReportAllWMTPWorkCycleDataTypeMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    CombinedReport = Variant.FromStructure(await ReadAsync(
                        new WmtpRecordQuery(), m_peripheral.Request(ctx), token).ConfigureAwait(false))
                };
            if (Has(WmtpRecordFeatures.ReportIndex))
            {
                state.AddCombinedReportIndex(context);
                state.CombinedReportIndex!.OnCallAsync = async (ctx, _, _, from, to, token) =>
                    new CombinedReportIndexWMTPWorkCycleDataTypeMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        CombinedReport = Variant.FromStructure(await ReadAsync(
                            IndexQuery(from, to), m_peripheral.Request(ctx), token).ConfigureAwait(false))
                    };
            }
            if (Has(WmtpRecordFeatures.ReportTime))
            {
                state.AddCombinedReportTime(context);
                state.CombinedReportTime!.OnCallAsync = async (ctx, _, _, from, to, token) =>
                    new CombinedReportTimeWMTPWorkCycleDataTypeMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        CombinedReport = Variant.FromStructure(await ReadAsync(
                            TimeQuery(from, to), m_peripheral.Request(ctx), token).ConfigureAwait(false))
                    };
            }
        }

        /// <summary>
        /// Binds the released service-cycle declarations with typed structure-array report outputs.
        /// </summary>
        internal void Bind(ISystemContext context, WMTPServiceCycleDataState state)
        {
            if (Has(WmtpRecordFeatures.DeleteAll))
            {
                state.AddDeleteAllStoredRecords(context);
            }
            if (Has(WmtpRecordFeatures.DeleteIndex))
            {
                state.AddDeleteStoredRecordsIndex(context);
            }
            if (Has(WmtpRecordFeatures.DeleteTime))
            {
                state.AddDeleteStoredRecordsTime(context);
            }
            if (Has(WmtpRecordFeatures.TimeCount))
            {
                state.AddReportNumberOfStoredRecordsTime(context);
            }
            if (Has(WmtpRecordFeatures.ReportFirst))
            {
                state.AddCombinedReportFirstValue(context);
            }
            if (Has(WmtpRecordFeatures.ReportLast))
            {
                state.AddCombinedReportLastValue(context);
            }
            BindCommon(state.AbortOperation!, state.ReportNumberOfStoredRecords!,
                state.ReportNumberOfStoredRecordsTime, state.DeleteAllStoredRecords,
                state.DeleteStoredRecordsIndex, state.DeleteStoredRecordsTime,
                state.CombinedReportFirstValue, state.CombinedReportLastValue);
            state.CombinedReportAll!.OnCallAsync = async (ctx, _, _, token) =>
                new CombinedReportAllWMTPServiceCycleDataTypeMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    CombinedReport = await ReadAsync(new WmtpRecordQuery(), m_peripheral.Request(ctx), token)
                        .ConfigureAwait(false)
                };
            if (Has(WmtpRecordFeatures.ReportIndex))
            {
                state.AddCombinedReportIndex(context);
                state.CombinedReportIndex!.OnCallAsync = async (ctx, _, _, from, to, token) =>
                    new CombinedReportIndexWMTPServiceCycleDataTypeMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        CombinedReport = await ReadAsync(IndexQuery(from, to), m_peripheral.Request(ctx), token)
                            .ConfigureAwait(false)
                    };
            }
            if (Has(WmtpRecordFeatures.ReportTime))
            {
                state.AddCombinedReportTime(context);
                state.CombinedReportTime!.OnCallAsync = async (ctx, _, _, from, to, token) =>
                    new CombinedReportTimeWMTPServiceCycleDataTypeMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        CombinedReport = await ReadAsync(TimeQuery(from, to), m_peripheral.Request(ctx), token)
                            .ConfigureAwait(false)
                    };
            }
        }

        /// <summary>
        /// Binds shared generated method types without rebuilding their arguments or service protocol.
        /// </summary>
        private void BindCommon(
            MethodState abort,
            ReportNumberOfStoredRecordsMethodState count,
            ReportNumberOfStoredRecordsTimeMethodState? timeCount,
            DeleteAllStoredRecordsMethodState? deleteAll,
            DeleteStoredRecordsIndexMethodState? deleteIndex,
            DeleteStoredRecordsTimeMethodState? deleteTime,
            CombinedReportFirstValueMethodState? first,
            CombinedReportLastValueMethodState? last)
        {
            abort.OnCallMethod2Async = async (context, _, _, _, _, token) =>
            {
                await m_peripheral.AbortAsync(m_peripheral.Request(context), token).ConfigureAwait(false);
                return ServiceResult.Good;
            };
            count.OnCallAsync = async (context, _, _, token) => new ReportNumberOfStoredRecordsMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                NumberOfStoredRecords = await CountAsync(new WmtpRecordQuery(), m_peripheral.Request(context), token)
                    .ConfigureAwait(false)
            };
            if (timeCount != null)
            {
                timeCount.OnCallAsync = async (context, _, _, from, to, token) =>
                    new ReportNumberOfStoredRecordsTimeMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        NumberOfStoredRecords = await CountAsync(
                            TimeQuery(from, to), m_peripheral.Request(context), token).ConfigureAwait(false)
                    };
            }
            if (deleteAll != null)
            {
                deleteAll.OnCallAsync = async (context, _, _, token) =>
                {
                    await DeleteAsync(new WmtpRecordQuery(), m_peripheral.Request(context), token)
                        .ConfigureAwait(false);
                    return new DeleteAllStoredRecordsMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        DeleteAllStoredRecordsSuccessfull = true
                    };
                };
            }
            if (deleteIndex != null)
            {
                deleteIndex.OnCallAsync = async (context, _, _, from, to, token) =>
                {
                    await DeleteAsync(IndexQuery(from, to), m_peripheral.Request(context), token)
                        .ConfigureAwait(false);
                    return new DeleteStoredRecordsIndexMethodStateResult { ServiceResult = ServiceResult.Good };
                };
            }
            if (deleteTime != null)
            {
                deleteTime.OnCallAsync = async (context, _, _, from, to, token) =>
                {
                    await DeleteAsync(TimeQuery(from, to), m_peripheral.Request(context), token)
                        .ConfigureAwait(false);
                    return new DeleteStoredRecordsTimeMethodStateResult { ServiceResult = ServiceResult.Good };
                };
            }
            if (first != null)
            {
                first.OnCallAsync = async (context, _, _, token) => new CombinedReportFirstValueMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    CombinedReport = (await ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.First },
                        m_peripheral.Request(context), token).ConfigureAwait(false))[0]
                };
            }
            if (last != null)
            {
                last.OnCallAsync = async (context, _, _, token) => new CombinedReportLastValueMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    CombinedReport = (await ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.Last },
                        m_peripheral.Request(context), token).ConfigureAwait(false))[0]
                };
            }
        }

        /// <summary>
        /// Tests an explicit instance capability.
        /// </summary>
        private bool Has(WmtpRecordFeatures feature)
        {
            return (Features & feature) != 0;
        }

        /// <summary>
        /// Refuses access to methods not selected for this dataset.
        /// </summary>
        private static void Require(bool available)
        {
            if (!available)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
        }

        /// <summary>
        /// Creates an inclusive index query.
        /// </summary>
        private static WmtpRecordQuery IndexQuery(uint from, uint to)
        {
            return new WmtpRecordQuery { Selection = WmtpRecordSelection.Index, FromIndex = from, ToIndex = to };
        }

        /// <summary>
        /// Creates an inclusive time query.
        /// </summary>
        private static WmtpRecordQuery TimeQuery(DateTimeUtc from, DateTimeUtc to)
        {
            return new WmtpRecordQuery { Selection = WmtpRecordSelection.Time, FromTime = from, ToTime = to };
        }

        /// <summary>The owner of authorization and operation lifetime.</summary>
        private readonly WmtpPeripheral m_peripheral;

        /// <summary>The independently configured dataset provider.</summary>
        private readonly IWmtpRecordStore m_store;

        /// <summary>The bounded complete-response encoding budget.</summary>
        private readonly int m_maximumReportBytes;
    }
}
