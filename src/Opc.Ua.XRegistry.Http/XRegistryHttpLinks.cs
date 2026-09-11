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
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    internal static class XRegistryHttpLinks
    {
        public static JsonElement Translate(
            JsonElement metadata,
            XRegistryRequest request,
            JsonElement model,
            XRegistryHttpAddress address,
            XRegistryHttpBody body,
            Uri? upstreamRequest = null,
            bool protocolLinks = false)
        {
            if (metadata.ValueKind != JsonValueKind.Object ||
                (XRegistryHttpShape.IsWellKnown(request.Path) && request.Path is not ("/" or "/export")) ||
                (upstreamRequest is not null && model.ValueKind == JsonValueKind.Undefined))
            {
                return metadata;
            }
            var root = (JsonObject)JsonNode.Parse(metadata.GetRawText(),
                documentOptions: new JsonDocumentOptions { MaxDepth = 1024 })!;
            string path = request.Path == "/export" ? "/" : request.Path;
            var shape = XRegistryHttpShape.Resolve(model, path);
            Visit(root, path, shape, model, address, upstreamRequest, shape.IsDocumentView(request), protocolLinks);
            return body.Parse(body.Encode(root));
        }

        private static void Visit(
            JsonObject entity,
            string path,
            XRegistryHttpShape shape,
            JsonElement model,
            XRegistryHttpAddress address,
            Uri? upstreamRequest,
            bool documentHeaders = false,
            bool protocolLinks = false)
        {
            if (shape.IsCollection)
            {
                foreach (System.Collections.Generic.KeyValuePair<string, JsonNode?> entry in entity)
                {
                    if (entry.Value is JsonObject child)
                    {
                        string childPath = XRegistryPath.FromSegments([.. XRegistryPath.GetSegments(path), entry.Key]);
                        Visit(child, childPath, XRegistryHttpShape.Resolve(model, childPath),
                            model, address, upstreamRequest, protocolLinks: protocolLinks);
                    }
                }
                return;
            }
            if (shape.Kind == XRegistryHttpEntityKind.Other)
            {
                return;
            }
            TranslateLink(entity, "self", address, upstreamRequest,
                shape.IsResource && shape.HasDocument && !documentHeaders, protocolLinks);
            if (shape.Kind is XRegistryHttpEntityKind.Resource or XRegistryHttpEntityKind.Version or
                XRegistryHttpEntityKind.Meta)
            {
                TranslateLink(entity, "metaurl", address, upstreamRequest, protocolLinks: protocolLinks);
                TranslateLink(entity, "versionsurl", address, upstreamRequest, protocolLinks: protocolLinks);
                TranslateLink(entity, "defaultversionurl", address, upstreamRequest, protocolLinks: protocolLinks);
            }
            if (shape.Kind == XRegistryHttpEntityKind.Resource)
            {
                VisitChild(entity, "meta", path + "/meta", model, address, upstreamRequest, protocolLinks);
                VisitChild(entity, "versions", path + "/versions", model, address, upstreamRequest, protocolLinks);
            }
            JsonElement collections = default;
            if (shape.Kind == XRegistryHttpEntityKind.Registry && model.ValueKind == JsonValueKind.Object)
            {
                model.TryGetProperty("groups", out collections);
            }
            else if (shape.Kind == XRegistryHttpEntityKind.Group)
            {
                shape.Definition.TryGetProperty("resources", out collections);
            }
            if (collections.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty collection in collections.EnumerateObject())
                {
                    TranslateLink(entity, collection.Name + "url", address, upstreamRequest,
                        protocolLinks: protocolLinks);
                    string childPath = XRegistryPath.FromSegments(
                        [.. XRegistryPath.GetSegments(path), collection.Name]);
                    VisitChild(entity, collection.Name, childPath, model, address, upstreamRequest, protocolLinks);
                }
            }
        }

        private static void VisitChild(
            JsonObject entity,
            string name,
            string path,
            JsonElement model,
            XRegistryHttpAddress address,
            Uri? upstreamRequest,
            bool protocolLinks)
        {
            if (entity.TryGetPropertyValue(name, out JsonNode? node) && node is JsonObject child)
            {
                Visit(child, path, XRegistryHttpShape.Resolve(model, path), model, address, upstreamRequest,
                    protocolLinks: protocolLinks);
            }
        }

        private static void TranslateLink(
            JsonObject entity,
            string name,
            XRegistryHttpAddress address,
            Uri? upstreamRequest,
            bool details = false,
            bool protocolLinks = false)
        {
            if (!entity.TryGetPropertyValue(name, out JsonNode? node) || node is null)
            {
                return;
            }
            if (node is not JsonValue value || !value.TryGetValue(out string? target))
            {
                throw new JsonException("A registry navigation URL must be a string.");
            }
            if (upstreamRequest is not null)
            {
                if (!protocolLinks || (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && !uri.IsFile))
                {
                    target = address.ReadLink(target!, upstreamRequest);
                }
                if (name == "self")
                {
                    (target, _) = XRegistryHttpAddress.SplitDetails(target);
                }
            }
            else
            {
                target = address.WriteLink(target!, details);
            }
            entity[name] = target;
        }
    }
}
