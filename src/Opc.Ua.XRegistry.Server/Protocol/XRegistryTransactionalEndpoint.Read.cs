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
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private XRegistryResponse Read(Transaction transaction, XRegistryModelRules model, XRegistryTarget target)
        {
            JsonObject result;
            if (target.Kind == XRegistryEntityKind.Special)
            {
                result = target.Singular switch
                {
                    "model" => (JsonObject)model.Model.DeepClone(),
                    "modelsource" => (JsonObject)transaction.Snapshot["modelsource"]!.DeepClone(),
                    "capabilities" or "capabilitiesoffered" => Capabilities(),
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
                        if (!requested.ContainsKey(id))
                        {
                            result.Remove(id);
                        }
                    }
                }
            }
            else
            {
                result = Represent(transaction, model, target, false);
            }
            if (target.Kind is XRegistryEntityKind.Resource or XRegistryEntityKind.Version &&
                XRegistryModelRules.Boolean(target.Definition["hasdocument"], true) &&
                transaction.Request.View == XRegistryView.Default)
            {
                string versionPath = target.Kind == XRegistryEntityKind.Version
                    ? target.Path
                    : DefaultPath(transaction, target.Path);
                JsonObject entry = GetEntry(transaction, versionPath);
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
                    ContentLocation = Url(versionPath)
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
            return new XRegistryResponse(200) { Metadata = Element(result) };
        }

        private JsonObject Represent(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target, bool export)
        {
            string path = StoragePath(target);
            JsonObject entry = GetEntry(transaction, path);
            JsonObject result;
            if (target.Kind == XRegistryEntityKind.Resource)
            {
                string versionPath = DefaultPath(transaction, target.Path);
                result = Represent(transaction, model, model.Resolve(versionPath), export);
                result["metaurl"] = Url(target.Path + "/meta");
                result["versionsurl"] = Url(target.Path + "/versions");
                result["versionscount"] = Children(transaction, target.Path + "/versions").Count;
                if (export || Inline(transaction, "meta"))
                {
                    result["meta"] = Represent(transaction, model, model.Resolve(target.Path + "/meta"), export);
                }
                if (export || Inline(transaction, "versions"))
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
            if (target.Kind == XRegistryEntityKind.Registry)
            {
                result["specversion"] = "1.0-rc4";
                if (export || Inline(transaction, "model"))
                {
                    result["model"] = model.Model.DeepClone();
                }
                if (export || Inline(transaction, "modelsource"))
                {
                    result["modelsource"] = transaction.Snapshot["modelsource"]!.DeepClone();
                }
                if (export || Inline(transaction, "capabilities"))
                {
                    result["capabilities"] = Capabilities();
                }
            }
            if (target.Kind is XRegistryEntityKind.Registry or XRegistryEntityKind.Group)
            {
                string collectionKey = target.Kind == XRegistryEntityKind.Registry ? "groups" : "resources";
                foreach ((string name, _) in XRegistryModelRules.Object(target.Definition[collectionKey]))
                {
                    string collectionPath = Child(target.Path, name);
                    result[name + "url"] = Url(collectionPath);
                    result[name + "count"] = Children(transaction, collectionPath).Count;
                    if (export || Inline(transaction, name) || HasFlag(transaction.Request, "collections"))
                    {
                        result[name] = Collection(transaction, model, collectionPath, export);
                    }
                }
            }
            if (target.Kind == XRegistryEntityKind.Meta)
            {
                result["defaultversionurl"] = Url(Child(Parent(target.Path) + "/versions",
                    XRegistryModelRules.Text(result["defaultversionid"])));
                result["readonly"] = false;
            }
            if (target.Kind == XRegistryEntityKind.Version)
            {
                JsonObject meta = Metadata(GetEntry(transaction, Parent(Parent(target.Path)) + "/meta"));
                result["isdefault"] = XRegistryModelRules.Text(meta["defaultversionid"]) == Identity(target.Path);
                if (XRegistryModelRules.Boolean(target.Definition["hasdocument"], true) &&
                    (export || HasFlag(transaction.Request, "doc") || Inline(transaction, target.Singular)))
                {
                    result[target.Singular + "base64"] =
                        entry["document"]?.DeepClone() ?? JsonValue.Create(string.Empty);
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
            return [.. children];
        }

        private static bool Inline(Transaction transaction, string attribute)
        {
            for (int index = 0; index < transaction.Request.Parameters.Count; index++)
            {
                XRegistryParameter parameter = transaction.Request.Parameters[index];
                if (parameter.Name != "inline")
                {
                    continue;
                }
                foreach (string selection in (parameter.Value ?? string.Empty).Split(','))
                {
                    if (selection == "*" || selection.Split('.').Contains(attribute, StringComparer.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
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
