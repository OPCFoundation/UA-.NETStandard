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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private XRegistryResponse Read(Transaction transaction, XRegistryModelRules model, XRegistryTarget target)
        {
            PrepareQuery(transaction, model, target);
            if (!target.IsCollection &&
                transaction.QueryPaths is not null &&
                !transaction.QueryPaths.Contains(target.Kind == XRegistryEntityKind.Special ? "/" : target.Path))
            {
                throw new XRegistryRejectionException(
                    "not_found", "The requested entity does not match the filter.", 404);
            }
            if (HasFlag(transaction.Request, "sort") && !target.IsCollection)
            {
                throw new XRegistryRejectionException("sort_noncollection", "Sorting requires a collection.");
            }
            if (transaction.OwnerCollectionPost)
            {
                var processed = new JsonObject();
                foreach ((string name, JsonNode? items) in Body(transaction.Request.Metadata)!)
                {
                    string collectionPath = Child(target.Path, name);
                    JsonObject requested = XRegistryModelRules.Object(items);
                    JsonObject collection = Collection(transaction, model, collectionPath, false);
                    foreach (string id in collection.Select(pair => pair.Key).ToArray())
                    {
                        if (!requested.ContainsKey(id) || transaction.IgnoredPaths.Contains(Child(collectionPath, id)))
                        {
                            collection.Remove(id);
                        }
                    }
                    processed[name] = collection;
                }
                return new XRegistryResponse(200) { Metadata = Element(processed) };
            }
            JsonObject result;
            if (target.Kind == XRegistryEntityKind.Special)
            {
                result = target.Singular switch
                {
                    "model" => model.CompleteModel(),
                    "modelsource" => (JsonObject)transaction.Snapshot["modelsource"]!.DeepClone(),
                    "capabilities" or "capabilitiesoffered" => ValidationCapabilities(transaction.CanWrite),
                    ".xregistry" => new JsonObject
                    {
                        ["registries"] = new JsonArray(
                        [
                            .. m_options.DiscoveryRegistries.ToList()
                                .Select(uri => (JsonNode?)JsonValue.Create(uri.AbsoluteUri))
                        ])
                    },
                    "export" => Represent(transaction, model, model.Resolve("/"), true),
                    _ => throw new XRegistryRejectionException("not_found", "The registry aspect does not exist.", 404)
                };
                return new XRegistryResponse(200) { Metadata = Element(result) };
            }
            if (target.IsCollection)
            {
                if (target.Kind != XRegistryEntityKind.Groups)
                {
                    _ = GetEntry(transaction,
                        target.Kind == XRegistryEntityKind.Versions
                            ? Parent(target.Path) + "/meta"
                            : Parent(target.Path));
                }
                result = Collection(transaction, model, target.Path, false);
                if (transaction.Request.IsMutation && Body(transaction.Request.Metadata) is JsonObject requested)
                {
                    foreach (string id in result.Select(pair => pair.Key).ToArray())
                    {
                        if (!requested.ContainsKey(id) || transaction.IgnoredPaths.Contains(Child(target.Path, id)))
                        {
                            result.Remove(id);
                        }
                    }
                }
                result = SortCollection(result, transaction, model, target);
                return Page(transaction, target, result);
            }
            result = Represent(transaction, model, target, false);
            if (target.Kind is XRegistryEntityKind.Resource or XRegistryEntityKind.Version &&
                XRegistryModelRules.Boolean(target.Definition["hasdocument"], true) &&
                !HasFlag(transaction.Request, "doc") &&
                transaction.Request.View == XRegistryView.Default)
            {
                if (target.Kind == XRegistryEntityKind.Resource &&
                    Metadata(GetEntry(transaction, target.Path + "/meta"))["defaultversionid"] is null)
                {
                    throw new XRegistryRejectionException("not_found", "The referenced document is unavailable.", 404);
                }
                string versionPath = target.Kind == XRegistryEntityKind.Version
                    ? target.Path
                    : DefaultPath(transaction, target.Path);
                JsonObject entry = GetEntry(transaction, versionPath);
                if (Metadata(entry)[target.Singular + "url"] is JsonNode reference)
                {
                    return new XRegistryResponse(transaction.Request.IsMutation ? 200 : 303)
                    {
                        Metadata = Element(result),
                        Document = transaction.Request.IsMutation ? ByteString.Empty : default,
                        ContentType =
                            Metadata(entry)["contenttype"] is JsonNode media ? XRegistryModelRules.Text(media) : null,
                        Location = XRegistryModelRules.Text(reference),
                        ContentLocation = Url(versionPath),
                        VersionIncarnation =
                            target.Kind == XRegistryEntityKind.Version ? GetVersionIncarnation(entry) : null
                    };
                }
                ByteString bytes = entry["document"] is JsonNode document
                    ? ByteString.From(Convert.FromBase64String(XRegistryModelRules.Text(document)))
                    : ByteString.Empty;
                return new XRegistryResponse(200)
                {
                    Metadata = Element(result),
                    Document = bytes,
                    ContentType = Metadata(entry)["contenttype"] is JsonNode type
                        ? XRegistryModelRules.Text(type)
                        : null,
                    ContentLocation = Url(versionPath),
                    VersionIncarnation = target.Kind == XRegistryEntityKind.Version
                        ? GetVersionIncarnation(entry) : null
                };
            }
            if (HasFlag(transaction.Request, "collections") &&
                target.Kind is XRegistryEntityKind.Registry or XRegistryEntityKind.Group)
            {
                JsonObject collections = XRegistryModelRules.Object(target.Definition[
                    target.Kind == XRegistryEntityKind.Registry ? "groups" : "resources"]);
                foreach (string name in result.Select(pair => pair.Key).ToArray())
                {
                    if (!collections.ContainsKey(name))
                    {
                        result.Remove(name);
                    }
                }
            }
            return new XRegistryResponse(200)
            {
                Metadata = Element(result),
                VersionIncarnation = target.Kind == XRegistryEntityKind.Version
                    ? GetVersionIncarnation(GetEntry(transaction, target.Path)) : null
            };
        }

        private JsonObject Represent(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target, bool export,
            XRegistryTarget? inlineScope = null)
        {
            XRegistryTarget scope = inlineScope ?? target;
            string path = StoragePath(target);
            JsonObject entry = GetEntry(transaction, path);
            JsonObject result;
            if (target.Kind == XRegistryEntityKind.Resource)
            {
                if (Metadata(entry)["defaultversionid"] is null && Metadata(entry)["xref"] is not null)
                {
                    result = new JsonObject
                    {
                        [target.Singular + "id"] = Identity(target.Path),
                        ["self"] = Url(target.Path),
                        ["xid"] = target.Path,
                        ["metaurl"] = Url(target.Path + "/meta")
                    };
                    AddShortLink(transaction, target.Path, result);
                    if (export || Inline(transaction, scope, "meta"))
                    {
                        result["meta"] = Represent(transaction, model, model.Resolve(target.Path + "/meta"), export);
                    }
                    return result;
                }
                string versionPath = DefaultPath(transaction, target.Path);
                result = Represent(transaction, model, model.Resolve(versionPath), export, target);
                result["metaurl"] = Url(target.Path + "/meta");
                result["versionsurl"] = Url(target.Path + "/versions");
                result["versionscount"] = Children(transaction, target.Path + "/versions").Count;
                if (export || Inline(transaction, scope, "meta"))
                {
                    result["meta"] = Represent(transaction, model, model.Resolve(target.Path + "/meta"), export);
                }
                if (export || Inline(transaction, scope, "versions"))
                {
                    result["versions"] = Collection(transaction, model, target.Path + "/versions", export);
                }
            }
            else
            {
                result = (JsonObject)Metadata(entry).DeepClone();
            }
            result["self"] = Url(target.Path);
            result["xid"] = target.Path;
            AddShortLink(transaction, target.Path, result);
            if (target.Kind == XRegistryEntityKind.Registry)
            {
                result["specversion"] = "1.0-rc4";
                if (export || Inline(transaction, scope, "model"))
                {
                    result["model"] = model.CompleteModel();
                }
                if (export || Inline(transaction, scope, "modelsource"))
                {
                    result["modelsource"] = transaction.Snapshot["modelsource"]!.DeepClone();
                }
                if (export || Inline(transaction, scope, "capabilities"))
                {
                    result["capabilities"] = ValidationCapabilities(transaction.CanWrite);
                }
            }
            if (target.Kind is XRegistryEntityKind.Registry or XRegistryEntityKind.Group)
            {
                string collectionKey = target.Kind == XRegistryEntityKind.Registry ? "groups" : "resources";
                foreach ((string name, _) in XRegistryModelRules.Object(target.Definition[collectionKey]))
                {
                    string collectionPath = Child(target.Path, name);
                    result[name + "url"] = Url(collectionPath);
                    int count = Children(transaction, collectionPath).Count;
                    result[name + "count"] = count;
                    if (transaction.QueryPaths is not null && count == 0)
                    {
                        result[name + "url"] = Url(collectionPath) + "?filter=excludeall";
                    }
                    if (export || Inline(transaction, scope, name) || HasFlag(transaction.Request, "collections"))
                    {
                        result[name] = Collection(transaction, model, collectionPath, export);
                    }
                }
            }
            if (target.Kind == XRegistryEntityKind.Meta && result["defaultversionid"] is JsonNode selected)
            {
                result["defaultversionurl"] = Url(Child(Parent(target.Path) + "/versions",
                    XRegistryModelRules.Text(selected)));
                result["readonly"] = result["xref"] is not null || XRegistryModelRules.Boolean(result["readonly"]);
            }
            if (target.Kind == XRegistryEntityKind.Version)
            {
                JsonObject meta = Metadata(GetEntry(transaction, Parent(Parent(target.Path)) + "/meta"));
                result["isdefault"] = XRegistryModelRules.Text(meta["defaultversionid"]) == Identity(target.Path);
                if (XRegistryModelRules.Boolean(target.Definition["hasdocument"], true) &&
                    (export || Inline(transaction, scope, target.Singular)))
                {
                    InlineDocument(result, entry, target, HasFlag(transaction.Request, "binary"));
                }
            }
            return result;
        }

        private JsonObject Collection(
            Transaction transaction, XRegistryModelRules model, string path, bool export)
        {
            var result = new JsonObject();
            foreach (string child in Children(transaction, path))
            {
                result[Identity(child)] = Represent(transaction, model, model.Resolve(child), export);
            }
            return result;
        }

        private static void InlineDocument(JsonObject result, JsonObject entry, XRegistryTarget target, bool binary)
        {
            if (Metadata(entry)[target.Singular + "url"] is not null)
            {
                return;
            }
            string encoded = entry["document"] is JsonNode stored ? XRegistryModelRules.Text(stored) : string.Empty;
            string
                ? contentType = Metadata(entry)["contenttype"] is JsonNode type ? XRegistryModelRules.Text(type) : null;
            string representation =
                binary ? "binary" : XRegistryModelRules.DocumentRepresentation(target.Definition, contentType);
            if (representation != "binary")
            {
                try
                {
                    string text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
                    result[target.Singular] = representation == "json" ? JsonNode.Parse(text) : JsonValue.Create(text);
                    return;
                }
                catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
                {
                    // Preserve exact bytes rather than inserting malformed JSON or replacing invalid UTF-8.
                }
            }
            result[target.Singular + "base64"] = encoded;
        }

        private static List<string> Children(Transaction transaction, string collection)
        {
            var children = new SortedSet<string>(StringComparer.Ordinal);
            string prefix = collection.TrimEnd('/') + "/";
            foreach ((string path, _) in transaction.Entries)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                int separator = path.AsSpan(prefix.Length).IndexOf('/');
                children.Add(separator < 0 ? path : path[..(prefix.Length + separator)]);
            }
            return transaction.QueryPaths is null ? [.. children] :
                [.. children.Where(transaction.QueryPaths.Contains)];
        }

        private static bool HasFlag(XRegistryRequest request, string name)
        {
            for (int index = 0; index < request.Parameters.Count; index++)
            {
                if (request.Parameters[index].Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private static string DefaultPath(Transaction transaction, string resource)
        {
            JsonObject meta = Metadata(GetEntry(transaction, resource + "/meta"));
            return Child(resource + "/versions", XRegistryModelRules.Text(meta["defaultversionid"]));
        }
    }
}
