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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    internal sealed class XRegistrySyncInventory
    {
        public XRegistryEndpointDescription? Description { get; set; }

        public XRegistrySyncModel? Model { get; set; }

        public bool Complete { get; set; } = true;

        public bool ScopeStable { get; set; }

        public SortedDictionary<string, XRegistrySyncObservation> Entries { get; } = new(StringComparer.Ordinal);

        public SortedDictionary<string, ArrayOf<string>> Collections { get; } = new(StringComparer.Ordinal);

        public List<XRegistrySyncRecord> Records { get; } = [];
    }

    internal sealed class XRegistrySyncInventoryReader(
        IXRegistryEndpoint endpoint,
        XRegistryCallContext context,
        XRegistrySyncOptions options,
        TimeProvider timeProvider,
        XRegistrySyncSide side)
    {
        public async ValueTask<XRegistrySyncInventory> ReadAsync(CancellationToken cancellationToken)
        {
            var inventory = new XRegistrySyncInventory();
            try
            {
                inventory.Description = await InspectAsync(cancellationToken).ConfigureAwait(false);
                inventory.Model = new XRegistrySyncModel(inventory.Description.Model, options);
                XRegistrySyncObservation root = await ReadRootAsync(inventory, cancellationToken).ConfigureAwait(false);
                Add(inventory, root);
                foreach (string groups in inventory.Model.GroupCollections.Span.ToArray())
                {
                    string groupCollection = XRegistrySyncModel.Child("/", groups);
                    ArrayOf<string> groupIds = await InventoryCollectionAsync(
                        inventory, groupCollection, cancellationToken).ConfigureAwait(false);
                    foreach (string id in groupIds.Span.ToArray())
                    {
                        string groupPath = XRegistrySyncModel.Child(groupCollection, id);
                        if (!await InventoryEntityAsync(inventory, groupPath, cancellationToken).ConfigureAwait(false))
                        {
                            continue;
                        }
                        foreach (string resources in inventory.Model.ResourceCollections(groupPath).Span.ToArray())
                        {
                            string resourceCollection = XRegistrySyncModel.Child(groupPath, resources);
                            ArrayOf<string> resourceIds = await InventoryCollectionAsync(
                                inventory, resourceCollection, cancellationToken).ConfigureAwait(false);
                            foreach (string resourceId in resourceIds.Span.ToArray())
                            {
                                string resourcePath = XRegistrySyncModel.Child(resourceCollection, resourceId);
                                if (!await InventoryEntityAsync(inventory, resourcePath + "/meta", cancellationToken)
                                    .ConfigureAwait(false))
                                {
                                    continue;
                                }
                                string versions = resourcePath + "/versions";
                                ArrayOf<string> versionIds = await InventoryCollectionAsync(
                                    inventory, versions, cancellationToken).ConfigureAwait(false);
                                if (versionIds.Count == 0)
                                {
                                    Fail(inventory, resourcePath,
                                        "A live Resource Meta has no complete Version inventory.");
                                }
                                foreach (string versionId in versionIds.Span.ToArray())
                                {
                                    await InventoryEntityAsync(
                                        inventory, XRegistrySyncModel.Child(versions, versionId), cancellationToken)
                                        .ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
                foreach ((string path, ArrayOf<string> expected) in inventory.Collections)
                {
                    ArrayOf<string> current = await ReadCollectionAsync(path, cancellationToken).ConfigureAwait(false);
                    if (!current.Span.SequenceEqual(expected.Span))
                    {
                        Fail(inventory, path, "Collection membership changed during the full inventory.");
                    }
                }
                XRegistryEndpointDescription final = await InspectAsync(cancellationToken).ConfigureAwait(false);
                var finalModel = new XRegistrySyncModel(final.Model, options);
                if (final.RegistryId != inventory.Description.RegistryId ||
                    finalModel.Signature != inventory.Model.Signature ||
                    final.SupportsAtomicMutations != inventory.Description.SupportsAtomicMutations ||
                    final.SupportsConditionalMutations != inventory.Description.SupportsConditionalMutations ||
                    final.SupportsPreparedMutations != inventory.Description.SupportsPreparedMutations)
                {
                    Fail(inventory, "/", "Registry identity, model, or mutation guarantees changed during inventory.");
                    return inventory;
                }
                XRegistrySyncObservation lastRoot = await ReadRootAsync(inventory, cancellationToken)
                    .ConfigureAwait(false);
                inventory.ScopeStable = true;
                if (lastRoot.Epoch != root.Epoch || lastRoot.Fingerprint != root.Fingerprint)
                {
                    Fail(inventory, "/", "The registry root changed during inventory.");
                }
                ValidateDefaults(inventory);
            }
            catch (Exception exception) when (IsInventoryFailure(exception, cancellationToken))
            {
                Fail(inventory, "/", exception.Message);
            }
            return inventory;
        }

        public async ValueTask<XRegistryEndpointDescription> InspectAsync(CancellationToken cancellationToken)
        {
            var deadline = new XRegistrySyncDeadline(timeProvider, options.RequestTimeout, cancellationToken);
            await using ConfiguredAsyncDisposable deadlineLifetime = deadline.ConfigureAwait(false);
            return await endpoint.InspectAsync(context, deadline.Token).AsTask().WaitAsync(deadline.Token)
                .ConfigureAwait(false);
        }

        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request,
            CancellationToken cancellationToken)
        {
            var deadline = new XRegistrySyncDeadline(timeProvider, options.RequestTimeout, cancellationToken);
            await using ConfiguredAsyncDisposable deadlineLifetime = deadline.ConfigureAwait(false);
            return await endpoint.ExecuteAsync(request with { Context = context }, deadline.Token).AsTask()
                .WaitAsync(deadline.Token).ConfigureAwait(false);
        }

        public async ValueTask<XRegistryOperationOutcome> OutcomeAsync(
            string operationId,
            CancellationToken cancellationToken)
        {
            if (endpoint is not IXRegistryOperationJournalEndpoint journal)
            {
                return new XRegistryOperationOutcome(XRegistryOperationState.Unknown, null);
            }
            var deadline = new XRegistrySyncDeadline(timeProvider, options.RequestTimeout, cancellationToken);
            await using ConfiguredAsyncDisposable deadlineLifetime = deadline.ConfigureAwait(false);
            return await journal.GetOperationOutcomeAsync(operationId, context, deadline.Token).AsTask()
                .WaitAsync(deadline.Token).ConfigureAwait(false);
        }

        public async ValueTask<XRegistrySyncObservation?> ReadEntityAsync(
            string path,
            XRegistrySyncModel model,
            CancellationToken cancellationToken)
        {
            XRegistrySyncDefinition definition = model.Resolve(path);
            var request = new XRegistryRequest(XRegistryAction.Read, path) { View = XRegistryView.Metadata };
            XRegistryResponse first = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (first.StatusCode == 404)
            {
                return null;
            }
            RequireRead(first, path);
            ByteString bytes = default;
            if (definition.HasDocument)
            {
                XRegistryResponse document = await ExecuteAsync(request with { View = XRegistryView.Default },
                    cancellationToken).ConfigureAwait(false);
                RequireRead(document, path);
                if (document.Document.IsNull || document.Document.Length > options.MaximumDocumentBytes)
                {
                    throw new InvalidDataException("Document content is absent or exceeds the synchronization quota.");
                }
                bytes = document.Document;
                XRegistryResponse last = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                RequireRead(last, path);
                XRegistrySyncObservation before = model.Observe(path, first.Metadata, default);
                XRegistrySyncObservation after = model.Observe(path, last.Metadata, default);
                if (before.Epoch != after.Epoch || before.Fingerprint != after.Fingerprint)
                {
                    throw new IOException(
                        "Metadata changed while reading the document; the observation is incomplete.");
                }
            }
            return model.Observe(path, first.Metadata, bytes);
        }

        public async ValueTask<ArrayOf<string>> ReadCollectionAsync(string path, CancellationToken cancellationToken)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var request = new XRegistryRequest(XRegistryAction.Read, path) { View = XRegistryView.Metadata };
            while (true)
            {
                if (++m_pages > options.MaximumPages)
                {
                    throw new InvalidDataException("The full inventory exceeds its page budget.");
                }
                XRegistryResponse response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                RequireRead(response, path);
                _ = new XRegistryProtocolCodec(options.MaximumInventoryBytes, options.MaximumJsonDepth)
                    .EncodeResponse(response);
                if (response.Metadata.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("A collection must be a keyed entity map.");
                }
                foreach (JsonProperty entry in response.Metadata.EnumerateObject())
                {
                    _ = XRegistrySyncModel.Child(path, entry.Name);
                    if (entry.Value.ValueKind != JsonValueKind.Object || !ids.Add(entry.Name))
                    {
                        throw new JsonException("A collection has a duplicate or invalid entity entry.");
                    }
                    if (ids.Count > options.MaximumEntities)
                    {
                        throw new InvalidDataException("A collection exceeds the full-inventory entity budget.");
                    }
                }
                XRegistryLink[] next = [.. response.Links.Span.ToArray().Where(link => link.Relation == "next")];
                if (next.Length == 0)
                {
                    return [.. ids];
                }
                if (next.Length != 1 || !visited.Add(next[0].Target))
                {
                    throw new JsonException("A collection has ambiguous or cyclic pagination.");
                }
                request = NextPage(path, next[0].Target);
            }
        }

        public static bool IsInventoryFailure(Exception exception, CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            return XRegistrySyncDeadline.IsEndpointFailure(exception) ||
                exception is ArgumentException or InvalidOperationException or KeyNotFoundException;
        }

        private async ValueTask<XRegistrySyncObservation> ReadRootAsync(
            XRegistrySyncInventory inventory,
            CancellationToken cancellationToken)
        {
            XRegistryResponse response = await ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/") { View = XRegistryView.Metadata },
                cancellationToken).ConfigureAwait(false);
            RequireRead(response, "/");
            if (XRegistrySyncJson.String(response.Metadata, "registryid") != inventory.Description!.RegistryId)
            {
                throw new JsonException("The registry root identity does not match endpoint inspection.");
            }
            return inventory.Model!.Observe("/", response.Metadata, default);
        }

        private async ValueTask<ArrayOf<string>> InventoryCollectionAsync(
            XRegistrySyncInventory inventory,
            string path,
            CancellationToken cancellationToken)
        {
            try
            {
                ArrayOf<string> result = await ReadCollectionAsync(path, cancellationToken).ConfigureAwait(false);
                inventory.Collections.Add(path, result);
                return result;
            }
            catch (Exception exception) when (IsInventoryFailure(exception, cancellationToken))
            {
                Fail(inventory, path, exception.Message);
                return [];
            }
        }

        private async ValueTask<bool> InventoryEntityAsync(
            XRegistrySyncInventory inventory,
            string path,
            CancellationToken cancellationToken)
        {
            try
            {
                XRegistrySyncObservation? observation = await ReadEntityAsync(path, inventory.Model!, cancellationToken)
                    .ConfigureAwait(false);
                if (observation is null)
                {
                    Fail(inventory, path, "An enumerated entity disappeared before it could be read.");
                    return false;
                }
                Add(inventory, observation);
                return true;
            }
            catch (Exception exception) when (IsInventoryFailure(exception, cancellationToken))
            {
                Fail(inventory, path, exception.Message);
                return false;
            }
        }

        private void Add(XRegistrySyncInventory inventory, XRegistrySyncObservation observation)
        {
            if (inventory.Entries.Count >= options.MaximumEntities)
            {
                throw new InvalidDataException("The full inventory exceeds its entity budget.");
            }
            m_bytes += XRegistrySyncJson.Encode(
                XRegistrySyncStateCodec.Observation(observation),
                options.MaximumInventoryBytes, options.MaximumJsonDepth).Length;
            if (m_bytes > options.MaximumInventoryBytes)
            {
                throw new InvalidDataException("The full inventory exceeds its byte budget.");
            }
            inventory.Entries.Add(observation.Path, observation);
        }

        private void Fail(XRegistrySyncInventory inventory, string path, string detail)
        {
            inventory.Complete = false;
            inventory.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Failure, $"{side}: {detail}"));
        }

        private static void RequireRead(XRegistryResponse response, string path)
        {
            if (response.StatusCode != 200)
            {
                throw new IOException(
                    $"Inventory read of '{path}' failed: {response.StatusCode} {response.Error?.Code}.");
            }
        }

        private static XRegistryRequest NextPage(string path, string target)
        {
            if (string.IsNullOrEmpty(target) ||
                target[0] is not ('/' or '?') ||
                target.StartsWith("//", StringComparison.Ordinal) ||
                target.AsSpan().IndexOf('#') >= 0)
            {
                throw new JsonException("Inventory page links must be registry-relative and stay in their collection.");
            }
            int question = target.AsSpan().IndexOf('?');
            string nextPath = target[0] == '?' ? path : question < 0 ? target : target[..question];
            if (XRegistryPath.Normalize(nextPath) != path || question < 0)
            {
                throw new JsonException("An inventory page link changed the collection or has no cursor.");
            }
            var parameters = new List<XRegistryParameter>();
            foreach (string part in target[(question + 1)..].Split('&'))
            {
                int equals = part.AsSpan().IndexOf('=');
                string name = Uri.UnescapeDataString(equals < 0 ? part : part[..equals]);
                string? value = equals < 0 ? null : Unescape(part, equals + 1);
                if (name is not ("cursor" or "page" or "limit" or "pagesize" or "offset"))
                {
                    throw new JsonException(
                        "This pagination parameter is not qualified for full synchronization scans.");
                }
                parameters.Add(new XRegistryParameter(name, value));
            }
            return new XRegistryRequest(XRegistryAction.Read, path)
            {
                View = XRegistryView.Metadata,
                Parameters = [.. parameters]
            };
        }

        private static string Unescape(string value, int start)
        {
#if NET9_0_OR_GREATER
            return Uri.UnescapeDataString(value.AsSpan(start));
#else
            return Uri.UnescapeDataString(value.Substring(start));
#endif
        }

        private static void ValidateDefaults(XRegistrySyncInventory inventory)
        {
            foreach (XRegistrySyncObservation meta in inventory.Entries.Values.Where(
                entry => entry.Kind == XRegistrySyncEntityKind.ResourceMeta))
            {
                if (!meta.Metadata.TryGetProperty("defaultversionid", out JsonElement id) ||
                    id.ValueKind != JsonValueKind.String ||
                    !inventory.Entries.ContainsKey(
                        XRegistrySyncModel.Child(XRegistrySyncModel.Parent(meta.Path) + "/versions", id.GetString()!)))
                {
                    inventory.Complete = false;
                    inventory.Records.Add(new XRegistrySyncRecord(meta.Path, XRegistrySyncRecordKind.Failure,
                        "The default Version is missing from the full inventory."));
                }
            }
        }

        private int m_pages;
        private long m_bytes;
    }
}
