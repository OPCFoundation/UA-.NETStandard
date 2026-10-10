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

            // A transferred subscription needs a client-side owner on the backup
            // session, otherwise its publish engine treats the incoming ids as
            // unknown and deletes them (or never publishes and they expire).
            // Subscriptions the backup has prepared for the takeover (not yet
            // created, TransferId = the active client's subscription id, e.g.
            // restored with ISession.Load) are transferred through the session so
            // it binds them and resumes publishing.
            var discovered = new HashSet<uint>();
            foreach (uint id in subscriptionIds)
            {
                discovered.Add(id);
            }
            var templates = new SubscriptionCollection();
            foreach (Subscription subscription in backupSession.Subscriptions)
            {
                if (!subscription.Created && discovered.Contains(subscription.TransferId))
                {
                    templates.Add(subscription);
                }
            }
            if (templates.Count > 0)
            {
                return await TransferPreparedSubscriptionsAsync(
                    backupSession,
                    subscriptionIds,
                    templates,
                    options.SendInitialValues,
                    ct).ConfigureAwait(false);
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

        private static async ValueTask<ArrayOf<TransferResult>> TransferPreparedSubscriptionsAsync(
            ISession backupSession,
            ArrayOf<uint> subscriptionIds,
            SubscriptionCollection templates,
            bool sendInitialValues,
            CancellationToken ct)
        {
            var byId = new Dictionary<uint, Subscription>();
            foreach (Subscription template in templates)
            {
                byId[template.TransferId] = template;
            }

            await backupSession.TransferSubscriptionsAsync(templates, sendInitialValues, ct)
                .ConfigureAwait(false);

            // Ids the backup has no prepared owner for are still moved with the raw
            // service, as before; the caller sees them in the results.
            var unowned = new List<uint>();
            foreach (uint id in subscriptionIds)
            {
                if (!byId.ContainsKey(id))
                {
                    unowned.Add(id);
                }
            }
            var unownedResults = new Dictionary<uint, TransferResult>();
            if (unowned.Count > 0)
            {
                var unownedIds = new ArrayOf<uint>(unowned.ToArray());
                TransferSubscriptionsResponse response = await backupSession.TransferSubscriptionsAsync(
                        null,
                        unownedIds,
                        sendInitialValues,
                        ct)
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, unownedIds);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, unownedIds);
                for (int ii = 0; ii < unownedIds.Count; ii++)
                {
                    unownedResults[unownedIds[ii]] = response.Results[ii];
                }
            }

            var results = new TransferResult[subscriptionIds.Count];
            for (int ii = 0; ii < subscriptionIds.Count; ii++)
            {
                uint id = subscriptionIds[ii];
                results[ii] = byId.TryGetValue(id, out Subscription? template)
                    ? new TransferResult
                    {
                        StatusCode = template.Created
                            ? StatusCodes.Good
                            : StatusCodes.BadSubscriptionIdInvalid
                    }
                    : unownedResults[id];
            }
            return new ArrayOf<TransferResult>(results);
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

            NodeId activeSessionId = NodeId.Null;
            foreach (SessionDiagnosticsDataType diagnostics in ReadSessionDiagnostics(value))
            {
                if (diagnostics.SessionId.IsNull ||
                    diagnostics.SessionId == session.SessionId ||
                    !string.Equals(diagnostics.SessionName, sessionName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!activeSessionId.IsNull && activeSessionId != diagnostics.SessionId)
                {
                    throw new InvalidOperationException(
                        "Multiple active sessions have the requested name. Specify ActiveSessionId for takeover.");
                }
                activeSessionId = diagnostics.SessionId;
            }

            return activeSessionId;
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
