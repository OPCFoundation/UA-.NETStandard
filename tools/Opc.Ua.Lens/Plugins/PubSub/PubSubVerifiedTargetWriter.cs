/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.DataSets;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// The adapter returns per-field UA statuses; the generic target sink does not throw
/// for bad statuses. Reject the local commit when a selected UA write fails.
/// </summary>
internal sealed class PubSubVerifiedTargetWriter : ITargetVariableWriter
{
    public PubSubVerifiedTargetWriter(ITargetVariableWriter writer, PubSubObservationStore store)
    {
        m_writer = writer ?? throw new ArgumentNullException(nameof(writer));
        m_store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask<StatusCode> WriteAsync(
        NodeId nodeId,
        uint attributeId,
        string? writeIndexRange,
        DataValue value,
        CancellationToken cancellationToken = default)
    {
        StatusCode status = await m_writer.WriteAsync(
            nodeId, attributeId, writeIndexRange, value, cancellationToken).ConfigureAwait(false);
        if (!StatusCode.IsGood(status))
        {
            m_store.RecordEvidence("Write-back", status,
                "A UA write failed. Earlier server writes may have applied; the local dataset was not committed.");
            throw new ServiceResultException(status);
        }
        return status;
    }

    private readonly ITargetVariableWriter m_writer;
    private readonly PubSubObservationStore m_store;
}
