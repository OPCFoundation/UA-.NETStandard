/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Thin façade over <see cref="ISession.HistoryUpdateAsync"/> for the
/// single-row operations exposed by the Historian tab (Insert / Replace
/// / Update — Part 11 §6.8.2 — and Delete-at-time — Part 11 §6.8.5).
/// Any operation-level error is surfaced as a
/// <see cref="ServiceResultException"/> so callers can render a friendly
/// status text without poking into <see cref="HistoryUpdateResult"/>.
/// </summary>
internal sealed class HistoryUpdater
{
    private readonly ISession m_session;

    public HistoryUpdater(ISession session)
    {
        m_session = session;
    }

    /// <summary>
    /// Performs an Insert / Replace / Update on a single historical
    /// timestamp using <see cref="UpdateDataDetails"/>.
    /// </summary>
    public async Task UpdateAsync(
        NodeId nodeId,
        PerformUpdateType action,
        DateTime timestamp,
        Variant value,
        StatusCode status,
        CancellationToken ct)
    {
        var dv = new DataValue
        {
            WrappedValue = value,
            StatusCode = status,
            SourceTimestamp = timestamp,
            ServerTimestamp = timestamp
        };
        var details = new UpdateDataDetails
        {
            NodeId = nodeId,
            PerformInsertReplace = action,
            UpdateValues = new DataValue[] { dv }
        };
        var detailsArray = new ExtensionObject[] { new(details) };

        HistoryUpdateResponse response = await m_session.HistoryUpdateAsync(
            requestHeader: null,
            historyUpdateDetails: detailsArray,
            ct: ct).ConfigureAwait(false);

        ThrowIfFailed(response);
    }

    /// <summary>
    /// Deletes a single historical timestamp using
    /// <see cref="DeleteAtTimeDetails"/>.
    /// </summary>
    public async Task DeleteAsync(
        NodeId nodeId,
        DateTime timestamp,
        CancellationToken ct)
    {
        var details = new DeleteAtTimeDetails
        {
            NodeId = nodeId,
            ReqTimes = new DateTimeUtc[] { timestamp }
        };
        var detailsArray = new ExtensionObject[] { new(details) };

        HistoryUpdateResponse response = await m_session.HistoryUpdateAsync(
            requestHeader: null,
            historyUpdateDetails: detailsArray,
            ct: ct).ConfigureAwait(false);

        ThrowIfFailed(response);
    }

    private static void ThrowIfFailed(HistoryUpdateResponse response)
    {
        if (response.Results.Count == 0)
        {
            return;
        }
        HistoryUpdateResult result = response.Results[0];
        if (StatusCode.IsBad(result.StatusCode))
        {
            throw new ServiceResultException(
                result.StatusCode,
                $"HistoryUpdate returned {result.StatusCode}.");
        }
        if (result.OperationResults.Count > 0)
        {
            foreach (StatusCode op in result.OperationResults)
            {
                if (StatusCode.IsBad(op))
                {
                    throw new ServiceResultException(
                        op,
                        $"HistoryUpdate per-operation result: {op}.");
                }
            }
        }
    }
}
