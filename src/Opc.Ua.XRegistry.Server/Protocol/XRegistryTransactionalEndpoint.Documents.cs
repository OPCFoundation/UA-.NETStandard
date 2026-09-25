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
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private async ValueTask StageDocumentsAsync(JsonObject snapshot, CancellationToken ct)
        {
            if (m_options.DocumentStore is not { } store)
            {
                return;
            }
            foreach ((_, JsonNode? node) in XRegistryModelRules.Object(snapshot["entries"]))
            {
                JsonObject entry = XRegistryModelRules.Object(node);
                if (entry["document"] is not JsonNode value || XRegistryModelRules.Text(value).Length == 0)
                {
                    continue;
                }
                byte[] bytes = Convert.FromBase64String(XRegistryModelRules.Text(value));
                using var stream = new MemoryStream(bytes, writable: false);
                XRegistryBlobReference reference = await store.StoreAsync(stream, m_options.MaxDocumentBytes, ct)
                    .ConfigureAwait(false);
                entry["blob"] = new JsonObject { ["sha256"] = reference.Sha256, ["length"] = reference.Length };
                entry.Remove("document");
            }
        }

        private async ValueTask<XRegistryResponse> ReadAsync(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target, CancellationToken ct)
        {
            ApplyContinuation(transaction, target);
            if (transaction.ResultVersionPath is { } resultPath && target.Kind == XRegistryEntityKind.Resource)
            {
                var result = new Transaction(transaction.Snapshot, transaction.Snapshot,
                    transaction.Request.AtPath(resultPath), m_time.GetUtcNow().UtcDateTime)
                {
                    CanWrite = transaction.CanWrite
                };
                return await ReadAsync(result, model, model.Resolve(resultPath), ct).ConfigureAwait(false);
            }
            if (HasFlag(transaction.Request, "doc") &&
                target.Kind is XRegistryEntityKind.Version or XRegistryEntityKind.Versions)
            {
                string resource = target.Kind == XRegistryEntityKind.Versions
                    ? Parent(target.Path) : Parent(Parent(target.Path));
                if (transaction.Entries[resource + "/meta"]?["metadata"]?["xref"] is not null)
                {
                    throw new XRegistryRejectionException("cannot_doc_xref",
                        "Referenced Versions do not exist in the source Resource's document view.");
                }
            }
            transaction = ReferenceView(transaction, model);
            var selected = new List<string>();
            foreach ((string path, JsonNode? value) in transaction.Entries)
            {
                if (value?["blob"] is null)
                {
                    continue;
                }
                XRegistryTarget version = model.Resolve(path);
                if (version.Kind != XRegistryEntityKind.Version)
                {
                    throw new InvalidDataException("A document reference must belong to a Version.");
                }
                if (NeedsDocument(transaction, target, version))
                {
                    selected.Add(path);
                }
            }
            if (selected.Count == 0)
            {
                return DocumentView(transaction, model, target, Read(transaction, model, target));
            }
            var snapshot = (JsonObject)transaction.Snapshot.DeepClone();
            Transaction view = CreateReadView(transaction, snapshot);
            long total = 0;
            foreach (string path in selected)
            {
                JsonObject entry = GetEntry(view, path);
                XRegistryBlobReference reference = BlobReference(entry["blob"]);
                total += reference.Length;
                if (total > m_options.MaxStateBytes)
                {
                    throw new XRegistryRejectionException("too_large",
                        "Requested inline documents exceed the aggregate read budget.", 413);
                }
                entry["document"] = Base64(await ReadDocumentAsync(entry, ct).ConfigureAwait(false));
                entry.Remove("blob");
            }
            return DocumentView(view, model, target, Read(view, model, target));
        }

        private Transaction CreateReadView(Transaction transaction, JsonObject snapshot)
        {
            var view = new Transaction(snapshot, snapshot, transaction.Request, m_time.GetUtcNow().UtcDateTime)
            {
                CanWrite = transaction.CanWrite,
                OwnerCollectionPost = transaction.OwnerCollectionPost,
                PageOffset = transaction.PageOffset,
                PageLimit = transaction.PageLimit,
                CursorExpires = transaction.CursorExpires
            };
            view.IgnoredPaths.UnionWith(transaction.IgnoredPaths);
            return view;
        }

        private static bool NeedsDocument(Transaction transaction, XRegistryTarget target, XRegistryTarget version)
        {
            XRegistryRequest request = transaction.Request;
            if (HasFlag(request, "doc") &&
                transaction.Entries[Parent(Parent(version.Path)) + "/meta"]?["metadata"]?["xref"] is not null)
            {
                return false;
            }
            if (request.View == XRegistryView.Default && !HasFlag(request, "doc"))
            {
                if ((target.Kind == XRegistryEntityKind.Version && target.Path == version.Path) ||
                    (target.Kind == XRegistryEntityKind.Resource &&
                        Metadata(GetEntry(transaction, target.Path + "/meta"))["defaultversionid"] is not null &&
                        DefaultPath(transaction, target.Path) == version.Path))
                {
                    return true;
                }
            }
            string scope =
                target.Singular == "export" && target.Kind == XRegistryEntityKind.Special ? "/" : target.Path;
            if (scope != "/" &&
                version.Path != scope &&
                !version.Path.StartsWith(scope.TrimEnd('/') + "/", StringComparison.Ordinal))
            {
                return false;
            }
            if ((scope == "/" && target.Kind == XRegistryEntityKind.Special && target.Singular == "export") ||
                Inline(transaction, version, version.Singular))
            {
                return true;
            }
            XRegistryTarget resource = version with
            {
                Kind = XRegistryEntityKind.Resource,
                Path = Parent(Parent(version.Path))
            };
            return Inline(transaction, resource, version.Singular) &&
                DefaultPath(transaction, resource.Path) == version.Path;
        }

        private async ValueTask<ByteString> ReadDocumentAsync(JsonObject entry, CancellationToken ct)
        {
            if (entry["document"] is JsonNode value)
            {
                return ByteString.From(Convert.FromBase64String(XRegistryModelRules.Text(value)));
            }
            if (entry["blob"] is not JsonNode blob)
            {
                return ByteString.Empty;
            }
            XRegistryBlobReference reference = BlobReference(blob);
            IXRegistryDocumentStore store = m_options.DocumentStore
                ?? throw new InvalidDataException("The persisted generation requires its configured document store.");
            if (reference.Length > m_options.MaxDocumentBytes)
            {
                throw new InvalidDataException("A persisted document exceeds its configured bound.");
            }
            using Stream stream = await store.OpenReadAsync(reference, ct).ConfigureAwait(false);
            byte[] bytes = new byte[(int)reference.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
#if NETSTANDARD2_1_OR_GREATER || NET
                int read = await stream.ReadAsync(bytes.AsMemory(offset), ct).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    throw new InvalidDataException("A committed document was truncated.");
                }
                offset += read;
            }
            var extra = new byte[1];
#if NETSTANDARD2_1_OR_GREATER || NET
            int remaining = await stream.ReadAsync(extra.AsMemory(), ct).ConfigureAwait(false);
#else
            int remaining = await stream.ReadAsync(extra, 0, 1, ct).ConfigureAwait(false);
#endif
            if (remaining != 0)
            {
                throw new InvalidDataException("The document store exceeded its declared content length.");
            }
            return new ByteString(bytes);
        }

        private static XRegistryBlobReference BlobReference(JsonNode? node)
        {
            JsonObject value = XRegistryModelRules.Object(node);
            return new XRegistryBlobReference(XRegistryModelRules.Text(value["sha256"]),
                value["length"]?.GetValue<long>() ?? throw new InvalidDataException("A blob length is missing."));
        }
    }
}
