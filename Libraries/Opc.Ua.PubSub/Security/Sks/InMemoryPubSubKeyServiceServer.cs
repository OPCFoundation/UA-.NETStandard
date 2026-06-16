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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// In-memory implementation of <see cref="IPubSubKeyServiceServer"/>
    /// suitable for unit / integration tests and for embedded SKS
    /// scenarios where keys live for the lifetime of the host
    /// process. Keys are produced by <see cref="SksKeyGenerator"/>
    /// using the configured <see cref="IPubSubSecurityPolicy"/>.
    /// </summary>
    /// <remarks>
    /// Implements the SKS server-side surface defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.1">
    /// Part 14 §8.3.1 PubSubKeyServiceType</see>. State is guarded
    /// by an internal <see cref="System.Threading.Lock"/>; the lock
    /// is never exposed.
    /// </remarks>
    public sealed class InMemoryPubSubKeyServiceServer : IPubSubKeyServiceServer
    {
        private const int DefaultMaxFutureKeyCount = 4;
        private const int DefaultMaxPastKeyCount = 4;

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, SecurityGroupState> m_groups =
            new(StringComparer.Ordinal);
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes a new
        /// <see cref="InMemoryPubSubKeyServiceServer"/>.
        /// </summary>
        /// <param name="timeProvider">Time source.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public InMemoryPubSubKeyServiceServer(
            TimeProvider? timeProvider = null,
            ITelemetryContext? telemetry = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry is null
                ? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
                : telemetry.CreateLogger<InMemoryPubSubKeyServiceServer>();
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> SecurityGroupIds
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_groups.Keys];
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask AddSecurityGroupAsync(
            SksSecurityGroup group,
            CancellationToken cancellationToken = default)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            cancellationToken.ThrowIfCancellationRequested();

            IPubSubSecurityPolicy? policy =
                PubSubSecurityPolicyRegistry.GetByUri(group.SecurityPolicyUri);
            if (policy is null)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadSecurityPolicyRejected,
                    $"SecurityPolicyUri '{group.SecurityPolicyUri}' is not supported.");
            }

            lock (m_lock)
            {
                if (m_groups.ContainsKey(group.SecurityGroupId))
                {
                    throw new OpcUaSksException(
                        StatusCodes.BadAlreadyExists,
                        $"SecurityGroup '{group.SecurityGroupId}' already exists.");
                }

                int maxFuture = group.MaxFutureKeyCount > 0
                    ? group.MaxFutureKeyCount
                    : DefaultMaxFutureKeyCount;
                int maxPast = group.MaxPastKeyCount > 0
                    ? group.MaxPastKeyCount
                    : DefaultMaxPastKeyCount;

                List<PubSubSecurityKey> keys = group.Keys is { Count: > 0 } seed
                    ? new List<PubSubSecurityKey>(seed)
                    : SeedInitialKeys(policy, maxFuture, group.KeyLifetime);

                uint nextTokenId = NextTokenIdAfter(keys);
                var configured = new SksSecurityGroup(
                    group.SecurityGroupId,
                    group.SecurityPolicyUri,
                    group.KeyLifetime,
                    maxFuture,
                    maxPast,
                    keys);
                var state = new SecurityGroupState(
                    configured,
                    policy,
                    keys,
                    nextTokenId);
                m_groups[group.SecurityGroupId] = state;
                m_logger.LogInformation(
                    "Registered SKS SecurityGroup {GroupId} with policy {PolicyUri}.",
                    group.SecurityGroupId,
                    group.SecurityPolicyUri);
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RemoveSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException(
                    "SecurityGroupId must be non-empty.",
                    nameof(securityGroupId));
            }
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                if (!m_groups.Remove(securityGroupId))
                {
                    throw new OpcUaSksException(
                        StatusCodes.BadNotFound,
                        $"SecurityGroup '{securityGroupId}' is not registered.");
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<SksSecurityGroup?> GetSecurityGroupAsync(
            string securityGroupId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException(
                    "SecurityGroupId must be non-empty.",
                    nameof(securityGroupId));
            }
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                if (!m_groups.TryGetValue(securityGroupId, out SecurityGroupState? state))
                {
                    return new ValueTask<SksSecurityGroup?>((SksSecurityGroup?)null);
                }
                return new ValueTask<SksSecurityGroup?>(SnapshotLocked(state));
            }
        }

        /// <inheritdoc/>
        public ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            string callerIdentity,
            SksKeyRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(callerIdentity))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "Caller identity must be authenticated.");
            }
            if (string.IsNullOrEmpty(request.SecurityGroupId))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadInvalidArgument,
                    "SecurityGroupId must be non-empty.");
            }
            cancellationToken.ThrowIfCancellationRequested();

            lock (m_lock)
            {
                if (!m_groups.TryGetValue(request.SecurityGroupId, out SecurityGroupState? state))
                {
                    throw new OpcUaSksException(
                        StatusCodes.BadNotFound,
                        $"SecurityGroup '{request.SecurityGroupId}' is not registered.");
                }

                EnsureFutureKeysLocked(state, request.RequestedKeyCount);

                uint currentTokenId = state.Keys.Count == 0
                    ? 0u
                    : state.Keys[0].TokenId;
                uint firstTokenId = request.StartingTokenId == 0u
                    ? currentTokenId
                    : request.StartingTokenId;

                var packed = new List<byte[]>();
                int matched = 0;
                for (int i = 0; i < state.Keys.Count && matched < request.RequestedKeyCount; i++)
                {
                    PubSubSecurityKey key = state.Keys[i];
                    if (key.TokenId < firstTokenId)
                    {
                        continue;
                    }
                    packed.Add(SksKeyGenerator.Pack(key));
                    matched++;
                }

                if (matched < request.RequestedKeyCount)
                {
                    int additional = (int)request.RequestedKeyCount - matched;
                    int allowed = (state.Group.MaxFutureKeyCount + 1) - state.Keys.Count;
                    int toGenerate = Math.Min(additional, allowed);
                    if (toGenerate > 0)
                    {
                        DateTimeUtc nowGen = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
                        for (int i = 0; i < toGenerate; i++)
                        {
                            PubSubSecurityKey newKey = SksKeyGenerator.Generate(
                                state.Policy,
                                state.NextTokenId,
                                nowGen,
                                state.Group.KeyLifetime);
                            state.Keys.Add(newKey);
                            state.NextTokenId = unchecked(state.NextTokenId + 1u);
                            packed.Add(SksKeyGenerator.Pack(newKey));
                            matched++;
                        }
                    }
                }

                if (packed.Count == 0)
                {
                    throw new OpcUaSksException(
                        StatusCodes.BadNotFound,
                        $"No keys available starting at TokenId {firstTokenId}.");
                }

                uint actualFirst = state.Keys[FindFirstIndexLocked(state, firstTokenId)].TokenId;
                TimeSpan timeToNextKey = ComputeTimeToNextKeyLocked(state);
                var response = new SksKeyResponse(
                    state.Group.SecurityPolicyUri,
                    actualFirst,
                    packed,
                    timeToNextKey,
                    state.Group.KeyLifetime);
                m_logger.LogDebug(
                    "Issued {Count} key(s) for {GroupId} starting at TokenId {TokenId} to {Caller}.",
                    packed.Count,
                    request.SecurityGroupId,
                    actualFirst,
                    callerIdentity);
                return new ValueTask<SksKeyResponse>(response);
            }
        }

        private static int FindFirstIndexLocked(SecurityGroupState state, uint tokenId)
        {
            for (int i = 0; i < state.Keys.Count; i++)
            {
                if (state.Keys[i].TokenId >= tokenId)
                {
                    return i;
                }
            }
            return state.Keys.Count - 1;
        }

        private List<PubSubSecurityKey> SeedInitialKeys(
            IPubSubSecurityPolicy policy,
            int maxFutureKeyCount,
            TimeSpan lifetime)
        {
            DateTimeUtc now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            var keys = new List<PubSubSecurityKey>(maxFutureKeyCount + 1);
            for (int i = 0; i <= maxFutureKeyCount; i++)
            {
                keys.Add(SksKeyGenerator.Generate(policy, (uint)(i + 1), now, lifetime));
            }
            return keys;
        }

        private void EnsureFutureKeysLocked(SecurityGroupState state, uint requestedKeyCount)
        {
            int total = state.Keys.Count;
            int needed = (int)requestedKeyCount;
            if (total >= needed)
            {
                return;
            }

            int maxPossible = state.Group.MaxFutureKeyCount + 1;
            int target = Math.Min(needed, maxPossible);
            int toAdd = target - total;
            if (toAdd <= 0)
            {
                return;
            }
            DateTimeUtc now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            for (int i = 0; i < toAdd; i++)
            {
                PubSubSecurityKey newKey = SksKeyGenerator.Generate(
                    state.Policy,
                    state.NextTokenId,
                    now,
                    state.Group.KeyLifetime);
                state.Keys.Add(newKey);
                state.NextTokenId = unchecked(state.NextTokenId + 1u);
            }
        }

        private TimeSpan ComputeTimeToNextKeyLocked(SecurityGroupState state)
        {
            if (state.Keys.Count == 0)
            {
                return TimeSpan.Zero;
            }
            PubSubSecurityKey current = state.Keys[0];
            DateTimeUtc now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            TimeSpan elapsed = now - current.IssuedAt;
            TimeSpan remaining = state.Group.KeyLifetime - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private static SksSecurityGroup SnapshotLocked(SecurityGroupState state)
        {
            return new SksSecurityGroup(
                state.Group.SecurityGroupId,
                state.Group.SecurityPolicyUri,
                state.Group.KeyLifetime,
                state.Group.MaxFutureKeyCount,
                state.Group.MaxPastKeyCount,
                [.. state.Keys]);
        }

        private static uint NextTokenIdAfter(List<PubSubSecurityKey> keys)
        {
            if (keys.Count == 0)
            {
                return 1u;
            }
            return unchecked(keys[keys.Count - 1].TokenId + 1u);
        }

        private sealed class SecurityGroupState
        {
            public SecurityGroupState(
                SksSecurityGroup group,
                IPubSubSecurityPolicy policy,
                List<PubSubSecurityKey> keys,
                uint nextTokenId)
            {
                Group = group;
                Policy = policy;
                Keys = keys;
                NextTokenId = nextTokenId;
            }

            public SksSecurityGroup Group { get; }

            public IPubSubSecurityPolicy Policy { get; }

            public List<PubSubSecurityKey> Keys { get; }

            public uint NextTokenId { get; set; }
        }
    }
}
