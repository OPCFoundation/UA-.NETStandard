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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    /// <summary>
    /// Deterministic, bounded polling over existing endpoints. Each pass attempts
    /// full model-driven inventories; notifications are not required for correctness.
    /// The host schedules further passes and owns endpoint/store lifetimes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three-way comparison uses meaningful fingerprints and independent endpoint-local epochs as mutation guards.
    /// The profile supports guarded metadata, explicit Version, document, and empty-group changes. Subtree and
    /// Version deletion additionally require a prepared destination with global mutation invalidation.
    /// Model replication, external references, protected attributes, assigned Version identities, automatic
    /// retention and ancestry changes require a qualified domain operation.
    /// </para>
    /// <para>
    /// Intents are persisted before dispatch and actual responses before verification or baseline publication.
    /// Pending mutations are never resent. Unknown creations remain held even if current content looks equal.
    /// The engine adds no transport, event feed, distributed transaction, or exactly-once guarantee.
    /// </para>
    /// </remarks>
    public sealed partial class XRegistrySynchronizer
    {
        /// <summary>
        /// Configures one bounded synchronization job without opening state or contacting either endpoint.
        /// </summary>
        /// <param name="opcUa">The caller-owned endpoint representing the OPC UA registry.</param>
        /// <param name="http">The caller-owned endpoint representing the HTTP registry.</param>
        /// <param name="stateStore">The caller-owned single-writer store for baselines and operation evidence.</param>
        /// <param name="options">
        /// The job identity, authenticated endpoint scopes, conflict policy, and finite limits.
        /// </param>
        /// <param name="telemetry">The telemetry context used to create the structured synchronization logger.</param>
        /// <param name="timeProvider">
        /// The clock for request deadlines and evidence timestamps, or null to use <see cref="TimeProvider.System"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// An endpoint, store, options, telemetry, or caller context is null.
        /// </exception>
        /// <exception cref="ArgumentException">The synchronization limits are invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The configured conflict policy is not recognized.</exception>
        public XRegistrySynchronizer(
            IXRegistryEndpoint opcUa,
            IXRegistryEndpoint http,
            IXRegistrySyncStateStore stateStore,
            XRegistrySyncOptions options,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            opcUa.ThrowIfNull(nameof(opcUa));
            http.ThrowIfNull(nameof(http));
            stateStore.ThrowIfNull(nameof(stateStore));
            options.ThrowIfNull(nameof(options));
            telemetry.ThrowIfNull(nameof(telemetry));
            options.Validate();
            m_opcUa = opcUa;
            m_http = http;
            m_store = stateStore;
            m_options = options;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<XRegistrySynchronizer>();
            m_codec = new XRegistrySyncStateCodec(options.MaximumStateBytes, options.MaximumJsonDepth);
            m_configuration = ConfigurationFingerprint(options);
        }

        /// <summary>
        /// Performs one bounded full-inventory, recovery, and three-way reconciliation pass.
        /// The host must schedule any subsequent polling passes.
        /// </summary>
        /// <param name="dryRun">
        /// True to read endpoints and preview planned work using a read-only state session, without changing endpoints,
        /// creating persistent artifacts, or recovering or resolving durable intents; false to execute guarded work.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the pass. Cancellation after dispatch can leave a pending intent; it does not authorize retry.
        /// </param>
        /// <returns>
        /// Ordered records, counts, completeness, and the overall status and exit code for this pass.
        /// State failures produce <see cref="XRegistrySyncStatus.Failed"/> rather than resetting saved evidence.
        /// </returns>
        /// <remarks>
        /// Incomplete inventories never authorize absence-based creation or deletion. Independent entries can continue
        /// while other paths are held by conflicts or uncertain outcomes. A later pass may verify a pending update or
        /// deletion by guarded readback, but never invents a server response or resends the pending mutation.
        /// </remarks>
        /// <exception cref="InvalidDataException">
        /// Invalid or differently bound persisted state is handled and returned as
        /// <see cref="XRegistrySyncStatus.Failed"/> in the report, rather than propagated to the caller.
        /// </exception>
        /// <exception cref="OperationCanceledException">The caller canceled the pass.</exception>
        public async ValueTask<XRegistrySyncReport> RunOnceAsync(
            bool dryRun = false,
            CancellationToken cancellationToken = default)
        {
            var records = new List<XRegistrySyncRecord>();
            Pass? pass = null;
            try
            {
                IXRegistrySyncStateSession session =
                    await m_store.OpenAsync(dryRun, cancellationToken).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable sessionLifetime = session.ConfigureAwait(false);
                ByteString bytes = await session.ReadAsync(cancellationToken).ConfigureAwait(false);
                XRegistrySyncState state = bytes.IsNull
                    ? new XRegistrySyncState(m_options.JobId, m_configuration)
                    : m_codec.Decode(bytes);
                if (state.JobId != m_options.JobId || state.ConfigurationFingerprint != m_configuration)
                {
                    throw new InvalidDataException(
                        "Synchronization state belongs to another job, endpoint identity, or authenticated scope.");
                }
                var nativeReader = new XRegistrySyncInventoryReader(
                    m_opcUa, m_options.OpcUaContext, m_options, m_timeProvider, XRegistrySyncSide.OpcUa);
                var httpReader = new XRegistrySyncInventoryReader(
                    m_http, m_options.HttpContext, m_options, m_timeProvider, XRegistrySyncSide.Http);
                Task<XRegistrySyncInventory> nativeTask = nativeReader.ReadAsync(cancellationToken).AsTask();
                Task<XRegistrySyncInventory> httpTask = httpReader.ReadAsync(cancellationToken).AsTask();
                await Task.WhenAll(nativeTask, httpTask).ConfigureAwait(false);
                XRegistrySyncInventory native = await nativeTask.ConfigureAwait(false);
                XRegistrySyncInventory http = await httpTask.ConfigureAwait(false);
                records.AddRange(native.Records);
                records.AddRange(http.Records);
                pass = new Pass(state, session, native, http, nativeReader, httpReader, dryRun, records);
                if (!native.ScopeStable || !http.ScopeStable)
                {
                    return Report(pass);
                }
                if (native.Model!.Signature != http.Model!.Signature)
                {
                    AddConflict(pass, "/", "incompatible_model",
                        "Effective models differ. Model/configuration replication is not supported by this slice.");
                    pass.ScopeValid = false;
                    await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                    return Report(pass);
                }
                string scope = ScopeFingerprint(native, http);
                if (state.ScopeFingerprint.Length != 0 && state.ScopeFingerprint != scope)
                {
                    AddConflict(pass, "/", "scope_changed",
                        "Registry identity or model changed; existing baselines do not authorize writes.");
                    pass.ScopeValid = false;
                    await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                    return Report(pass);
                }
                if (state.ScopeFingerprint.Length == 0)
                {
                    state.ScopeFingerprint = scope;
                    pass.Dirty = true;
                }
                foreach (XRegistrySyncConflict conflict in state.Conflicts.Values.Where(
                    conflict => XRegistrySyncStateManager.IsActive(conflict) &&
                        conflict.Reason is "scope_changed" or "incompatible_model").ToArray())
                {
                    state.Conflicts[conflict.Id] = conflict with { Status = XRegistrySyncConflictStatus.Resolved };
                    pass.Dirty = true;
                }
                await RecoverAsync(pass, cancellationToken).ConfigureAwait(false);
                string[] paths = [.. native.Entries.Keys.Concat(http.Entries.Keys).Concat(state.Baselines.Keys)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => Order(pass, path)).ThenBy(path => path, StringComparer.Ordinal)];
                foreach (string path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!pass.ScopeValid)
                    {
                        break;
                    }
                    try
                    {
                        await ReconcileAsync(pass, path, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        !pass.StateCommitInProgress &&
                        XRegistrySyncInventoryReader.IsInventoryFailure(exception, cancellationToken))
                    {
                        records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Failure, exception.Message));
                        AddConflict(pass, path, "observation_failed",
                            "The current observation or its write verification failed; this entity is held.");
                    }
                }
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                XRegistrySyncReport report = Report(pass);
                m_logger.SyncPassCompleted(report.Status, report.Applied, report.Pending, report.Conflicts);
                return report;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                UnauthorizedAccessException or ObjectDisposedException or OverflowException)
            {
                records.Add(new XRegistrySyncRecord("/", XRegistrySyncRecordKind.Failure, exception.Message));
                m_logger.SyncStateFailed(exception);
                return Report(pass, records, dryRun, failed: true);
            }
        }

        private async ValueTask ReconcileAsync(Pass pass, string path, CancellationToken cancellationToken)
        {
            if (pass.Processed.Contains(path))
            {
                return;
            }
            XRegistrySyncObservation? native = Find(pass.Native, path);
            XRegistrySyncObservation? http = Find(pass.Http, path);
            pass.State.Baselines.TryGetValue(path, out XRegistrySyncBaseline? baseline);
            XRegistrySyncIntent? pending = pass.State.Intents.Values.FirstOrDefault(
                intent => intent.Pending && XRegistrySyncModel.Overlaps(intent.Path, path));
            if (pending is not null)
            {
                pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Pending,
                    "A durable operation is still uncertain; no mutation will be repeated.")
                {
                    OperationId = pending.Id
                });
                return;
            }
            if (native is not null && http is not null && native.Fingerprint == http.Fingerprint)
            {
                EstablishBaseline(pass, native, http);
                return;
            }
            if (native is null && http is null)
            {
                if (pass.Complete && baseline is not null)
                {
                    Tombstone(pass, path, baseline);
                    ResolveConflicts(pass, path);
                }
                return;
            }
            if ((native is null || http is null) && !pass.Complete)
            {
                pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Skipped,
                    "Incomplete inventory cannot establish absence, authorize creation, or propagate deletion."));
                return;
            }
            XRegistrySyncConflict? active = pass.State.Conflicts.Values.FirstOrDefault(
                conflict => conflict.Path == path && XRegistrySyncStateManager.IsActive(conflict));
            XRegistrySyncConflictPolicy policy = m_options.ConflictPolicy;
            bool explicitResolution = active?.Status == XRegistrySyncConflictStatus.ResolutionRequested;
            if (explicitResolution)
            {
                if (!SameObservation(native, active!.OpcUa) || !SameObservation(http, active.Http))
                {
                    pass.State.Conflicts[active.Id] = active with { Status = XRegistrySyncConflictStatus.Superseded };
                    pass.Dirty = true;
                    AddConflict(pass, path, "resolution_stale",
                        "An endpoint changed after this resolution was recorded. The newer edit is not overridden.");
                    return;
                }
                policy = active!.Resolution;
            }
            else if (active?.Reason is "mutation_rejected" or "resolution_stale" or "verification_changed")
            {
                pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Conflict,
                    "This held operation needs a fresh guarded resolution decision.")
                { ConflictId = active.Id });
                return;
            }

            XRegistrySyncSide winner;
            bool divergent = false;
            if (baseline is null)
            {
                bool tombstoned = pass.State.Tombstones.Values.Any(tombstone => tombstone.Path == path);
                if ((native is null || http is null) && !tombstoned)
                {
                    winner = native is null ? XRegistrySyncSide.Http : XRegistrySyncSide.OpcUa;
                }
                else
                {
                    divergent = true;
                    winner = Preferred(policy);
                    AddConflict(pass, path, tombstoned ? "reappeared_after_delete" : "initial_divergence",
                        tombstoned
                            ? "A previously deleted identity reappeared; resurrection is not inferred from its epoch."
                            : "Both initial observations differ; an explicit conflict policy is required.");
                }
            }
            else if (native is null || http is null)
            {
                if (!m_options.PropagateDeletes)
                {
                    pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Skipped,
                        "Deletion propagation is disabled; the live baseline is retained."));
                    return;
                }
                XRegistrySyncObservation survivor = native ?? http!;
                if (survivor.Fingerprint == baseline.OpcUa.Fingerprint)
                {
                    winner = native is null ? XRegistrySyncSide.OpcUa : XRegistrySyncSide.Http;
                }
                else
                {
                    divergent = true;
                    winner = Preferred(policy);
                    AddConflict(pass, path, "delete_edit_conflict",
                        "One side deleted this identity while the other changed its meaningful state.");
                }
            }
            else
            {
                bool nativeChanged = native.Fingerprint != baseline.OpcUa.Fingerprint;
                bool httpChanged = http.Fingerprint != baseline.Http.Fingerprint;
                if (nativeChanged && httpChanged)
                {
                    divergent = true;
                    winner = Preferred(policy);
                    AddConflict(pass, path, "both_changed", "Both endpoints changed since the confirmed baseline.");
                }
                else
                {
                    winner = nativeChanged ? XRegistrySyncSide.OpcUa : XRegistrySyncSide.Http;
                }
            }
            if (explicitResolution)
            {
                winner = Preferred(policy);
            }
            else if (divergent && policy == XRegistrySyncConflictPolicy.Manual)
            {
                return;
            }
            XRegistrySyncObservation? source = winner == XRegistrySyncSide.OpcUa ? native : http;
            XRegistrySyncObservation? destination = winner == XRegistrySyncSide.OpcUa ? http : native;
            if (source is null)
            {
                await DeleteAsync(pass, destination!, Other(winner), baseline, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CopyAsync(pass, source, destination, Other(winner), cancellationToken).ConfigureAwait(false);
            }
        }

        private void EstablishBaseline(
            Pass pass,
            XRegistrySyncObservation native,
            XRegistrySyncObservation http)
        {
            if (!pass.State.Baselines.TryGetValue(native.Path, out XRegistrySyncBaseline? old) ||
                !SameObservation(native, old.OpcUa) ||
                !SameObservation(http, old.Http))
            {
                pass.State.Baselines[native.Path] = new XRegistrySyncBaseline(native, http, m_timeProvider.GetUtcNow());
                pass.Dirty = true;
            }
            ResolveConflicts(pass, native.Path);
            pass.Records.Add(new XRegistrySyncRecord(
                native.Path, XRegistrySyncRecordKind.Converged,
                "Both live states have the same meaningful fingerprint."));
        }

        private XRegistrySyncConflict AddConflict(
            Pass pass,
            string path,
            string reason,
            string detail,
            string? operationId = null)
        {
            XRegistrySyncObservation? native = Find(pass.Native, path);
            XRegistrySyncObservation? http = Find(pass.Http, path);
            XRegistrySyncConflict? old = pass.State.Conflicts.Values.FirstOrDefault(
                conflict => conflict.Path == path && XRegistrySyncStateManager.IsActive(conflict));
            if (old is not null &&
                old.Reason == reason &&
                old.OperationId == operationId &&
                SameObservation(native, old.OpcUa) &&
                SameObservation(http, old.Http))
            {
                pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Conflict, detail)
                {
                    ConflictId = old.Id,
                    OperationId = operationId
                });
                return old;
            }
            if (old is not null)
            {
                pass.State.Conflicts[old.Id] = old with { Status = XRegistrySyncConflictStatus.Superseded };
            }
            var conflict = new XRegistrySyncConflict(
                pass.State.NextId("conflict"), path, reason, m_timeProvider.GetUtcNow())
            {
                OpcUa = native,
                Http = http,
                BaselineFingerprint = pass.State.Baselines.TryGetValue(path, out XRegistrySyncBaseline? baseline)
                    ? baseline.OpcUa.Fingerprint
                    : null,
                OperationId = operationId
            };
            pass.State.Conflicts.Add(conflict.Id, conflict);
            pass.Dirty = true;
            pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Conflict, detail)
            {
                ConflictId = conflict.Id,
                OperationId = operationId
            });
            return conflict;
        }

        private void Tombstone(Pass pass, string path, XRegistrySyncBaseline baseline)
        {
            string id = pass.State.NextId("tombstone");
            pass.State.Tombstones.Add(id, new XRegistrySyncTombstone(id, path, baseline, m_timeProvider.GetUtcNow()));
            pass.State.Baselines.Remove(path);
            pass.Dirty = true;
        }

        private async ValueTask SaveAsync(Pass pass, CancellationToken cancellationToken)
        {
            if (!pass.Dirty || pass.DryRun)
            {
                return;
            }
            pass.StateCommitInProgress = true;
            pass.State.Generation = checked(pass.State.Generation + 1);
            ByteString bytes = m_codec.Encode(pass.State);
            await pass.Session.CommitAsync(bytes, cancellationToken).ConfigureAwait(false);
            pass.Dirty = false;
            pass.StateCommitInProgress = false;
        }

        private static void ResolveConflicts(Pass pass, string path)
        {
            foreach (XRegistrySyncConflict conflict in pass.State.Conflicts.Values.Where(
                conflict => conflict.Path == path && XRegistrySyncStateManager.IsActive(conflict)).ToArray())
            {
                pass.State.Conflicts[conflict.Id] = conflict with { Status = XRegistrySyncConflictStatus.Resolved };
                pass.Dirty = true;
            }
        }

        private static bool SameObservation(XRegistrySyncObservation? first, XRegistrySyncObservation? second)
        {
            return first is null ? second is null : second is not null &&
                first.Path == second.Path &&
                first.Kind == second.Kind &&
                first.Fingerprint == second.Fingerprint &&
                first.Epoch == second.Epoch;
        }

        private static XRegistrySyncObservation? Find(XRegistrySyncInventory inventory, string path)
        {
            return inventory.Entries.TryGetValue(path, out XRegistrySyncObservation? observation) ? observation : null;
        }

        private static XRegistrySyncSide Preferred(XRegistrySyncConflictPolicy policy)
        {
            return policy == XRegistrySyncConflictPolicy.PreferHttp ? XRegistrySyncSide.Http : XRegistrySyncSide.OpcUa;
        }

        private static XRegistrySyncSide Other(XRegistrySyncSide side)
        {
            return side == XRegistrySyncSide.OpcUa ? XRegistrySyncSide.Http : XRegistrySyncSide.OpcUa;
        }

        private int Order(Pass pass, string path)
        {
            XRegistrySyncObservation? native = Find(pass.Native, path);
            XRegistrySyncObservation? http = Find(pass.Http, path);
            XRegistrySyncEntityKind kind = (native ?? http)?.Kind ??
                pass.State.Baselines[path].OpcUa.Kind;
            bool oneMissing = native is null || http is null;
            bool existed = pass.State.Baselines.ContainsKey(path);
            if (existed && oneMissing)
            {
                XRegistrySyncSide destination = native is null ? XRegistrySyncSide.Http : XRegistrySyncSide.OpcUa;
                IXRegistryEndpoint endpoint = destination == XRegistrySyncSide.Http ? m_http : m_opcUa;
                if (pass.Inventory(destination).Description is { SupportsPreparedMutations: true } &&
                    endpoint is IXRegistryPreparedEndpoint)
                {
                    return kind == XRegistrySyncEntityKind.Group ? 1 : 2;
                }
                return kind == XRegistrySyncEntityKind.Group ? 7 : 6;
            }
            return kind switch
            {
                XRegistrySyncEntityKind.Registry => 0,
                XRegistrySyncEntityKind.Group => 1,
                XRegistrySyncEntityKind.ResourceMeta when oneMissing => 2,
                XRegistrySyncEntityKind.Version => 3,
                _ => 4
            };
        }

        private static string ConfigurationFingerprint(XRegistrySyncOptions options)
        {
            var value = new JsonObject
            {
                ["job"] = options.JobId,
                ["opcUaEndpoint"] = options.OpcUaEndpointIdentity,
                ["httpEndpoint"] = options.HttpEndpointIdentity,
                ["opcUaContext"] = Context(options.OpcUaContext),
                ["httpContext"] = Context(options.HttpContext)
            };
            return XRegistrySyncJson.Fingerprint(value);
        }

        private static JsonObject Context(XRegistryCallContext context)
        {
            return new JsonObject
            {
                ["subject"] = context.Subject,
                ["authority"] = context.Authority,
                ["authenticated"] = context.IsAuthenticated,
                ["roles"] = new JsonArray(
                    [.. context.Roles.Span.ToArray().OrderBy(role => role, StringComparer.Ordinal).Select(role =>
                        (JsonNode?)JsonValue.Create(role))])
            };
        }

        private static string ScopeFingerprint(XRegistrySyncInventory native, XRegistrySyncInventory http)
        {
            return XRegistrySyncJson.Fingerprint(new JsonObject
            {
                ["opcUaRegistry"] = native.Description!.RegistryId,
                ["httpRegistry"] = http.Description!.RegistryId,
                ["model"] = native.Model!.Signature
            });
        }

        private static XRegistrySyncReport Report(
            Pass? pass,
            List<XRegistrySyncRecord>? records = null,
            bool dryRun = false,
            bool failed = false)
        {
            records ??= pass?.Records ?? [];
            int conflicts = pass?.State.Conflicts.Values.Count(XRegistrySyncStateManager.IsActive) ?? 0;
            int pending = pass?.State.Intents.Values.Count(intent => intent.Pending) ?? 0;
            bool complete = pass is not null && pass.Complete && !pass.Exhausted;
            XRegistrySyncStatus status = failed ? XRegistrySyncStatus.Failed :
                !complete ? XRegistrySyncStatus.Incomplete :
                conflicts != 0 || pending != 0 ? XRegistrySyncStatus.Conflicts : XRegistrySyncStatus.Succeeded;
            return new XRegistrySyncReport
            {
                Status = status,
                DryRun = pass?.DryRun ?? dryRun,
                InventoryComplete = complete,
                Observed =
                    (pass?.Native.Entries.Keys.Concat(pass.Http.Entries.Keys).Distinct(StringComparer.Ordinal)
                    .Count()) ??
                    0,
                Converged = records.Where(record => record.Kind == XRegistrySyncRecordKind.Converged)
                    .Select(record => record.Path).Distinct(StringComparer.Ordinal).Count(),
                Applied = records.Count(record => record.Kind == XRegistrySyncRecordKind.Applied),
                Deleted = records.Count(record => record.Kind == XRegistrySyncRecordKind.Deleted),
                Planned = records.Count(record => record.Kind == XRegistrySyncRecordKind.Planned),
                Conflicts = conflicts,
                Pending = pending,
                Failures = records.Count(record => record.Kind == XRegistrySyncRecordKind.Failure),
                Records = [.. records]
            };
        }

        private readonly IXRegistryEndpoint m_opcUa;
        private readonly IXRegistryEndpoint m_http;
        private readonly IXRegistrySyncStateStore m_store;
        private readonly XRegistrySyncOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly XRegistrySyncStateCodec m_codec;
        private readonly string m_configuration;

        private sealed class Pass(
            XRegistrySyncState state,
            IXRegistrySyncStateSession session,
            XRegistrySyncInventory native,
            XRegistrySyncInventory http,
            XRegistrySyncInventoryReader nativeReader,
            XRegistrySyncInventoryReader httpReader,
            bool dryRun,
            List<XRegistrySyncRecord> records)
        {
            public XRegistrySyncState State { get; } = state;

            public IXRegistrySyncStateSession Session { get; } = session;

            public XRegistrySyncInventory Native { get; } = native;

            public XRegistrySyncInventory Http { get; } = http;

            public bool DryRun { get; } = dryRun;

            public List<XRegistrySyncRecord> Records { get; } = records;

            public bool Dirty { get; set; }

            public bool ScopeValid { get; set; } = native.ScopeStable && http.ScopeStable;

            public bool Exhausted { get; set; }

            public int Operations { get; set; }

            public bool StateCommitInProgress { get; set; }

            public HashSet<string> Processed { get; } = new(StringComparer.Ordinal);

            public bool Complete => ScopeValid && Native.Complete && Http.Complete;

            public XRegistrySyncInventory Inventory(XRegistrySyncSide side)
            {
                return side == XRegistrySyncSide.OpcUa ? Native : Http;
            }

            public XRegistrySyncInventoryReader Reader(XRegistrySyncSide side)
            {
                return side == XRegistrySyncSide.OpcUa ? nativeReader : httpReader;
            }
        }
    }

    internal static partial class XRegistrySynchronizerLog
    {
        [LoggerMessage(EventId = XRegistryBridgeEventIds.XRegistrySynchronizer, Level = LogLevel.Information,
            Message = "Synchronization pass {Status}: {Applied} applied, {Pending} pending, {Conflicts} conflicts.")]
        public static partial void SyncPassCompleted(
            this ILogger logger, XRegistrySyncStatus status, int applied, int pending, int conflicts);

        [LoggerMessage(EventId = XRegistryBridgeEventIds.XRegistrySynchronizer + 1, Level = LogLevel.Error,
            Message = "Synchronization stopped because state ownership, integrity, quota, or durability failed.")]
        public static partial void SyncStateFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryBridgeEventIds.XRegistrySynchronizer + 2, Level = LogLevel.Warning,
            Message = "An abandoned preparation could not be released after its deadline.")]
        public static partial void PreparationCleanupFailed(this ILogger logger, Exception exception);
    }
}
