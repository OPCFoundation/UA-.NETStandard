/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Discovers and transfers Subscriptions for OPC 10000-4 §6.6.3 client redundancy Failover.
    /// </summary>
    public sealed class ClientFailoverCoordinator : IClientFailoverCoordinator
    {
        /// <inheritdoc/>
        public async ValueTask<ArrayOf<uint>> DiscoverActiveSubscriptionIdsAsync(
            ISession backupSession,
            ClientRedundancyTransferOptions options,
            CancellationToken ct = default)
        {
            if (backupSession is null)
            {
                throw new ArgumentNullException(nameof(backupSession));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            EnsureSameUser(backupSession, options);
            NodeId activeSessionId = options.ActiveSessionId;
            if (activeSessionId.IsNull)
            {
                activeSessionId = await FindSessionIdByNameAsync(
                    backupSession,
                    options.ActiveSessionName,
                    ct).ConfigureAwait(false);
            }

            if (activeSessionId.IsNull)
            {
                return [];
            }

            return await FindSubscriptionIdsAsync(
                backupSession,
                activeSessionId,
                ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<TransferResult>> TransferActiveSubscriptionsAsync(
            ISession backupSession,
            ClientRedundancyTransferOptions options,
            CancellationToken ct = default)
        {
            ArrayOf<uint> subscriptionIds = await DiscoverActiveSubscriptionIdsAsync(
                backupSession,
                options,
                ct).ConfigureAwait(false);
            if (subscriptionIds.Count == 0)
            {
                return [];
            }

            TransferSubscriptionsResponse response = await backupSession.TransferSubscriptionsAsync(
                    null,
                    subscriptionIds,
                    options.SendInitialValues,
                    ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, subscriptionIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);
            return response.Results;
        }

        private static async ValueTask<NodeId> FindSessionIdByNameAsync(
            ISession session,
            string sessionName,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(sessionName))
            {
                return NodeId.Null;
            }

            DataValue value = await session.ReadValueAsync(
                VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                return NodeId.Null;
            }

            foreach (SessionDiagnosticsDataType diagnostics in ReadSessionDiagnostics(value))
            {
                if (string.Equals(diagnostics.SessionName, sessionName, StringComparison.Ordinal))
                {
                    return diagnostics.SessionId;
                }
            }

            return NodeId.Null;
        }

        private static async ValueTask<ArrayOf<uint>> FindSubscriptionIdsAsync(
            ISession session,
            NodeId activeSessionId,
            CancellationToken ct)
        {
            DataValue value = await session.ReadValueAsync(
                VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray,
                ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                return [];
            }

            var subscriptionIds = new List<uint>();
            foreach (SubscriptionDiagnosticsDataType diagnostics in ReadSubscriptionDiagnostics(value))
            {
                if (diagnostics.SessionId == activeSessionId)
                {
                    subscriptionIds.Add(diagnostics.SubscriptionId);
                }
            }

            return new ArrayOf<uint>(subscriptionIds.ToArray());
        }

        private static IEnumerable<SessionDiagnosticsDataType> ReadSessionDiagnostics(
            DataValue value)
        {
            if (value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> extensionObjects))
            {
                for (int ii = 0; ii < extensionObjects.Count; ii++)
                {
                    ExtensionObject extensionObject = extensionObjects[ii];
                    if (extensionObject.TryGetValue(out SessionDiagnosticsDataType? diagnostics))
                    {
                        yield return diagnostics;
                    }
                }
            }
        }

        private static IEnumerable<SubscriptionDiagnosticsDataType> ReadSubscriptionDiagnostics(
            DataValue value)
        {
            if (value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> extensionObjects))
            {
                for (int ii = 0; ii < extensionObjects.Count; ii++)
                {
                    ExtensionObject extensionObject = extensionObjects[ii];
                    if (extensionObject.TryGetValue(out SubscriptionDiagnosticsDataType? diagnostics))
                    {
                        yield return diagnostics;
                    }
                }
            }
        }

        private static void EnsureSameUser(
            ISession backupSession,
            ClientRedundancyTransferOptions options)
        {
            if (string.IsNullOrEmpty(options.ActiveUserDisplayName))
            {
                return;
            }

            string backupUserName = backupSession.Identity.DisplayName;

            if (!string.Equals(
                backupUserName,
                options.ActiveUserDisplayName,
                StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    "TransferSubscriptions requires the backup client to use the same user.");
            }
        }
    }
}
