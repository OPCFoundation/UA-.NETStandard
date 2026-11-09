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
