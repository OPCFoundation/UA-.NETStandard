// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.Redaction;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Implementation of <see cref="IPubSubKeyServiceServer"/> with a process-local
    /// default. A coordinated store makes it possible to run multiple SKS replicas.
    /// Keys are produced by <see cref="SksKeyGenerator"/> using the configured
    /// <see cref="IPubSubSecurityPolicy"/>.
    /// </summary>
    /// <remarks>
    /// Implements the SKS server-side surface defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.1">
    /// Part 14 §8.3.1 PubSubKeyServiceType</see>. For coordinated stores no
    /// local cache is used; authorization and keys come from the same
    /// authoritative snapshot.
    /// </remarks>
    public sealed class InMemoryPubSubKeyServiceServer : IPubSubKeyServiceServer, IAsyncPubSubSecurityGroupCatalog
    {
        private const int DefaultMaxFutureKeyCount = 4;
        private const int DefaultMaxPastKeyCount = 4;
        private const int MaxCommitAttempts = 16;

        private readonly Lock m_lock = new();

        private readonly Dictionary<string, SecurityGroupState> m_groups =
            new(StringComparer.Ordinal);

        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly IPubSubSecurityEventSink? m_securityEventSink;
        private readonly IPubSubSecurityKeyStore m_keyStore;
        private readonly ICoordinatedPubSubSecurityKeyStore? m_coordinatedStore;
        private readonly IPubSubSecurityPolicyRegistry m_securityPolicies;

        /// <summary>
        /// Initializes a new
        /// <see cref="InMemoryPubSubKeyServiceServer"/>.
        /// </summary>
        /// <param name="timeProvider">Time source.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="securityEventSink">Optional structured security-event sink.</param>
        /// <param name="keyStore">Optional external SecurityGroup key store.</param>
        /// <param name="securityPolicies">
        /// The PubSub policy set SecurityGroups are validated against, or
        /// <see langword="null"/> to use
        /// <see cref="PubSubSecurityPolicyRegistry.Default"/>.
        /// </param>
        public InMemoryPubSubKeyServiceServer(
            TimeProvider? timeProvider = null,
            ITelemetryContext? telemetry = null,
            IPubSubSecurityEventSink? securityEventSink = null,
            IPubSubSecurityKeyStore? keyStore = null,
            IPubSubSecurityPolicyRegistry? securityPolicies = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry is null
                ? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
                : telemetry.CreateLogger<InMemoryPubSubKeyServiceServer>();
            m_securityEventSink = securityEventSink;
            m_keyStore = keyStore ?? new InMemoryPubSubSecurityKeyStore();
            m_coordinatedStore = m_keyStore as ICoordinatedPubSubSecurityKeyStore;
            m_securityPolicies = securityPolicies ?? PubSubSecurityPolicyRegistry.Default;
            if (m_coordinatedStore is null)
            {
                RestoreSecurityGroups();
            }
        }

        /// <inheritdoc/>
        public ArrayOf<string> SecurityGroupIds
        {
            get
            {
                if (m_coordinatedStore is not null)
                {
                    if (m_coordinatedStore.IsProcessLocal)
                    {
                        ValueTask<ArrayOf<string>> ids = m_coordinatedStore.GetSecurityGroupIdsAsync();
                        if (ids.IsCompletedSuccessfully)
                        {
                            return ids.Result;
                        }
                    }
                    throw new NotSupportedException("Use GetSecurityGroupIdsAsync with a coordinated SKS store.");
                }
                lock (m_lock)
                {
                    return [.. m_groups.Keys];
                }
            }
        }

        /// <summary>
        /// Whether enumeration and method dispatch must use asynchronous bindings.
        /// </summary>
        internal bool UsesRemoteStore => m_coordinatedStore is { IsProcessLocal: false };

        /// <inheritdoc/>
        public ValueTask<ArrayOf<string>> GetSecurityGroupIdsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return m_coordinatedStore is null
                ? new ValueTask<ArrayOf<string>>(SecurityGroupIds)
                : m_coordinatedStore.GetSecurityGroupIdsAsync(cancellationToken);
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

            IPubSubSecurityPolicy policy = GetRequiredPolicy(group);
            if (m_coordinatedStore is not null)
            {
                return AddCoordinatedSecurityGroupAsync(m_coordinatedStore, group, policy, cancellationToken);
            }

            SksSecurityGroup snapshot;
            lock (m_lock)
            {
                if (m_groups.ContainsKey(group.SecurityGroupId))
                {
                    throw SecurityGroupAlreadyExists(group.SecurityGroupId);
                }

                SecurityGroupState state = CreateSecurityGroupState(group, policy);
                m_groups[group.SecurityGroupId] = state;
                snapshot = Snapshot(state);
                m_logger.RegisteredSksSecurityGroup(group.SecurityGroupId, group.SecurityPolicyUri);
            }
            return m_keyStore.SaveSecurityGroupAsync(snapshot, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveSecurityGroupAsync(
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

            if (m_coordinatedStore is not null)
            {
                if (!await m_coordinatedStore.RemoveSecurityGroupAsync(securityGroupId, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw SecurityGroupNotFound(securityGroupId);
                }
                return;
            }

            lock (m_lock)
            {
                if (!m_groups.Remove(securityGroupId))
                {
                    throw SecurityGroupNotFound(securityGroupId);
                }
            }
            _ = await m_keyStore.RemoveSecurityGroupAsync(securityGroupId, cancellationToken)
                .ConfigureAwait(false);
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

            if (m_coordinatedStore is not null)
            {
                return ReadCoordinatedSecurityGroupAsync(m_coordinatedStore, securityGroupId, cancellationToken);
            }

            lock (m_lock)
            {
                if (!m_groups.TryGetValue(securityGroupId, out SecurityGroupState? state))
                {
                    return new ValueTask<SksSecurityGroup?>((SksSecurityGroup?)null);
                }
                return new ValueTask<SksSecurityGroup?>(Snapshot(state));
            }
        }

        private void RestoreSecurityGroups()
        {
            try
            {
                ValueTask<ArrayOf<string>> idsTask =
                    m_keyStore.GetSecurityGroupIdsAsync(CancellationToken.None);
                if (idsTask.IsCompletedSuccessfully)
                {
                    RestoreSecurityGroups(idsTask.Result);
                    return;
                }

                _ = RestoreSecurityGroupsAsync(idsTask.AsTask());
            }
            catch (Exception ex)
            {
                m_logger.FailedToRestorePersistedSksSecurityGroups(ex);
            }
        }

        private void RestoreSecurityGroups(ArrayOf<string> securityGroupIds)
        {
            if (securityGroupIds.IsNull)
            {
                return;
            }

            foreach (string securityGroupId in securityGroupIds)
            {
                ValueTask<SksSecurityGroup?> groupTask =
                    m_keyStore.GetSecurityGroupAsync(securityGroupId, CancellationToken.None);
                if (groupTask.IsCompletedSuccessfully && groupTask.Result is SksSecurityGroup group)
                {
                    RestoreSecurityGroup(group);
                }
            }
        }

        private async Task RestoreSecurityGroupsAsync(Task<ArrayOf<string>> idsTask)
        {
            try
            {
                ArrayOf<string> ids = await idsTask.ConfigureAwait(false);
                if (ids.IsNull)
                {
                    return;
                }

                foreach (string securityGroupId in (string[])[.. ids])
                {
                    SksSecurityGroup? group = await m_keyStore
                        .GetSecurityGroupAsync(securityGroupId, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (group is not null)
                    {
                        RestoreSecurityGroup(group);
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToRestorePersistedSksSecurityGroups(ex);
            }
        }

        private void RestoreSecurityGroup(SksSecurityGroup group)
        {
            IPubSubSecurityPolicy? policy =
                m_securityPolicies.GetByUri(group.SecurityPolicyUri);
            if (policy is null)
            {
                return;
            }

            List<PubSubSecurityKey> keys = group.Keys.IsNull
                ? []
                : new List<PubSubSecurityKey>([.. group.Keys]);
            if (keys.Count == 0)
            {
                keys = SeedInitialKeys(policy, group.MaxFutureKeyCount, group.KeyLifetime);
            }

            var state = new SecurityGroupState(
                group,
                policy,
                keys,
                NextTokenIdAfter(keys),
                group.CurrentKeyIndex);
            lock (m_lock)
            {
                m_groups[group.SecurityGroupId] = state;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<SksKeyResponse> GetSecurityKeysAsync(
            string callerIdentity,
            SksKeyRequest request,
            ArrayOf<NodeId> callerRoleIds = default,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(request.SecurityGroupId))
            {
                throw new OpcUaSksException(
                    StatusCodes.BadInvalidArgument,
                    "SecurityGroupId must be non-empty.");
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (m_coordinatedStore is not null)
            {
                return await GetCoordinatedSecurityKeysAsync(
                    m_coordinatedStore, callerIdentity, request, callerRoleIds, cancellationToken)
                    .ConfigureAwait(false);
            }

            SksSecurityGroup snapshot;
            SksKeyResponse response;
            lock (m_lock)
            {
                if (!m_groups.TryGetValue(request.SecurityGroupId, out SecurityGroupState? state))
                {
                    throw DeniedKeyRequest(request, callerIdentity, StatusCodes.BadNotFound,
                        "The requested SecurityGroup does not exist.");
                }
                CheckCallerAuthorization(state.Group, request, callerIdentity, callerRoleIds);
                List<PubSubSecurityKey> keys = SelectKeys(state, request);
                response = CreateKeyResponse(state, keys);
                snapshot = Snapshot(state);
            }

            await m_keyStore.SaveSecurityGroupAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return ReportKeysIssued(response, request, callerIdentity);
        }

        /// <inheritdoc/>
        public async ValueTask InvalidateKeysAsync(
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

            if (m_coordinatedStore is not null)
            {
                await CommitKeyChangeAsync(m_coordinatedStore, securityGroupId, InvalidateKeys, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            SksSecurityGroup snapshot;
            lock (m_lock)
            {
                if (!m_groups.TryGetValue(securityGroupId, out SecurityGroupState? state))
                {
                    throw SecurityGroupNotFound(securityGroupId);
                }

                InvalidateKeys(state);
                snapshot = Snapshot(state);
            }
            await m_keyStore.SaveSecurityGroupAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask ForceKeyRotationAsync(
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

            if (m_coordinatedStore is not null)
            {
                await CommitKeyChangeAsync(m_coordinatedStore, securityGroupId, RotateKeys, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            SksSecurityGroup snapshot;
            lock (m_lock)
            {
                if (!m_groups.TryGetValue(securityGroupId, out SecurityGroupState? state))
                {
                    throw SecurityGroupNotFound(securityGroupId);
                }

                RotateKeys(state);
                snapshot = Snapshot(state);
            }
            await m_keyStore.SaveSecurityGroupAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        private IPubSubSecurityPolicy GetRequiredPolicy(SksSecurityGroup group)
        {
            return m_securityPolicies.GetByUri(group.SecurityPolicyUri) ??
                throw new OpcUaSksException(
                    StatusCodes.BadSecurityPolicyRejected,
                    $"SecurityPolicyUri '{group.SecurityPolicyUri}' is not supported.");
        }

        private SecurityGroupState CreateSecurityGroupState(SksSecurityGroup group, IPubSubSecurityPolicy policy)
        {
            int maxFuture = group.MaxFutureKeyCount > 0 ? group.MaxFutureKeyCount : DefaultMaxFutureKeyCount;
            int maxPast = group.MaxPastKeyCount > 0 ? group.MaxPastKeyCount : DefaultMaxPastKeyCount;
            List<PubSubSecurityKey> keys = group.Keys.Count > 0
                ? [.. group.Keys]
                : SeedInitialKeys(policy, maxFuture, group.KeyLifetime);
            var configured = new SksSecurityGroup(
                group.SecurityGroupId,
                group.SecurityPolicyUri,
                group.KeyLifetime,
                maxFuture,
                maxPast,
                keys,
                group.AuthorizedCallerIdentities,
                group.RolePermissions);
            return new SecurityGroupState(configured, policy, keys, NextTokenIdAfter(keys), group.CurrentKeyIndex);
        }

        private SecurityGroupState CreateWorkingState(SksSecurityGroup group)
        {
            List<PubSubSecurityKey> keys = group.Keys.IsNull ? [] : [.. group.Keys];
            return new SecurityGroupState(
                group, GetRequiredPolicy(group), keys, NextTokenIdAfter(keys), group.CurrentKeyIndex);
        }

        private async ValueTask AddCoordinatedSecurityGroupAsync(
            ICoordinatedPubSubSecurityKeyStore store,
            SksSecurityGroup group,
            IPubSubSecurityPolicy policy,
            CancellationToken cancellationToken)
        {
            SecurityGroupState state = CreateSecurityGroupState(group, policy);
            try
            {
                if (!await store.TryCommitSecurityGroupAsync(Snapshot(state), default, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw SecurityGroupAlreadyExists(group.SecurityGroupId);
                }
                m_logger.RegisteredSksSecurityGroup(group.SecurityGroupId, group.SecurityPolicyUri);
            }
            finally
            {
                if (group.Keys.Count == 0)
                {
                    state.Dispose();
                }
            }
        }

        private static async ValueTask<SksSecurityGroup?> ReadCoordinatedSecurityGroupAsync(
            ICoordinatedPubSubSecurityKeyStore store,
            string securityGroupId,
            CancellationToken cancellationToken)
        {
            (SksSecurityGroup? group, _) = await store.ReadSecurityGroupAsync(securityGroupId, cancellationToken)
                .ConfigureAwait(false);
            return group;
        }

        private async ValueTask<SksKeyResponse> GetCoordinatedSecurityKeysAsync(
            ICoordinatedPubSubSecurityKeyStore store,
            string callerIdentity,
            SksKeyRequest request,
            ArrayOf<NodeId> callerRoleIds,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaxCommitAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (SksSecurityGroup? group, ByteString revision) = await store
                    .ReadSecurityGroupAsync(request.SecurityGroupId, cancellationToken).ConfigureAwait(false);
                if (group is null)
                {
                    throw DeniedKeyRequest(request, callerIdentity, StatusCodes.BadNotFound,
                        "The requested SecurityGroup does not exist.");
                }

                using SecurityGroupState state = CreateWorkingState(group);
                CheckCallerAuthorization(group, request, callerIdentity, callerRoleIds);
                uint nextTokenId = state.NextTokenId;
                List<PubSubSecurityKey> keys = SelectKeys(state, request);
                bool changed = state.NextTokenId != nextTokenId ||
                    state.CurrentIndex != group.CurrentKeyIndex ||
                    state.Keys.Count != group.Keys.Count;
                if (changed && !await store.TryCommitSecurityGroupAsync(Snapshot(state), revision, cancellationToken)
                    .ConfigureAwait(false))
                {
                    continue;
                }

                // Pack and audit only keys from a committed generation.
                return ReportKeysIssued(CreateKeyResponse(state, keys), request, callerIdentity);
            }
            throw CommitContention();
        }

        private async ValueTask CommitKeyChangeAsync(
            ICoordinatedPubSubSecurityKeyStore store,
            string securityGroupId,
            Action<SecurityGroupState> change,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < MaxCommitAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (SksSecurityGroup? group, ByteString revision) = await store
                    .ReadSecurityGroupAsync(securityGroupId, cancellationToken).ConfigureAwait(false);
                using SecurityGroupState state = CreateWorkingState(
                    group ?? throw SecurityGroupNotFound(securityGroupId));
                change(state);
                if (await store.TryCommitSecurityGroupAsync(Snapshot(state), revision, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return;
                }
            }
            throw CommitContention();
        }

        private void CheckCallerAuthorization(
            SksSecurityGroup group,
            SksKeyRequest request,
            string callerIdentity,
            ArrayOf<NodeId> callerRoleIds)
        {
            if (!group.IsCallerAuthorized(callerIdentity, callerRoleIds))
            {
                throw DeniedKeyRequest(request, callerIdentity, StatusCodes.BadUserAccessDenied,
                    "Caller is not authorized to retrieve keys for the requested SecurityGroup.");
            }
        }

        private OpcUaSksException DeniedKeyRequest(
            SksKeyRequest request,
            string callerIdentity,
            StatusCode status,
            string message)
        {
            EmitSecurityEvent(new PubSubSecurityEvent(
                PubSubSecurityEventKind.SksKeyRequestDenied,
                DateTimeOffset.UtcNow,
                PubSubSecurityEventOutcome.Rejected,
                securityGroupId: request.SecurityGroupId,
                callerIdentity: callerIdentity));
            return new OpcUaSksException(status, message);
        }

        private SksKeyResponse ReportKeysIssued(SksKeyResponse response, SksKeyRequest request, string callerIdentity)
        {
            m_logger.IssuedKeys(response.Keys.Count, request.SecurityGroupId, response.FirstTokenId, callerIdentity);
            EmitSecurityEvent(new PubSubSecurityEvent(
                PubSubSecurityEventKind.SksKeysIssued,
                DateTimeOffset.UtcNow,
                PubSubSecurityEventOutcome.Success,
                tokenId: response.FirstTokenId,
                securityGroupId: request.SecurityGroupId,
                callerIdentity: callerIdentity));
            return response;
        }

        private List<PubSubSecurityKey> SelectKeys(SecurityGroupState state, SksKeyRequest request)
        {
            EnsureLiveCurrentKey(state);
            PrunePastKeys(state);
            EnsureFutureKeys(state, request.RequestedKeyCount);

            uint firstTokenId = request.StartingTokenId == 0
                ? state.Keys[state.CurrentIndex].TokenId
                : request.StartingTokenId;
            var selected = new List<PubSubSecurityKey>();
            for (int i = 0; i < state.Keys.Count && selected.Count < request.RequestedKeyCount; i++)
            {
                PubSubSecurityKey key = state.Keys[i];
                if (key.TokenId >= firstTokenId)
                {
                    selected.Add(key);
                }
            }

            int available = state.Group.MaxFutureKeyCount + 1 - FutureKeyCount(state);
            int toGenerate = Math.Min((int)request.RequestedKeyCount - selected.Count, available);
            if (toGenerate > 0)
            {
                var now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
                for (int i = 0; i < toGenerate; i++)
                {
                    PubSubSecurityKey key = AppendKey(state, NextIssuedAt(state, now));
                    selected.Add(key);
                }
            }

            if (selected.Count == 0)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadNotFound,
                    $"No keys available starting at TokenId {firstTokenId}.");
            }
            return selected;
        }

        private SksKeyResponse CreateKeyResponse(SecurityGroupState state, List<PubSubSecurityKey> keys)
        {
            return new SksKeyResponse(
                state.Group.SecurityPolicyUri,
                keys[0].TokenId,
                keys.ConvertAll(SksKeyGenerator.Pack),
                ComputeTimeToNextKey(state),
                state.Group.KeyLifetime);
        }

        private void InvalidateKeys(SecurityGroupState state)
        {
            for (int i = state.CurrentIndex; i < state.Keys.Count; i++)
            {
                state.Keys[i].Dispose();
            }
            state.Keys.RemoveRange(state.CurrentIndex, state.Keys.Count - state.CurrentIndex);
            PrunePastKeys(state);
            AppendKey(state, DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime));
            state.CurrentIndex = state.Keys.Count - 1;
            EnsureFutureKeys(state, (uint)(state.Group.MaxFutureKeyCount + 1));
        }

        private void RotateKeys(SecurityGroupState state)
        {
            // Skip keys that expired while the SKS was idle before forcing a successor.
            EnsureLiveCurrentKey(state);
            EnsureFutureKeys(state, 2);
            if (state.CurrentIndex + 1 >= state.Keys.Count)
            {
                throw new OpcUaSksException(
                    StatusCodes.BadNotFound,
                    $"No future keys are available for SecurityGroup '{state.Group.SecurityGroupId}'.");
            }
            state.CurrentIndex++;
            var now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            for (int i = state.CurrentIndex; i < state.Keys.Count; i++)
            {
                state.Keys[i] = state.Keys[i].WithIssuedAt(
                    now + TimeSpan.FromTicks(state.Group.KeyLifetime.Ticks * (i - state.CurrentIndex)));
            }
            PrunePastKeys(state);
            EnsureFutureKeys(state, (uint)(state.Group.MaxFutureKeyCount + 1));
        }

        private static OpcUaSksException SecurityGroupAlreadyExists(string id)
        {
            return new OpcUaSksException(StatusCodes.BadAlreadyExists, $"SecurityGroup '{id}' already exists.");
        }

        private static OpcUaSksException SecurityGroupNotFound(string id)
        {
            return new OpcUaSksException(StatusCodes.BadNotFound, $"SecurityGroup '{id}' is not registered.");
        }

        private static OpcUaSksException CommitContention()
        {
            return new OpcUaSksException(StatusCodes.BadServerTooBusy, "The SecurityGroup changed repeatedly.");
        }

        private List<PubSubSecurityKey> SeedInitialKeys(
            IPubSubSecurityPolicy policy,
            int maxFutureKeyCount,
            TimeSpan lifetime)
        {
            var now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            var keys = new List<PubSubSecurityKey>(maxFutureKeyCount + 1);
            for (int i = 0; i <= maxFutureKeyCount; i++)
            {
                // Each key is issued when its predecessor expires.
                keys.Add(SksKeyGenerator.Generate(
                    policy,
                    (uint)(i + 1),
                    now + TimeSpan.FromTicks(lifetime.Ticks * i),
                    lifetime));
            }
            return keys;
        }

        private void EmitSecurityEvent(PubSubSecurityEvent securityEvent)
        {
            if (m_securityEventSink is null)
            {
                return;
            }

            try
            {
                m_securityEventSink.OnSecurityEvent(securityEvent);
            }
            catch (Exception ex)
            {
                m_logger.PubSubSecurityEventSinkRaisedException(ex);
            }
        }

        private void EnsureFutureKeys(SecurityGroupState state, uint requestedKeyCount)
        {
            int totalFromCurrent = FutureKeyCount(state);
            int needed = (int)requestedKeyCount;
            if (totalFromCurrent >= needed)
            {
                return;
            }

            int maxPossible = state.Group.MaxFutureKeyCount + 1;
            int target = Math.Min(needed, maxPossible);
            int toAdd = target - totalFromCurrent;
            if (toAdd <= 0)
            {
                return;
            }
            var now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            for (int i = 0; i < toAdd; i++)
            {
                AppendKey(state, NextIssuedAt(state, now));
            }
        }

        private static PubSubSecurityKey AppendKey(SecurityGroupState state, DateTimeUtc issuedAt)
        {
            PubSubSecurityKey key = SksKeyGenerator.Generate(
                state.Policy, state.NextTokenId, issuedAt, state.Group.KeyLifetime);
            state.Keys.Add(key);
            state.NextTokenId = unchecked(state.NextTokenId + 1u);
            return key;
        }

        /// <summary>
        /// Issuance time of a key appended to <paramref name="state"/>: the
        /// expiry of the last key, so that keys become current one after the
        /// other (Part 14 §8.3), or <paramref name="now"/> when the last key
        /// has already expired. Stamping future keys with the time they were
        /// generated made them all expire together, and the current key then
        /// jumped over every future key at once.
        /// </summary>
        private static DateTimeUtc NextIssuedAt(SecurityGroupState state, DateTimeUtc now)
        {
            if (state.Keys.Count == 0)
            {
                return now;
            }
            PubSubSecurityKey last = state.Keys[^1];
            DateTimeUtc next = last.IssuedAt + last.Lifetime;
            return next < now ? now : next;
        }

        private TimeSpan ComputeTimeToNextKey(SecurityGroupState state)
        {
            if (state.Keys.Count == 0)
            {
                return TimeSpan.Zero;
            }
            PubSubSecurityKey current = state.Keys[state.CurrentIndex];
            var now = DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime);
            TimeSpan elapsed = now - current.IssuedAt;
            TimeSpan remaining = state.Group.KeyLifetime - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private void RotateExpiredCurrent(SecurityGroupState state)
        {
            while (state.CurrentIndex + 1 < state.Keys.Count &&
                state.Keys[state.CurrentIndex].IsExpired(m_timeProvider))
            {
                state.CurrentIndex++;
            }
        }

        /// <summary>
        /// Makes the current key of <paramref name="state"/> one that has not
        /// expired: advances past expired keys and, when every key has
        /// expired (an idle SKS), generates a new current key issued now.
        /// Without that, an expired last key would count as the current key
        /// and be handed out again on every request.
        /// </summary>
        private void EnsureLiveCurrentKey(SecurityGroupState state)
        {
            RotateExpiredCurrent(state);
            if (state.Keys.Count > 0 &&
                !state.Keys[state.CurrentIndex].IsExpired(m_timeProvider))
            {
                return;
            }
            AppendKey(state, DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime));
            state.CurrentIndex = state.Keys.Count - 1;
            PrunePastKeys(state);
        }

        private static int FutureKeyCount(SecurityGroupState state)
        {
            return Math.Max(0, state.Keys.Count - state.CurrentIndex);
        }

        private static void PrunePastKeys(SecurityGroupState state)
        {
            while (state.CurrentIndex > state.Group.MaxPastKeyCount)
            {
                PubSubSecurityKey old = state.Keys[0];
                state.Keys.RemoveAt(0);
                old.Dispose();
                state.CurrentIndex--;
            }
        }

        private static SksSecurityGroup Snapshot(SecurityGroupState state)
        {
            return new SksSecurityGroup(
                state.Group.SecurityGroupId,
                state.Group.SecurityPolicyUri,
                state.Group.KeyLifetime,
                state.Group.MaxFutureKeyCount,
                state.Group.MaxPastKeyCount,
                [.. state.Keys],
                state.Group.AuthorizedCallerIdentities,
                state.Group.RolePermissions)
            {
                CurrentKeyIndex = state.CurrentIndex
            };
        }

        private static uint NextTokenIdAfter(List<PubSubSecurityKey> keys)
        {
            if (keys.Count == 0)
            {
                return 1u;
            }
            return unchecked(keys[^1].TokenId + 1u);
        }

        private sealed class SecurityGroupState : IDisposable
        {
            public SecurityGroupState(
                SksSecurityGroup group,
                IPubSubSecurityPolicy policy,
                List<PubSubSecurityKey> keys,
                uint nextTokenId,
                int currentIndex)
            {
                Group = group;
                Policy = policy;
                Keys = keys;
                NextTokenId = nextTokenId;
                CurrentIndex = currentIndex;
            }

            public SksSecurityGroup Group { get; }

            public IPubSubSecurityPolicy Policy { get; }

            public List<PubSubSecurityKey> Keys { get; }

            public uint NextTokenId { get; set; }

            public int CurrentIndex { get; set; }

            public void Dispose()
            {
                foreach (PubSubSecurityKey key in Keys)
                {
                    key.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="InMemoryPubSubKeyServiceServer"/>.
    /// </summary>
    internal static partial class InMemoryPubSubKeyServiceServerLog
    {
        [LoggerMessage(EventId = PubSubEventIds.InMemoryPubSubKeyServiceServer + 0, Level = LogLevel.Information,
            Message = "Registered SKS SecurityGroup {GroupId} with policy {PolicyUri}.")]
        public static partial void RegisteredSksSecurityGroup(
            this ILogger logger,
            string groupId,
            string policyUri);

        [LoggerMessage(EventId = PubSubEventIds.InMemoryPubSubKeyServiceServer + 1, Level = LogLevel.Debug,
            Message = "Failed to restore persisted SKS SecurityGroups.")]
        public static partial void FailedToRestorePersistedSksSecurityGroups(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = PubSubEventIds.InMemoryPubSubKeyServiceServer + 2, Level = LogLevel.Debug,
            Message = "Issued {Count} key(s) for {GroupId} starting at TokenId {TokenId} to {Caller}.")]
        public static partial void IssuedKeys(
            this ILogger logger,
            int count,
            string groupId,
            uint tokenId,
            [UserIdentity] string? caller);
    }

}
