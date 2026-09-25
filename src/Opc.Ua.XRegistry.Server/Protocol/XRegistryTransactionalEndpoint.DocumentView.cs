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
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private XRegistryResponse DocumentView(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target, XRegistryResponse response)
        {
            if (!HasFlag(transaction.Request, "doc") ||
                !response.IsSuccess ||
                response.Metadata.ValueKind != JsonValueKind.Object ||
                (target.Kind == XRegistryEntityKind.Special && target.Singular != "export"))
            {
                return response;
            }
            JsonObject root = XRegistryModelRules.Object(JsonNode.Parse(response.Metadata.GetRawText()));
            var pointers = new Dictionary<string, string>(StringComparer.Ordinal);
            var links = new List<(JsonObject Entity, string Name)>();
            string path = target.Singular == "export" && target.Kind == XRegistryEntityKind.Special ? "/" : target.Path;
            bool collectionsOnly = transaction.OwnerCollectionPost || HasFlag(transaction.Request, "collections");
            Visit(root, path, string.Empty, collectionsOnly);
            foreach ((JsonObject entity, string name) in links)
            {
                if (entity[name] is JsonValue value &&
                    value.TryGetValue(out string? address) &&
                    address is not null &&
                    pointers.TryGetValue(address, out string? pointer))
                {
                    entity[name] = pointer;
                }
            }
            return response with { Metadata = Element(root), Document = default };

            void Visit(JsonObject value, string entityPath, string pointer, bool ownerCollections = false)
            {
                XRegistryTarget entity = model.Resolve(entityPath);
                if (entity.IsCollection)
                {
                    pointers[Url(entityPath)] = "#" + (pointer.Length == 0 ? "/" : pointer);
                    foreach ((string id, JsonNode? item) in value)
                    {
                        Visit(XRegistryModelRules.Object(item), Child(entityPath, id), PointerChild(pointer, id));
                    }
                    return;
                }
                if (!ownerCollections)
                {
                    pointers[Url(entityPath)] = "#" + (pointer.Length == 0 ? "/" : pointer);
                    links.Add((value, "self"));
                }
                if (entity.Kind is XRegistryEntityKind.Registry or XRegistryEntityKind.Group)
                {
                    string name = entity.Kind == XRegistryEntityKind.Registry ? "groups" : "resources";
                    foreach ((string collection, _) in XRegistryModelRules.Object(entity.Definition[name]))
                    {
                        links.Add((value, collection + "url"));
                        if (value[collection] is JsonObject contents)
                        {
                            Visit(contents, Child(entityPath, collection), PointerChild(pointer, collection));
                        }
                    }
                }
                else if (entity.Kind == XRegistryEntityKind.Resource)
                {
                    JsonObject meta = Metadata(GetEntry(transaction, entityPath + "/meta"));
                    bool reference = meta["xref"] is not null;
                    foreach (string name in value.Select(pair => pair.Key).ToArray())
                    {
                        if (name != entity.Singular + "id" &&
                            name is not ("self" or "xid" or "meta" or "metaurl") &&
                            (reference || name is not ("versions" or "versionsurl" or "versionscount")))
                        {
                            value.Remove(name);
                        }
                    }
                    links.Add((value, "metaurl"));
                    links.Add((value, "versionsurl"));
                    if (value["meta"] is JsonObject metadata)
                    {
                        Visit(metadata, entityPath + "/meta", PointerChild(pointer, "meta"));
                    }
                    if (value["versions"] is JsonObject versions)
                    {
                        Visit(versions, entityPath + "/versions", PointerChild(pointer, "versions"));
                    }
                }
                else if (entity.Kind == XRegistryEntityKind.Meta)
                {
                    if (value["xref"] is not null)
                    {
                        foreach (string name in value.Select(pair => pair.Key).ToArray())
                        {
                            if (name != entity.Singular + "id" && name is not ("self" or "xid" or "xref"))
                            {
                                value.Remove(name);
                            }
                        }
                    }
                    else
                    {
                        links.Add((value, "defaultversionurl"));
                    }
                }
                else if (entity.Kind == XRegistryEntityKind.Version)
                {
                    foreach (string name in s_validationAttributes)
                    {
                        value.Remove(name);
                    }
                }
            }
        }

        private static string PointerChild(string parent, string name)
        {
            string escaped =
                name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            return parent + "/" + Uri.EscapeDataString(escaped);
        }
    }
}
