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

using System.Text.Json;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    internal enum XRegistryHttpEntityKind
    {
        Other,
        Registry,
        Groups,
        Group,
        Resources,
        Resource,
        Meta,
        Versions,
        Version
    }

    internal sealed record XRegistryHttpShape
    {
        public XRegistryHttpEntityKind Kind { get; init; }

        public JsonElement Definition { get; init; }

        public string? Singular { get; init; }

        public bool HasDocument { get; init; }

        public bool IsResource => Kind is XRegistryHttpEntityKind.Resource or XRegistryHttpEntityKind.Version;

        public bool IsCollection => Kind is
            XRegistryHttpEntityKind.Groups or XRegistryHttpEntityKind.Resources or XRegistryHttpEntityKind.Versions;

        public bool IsDocumentView(XRegistryRequest request)
        {
            return IsResource &&
                HasDocument &&
                request.View == XRegistryView.Default &&
                request.Action is not (XRegistryAction.Delete or XRegistryAction.Describe);
        }

        public string AttributeType(string name, bool mapItem = false)
        {
            if (name == "labels")
            {
                return mapItem ? "string" : "map";
            }
            if (name is "epoch" or "versionscount")
            {
                return "uinteger";
            }
            if (name is "isdefault" or "defaultversionsticky" or "formatvalidated" or "compatibilityvalidated")
            {
                return "boolean";
            }
            if (Definition.ValueKind == JsonValueKind.Object)
            {
                string first = Kind == XRegistryHttpEntityKind.Meta ? "metaattributes" : "attributes";
                if (TryAttributeType(Definition, first, name, mapItem, out string? type) ||
                    (Kind == XRegistryHttpEntityKind.Resource &&
                        TryAttributeType(Definition, "resourceattributes", name, mapItem, out type)))
                {
                    return type!;
                }
            }
            return "string";
        }

        public static bool IsWellKnown(string path)
        {
            return path is "/" or "/model" or "/modelsource" or "/capabilities" or
                "/capabilitiesoffered" or "/export" or "/.xregistry";
        }

        public static XRegistryHttpShape Resolve(JsonElement model, string path)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(path);
            if (segments.Count == 0)
            {
                return new XRegistryHttpShape { Kind = XRegistryHttpEntityKind.Registry, Definition = model };
            }
            if (IsWellKnown(path))
            {
                return new XRegistryHttpShape();
            }
            if (model.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("An effective registry model must be a JSON object.");
            }
            if (!model.TryGetProperty("groups", out JsonElement groups) ||
                groups.ValueKind != JsonValueKind.Object ||
                !groups.TryGetProperty(segments[0], out JsonElement group))
            {
                return new XRegistryHttpShape();
            }
            if (group.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("A group model must be a JSON object.");
            }
            if (segments.Count <= 2)
            {
                return new XRegistryHttpShape
                {
                    Kind = segments.Count == 1 ? XRegistryHttpEntityKind.Groups : XRegistryHttpEntityKind.Group,
                    Definition = group
                };
            }
            if (!group.TryGetProperty("resources", out JsonElement resources) ||
                resources.ValueKind != JsonValueKind.Object ||
                !resources.TryGetProperty(segments[2], out JsonElement resource))
            {
                return new XRegistryHttpShape();
            }
            if (resource.ValueKind != JsonValueKind.Object ||
                !resource.TryGetProperty("singular", out JsonElement singular) ||
                singular.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(singular.GetString()))
            {
                throw new JsonException("A resource model must declare its singular name.");
            }
            bool hasDocument = true;
            if (resource.TryGetProperty("hasdocument", out JsonElement aspect))
            {
                if (aspect.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new JsonException("The hasdocument model aspect must be a boolean.");
                }
                hasDocument = aspect.GetBoolean();
            }
            XRegistryHttpEntityKind kind = segments.Count switch
            {
                3 => XRegistryHttpEntityKind.Resources,
                4 => XRegistryHttpEntityKind.Resource,
                5 when segments[4] == "meta" => XRegistryHttpEntityKind.Meta,
                5 when segments[4] == "versions" => XRegistryHttpEntityKind.Versions,
                6 when segments[4] == "versions" => XRegistryHttpEntityKind.Version,
                _ => XRegistryHttpEntityKind.Other
            };
            return new XRegistryHttpShape
            {
                Kind = kind,
                Definition = resource,
                Singular = singular.GetString(),
                HasDocument = hasDocument
            };
        }

        private static bool TryAttributeType(
            JsonElement definition,
            string container,
            string name,
            bool mapItem,
            out string? type)
        {
            type = null;
            if (!definition.TryGetProperty(container, out JsonElement attributes) ||
                attributes.ValueKind != JsonValueKind.Object ||
                !attributes.TryGetProperty(name, out JsonElement attribute) ||
                attribute.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (mapItem &&
                (!attribute.TryGetProperty("item", out attribute) ||
                    attribute.ValueKind != JsonValueKind.Object))
            {
                return false;
            }
            if (!attribute.TryGetProperty("type", out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("An effective attribute model must declare its type.");
            }
            type = value.GetString();
            return true;
        }
    }
}
