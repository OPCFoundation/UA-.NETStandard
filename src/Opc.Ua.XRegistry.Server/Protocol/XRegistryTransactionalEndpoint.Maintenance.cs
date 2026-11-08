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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    /// <summary>
    /// Local, explicit outcome acknowledgment. This is not part of the standard HTTP API.
    /// Retired operation identities remain permanent replay barriers.
    /// </summary>
    public interface IXRegistryJournalMaintenance
    {
        /// <summary>
        /// Retires acknowledged response bodies owned by this caller, retaining digest and committed/rejected state.
        /// No pending preparation can coexist with maintenance.
        /// </summary>
        ValueTask<int> RetireOutcomesAsync(
            ArrayOf<string> operationIds, XRegistryCallContext context, CancellationToken cancellationToken = default);
    }

    public sealed partial class XRegistryTransactionalEndpoint : IXRegistryJournalMaintenance
    {
        /// <inheritdoc/>
        public async ValueTask<int> RetireOutcomesAsync(
            ArrayOf<string> operationIds, XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            if (!await AuthorizeAsync(context, true, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("Operation acknowledgment requires write authorization.");
            }
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_preparedOperations) != 0)
                {
                    throw new InvalidOperationException(
                        "Complete or abort all prepared operations before journal maintenance.");
                }
                JsonObject prior = await LoadAsync(cancellationToken).ConfigureAwait(false);
                var candidate = (JsonObject)prior.DeepClone();
                JsonObject operations = XRegistryModelRules.Object(candidate["operations"]);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                int count = 0;
                foreach (string id in operationIds)
                {
                    string key = OperationKey(id, context);
                    if (!keys.Add(key) || operations[key] is not JsonObject operation)
                    {
                        throw new ArgumentException(
                            "Acknowledgments must identify distinct operations owned by this caller.",
                            nameof(operationIds));
                    }
                    if (operation["response"] is not JsonNode encoded)
                    {
                        continue;
                    }
                    XRegistryResponse response = m_codec.DecodeResponse(
                        ByteString.From(Convert.FromBase64String(XRegistryModelRules.Text(encoded))));
                    operation["retiredstatus"] = response.StatusCode;
                    operation.Remove("response");
                    count++;
                }
                if (count == 0)
                {
                    return 0;
                }
                candidate["generation"] = XRegistryModelRules.Unsigned(prior["generation"]).AddOne();
                ByteString replacement = EncodeSnapshot(candidate);
                cancellationToken.ThrowIfCancellationRequested();
                m_indeterminate = true;
                bool committed =
                    await m_store.CommitAsync(m_expected, replacement, cancellationToken).ConfigureAwait(false);
                m_indeterminate = false;
                if (!committed)
                {
                    m_snapshot = null;
                    throw new InvalidOperationException(
                        "The journal changed before acknowledgment; no outcome was retired.");
                }
                m_expected = replacement;
                m_snapshot = candidate;
                m_observationGeneration = Guid.NewGuid().ToString("N");
                return count;
            }
            finally
            {
                m_serial.Release();
            }
        }
    }
}
