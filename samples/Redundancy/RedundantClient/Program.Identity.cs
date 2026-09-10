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
using Opc.Ua;
using Opc.Ua.Client;

namespace RedundantClient
{
    public static partial class Program
    {
        private static async Task RunIdentityFailoverScenarioAsync(ManagedSession session, CancellationToken ct)
        {
            NodeId folder = await BrowseIdentityChildAsync(session, ObjectIds.ObjectsFolder, "HighAvailability", ct)
                .ConfigureAwait(false);
            NodeId generated = await BrowseIdentityChildAsync(session, folder, "FactoryAssigned", ct)
                .ConfigureAwait(false);
            NodeId value = await BrowseIdentityChildAsync(session, generated, "Value", ct).ConfigureAwait(false);
            NodeId target = await BrowseIdentityChildAsync(session, generated, "Target", ct).ConfigureAwait(false);
            await ReadCachedIdentityAsync(session, value, target, ct).ConfigureAwait(false);
            string initialServer = await ReadServingServerUriAsync(session, ct).ConfigureAwait(false);
            var changedReplica = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int interrupted = 0;
            void OnConnectionStateChanged(object? _, ConnectionStateChangedEventArgs args)
            {
                if (args.NewState is ConnectionState.Reconnecting or ConnectionState.Failover)
                {
                    Interlocked.Exchange(ref interrupted, 1);
                }
                else if (args.NewState == ConnectionState.Connected && Volatile.Read(ref interrupted) != 0)
                {
                    changedReplica.TrySetResult(true);
                }
            }
            session.ConnectionStateChanged += OnConnectionStateChanged;
            try
            {
                Console.WriteLine(
                    "IDENTITY: cached shared NodeIds Value={0}, Target={1}; remove the active server now.",
                    value, target);
                await changedReplica.Task.WaitAsync(TimeSpan.FromSeconds(90), ct).ConfigureAwait(false);
                if (await ReadServingServerUriAsync(session, ct).ConfigureAwait(false) == initialServer)
                {
                    throw new InvalidOperationException("The identity scenario requires a different serving replica.");
                }
                await ReadCachedIdentityAsync(session, value, target, ct).ConfigureAwait(false);
                await CreateCachedIdentityMonitoredItemAsync(session, value, ct).ConfigureAwait(false);
                Console.WriteLine(
                    "IDENTITY HA OK: exact cached NodeIds read and monitored on the promoted replica, " +
                    "without remapping or rebrowsing.");
            }
            finally
            {
                session.ConnectionStateChanged -= OnConnectionStateChanged;
            }
        }

        private static async Task<NodeId> BrowseIdentityChildAsync(
            ManagedSession session,
            NodeId parent,
            string name,
            CancellationToken ct)
        {
            BrowseResponse response = await session.BrowseAsync(null, null, 0,
                [
                    new BrowseDescription
                    {
                        NodeId = parent,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], ct).ConfigureAwait(false);
            if (response.Results.Count != 1 || StatusCode.IsBad(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    $"Cannot browse shared node '{parent}'.");
            }
            ArrayOf<ReferenceDescription> references = response.Results[0].References;
            for (int i = 0; i < references.Count; i++)
            {
                ReferenceDescription reference = references[i];
                if (reference.BrowseName.Name == name &&
                    reference.NodeId.ServerIndex == 0 &&
                    string.IsNullOrEmpty(reference.NodeId.NamespaceUri))
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                }
            }
            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, $"Shared child '{name}' was not returned.");
        }

        private static async Task<string> ReadServingServerUriAsync(ManagedSession session, CancellationToken ct)
        {
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = VariableIds.Server_ServerArray, AttributeId = Attributes.Value }], ct)
                .ConfigureAwait(false);
            if (response.Results.Count != 1 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                !response.Results[0].WrappedValue.TryGetValue(out ArrayOf<string> serverUris) ||
                serverUris.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "The serving replica did not return its application URI in ServerArray.");
            }
            return serverUris[0];
        }

        private static async Task ReadCachedIdentityAsync(
            ManagedSession session,
            NodeId value,
            NodeId target,
            CancellationToken ct)
        {
            if (session.NamespaceUris.GetString(value.NamespaceIndex) != kHighAvailabilityNamespaceUri ||
                session.NamespaceUris.GetString(target.NamespaceIndex) != kHighAvailabilityNamespaceUri)
            {
                throw new InvalidOperationException("The serving replica changed a cached shared namespace index.");
            }
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [
                    new ReadValueId { NodeId = value, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = target, AttributeId = Attributes.Value }
                ], ct).ConfigureAwait(false);
            if (response.Results.Count != 2 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                StatusCode.IsBad(response.Results[1].StatusCode) ||
                response.Results[0].WrappedValue != Variant.From(12345) ||
                response.Results[1].WrappedValue != Variant.From(value))
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "Cached shared identities or their NodeId-valued references did not survive failover unchanged.");
            }
        }

        private static async Task CreateCachedIdentityMonitoredItemAsync(
            ManagedSession session,
            NodeId value,
            CancellationToken ct)
        {
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 1000, 60, 10, 0, false, 0, ct).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId { NodeId = value, AttributeId = Attributes.Value },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 1000,
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    ], ct).ConfigureAwait(false);
                if (items.Results.Count != 1 || StatusCode.IsBad(items.Results[0].StatusCode))
                {
                    throw new ServiceResultException(StatusCodes.BadMonitoredItemIdInvalid,
                        "A monitored item could not use the exact cached shared NodeId after failover.");
                }
            }
            finally
            {
                await session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], ct).ConfigureAwait(false);
            }
        }
    }
}
