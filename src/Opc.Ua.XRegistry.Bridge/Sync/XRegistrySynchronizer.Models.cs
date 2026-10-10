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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    public sealed partial class XRegistrySynchronizer
    {
        private async ValueTask<bool> SynchronizeModelsAsync(Pass pass, CancellationToken ct)
        {
            string registryScope = XRegistrySyncJson.Fingerprint(new JsonObject
            {
                ["opcua"] = pass.Native.Description!.RegistryId,
                ["http"] = pass.Http.Description!.RegistryId
            });
            if (pass.State.RegistryScope.Length != 0 && pass.State.RegistryScope != registryScope)
            {
                AddConflict(pass, "/", "scope_changed",
                    "A registry identity changed; model replication cannot rebind this job.");
                return false;
            }
            if (pass.State.RegistryScope.Length == 0 &&
                pass.State.ScopeFingerprint.Length != 0 &&
                (pass.Native.Model!.Signature != pass.Http.Model!.Signature ||
                    pass.State.ScopeFingerprint != ScopeFingerprint(pass.Native, pass.Http)))
            {
                AddConflict(pass, "/", "scope_changed",
                    "Legacy state must be explicitly revalidated before a changed model or registry can be adopted.");
                return false;
            }
            XRegistrySyncIntent? pending = pass.State.Intents.Values.FirstOrDefault(intent =>
                intent.Path == "/modelsource" && intent.Pending);
            if (pending is not null)
            {
                if (pass.DryRun)
                {
                    Hold(pass, pending, "Dry run cannot resolve a pending model mutation.");
                    return false;
                }
                return await VerifyModelIntentAsync(pass, pending, false, ct).ConfigureAwait(false);
            }
            bool same = pass.Native.Model!.Signature == pass.Http.Model!.Signature;
            if (same)
            {
                string scope = ScopeFingerprint(pass.Native, pass.Http);
                if (pass.State.ScopeFingerprint.Length != 0 &&
                    pass.State.ScopeFingerprint != scope &&
                    (pass.State.ModelDefinition.ValueKind != JsonValueKind.Object ||
                        !CompatibleModelExtension(pass.State.ModelDefinition, pass.Native.Model.Document) ||
                        pass.Native.ModelSource is null ||
                        pass.Http.ModelSource is null ||
                        !SourceDefinesEffectiveModel(pass.Native) ||
                        !SourceDefinesEffectiveModel(pass.Http)))
                {
                    AddConflict(pass, "/", "scope_changed",
                        "The model changed outside a compatible extension; existing baselines remain held.");
                    return false;
                }
                CaptureModelBaseline(pass, registryScope);
                return true;
            }
            XRegistrySyncObservation? native = pass.Native.ModelSource;
            XRegistrySyncObservation? http = pass.Http.ModelSource;
            if (native is null || http is null || !pass.Complete)
            {
                AddConflict(pass, "/", "incompatible_model",
                    "Different models need complete modelsource observations on both endpoints.");
                return false;
            }
            XRegistrySyncConflict? conflict = pass.State.Conflicts.Values.FirstOrDefault(value =>
                value.Path == "/modelsource" && XRegistrySyncStateManager.IsActive(value));
            XRegistrySyncConflictPolicy policy = m_options.ConflictPolicy;
            if (conflict?.Status == XRegistrySyncConflictStatus.ResolutionRequested)
            {
                if (!SameObservation(native, conflict.OpcUa) || !SameObservation(http, conflict.Http))
                {
                    AddConflict(
                        pass, "/modelsource", "resolution_stale", "The model observations changed after the decision.");
                    return false;
                }
                policy = conflict.Resolution;
            }
            else if (conflict?.Reason is "mutation_rejected" or "resolution_stale")
            {
                pass.Records.Add(new XRegistrySyncRecord("/modelsource", XRegistrySyncRecordKind.Conflict,
                    "A rejected model change needs a fresh explicit resolution.")
                { ConflictId = conflict.Id });
                return false;
            }
            bool nativeChanged = pass.State.ModelBaseline is null ||
                native.Fingerprint != pass.State.ModelBaseline.OpcUa.Fingerprint;
            bool httpChanged = pass.State.ModelBaseline is null ||
                http.Fingerprint != pass.State.ModelBaseline.Http.Fingerprint;
            XRegistrySyncSide sourceSide;
            if (nativeChanged != httpChanged)
            {
                sourceSide = nativeChanged ? XRegistrySyncSide.OpcUa : XRegistrySyncSide.Http;
            }
            else if (policy != XRegistrySyncConflictPolicy.Manual)
            {
                sourceSide = Preferred(policy);
            }
            else
            {
                AddConflict(pass, "/modelsource", "incompatible_model",
                    "Divergent model changes require an explicit preference.");
                return false;
            }
            XRegistrySyncSide destination = Other(sourceSide);
            XRegistrySyncInventory source = pass.Inventory(sourceSide);
            XRegistrySyncInventory target = pass.Inventory(destination);
            if (!Qualified(target) ||
                !CompatibleModelExtension(target.Model!.Document, source.Model!.Document) ||
                !SourceDefinesEffectiveModel(source))
            {
                Unsupported(pass, "/modelsource",
                    "Only independently verifiable compatible model extensions are copied; " +
                    "retention/type-changing or unresolved includes need a qualified migration.");
                return false;
            }
            if (!TakeOperation(pass, "/modelsource"))
            {
                return false;
            }
            if (pass.DryRun)
            {
                pass.Records.Add(new XRegistrySyncRecord("/modelsource", XRegistrySyncRecordKind.Planned,
                    "Would publish the guarded model extension before creating dependent entities."));
                return false;
            }
            XRegistrySyncObservation from = source.ModelSource!;
            XRegistrySyncObservation before = target.ModelSource!;
            if (!SameObservation(from, await pass.Reader(sourceSide).ReadEntityAsync("/modelsource", source.Model, ct)
                .ConfigureAwait(false)) ||
                !SameObservation(
                    before, await pass.Reader(destination).ReadEntityAsync("/modelsource", target.Model, ct)
                    .ConfigureAwait(false)))
            {
                AddConflict(
                    pass, "/modelsource", "concurrent_edit", "A model changed before dispatch; no write was sent.");
                return false;
            }
            string id = pass.State.NextId("model");
            bool replay = target.Description!.SupportsOperationReplay &&
                (destination == XRegistrySyncSide.OpcUa ? m_opcUa : m_http) is IXRegistryOperationJournalEndpoint;
            var request = new XRegistryRequest(XRegistryAction.Merge, "/")
            {
                View = XRegistryView.Metadata,
                OperationId = replay ? id : null,
                Metadata = XRegistrySyncJson.Element(new JsonObject
                {
                    ["epoch"] = JsonNode.Parse(before.Epoch),
                    ["modelsource"] = XRegistrySyncJson.Object(from.Metadata)
                }, m_options.MaximumStateBytes, m_options.MaximumJsonDepth)
            };
            var intent = new XRegistrySyncIntent(id, "/modelsource", destination, XRegistrySyncIntentKind.Replace,
                request, [from], [before], m_timeProvider.GetUtcNow());
            pass.State.Intents[id] = intent;
            pass.Dirty = true;
            await SaveAsync(pass, ct).ConfigureAwait(false);
            try
            {
                XRegistryResponse response =
                    await pass.Reader(destination).ExecuteAsync(request, ct).ConfigureAwait(false);
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.SyncMutationCompleted(Redact.Create(m_options.JobId), Redact.Create(id),
                        Redact.Create("/modelsource"), destination, response.StatusCode);
                }
                intent = intent with
                {
                    Response = response,
                    State = response.IsSuccess ? XRegistrySyncIntentState.Responded : XRegistrySyncIntentState.Rejected
                };
                pass.State.Intents[id] = intent;
                pass.Dirty = true;
                await SaveAsync(pass, ct).ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    AddConflict(
                        pass, "/modelsource", "mutation_rejected", "The guarded model extension was rejected.", id);
                    await SaveAsync(pass, ct).ConfigureAwait(false);
                    return false;
                }
            }
            catch (Exception exception) when (!pass.StateCommitInProgress &&
                XRegistryOperationDeadline.IsEndpointFailure(exception) &&
                !(exception is OperationCanceledException && ct.IsCancellationRequested))
            {
                pass.State.Intents[id] = intent = intent with { Failure = exception.Message };
                pass.Dirty = true;
                await SaveAsync(pass, ct).ConfigureAwait(false);
            }
            pass.Native = await pass.Reader(XRegistrySyncSide.OpcUa).ReadAsync(ct).ConfigureAwait(false);
            pass.Http = await pass.Reader(XRegistrySyncSide.Http).ReadAsync(ct).ConfigureAwait(false);
            pass.Records.AddRange(pass.Native.Records);
            pass.Records.AddRange(pass.Http.Records);
            return await VerifyModelIntentAsync(pass, intent, true, ct).ConfigureAwait(false);
        }

        private async ValueTask<bool> VerifyModelIntentAsync(
            Pass pass, XRegistrySyncIntent intent, bool sent, CancellationToken ct)
        {
            if (intent.Response is null &&
                intent.Request.OperationId is not null &&
                pass.Inventory(intent.Destination).Description?.SupportsOperationReplay == true)
            {
                XRegistryOperationOutcome outcome = await pass.Reader(intent.Destination).OutcomeAsync(intent.Id, ct)
                    .ConfigureAwait(false);
                if (outcome.Response is not null)
                {
                    intent = intent with
                    {
                        Response = outcome.Response,
                        State = outcome.State == XRegistryOperationState.Committed
                            ? XRegistrySyncIntentState.Responded : XRegistrySyncIntentState.Rejected
                    };
                    pass.State.Intents[intent.Id] = intent;
                    pass.Dirty = true;
                }
            }
            if (intent.State == XRegistrySyncIntentState.Rejected)
            {
                AddConflict(pass, "/modelsource", "mutation_rejected",
                    "The model outcome confirms rejection; it is never resent.", intent.Id);
                await SaveAsync(pass, ct).ConfigureAwait(false);
                return false;
            }
            XRegistrySyncObservation desired = intent.Source[0];
            if (!pass.Complete ||
                pass.Native.Model?.Signature != pass.Http.Model?.Signature ||
                pass.Native.ModelSource?.Fingerprint != desired.Fingerprint ||
                pass.Http.ModelSource?.Fingerprint != desired.Fingerprint)
            {
                Hold(pass, intent, "The model outcome is not verified; dependent entity writes remain blocked.");
                await SaveAsync(pass, ct).ConfigureAwait(false);
                return false;
            }
            pass.State.Intents[intent.Id] = intent with { State = XRegistrySyncIntentState.Verified };
            pass.Dirty = true;
            CaptureModelBaseline(pass, XRegistrySyncJson.Fingerprint(new JsonObject
            {
                ["opcua"] = pass.Native.Description!.RegistryId,
                ["http"] = pass.Http.Description!.RegistryId
            }));
            ResolveConflicts(pass, "/modelsource");
            pass.Records.Add(new XRegistrySyncRecord(
                "/modelsource", sent ? XRegistrySyncRecordKind.Applied : XRegistrySyncRecordKind.Converged,
                "Both model sources and effective schemas are verified; dependent inventory can proceed.")
            {
                OperationId = intent.Id
            });
            await SaveAsync(pass, ct).ConfigureAwait(false);
            return true;
        }

        private void CaptureModelBaseline(Pass pass, string registryScope)
        {
            string scope = ScopeFingerprint(pass.Native, pass.Http);
            if (pass.State.ScopeFingerprint != scope || pass.State.RegistryScope != registryScope)
            {
                pass.State.ScopeFingerprint = scope;
                pass.State.RegistryScope = registryScope;
                pass.Dirty = true;
            }
            if (pass.Native.ModelSource is { } native && pass.Http.ModelSource is { } http)
            {
                if (!SameObservation(pass.State.ModelBaseline?.OpcUa, native) ||
                    !SameObservation(pass.State.ModelBaseline?.Http, http))
                {
                    pass.State.ModelBaseline = new XRegistrySyncBaseline(native, http, m_timeProvider.GetUtcNow());
                    pass.State.ModelDefinition = pass.Native.Model!.Document.Clone();
                    pass.Dirty = true;
                }
            }
        }

        private bool SourceDefinesEffectiveModel(XRegistrySyncInventory source)
        {
            try
            {
                return new XRegistrySyncModel(source.ModelSource!.Metadata, m_options)
                    .Signature == source.Model!.Signature;
            }
            catch (XRegistryRejectionException)
            {
                return false;
            }
        }

        private static bool CompatibleModelExtension(JsonElement previous, JsonElement proposed)
        {
            if (previous.ValueKind != JsonValueKind.Object || proposed.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            return ModelSubset(XRegistrySyncJson.Object(previous), XRegistrySyncJson.Object(proposed), true);
        }

        private static bool ModelSubset(
            JsonNode? previous, JsonNode? proposed, bool root = false, bool attributes = false)
        {
            if (previous is JsonObject first && proposed is JsonObject second)
            {
                bool definition = root || first["type"] is JsonValue || first["singular"] is JsonValue;
                foreach ((string key, JsonNode? value) in first)
                {
                    if (definition &&
                        key is "description" or "documentation" or "icon" or "labels" or "modelversion"
                            or "modelcompatiblewith")
                    {
                        continue;
                    }
                    if (!second.TryGetPropertyValue(key, out JsonNode? candidate) ||
                        !ModelSubset(value, candidate,
                            attributes: key is "attributes" or "metaattributes" or "resourceattributes"))
                    {
                        return false;
                    }
                }
                if (attributes)
                {
                    foreach ((string name, JsonNode? value) in second)
                    {
                        if (!first.ContainsKey(name) &&
                            value is JsonObject added &&
                            (added["default"] is not null || added["required"]?.GetValue<bool>() == true))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            return JsonNode.DeepEquals(previous, proposed);
        }
    }
}
