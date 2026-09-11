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
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    public sealed partial class XRegistrySynchronizer
    {
        private async ValueTask CopyAsync(
            Pass pass,
            XRegistrySyncObservation source,
            XRegistrySyncObservation? destination,
            XRegistrySyncSide destinationSide,
            CancellationToken cancellationToken)
        {
            XRegistrySyncInventory target = pass.Inventory(destinationSide);
            XRegistrySyncInventory origin = pass.Inventory(Other(destinationSide));
            XRegistrySyncModel model = target.Model!;
            XRegistrySyncDefinition definition = model.Resolve(source.Path);
            string? unsupported = XRegistrySyncModel.UnsupportedWrite(definition, source, destination);
            if (unsupported is not null || !Qualified(target))
            {
                Unsupported(pass, source.Path, unsupported ??
                    "The destination does not qualify atomic mutations with exact conditional epochs.");
                return;
            }
            ArrayOf<XRegistrySyncObservation> sources = [source];
            if (destination is null && source.Kind == XRegistrySyncEntityKind.ResourceMeta)
            {
                string resource = XRegistrySyncModel.Parent(source.Path);
                sources =
                [
                    source,
                    .. origin.Entries.Values.Where(entry => entry.Kind == XRegistrySyncEntityKind.Version &&
                        entry.Path.StartsWith(resource + "/versions/", StringComparison.Ordinal))
                        .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                ];
                if (sources.Count == 1)
                {
                    Unsupported(pass, source.Path,
                        "A resource cannot be created from an incomplete Version inventory.");
                    return;
                }
                foreach (XRegistrySyncObservation version in sources.Span.ToArray().Skip(1))
                {
                    unsupported = XRegistrySyncModel.UnsupportedWrite(model.Resolve(version.Path), version, null);
                    if (unsupported is not null)
                    {
                        Unsupported(pass, source.Path, unsupported);
                        return;
                    }
                }
            }
            if (!TakeOperation(pass, source.Path))
            {
                return;
            }
            if (pass.DryRun)
            {
                pass.Records.Add(new XRegistrySyncRecord(source.Path, XRegistrySyncRecordKind.Planned,
                    destination is null
                        ? $"Would conditionally create the explicitly identified entity at {destinationSide}."
                        : $"Would conditionally replace meaningful metadata and content at {destinationSide}."));
                foreach (XRegistrySyncObservation observation in sources)
                {
                    pass.Processed.Add(observation.Path);
                }
                return;
            }
            if (!await CheckScopeAsync(pass, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            XRegistryRequest request;
            ArrayOf<XRegistrySyncObservation> before;
            if (destination is not null)
            {
                JsonObject metadata = Payload(model, source);
                metadata["epoch"] = JsonNode.Parse(destination.Epoch);
                request = new XRegistryRequest(XRegistryAction.Replace, destination.Path)
                {
                    View = XRegistryView.Metadata,
                    Metadata = XRegistrySyncJson.Element(
                        metadata, m_options.MaximumStateBytes, m_options.MaximumJsonDepth)
                };
                before = [destination];
            }
            else if (source.Kind is XRegistrySyncEntityKind.Group or XRegistrySyncEntityKind.ResourceMeta)
            {
                string entityPath = source.Kind == XRegistrySyncEntityKind.Group
                    ? source.Path : XRegistrySyncModel.Parent(source.Path);
                string collection = XRegistrySyncModel.Parent(entityPath);
                string parentPath = XRegistrySyncModel.Parent(collection);
                XRegistrySyncObservation? parent = await pass.Reader(destinationSide)
                    .ReadEntityAsync(parentPath, model, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    Unsupported(pass, source.Path,
                        "The destination parent does not exist; implicit ancestry is unsafe.");
                    return;
                }
                JsonObject entity = source.Kind == XRegistrySyncEntityKind.Group
                    ? XRegistrySyncJson.Object(source.Metadata)
                    : ResourcePayload(model, sources);
                ArrayOf<string> segments = XRegistryPath.GetSegments(entityPath);
                var body = new JsonObject
                {
                    ["epoch"] = JsonNode.Parse(parent.Epoch),
                    [segments[^2]] = new JsonObject
                    {
                        [segments[^1]] = entity
                    }
                };
                request = new XRegistryRequest(XRegistryAction.Merge, parentPath)
                {
                    View = XRegistryView.Metadata,
                    Metadata = XRegistrySyncJson.Element(body, m_options.MaximumStateBytes, m_options.MaximumJsonDepth)
                };
                before = [parent];
            }
            else if (source.Kind == XRegistrySyncEntityKind.Version)
            {
                string resource = XRegistrySyncModel.ResourcePath(source.Path);
                XRegistrySyncObservation? meta = await pass.Reader(destinationSide)
                    .ReadEntityAsync(resource + "/meta", model, cancellationToken).ConfigureAwait(false);
                if (meta is null)
                {
                    Unsupported(pass, source.Path,
                        "An exact Version needs an existing, independently guarded Resource Meta.");
                    return;
                }
                JsonObject body = Payload(model, source);
                body["versionid"] = XRegistryPath.GetSegments(source.Path)[5];
                JsonObject metaGuard = XRegistrySyncJson.Object(meta.Metadata);
                metaGuard["epoch"] = JsonNode.Parse(meta.Epoch);
                body["meta"] = metaGuard;
                request = new XRegistryRequest(XRegistryAction.Create, resource)
                {
                    View = XRegistryView.Metadata,
                    Metadata = XRegistrySyncJson.Element(body, m_options.MaximumStateBytes, m_options.MaximumJsonDepth)
                };
                before = [meta];
            }
            else
            {
                Unsupported(pass, source.Path, "Registry roots cannot be implicitly created.");
                return;
            }
            await PerformAsync(pass, source.Path, destinationSide,
                destination is null ? XRegistrySyncIntentKind.Create : XRegistrySyncIntentKind.Replace,
                request, sources, before, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask DeleteAsync(
            Pass pass,
            XRegistrySyncObservation destination,
            XRegistrySyncSide destinationSide,
            XRegistrySyncBaseline? baseline,
            CancellationToken cancellationToken)
        {
            if (!m_options.PropagateDeletes || !pass.Complete)
            {
                pass.Records.Add(new XRegistrySyncRecord(destination.Path, XRegistrySyncRecordKind.Skipped,
                    "Deletion requires enabled propagation and complete, stable inventories from both endpoints."));
                return;
            }
            baseline ??= pass.State.Tombstones.Values.FirstOrDefault(
                tombstone => tombstone.Path == destination.Path)?.Baseline;
            if (baseline is null)
            {
                Unsupported(pass, destination.Path,
                    "Deletion requires a confirmed baseline, not just prior membership.");
                return;
            }
            XRegistrySyncInventory target = pass.Inventory(destinationSide);
            bool hasDescendants = target.Entries.Keys.Any(path =>
                path.StartsWith(destination.Path + "/", StringComparison.Ordinal));
            if (destination.Kind != XRegistrySyncEntityKind.Group || hasDescendants)
            {
                await DeletePreparedAsync(pass, destination, destinationSide, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (!Qualified(target))
            {
                Unsupported(pass, destination.Path, "The destination does not provide guarded atomic deletion.");
                return;
            }
            if (target.Entries.Keys.Any(path => path.StartsWith(destination.Path + "/", StringComparison.Ordinal)) ||
                pass.State.Conflicts.Values.Any(conflict => XRegistrySyncStateManager.IsActive(conflict) &&
                    conflict.Path.StartsWith(destination.Path + "/", StringComparison.Ordinal)))
            {
                Unsupported(pass, destination.Path, "Descendants and their edits are protected from parent deletion.");
                return;
            }
            if (!await CheckScopeAsync(pass, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            foreach (string collection in target.Model!.ResourceCollections(destination.Path).Span.ToArray())
            {
                ArrayOf<string> children = await pass.Reader(destinationSide).ReadCollectionAsync(
                    XRegistrySyncModel.Child(destination.Path, collection), cancellationToken).ConfigureAwait(false);
                if (children.Count != 0)
                {
                    Unsupported(pass, destination.Path, "A newly observed descendant prevents parent deletion.");
                    return;
                }
            }
            if (!TakeOperation(pass, destination.Path))
            {
                return;
            }
            if (pass.DryRun)
            {
                pass.Records.Add(new XRegistrySyncRecord(destination.Path, XRegistrySyncRecordKind.Planned,
                    $"Would delete the empty group at {destinationSide} using that group's own epoch."));
                return;
            }
            var request = new XRegistryRequest(XRegistryAction.Delete, destination.Path)
            {
                View = XRegistryView.Metadata,
                Parameters = [new XRegistryParameter("epoch", destination.Epoch)]
            };
            await PerformAsync(pass, destination.Path, destinationSide, XRegistrySyncIntentKind.Delete,
                request, [], [destination], cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask PerformAsync(
            Pass pass,
            string path,
            XRegistrySyncSide destinationSide,
            XRegistrySyncIntentKind kind,
            XRegistryRequest request,
            ArrayOf<XRegistrySyncObservation> sources,
            ArrayOf<XRegistrySyncObservation> before,
            CancellationToken cancellationToken,
            bool preparedDeletion = false)
        {
            foreach (XRegistrySyncObservation source in sources.Span.ToArray())
            {
                XRegistrySyncObservation? current = await pass.Reader(Other(destinationSide)).ReadEntityAsync(
                    source.Path, pass.Inventory(Other(destinationSide)).Model!, cancellationToken)
                    .ConfigureAwait(false);
                if (!SameObservation(source, current))
                {
                    SetObservation(pass.Inventory(Other(destinationSide)), source.Path, current);
                    AddConflict(pass, path, "concurrent_edit",
                        "The source changed after the inventory; no write was sent.");
                    return;
                }
            }
            foreach (XRegistrySyncObservation expected in before.Span.ToArray())
            {
                XRegistrySyncObservation? current = await pass.Reader(destinationSide).ReadEntityAsync(
                    expected.Path, pass.Inventory(destinationSide).Model!, cancellationToken).ConfigureAwait(false);
                if (!SameObservation(expected, current))
                {
                    SetObservation(pass.Inventory(destinationSide), expected.Path, current);
                    AddConflict(pass, path, "concurrent_edit",
                        "The destination or parent changed. A fresh epoch is not substituted into this mutation.");
                    return;
                }
            }
            if (kind is XRegistrySyncIntentKind.Create or XRegistrySyncIntentKind.Delete)
            {
                XRegistrySyncSide absentSide = kind == XRegistrySyncIntentKind.Create
                    ? destinationSide : Other(destinationSide);
                XRegistrySyncObservation? current = await pass.Reader(absentSide).ReadEntityAsync(
                    path, pass.Inventory(absentSide).Model!, cancellationToken).ConfigureAwait(false);
                if (current is not null)
                {
                    SetObservation(pass.Inventory(absentSide), path, current);
                    AddConflict(pass, path, "concurrent_edit",
                        "The previously absent identity now exists; no write was sent.");
                    return;
                }
            }
            string operationId = pass.State.NextId("operation");
            bool replay = pass.Inventory(destinationSide).Description!.SupportsOperationReplay &&
                (destinationSide == XRegistrySyncSide.OpcUa ? m_opcUa : m_http) is IXRegistryOperationJournalEndpoint;
            request = request with { OperationId = replay ? operationId : null };
            var intent = new XRegistrySyncIntent(
                operationId, path, destinationSide, kind, request, sources, before, m_timeProvider.GetUtcNow());
            pass.State.Intents.Add(intent.Id, intent);
            pass.Dirty = true;
            await SaveAsync(pass, cancellationToken).ConfigureAwait(false);

            XRegistryResponse response;
            try
            {
                if (preparedDeletion)
                {
                    PreparedDeletionResult prepared = await CommitPreparedDeletionAsync(pass, intent, cancellationToken)
                        .ConfigureAwait(false);
                    if (prepared.Response is null)
                    {
                        pass.State.Intents[intent.Id] = intent with
                        {
                            State = XRegistrySyncIntentState.Conflicted,
                            Failure = prepared.Failure
                        };
                        pass.Dirty = true;
                        foreach (XRegistrySyncObservation observation in before)
                        {
                            pass.Processed.Add(observation.Path);
                        }
                        AddConflict(pass, path, "concurrent_edit",
                            prepared.Failure!, intent.Id);
                        await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    response = prepared.Response;
                }
                else
                {
                    response = await pass.Reader(destinationSide).ExecuteAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (XRegistrySyncDeadline.IsEndpointFailure(exception) &&
                !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                intent = intent with { Failure = exception.Message };
                pass.State.Intents[intent.Id] = intent;
                pass.Dirty = true;
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                await RecoverIntentAsync(pass, intent, cancellationToken).ConfigureAwait(false);
                return;
            }
            intent = intent with
            {
                Response = response,
                State = response.IsSuccess ? XRegistrySyncIntentState.Responded : XRegistrySyncIntentState.Rejected
            };
            pass.State.Intents[intent.Id] = intent;
            pass.Dirty = true;
            if (!response.IsSuccess)
            {
                if (preparedDeletion)
                {
                    foreach (XRegistrySyncObservation observation in before)
                    {
                        pass.Processed.Add(observation.Path);
                    }
                }
                AddConflict(pass, path, "mutation_rejected",
                    $"Guarded mutation rejected ({response.StatusCode}, {response.Error?.Code}); no retry is sent.",
                    intent.Id);
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                return;
            }
            await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
            await VerifyIntentAsync(pass, intent, sentThisPass: true, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<bool> CheckScopeAsync(Pass pass, CancellationToken cancellationToken)
        {
            foreach (XRegistrySyncSide side in new[] { XRegistrySyncSide.OpcUa, XRegistrySyncSide.Http })
            {
                XRegistryEndpointDescription description = await pass.Reader(side).InspectAsync(cancellationToken)
                    .ConfigureAwait(false);
                XRegistrySyncInventory inventory = pass.Inventory(side);
                var model = new XRegistrySyncModel(description.Model, m_options);
                XRegistryResponse root = await pass.Reader(side).ExecuteAsync(
                    new XRegistryRequest(XRegistryAction.Read, "/") { View = XRegistryView.Metadata },
                    cancellationToken).ConfigureAwait(false);
                if (description.RegistryId != inventory.Description!.RegistryId ||
                    model.Signature != inventory.Model!.Signature ||
                    root.StatusCode != 200 ||
                    XRegistrySyncJson.String(root.Metadata, "registryid") != description.RegistryId ||
                    description.SupportsAtomicMutations != inventory.Description.SupportsAtomicMutations ||
                    description.SupportsConditionalMutations != inventory.Description.SupportsConditionalMutations ||
                    description.SupportsPreparedMutations != inventory.Description.SupportsPreparedMutations)
                {
                    pass.ScopeValid = false;
                    AddConflict(pass, "/", "scope_changed",
                        "Registry/model scope changed; no further writes are authorized.");
                    return false;
                }
            }
            return true;
        }

        private bool TakeOperation(Pass pass, string path)
        {
            if (pass.Operations >= m_options.MaximumOperations)
            {
                pass.Exhausted = true;
                pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Skipped,
                    "The bounded pass reached its operation limit; polling must continue."));
                return false;
            }
            pass.Operations++;
            return true;
        }

        private void Unsupported(Pass pass, string path, string detail)
        {
            XRegistrySyncConflict conflict = AddConflict(pass, path, "unsupported_guard", detail);
            pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Unsupported, detail)
            {
                ConflictId = conflict.Id
            });
        }

        private static bool Qualified(XRegistrySyncInventory inventory)
        {
            return inventory.Description is
            {
                SupportsAtomicMutations: true,
                SupportsConditionalMutations: true,
                SupportsWriteTouch: true
            };
        }

        private static JsonObject Payload(XRegistrySyncModel model, XRegistrySyncObservation source)
        {
            JsonObject body = XRegistrySyncJson.Object(source.Metadata);
            if (!source.Document.IsNull)
            {
                body[model.Resolve(source.Path).Singular + "base64"] =
                    Convert.ToBase64String(source.Document.ToArray());
                if (!body.ContainsKey("contenttype"))
                {
                    body["contenttype"] = null;
                }
            }
            return body;
        }

        private static JsonObject ResourcePayload(XRegistrySyncModel model, ArrayOf<XRegistrySyncObservation> sources)
        {
            var versions = new JsonObject();
            foreach (XRegistrySyncObservation source in sources.Span.ToArray().Skip(1))
            {
                versions[XRegistryPath.GetSegments(source.Path)[5]] = Payload(model, source);
            }
            return new JsonObject
            {
                ["meta"] = XRegistrySyncJson.Object(sources[0].Metadata),
                ["versions"] = versions
            };
        }

        private static void SetObservation(
            XRegistrySyncInventory inventory,
            string path,
            XRegistrySyncObservation? value)
        {
            if (value is null)
            {
                inventory.Entries.Remove(path);
            }
            else
            {
                inventory.Entries[path] = value;
            }
        }
    }
}
