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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Routes authorized host aliases and opaque issued references to existing secret stores.
    /// It exposes no MCP secret-read tool and never uses caller input as a store path.
    /// </summary>
    public sealed class McpSecretReferences : IAsyncDisposable
    {
        /// <summary>
        /// Creates a secret gateway. Issuance stays disabled until a host supplies a writable store.
        /// </summary>
        public McpSecretReferences(
            IMcpOperationPolicy policy,
            ISecretRegistry registry,
            TimeProvider timeProvider,
            ArrayOf<McpSecretAlias> aliases = default,
            ISecretStore? issuanceStore = null,
            string? issuanceStorePath = null,
            int maxIssuedReferences = 128,
            int maxSecretBytes = 1_048_576,
            ITelemetryContext? telemetry = null)
        {
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_logger = telemetry?.CreateLogger<McpSecretReferences>() ?? NullLogger<McpSecretReferences>.Instance;
            m_store = issuanceStore;
            m_storePath = issuanceStorePath;
            ValidateText(issuanceStorePath, 2048, optional: true);
            if (issuanceStore is not null)
            {
                try
                {
                    m_storeType = issuanceStore.StoreType;
                }
                catch (OperationCanceledException failure)
                {
                    LogProviderFailure("StoreType", failure);
                    throw new OperationCanceledException(failure.CancellationToken);
                }
                catch (Exception failure)
                {
                    LogProviderFailure("StoreType", failure);
                    throw ProviderUnavailable(failure);
                }
                ValidateText(m_storeType, 128);
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(maxIssuedReferences, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxIssuedReferences, 4096);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxSecretBytes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSecretBytes, 16 * 1024 * 1024);
            m_maxReferences = maxIssuedReferences;
            m_maxSecretBytes = maxSecretBytes;
            m_aliases = new Dictionary<string, McpSecretAlias>(StringComparer.Ordinal);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(aliases.Count, 512);
            foreach (McpSecretAlias alias in aliases)
            {
                ArgumentNullException.ThrowIfNull(alias);
                ArgumentNullException.ThrowIfNull(alias.Identifier);
                ValidateText(alias.Alias, 128);
                ValidateText(alias.Resource, 2048);
                ValidateText(alias.Identifier.Name, 256);
                ValidateText(alias.Identifier.StoreType, 128);
                ValidateText(alias.Identifier.StorePath, 2048, optional: true);
                if (alias.Alias.Contains(':', StringComparison.Ordinal))
                {
                    throw new ArgumentException("An alias name cannot contain a colon.", nameof(aliases));
                }
                if (!m_aliases.TryAdd(alias.Alias, alias))
                {
                    throw new ArgumentException("Secret aliases must be unique.", nameof(aliases));
                }
            }
        }

        /// <summary>
        /// Creates an immutable registration/endpoint binding for a strictly host-approved session and model root.
        /// Tool callers cannot substitute a store path, claimed server URI or ownership identity.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static string GetResourceBinding(OpcUaSessionManager.SessionInfo session, NodeId root)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (root.IsNull)
            {
                throw new ArgumentException("A secret resource requires a non-null model root.", nameof(root));
            }
            if (root.NamespaceIndex >= session.Session.NamespaceUris.Count)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                    "The resource namespace is not present in the registered session.");
            }
            McpSessionAuthority? authority = session.Authority;
            if (authority is null ||
                !authority.Active ||
                authority.Owner is not null ||
                authority.AutoAccepted ||
                authority.TargetIdentity != McpSessionAuthority.GetTargetIdentity(session.Endpoint) ||
                authority.TargetIdentity != McpSessionAuthority.GetTargetIdentity(
                    session.Session.ConfiguredEndpoint.Description))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "Protected secrets require the current, strictly validated host-approved registration.");
            }
            string? serverUri = session.Endpoint.Server?.ApplicationUri;
            ValidateText(serverUri, 2048);
            string resource = "opcua-registration:" +
                authority.RegistrationId +
                ":" +
                authority.TargetIdentity +
                ":opcua:" +
                serverUri +
                "#" +
                NodeId.ToExpandedNodeId(root, session.Session.NamespaceUris);
            ValidateText(resource, 2048);
            return resource;
        }

        /// <summary>
        /// Refuses unsupported issuance before a remote request can produce unrecoverable private material.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void RequireIssuanceStore()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_store is null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The host has not configured a writable store for issued MCP credentials.");
            }
        }

        /// <summary>
        /// Reserves reference capacity before a remote finish operation can return private material.
        /// One reservation can collect a token pair without racing another caller for the final slot.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public McpSecretReservation ReserveIssuance(McpCapability capability, int count = 1)
        {
            RequireIssuanceStore();
            McpCallerIdentity caller = m_policy.Require(capability);
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16);
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_issued.Count + m_reserved > m_maxReferences - count)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The host's issued-credential reference quota is full.");
                }
                m_reserved += count;
                var reservation = new McpSecretReservation(this, caller, capability, count);
                m_reservations.Add(reservation);
                return reservation;
            }
        }

        /// <summary>
        /// Persists issued material before publishing an owner-bound opaque reference.
        /// The caller must report a local-store failure separately from successful remote issuance.
        /// </summary>
        public async ValueTask<McpSecretReference> StoreIssuedAsync(
            ByteString secret,
            McpCapability capability,
            string purpose,
            string resource,
            DateTimeOffset expiresAt,
            string? credentialId = null,
            CancellationToken ct = default)
        {
            using McpSecretReservation reservation = ReserveIssuance(capability);
            return await StoreReservedAsync(
                reservation, secret, purpose, resource, expiresAt, credentialId, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Stores one result into capacity reserved before the remote operation was dispatched.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<McpSecretReference> StoreReservedAsync(
            McpSecretReservation reservation,
            ByteString secret,
            string purpose,
            string resource,
            DateTimeOffset expiresAt,
            string? credentialId,
            string? issuer,
            CancellationToken ct)
        {
            // Authority was captured before remote dispatch. Rechecking a request scope here
            // can discard a committed credential when that scope expires during issuance.
            ValidateText(purpose, 128);
            ValidateText(resource, 2048);
            ValidateText(credentialId, 2048, optional: true);
            ValidateText(issuer, 2048, optional: true);
            if (secret.IsEmpty || secret.Length > m_maxSecretBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "Issued secret material is empty or exceeds the configured secret-size ceiling.");
            }
            TimeSpan lifetime = expiresAt - m_time.GetUtcNow();
            if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(3650))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "An issued reference must expire in the next ten years.");
            }
            string reference = "issued:" + Guid.NewGuid().ToString("N");
            var identifier = new SecretIdentifier(
                "mcp-" + Guid.NewGuid().ToString("N"), m_storeType!, m_storePath);
            var metadata = new McpSecretReference(reference, purpose, resource, expiresAt, credentialId, issuer);
            var entry = new Issued(reservation.Owner, identifier, metadata);
            lock (m_state)
            {
                if (reservation.Remaining == 0 || reservation.Released)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The issued-secret reservation is closed or has no remaining slots.");
                }
                reservation.Remaining--;
                m_reserved--;
                if (m_disposed)
                {
                    entry.Revoked = true;
                    entry.Invalidated.TrySetResult();
                }
                m_issued.Add(reference, entry);
            }
            try
            {
                await m_store!.SetAsync(identifier, secret.Memory, ct).ConfigureAwait(false);
                lock (m_state)
                {
                    entry.Ready = true;
                }
                return metadata;
            }
            catch (Exception failure)
            {
                LogProviderFailure("Set", failure);
                lock (m_state)
                {
                    entry.Revoked = true;
                    entry.Invalidated.TrySetResult();
                }
                bool cleanupFailed = false;
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_time);
                    _ = await m_store!.RemoveAsync(identifier, cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    LogProviderFailure("RemoveAfterFailedSet", cleanupFailure);
                    cleanupFailed = true;
                }
                lock (m_state)
                {
                    if (!cleanupFailed)
                    {
                        m_issued.Remove(reference);
                    }
                }
                // Retain an uncertain owned key for explicit cleanup; never turn a provider's
                // message or inner exception (which can contain the secret) into MCP output.
                throw PersistenceFailure(metadata, failure, cleanupFailed);
            }
            finally
            {
                entry.PersistenceCompleted.TrySetResult();
            }
        }

        /// <summary>
        /// Releases only unused reservation slots, leaving successfully stored results owned by their references.
        /// </summary>
        internal void ReleaseReservation(McpSecretReservation reservation)
        {
            lock (m_state)
            {
                if (!reservation.Released)
                {
                    m_reserved -= reservation.Remaining;
                    reservation.Remaining = 0;
                    reservation.Released = true;
                    m_reservations.Remove(reservation);
                    reservation.Completed.TrySetResult();
                }
            }
        }

        /// <summary>
        /// Resolves one allowed alias or owned reference for a specific resource and operation.
        /// Trusted callers dispose the returned short-lived secret view after use.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async ValueTask<ISecret> ResolveAsync(
            string reference, string resource, McpCapability capability, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference);
            ValidateText(reference, 256);
            ValidateText(resource, 2048);
            ObjectDisposedException.ThrowIf(m_disposed, this);
            McpCallerIdentity caller = m_policy.Require(capability);
            ISecret? secret;
            Issued? issued = null;
            if (reference.StartsWith("alias:", StringComparison.Ordinal))
            {
                string name = reference["alias:".Length..];
                _ = m_policy.RequireSecret(capability, name);
                if (!m_aliases.TryGetValue(name, out McpSecretAlias? alias) ||
                    !MatchesAlias(name, alias, resource))
                {
                    throw Refused();
                }
                try
                {
                    secret = await m_registry.GetAsync(alias.Identifier, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException failure)
                {
                    LogProviderFailure("GetAlias", failure);
                    throw new OperationCanceledException(failure.CancellationToken);
                }
                catch (Exception failure)
                {
                    LogProviderFailure("GetAlias", failure);
                    throw ProviderUnavailable(failure);
                }
            }
            else
            {
                lock (m_state)
                {
                    if (!m_issued.TryGetValue(reference, out Issued? found) ||
                        found.Owner != caller ||
                        !found.Ready ||
                        found.Revoked ||
                        found.Metadata.ExpiresAt <= m_time.GetUtcNow() ||
                        !string.Equals(found.Metadata.Resource, resource, StringComparison.Ordinal))
                    {
                        throw Refused();
                    }
                    issued = found;
                }
                try
                {
                    secret = await m_store!.GetAsync(issued.Identifier, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException failure)
                {
                    LogProviderFailure("GetIssued", failure);
                    throw new OperationCanceledException(failure.CancellationToken);
                }
                catch (Exception failure)
                {
                    LogProviderFailure("GetIssued", failure);
                    throw ProviderUnavailable(failure);
                }
            }
            if (secret is null)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound,
                    "The authorized secret is unavailable in its configured store.");
            }
            MaterializedSecret materialized = Materialize(secret);
            bool refused;
            lock (m_state)
            {
                refused = m_disposed ||
                    (issued is not null &&
                        (issued.Revoked || issued.Metadata.ExpiresAt <= m_time.GetUtcNow()));
            }
            if (refused)
            {
                materialized.Dispose();
                throw Refused();
            }
            return materialized;
        }

        /// <summary>
        /// Invalidates an owned local reference before removing its stored material.
        /// Remote revocation remains a separate, explicit domain operation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask RevokeReferenceAsync(string reference, CancellationToken ct = default)
        {
            ValidateText(reference, 256);
            McpCallerIdentity caller = m_policy.RequireCaller();
            Issued entry;
            lock (m_state)
            {
                if (!m_issued.TryGetValue(reference, out Issued? found) || found.Owner != caller)
                {
                    throw Refused();
                }
                entry = found;
                entry.Revoked = true;
                entry.Invalidated.TrySetResult();
            }
            await RemoveOwnedAsync(reference, entry, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Invalidates all caller-owned references for one remotely revoked credential before cleanup begins.
        /// A cleanup failure never leaves a sibling reference usable.
        /// </summary>
        public async ValueTask RevokeCredentialAsync(
            string resource, string credentialId, CancellationToken ct = default)
        {
            ValidateText(resource, 2048);
            ValidateText(credentialId, 2048);
            McpCallerIdentity caller = m_policy.RequireCaller();
            var entries = new List<KeyValuePair<string, Issued>>();
            lock (m_state)
            {
                foreach (KeyValuePair<string, Issued> pair in m_issued)
                {
                    if (pair.Value.Owner == caller &&
                        string.Equals(pair.Value.Metadata.Resource, resource, StringComparison.Ordinal) &&
                        string.Equals(pair.Value.Metadata.CredentialId, credentialId, StringComparison.Ordinal))
                    {
                        pair.Value.Revoked = true;
                        pair.Value.Invalidated.TrySetResult();
                        entries.Add(pair);
                    }
                }
            }
            foreach (KeyValuePair<string, Issued> pair in entries)
            {
                await RemoveOwnedAsync(pair.Key, pair.Value, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rechecks an issued reference before a dependent runtime dispatch. Consumers must not cache
        /// material across calls; expiry and revocation invalidate use even if store removal fails.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public McpSecretReference RequireReference(
            string reference, string resource, McpCapability capability)
        {
            ValidateText(reference, 256);
            ValidateText(resource, 2048);
            ObjectDisposedException.ThrowIf(m_disposed, this);
            McpCallerIdentity caller = m_policy.Require(capability);
            lock (m_state)
            {
                if (!m_issued.TryGetValue(reference, out Issued? entry) ||
                    entry.Owner != caller ||
                    !entry.Ready ||
                    entry.Revoked ||
                    entry.Metadata.ExpiresAt <= m_time.GetUtcNow() ||
                    !string.Equals(entry.Metadata.Resource, resource, StringComparison.Ordinal))
                {
                    throw Refused();
                }
                return entry.Metadata;
            }
        }

        /// <summary>
        /// Captures an authorized runtime dependency without materializing secret bytes.
        /// Consumers must stop when Invalidated completes or Reference.ExpiresAt is reached, whichever occurs first,
        /// and detach their own bounded wait when the dependent runtime closes.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public McpSecretDependency GetDependency(
            string reference, string resource, McpCapability capability)
        {
            _ = RequireReference(reference, resource, capability);
            lock (m_state)
            {
                if (!m_issued.TryGetValue(reference, out Issued? entry) ||
                    entry.Revoked ||
                    entry.Metadata.ExpiresAt <= m_time.GetUtcNow() ||
                    m_disposed)
                {
                    throw Refused();
                }
                return new McpSecretDependency(entry.Metadata, entry.Invalidated.Task);
            }
        }

        /// <summary>
        /// Removes expired owned keys without a timer or ownership of the host's store.
        /// Hosts may call this before admission or during their existing maintenance loop.
        /// A failed removal remains tracked and keeps its quota slot until cleanup succeeds.
        /// </summary>
        public async ValueTask PurgeExpiredAsync(CancellationToken ct = default)
        {
            KeyValuePair<string, Issued>[] entries;
            lock (m_state)
            {
                entries = [.. m_issued];
            }
            foreach (KeyValuePair<string, Issued> pair in entries)
            {
                if (pair.Value.Metadata.ExpiresAt <= m_time.GetUtcNow())
                {
                    lock (m_state)
                    {
                        pair.Value.Revoked = true;
                        pair.Value.Invalidated.TrySetResult();
                    }
                    await RemoveOwnedAsync(pair.Key, pair.Value, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Invalidates and removes only this gateway's issued keys. The host store is never disposed.
        /// Cleanup failures remain tracked; calling disposal again retries only retained keys.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask DisposeAsync()
        {
            KeyValuePair<string, Issued>[] entries;
            Task[] reservations;
            lock (m_state)
            {
                m_disposed = true;
                foreach (Issued entry in m_issued.Values)
                {
                    entry.Revoked = true;
                    entry.Invalidated.TrySetResult();
                }
                var completions = new List<Task>(m_reservations.Count);
                foreach (McpSecretReservation reservation in m_reservations)
                {
                    completions.Add(reservation.Completed.Task);
                }
                reservations = [.. completions];
            }
            try
            {
                await Task.WhenAll(reservations).WaitAsync(TimeSpan.FromSeconds(30), m_time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new ServiceResultException(StatusCodes.BadTimeout,
                    "Issued-credential operations are still in flight; gateway shutdown cleanup must be retried.");
            }
            lock (m_state)
            {
                entries = [.. m_issued];
                foreach (KeyValuePair<string, Issued> pair in entries)
                {
                    pair.Value.Revoked = true;
                    pair.Value.Invalidated.TrySetResult();
                }
            }
            foreach (KeyValuePair<string, Issued> pair in entries)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_time);
                await RemoveOwnedAsync(pair.Key, pair.Value, cleanup.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Keeps legacy host aliases usable on their first approved registration, then pins them to that
        /// immutable registration and endpoint. A replacement never inherits an earlier alias's eligibility.
        /// </summary>
        private bool MatchesAlias(string name, McpSecretAlias alias, string resource)
        {
            bool matches = string.Equals(alias.Resource, resource, StringComparison.Ordinal);
            const string prefix = "opcua-registration:";
            const int identityLength = 32 + 1 + 64 + 1;
            if (!matches &&
                resource.StartsWith(prefix, StringComparison.Ordinal) &&
                resource.Length > prefix.Length + identityLength &&
                Guid.TryParseExact(resource.AsSpan(prefix.Length, 32), "N", out _) &&
                resource[prefix.Length + 32] == ':' &&
                resource[prefix.Length + identityLength - 1] == ':')
            {
                matches = string.Equals(
                    alias.Resource, resource[(prefix.Length + identityLength)..], StringComparison.Ordinal);
            }
            if (!matches)
            {
                return false;
            }
            lock (m_state)
            {
                if (m_aliasBindings.TryGetValue(name, out string? bound))
                {
                    return string.Equals(bound, resource, StringComparison.Ordinal);
                }
                m_aliasBindings.Add(name, resource);
                return true;
            }
        }

        /// <summary>
        /// Removes a generated key, retaining its identity on any known storage failure.
        /// </summary>
        private async ValueTask RemoveOwnedAsync(string reference, Issued entry, CancellationToken ct)
        {
            try
            {
                await entry.PersistenceCompleted.Task.WaitAsync(ct).ConfigureAwait(false);
                _ = await m_store!.RemoveAsync(entry.Identifier, ct).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                LogProviderFailure("Remove", failure);
                throw PersistenceFailure(entry.Metadata, failure, cleanupFailed: true);
            }
            lock (m_state)
            {
                m_issued.Remove(reference);
            }
        }

        /// <summary>
        /// Copies one bounded provider view into an owned, zeroed-on-disposal view. Provider property/disposal
        /// failures are sanitized too, and cleanup cannot replace a previously identified primary failure.
        /// </summary>
        private MaterializedSecret Materialize(ISecret secret)
        {
            byte[]? bytes = null;
            Exception? primary = null;
            try
            {
                ReadOnlySpan<byte> source = secret.Bytes;
                if (source.IsEmpty || source.Length > m_maxSecretBytes)
                {
                    primary = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The configured secret is empty or exceeds the host secret-size limit.");
                }
                else
                {
                    bytes = source.ToArray();
                }
            }
            catch (Exception failure)
            {
                LogProviderFailure("Materialize", failure);
                primary = failure is OperationCanceledException canceled
                    ? new OperationCanceledException(canceled.CancellationToken) : ProviderUnavailable(failure);
            }
            try
            {
                secret.Dispose();
            }
            catch (Exception failure)
            {
                LogProviderFailure("DisposeView", failure);
                primary ??= failure is OperationCanceledException canceled
                    ? new OperationCanceledException(canceled.CancellationToken) : ProviderUnavailable(failure);
            }
            if (primary is not null)
            {
                if (bytes is not null)
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
                throw primary;
            }
            return new MaterializedSecret(bytes!);
        }

        /// <summary>
        /// Validates host and returned metadata without echoing the rejected value.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateText(string? value, int maximum, bool optional = false)
        {
            if ((value is null && !optional) ||
                (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > maximum)))
            {
                throw new ArgumentException("Secret-reference metadata is absent or exceeds its length limit.");
            }
        }

        /// <summary>
        /// Records only the operation and exception type at the secret-bearing provider boundary.
        /// Never pass a provider exception object, its text, identifiers or secret values to telemetry.
        /// </summary>
        private void LogProviderFailure(string operation, Exception failure)
        {
            m_logger.SecretProviderFailed(operation, failure.GetType().FullName ?? nameof(Exception));
        }

        /// <summary>
        /// Keeps cancellation a cancellation while attaching safe local persistence/cleanup metadata.
        /// </summary>
        private static Exception PersistenceFailure(
            McpSecretReference reference, Exception failure, bool cleanupFailed)
        {
            return failure is OperationCanceledException canceled
                ? new McpSecretPersistenceCanceledException(reference, cleanupFailed, canceled.CancellationToken)
                : new McpSecretPersistenceException(reference, FailureStatus(failure), cleanupFailed);
        }

        /// <summary>
        /// Retains only a numeric failure category, never provider messages, paths or inner exceptions.
        /// </summary>
        private static StatusCode FailureStatus(Exception exception)
        {
            return exception is ServiceResultException service
                ? service.StatusCode
                : exception is OperationCanceledException
                    ? StatusCodes.BadRequestCancelledByClient
                    : StatusCodes.BadResourceUnavailable;
        }

        /// <summary>
        /// Constructs a secret-free provider diagnostic.
        /// </summary>
        private static ServiceResultException ProviderUnavailable(Exception failure)
        {
            return new ServiceResultException(FailureStatus(failure),
                "The configured secret provider could not complete the authorized operation.");
        }

        /// <summary>
        /// Uses the same error for absent, foreign, expired and resource-mismatched references.
        /// </summary>
        private static ServiceResultException Refused()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The credential reference is unavailable or not authorized for this caller and resource.");
        }

        private readonly IMcpOperationPolicy m_policy;
        private readonly ILogger m_logger;
        private readonly ISecretRegistry m_registry;
        private readonly ISecretStore? m_store;
        private readonly string? m_storePath;
        private readonly string? m_storeType;
        private readonly TimeProvider m_time;
        private readonly int m_maxReferences;
        private readonly int m_maxSecretBytes;
        private readonly Dictionary<string, McpSecretAlias> m_aliases;

        /// <summary>Immutable registration/target first used for each host alias.</summary>
        private readonly Dictionary<string, string> m_aliasBindings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Issued> m_issued = new(StringComparer.Ordinal);
        private readonly HashSet<McpSecretReservation> m_reservations = [];
        private readonly Lock m_state = new();
        private int m_reserved;
        private bool m_disposed;

        /// <summary>
        /// Retains only identifiers and authorization metadata, never the secret bytes.
        /// </summary>
        private sealed class Issued(
            McpCallerIdentity owner, SecretIdentifier identifier, McpSecretReference metadata)
        {
            /// <summary>
            /// Gets the exact authority captured before remote dispatch.
            /// </summary>
            public McpCallerIdentity Owner { get; } = owner;

            /// <summary>
            /// Gets the gateway-generated storage identifier.
            /// </summary>
            public SecretIdentifier Identifier { get; } = identifier;

            /// <summary>
            /// Gets the non-sensitive reference metadata.
            /// </summary>
            public McpSecretReference Metadata { get; } = metadata;

            /// <summary>
            /// Gets or sets whether persistence was acknowledged.
            /// </summary>
            public bool Ready { get; set; }

            /// <summary>
            /// Gets or sets whether local use is irrevocably disabled.
            /// </summary>
            public bool Revoked { get; set; }

            /// <summary>
            /// Gets completion of the write and any uncertain-write cleanup before removal may run.
            /// </summary>
            public TaskCompletionSource PersistenceCompleted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets a signal for dependent runtimes; asynchronous continuations never run under the state guard.
            /// </summary>
            public TaskCompletionSource Invalidated { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// A bounded gateway-owned view with no provider callbacks after authorization.
        /// </summary>
        private sealed class MaterializedSecret(byte[] bytes) : ISecret
        {
            /// <inheritdoc/>
            public ReadOnlySpan<byte> Bytes => bytes;

            /// <inheritdoc/>
            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    /// <summary>
    /// A secret-free local persistence failure following a possibly committed remote issuance.
    /// The reference can be used for cleanup, but cannot resolve unacknowledged material.
    /// </summary>
    public sealed class McpSecretPersistenceException : Exception, IMcpSecretPersistenceFailure
    {
        /// <summary>
        /// Creates a secret-free persistence failure without provider-controlled diagnostic text.
        /// </summary>
        public McpSecretPersistenceException()
            : this(new McpSecretReference(
                string.Empty, string.Empty, string.Empty, DateTimeOffset.MinValue, null),
                StatusCodes.BadResourceUnavailable, cleanupFailed: false)
        {
        }

        /// <summary>
        /// Creates a safe exception. The supplied message is deliberately not retained because provider
        /// diagnostics can contain credential material.
        /// </summary>
        public McpSecretPersistenceException(string? message)
            : this()
        {
        }

        /// <summary>
        /// Creates a safe exception without retaining either provider-controlled diagnostic input.
        /// </summary>
        public McpSecretPersistenceException(string? message, Exception? innerException)
            : this()
        {
        }

        /// <summary>
        /// Captures safe primary failure and cleanup state without retaining a provider exception.
        /// </summary>
        internal McpSecretPersistenceException(
            McpSecretReference reference, StatusCode providerStatus, bool cleanupFailed)
            : base("Remote issuance may have succeeded, but local credential persistence failed. " +
                "Do not retry issuance blindly; inspect the remote request and local cleanup state.")
        {
            Reference = reference;
            ProviderStatus = providerStatus;
            CleanupFailed = cleanupFailed;
        }

        /// <summary>
        /// Gets the opaque reference and its non-sensitive metadata.
        /// </summary>
        public McpSecretReference Reference { get; }

        /// <summary>
        /// Gets the primary provider failure category without a provider message.
        /// </summary>
        public StatusCode ProviderStatus { get; }

        /// <summary>
        /// Gets whether an uncertain stored key remains tracked for explicit cleanup.
        /// </summary>
        public bool CleanupFailed { get; }
    }

    /// <summary>
    /// Secret-free outcome metadata for local persistence and cleanup failures, including cancellation.
    /// </summary>
    public interface IMcpSecretPersistenceFailure
    {
        /// <summary>
        /// Gets the non-sensitive reference metadata.
        /// </summary>
        McpSecretReference Reference { get; }

        /// <summary>
        /// Gets the local provider's safe numeric failure category.
        /// </summary>
        StatusCode ProviderStatus { get; }

        /// <summary>
        /// Gets whether the generated storage key remains tracked for cleanup.
        /// </summary>
        bool CleanupFailed { get; }
    }

    /// <summary>
    /// A canceled local persistence operation with safe recovery metadata and no provider-controlled exception text.
    /// Domain tools catch this after acknowledged remote issuance to retain that remote result.
    /// </summary>
    public sealed class McpSecretPersistenceCanceledException : OperationCanceledException, IMcpSecretPersistenceFailure
    {
        /// <summary>
        /// Creates a safe cancellation without provider diagnostics.
        /// </summary>
        public McpSecretPersistenceCanceledException()
            : this(new McpSecretReference(
                string.Empty, string.Empty, string.Empty, DateTimeOffset.MinValue, null), false, default)
        {
        }

        /// <summary>
        /// Creates a safe cancellation without retaining the supplied provider message.
        /// </summary>
        public McpSecretPersistenceCanceledException(string? message)
            : this()
        {
        }

        /// <summary>
        /// Creates a safe cancellation without retaining either provider-controlled diagnostic input.
        /// </summary>
        public McpSecretPersistenceCanceledException(string? message, Exception? innerException)
            : this()
        {
        }

        /// <summary>
        /// Creates a safe cancellation retaining only the cancellation token.
        /// </summary>
        public McpSecretPersistenceCanceledException(CancellationToken cancellationToken)
            : this(new McpSecretReference(
                string.Empty, string.Empty, string.Empty, DateTimeOffset.MinValue, null), false, cancellationToken)
        {
        }

        /// <summary>
        /// Creates a safe cancellation without retaining the supplied provider message.
        /// </summary>
        public McpSecretPersistenceCanceledException(string? message, CancellationToken cancellationToken)
            : this(cancellationToken)
        {
        }

        /// <summary>
        /// Creates a safe cancellation retaining the token but neither provider-controlled diagnostic input.
        /// </summary>
        public McpSecretPersistenceCanceledException(
            string? message, Exception? innerException, CancellationToken cancellationToken)
            : this(cancellationToken)
        {
        }

        /// <summary>
        /// Captures cleanup metadata and the original cancellation token without an inner exception.
        /// </summary>
        internal McpSecretPersistenceCanceledException(
            McpSecretReference reference, bool cleanupFailed, CancellationToken cancellationToken)
            : base("Local credential persistence was canceled; remote issuance may already have committed.",
                cancellationToken)
        {
            Reference = reference;
            CleanupFailed = cleanupFailed;
        }

        /// <inheritdoc/>
        public McpSecretReference Reference { get; }

        /// <inheritdoc/>
        public StatusCode ProviderStatus => StatusCodes.BadRequestCancelledByClient;

        /// <inheritdoc/>
        public bool CleanupFailed { get; }
    }

    /// <summary>
    /// A host-authored alias for one existing secret and its exact permitted resource.
    /// </summary>
    public sealed record McpSecretAlias(string Alias, SecretIdentifier Identifier, string Resource);

    /// <summary>
    /// Safe public result of secret persistence, with no plaintext or store location.
    /// </summary>
    public sealed record McpSecretReference(
        string Reference, string Purpose, string Resource, DateTimeOffset ExpiresAt, string? CredentialId,
        string? Issuer = null);

    /// <summary>
    /// A runtime's authorized metadata and revocation signal. The runtime also enforces the exact metadata expiry;
    /// no provider, timer or secret bytes are transferred by this dependency.
    /// </summary>
    public sealed record McpSecretDependency(McpSecretReference Reference, Task Invalidated);

    /// <summary>
    /// Caller-bound reference capacity held while one explicit remote issuance operation completes.
    /// </summary>
    public sealed class McpSecretReservation : IDisposable
    {
        /// <summary>
        /// Captures capacity already admitted by the issuing gateway.
        /// </summary>
        internal McpSecretReservation(
            McpSecretReferences owner, McpCallerIdentity caller, McpCapability capability, int remaining)
        {
            m_gateway = owner;
            Owner = caller;
            Capability = capability;
            Remaining = remaining;
        }

        /// <summary>
        /// Gets the authority that owns this reservation.
        /// </summary>
        internal McpCallerIdentity Owner { get; }

        /// <summary>
        /// Gets the capability checked before remote dispatch.
        /// </summary>
        internal McpCapability Capability { get; }

        /// <summary>
        /// Gets or sets unused capacity under the gateway's private state guard.
        /// </summary>
        internal int Remaining { get; set; }

        /// <summary>
        /// Gets or sets whether unused capacity was released.
        /// </summary>
        internal bool Released { get; set; }

        /// <summary>
        /// Gets completion of the admitted remote operation's reservation lifetime.
        /// </summary>
        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Persists one secret and returns only public reference metadata.
        /// </summary>
        public ValueTask<McpSecretReference> StoreAsync(
            ByteString secret,
            string purpose,
            string resource,
            DateTimeOffset expiresAt,
            string? credentialId = null,
            CancellationToken ct = default)
        {
            return m_gateway.StoreReservedAsync(this, secret, purpose, resource, expiresAt, credentialId, null, ct);
        }

        /// <summary>
        /// Persists an issuer-bound credential so refresh/proof material cannot be forwarded to a different issuer.
        /// The audience resource remains independent from the issuing service's exact model identity.
        /// </summary>
        public ValueTask<McpSecretReference> StoreForIssuerAsync(
            ByteString secret,
            string purpose,
            string resource,
            string issuer,
            DateTimeOffset expiresAt,
            string? credentialId = null,
            CancellationToken ct = default)
        {
            return m_gateway.StoreReservedAsync(this, secret, purpose, resource, expiresAt, credentialId, issuer, ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_gateway.ReleaseReservation(this);
        }

        private readonly McpSecretReferences m_gateway;
    }

    /// <summary>
    /// Secret-free provider-boundary diagnostics.
    /// </summary>
    internal static partial class McpSecretReferencesLog
    {
        /// <summary>
        /// Emits only the provider operation and exception type.
        /// </summary>
        [LoggerMessage(EventId = McpServerEventIds.McpSecretReferences, Level = LogLevel.Error,
            Message = "MCP secret provider operation {Operation} failed with {ExceptionType}.")]
        internal static partial void SecretProviderFailed(
            this ILogger logger, string operation, string exceptionType);
    }
}
