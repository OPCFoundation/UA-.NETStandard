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
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private static void ValidateFlags(
            XRegistryModelRules model, XRegistryTarget target, XRegistryRequest request)
        {
            ValidateInlinePaths(request, model, target);
            ValidateSortTarget(model, target, request);
            _ = IgnoredAspects(request);
            if (request.Parameters.ToList().Any(
                parameter => parameter.Name == "filter" && parameter.Value == "excludeall") &&
                request.Parameters.ToList().Count(parameter => parameter.Name == "filter") != 1)
            {
                throw new XRegistryRejectionException(
                    "bad_filter", "excludeall cannot be combined with other filters.");
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            int expressions = 0;
            for (int index = 0; index < request.Parameters.Count; index++)
            {
                XRegistryParameter parameter = request.Parameters[index];
                if (s_singleValueFlags.Contains(parameter.Name) && !unique.Add(parameter.Name))
                {
                    throw new XRegistryRejectionException("bad_flag", "The flag requires a single value.");
                }
                expressions += ValidateFlag(target, request, parameter);
                if (expressions > 128)
                {
                    throw new XRegistryRejectionException("bad_filter", "The filter expression limit was exceeded.");
                }
            }
        }

        private static int ValidateFlag(
            XRegistryTarget target, XRegistryRequest request, XRegistryParameter parameter)
        {
            switch (parameter.Name)
            {
                case "collections" when target.Kind is not (XRegistryEntityKind.Registry or XRegistryEntityKind.Group):
                    throw new XRegistryRejectionException(
                        "bad_flag", "collections applies only to a registry or group.");
                case "specversion" when !MatchesSpecVersion(parameter.Value):
                    throw new XRegistryRejectionException("unsupported_specversion",
                        "The requested specversion is unsupported; this endpoint supports 1.0-rc4.");
                case "ignore" when !request.IsMutation:
                    throw new XRegistryRejectionException("bad_flag", "ignore requires a write operation.");
                case "setdefaultversionid":
                    ValidateDefaultSelectionFlag(target, request, parameter.Value);
                    break;
                case "filter":
                    return ValidateFilterSyntax(parameter.Value);
                case "limit":
                    if (!target.IsCollection ||
                        request.Action != XRegistryAction.Read ||
                        !ulong.TryParse(parameter.Value, NumberStyles.None, CultureInfo.InvariantCulture,
                            out ulong limit) ||
                        limit == 0)
                    {
                        throw new XRegistryRejectionException("bad_flag",
                            "limit requires a collection read and a positive UInt64 value.");
                    }
                    break;
                case "cursor" when request.Action != XRegistryAction.Read:
                    throw new XRegistryRejectionException("bad_flag", "Continuations require a collection read.");
            }
            return 0;
        }

        private static int ValidateFilterSyntax(string? value)
        {
            if (value is null or "" || value.Length > 16384)
            {
                throw new XRegistryRejectionException("bad_filter", "A bounded nonempty filter is required.");
            }
            if (value == "excludeall")
            {
                return 0;
            }
            List<string> expressions = XRegistryQueryPath.Split(value, ',', "bad_filter");
            foreach (string expression in expressions)
            {
                _ = ParseExpression(expression);
            }
            return expressions.Count;
        }

        private static void ValidateDefaultSelectionFlag(
            XRegistryTarget target, XRegistryRequest request, string? value)
        {
            if (!request.IsMutation ||
                target.Kind is not (XRegistryEntityKind.Resource
                    or XRegistryEntityKind.Meta or XRegistryEntityKind.Version or XRegistryEntityKind.Versions))
            {
                throw new XRegistryRejectionException("bad_flag",
                    "Default selection requires a write to exactly one Resource or its Versions.");
            }
            if (string.IsNullOrEmpty(value))
            {
                throw new XRegistryRejectionException("bad_defaultversionid", "A default Version ID is required.");
            }
            if (value == "request" &&
                (request.Action != XRegistryAction.Create || target.Kind != XRegistryEntityKind.Resource))
            {
                throw new XRegistryRejectionException("bad_flag",
                    "request can select only the single Version created by POST to a Resource.");
            }
        }

        private static bool MatchesSpecVersion(string? value)
        {
            if (value is null)
            {
                return false;
            }
            int suffix = value.IndexOf('-', StringComparison.Ordinal);
            if (suffix < 0 || !value[suffix..].Equals("-rc4", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string[] numbers = value[..suffix].Split('.');
            return numbers.Length is 2 or 3 &&
                numbers[0] == "1" &&
                numbers[1] == "0" &&
                (numbers.Length == 2 ||
                    (numbers[2].Length > 0 &&
                        numbers[2].All(character => character is >= '0' and <= '9')));
        }

        private static HashSet<string> IgnoredAspects(XRegistryRequest request)
        {
            var ignored = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRegistryParameter parameter in request.Parameters)
            {
                if (parameter.Name != "ignore")
                {
                    continue;
                }
                foreach (string name in (parameter.Value ?? string.Empty).Split(','))
                {
                    if (name is "" or "*")
                    {
                        ignored.UnionWith(s_ignoredAspects);
                    }
                    else if (s_ignoredAspects.Contains(name))
                    {
                        ignored.Add(name);
                    }
                    else
                    {
                        throw new XRegistryRejectionException("bad_ignore", "The ignore aspect is not supported.");
                    }
                }
            }
            return ignored;
        }

        private static JsonObject? PrepareWriteBody(Transaction transaction, XRegistryModelRules model,
            XRegistryTarget target, JsonObject? body)
        {
            transaction.IgnoredAspects = IgnoredAspects(transaction.Request);
            if (body is null)
            {
                return null;
            }
            bool selection = HasFlag(transaction.Request, "setdefaultversionid");
            PrepareIgnoredEntity(model, target, body, transaction.IgnoredAspects, selection, true);
            transaction.Request = transaction.Request with { Metadata = Element(body) };
            return body;
        }

        private static void PrepareIgnoredEntity(
            XRegistryModelRules model, XRegistryTarget current, JsonObject input,
            HashSet<string> ignored, bool selection, bool root)
        {
            if (current.IsCollection)
            {
                foreach ((string id, JsonNode? child) in input)
                {
                    PrepareIgnoredEntity(model, model.Resolve(Child(current.Path, id)),
                        XRegistryModelRules.Object(child), ignored, selection, false);
                }
                return;
            }
            if (ignored.Contains("epoch"))
            {
                input.Remove("epoch");
            }
            if (root && ignored.Contains("id"))
            {
                input.Remove(current.Singular + "id");
                if (current.Kind == XRegistryEntityKind.Version)
                {
                    input.Remove("versionid");
                }
            }
            if (current.Kind == XRegistryEntityKind.Registry)
            {
                foreach (string name in new[] { "modelsource", "capabilities" })
                {
                    if (ignored.Contains(name))
                    {
                        input.Remove(name);
                    }
                }
            }
            if (current.Kind == XRegistryEntityKind.Meta)
            {
                foreach (string name in new[] { "defaultversionid", "defaultversionsticky" })
                {
                    if (selection || ignored.Contains(name))
                    {
                        input.Remove(name);
                    }
                }
            }
            if (current.Kind == XRegistryEntityKind.Special)
            {
                return;
            }
            string? collections = current.Kind == XRegistryEntityKind.Registry ? "groups" :
                current.Kind == XRegistryEntityKind.Group ? "resources" : null;
            if (collections is not null && current.Definition[collections] is JsonObject types)
            {
                foreach ((string name, _) in types)
                {
                    if (input[name] is JsonObject children)
                    {
                        PrepareIgnoredEntity(model, model.Resolve(Child(current.Path, name)), children,
                            ignored, selection, false);
                    }
                }
            }
            if (current.Kind == XRegistryEntityKind.Resource)
            {
                if (input["meta"] is JsonObject meta)
                {
                    PrepareIgnoredEntity(model, model.Resolve(current.Path + "/meta"), meta, ignored, selection, false);
                }
                if (input["versions"] is JsonObject versions)
                {
                    PrepareIgnoredEntity(model, model.Resolve(current.Path + "/versions"), versions,
                        ignored, selection, false);
                }
            }
        }

        private static void ApplyDefaultSelection(Transaction transaction, XRegistryTarget target)
        {
            XRegistryParameter? selection = transaction.Request.Parameters.ToList()
                .Find(parameter => parameter.Name == "setdefaultversionid");
            if (selection is null)
            {
                return;
            }
            string resource = target.Kind switch
            {
                XRegistryEntityKind.Resource => target.Path,
                XRegistryEntityKind.Meta or XRegistryEntityKind.Versions => Parent(target.Path),
                _ => Parent(Parent(target.Path))
            };
            if (transaction.Entries[resource + "/meta"] is not JsonObject entry)
            {
                throw new XRegistryRejectionException(
                    "unknown_id", "The Resource no longer exists after this operation.");
            }
            JsonObject meta = Metadata(entry);
            string selected = selection.Value!;
            if (selected == "null")
            {
                meta["defaultversionsticky"] = false;
            }
            else
            {
                if (selected == "request")
                {
                    string[] created = [.. transaction.Created.Where(path =>
                        Parent(path) == resource + "/versions" && transaction.Entries.ContainsKey(path))];
                    if (created.Length != 1)
                    {
                        throw new XRegistryRejectionException(created.Length == 0 ? "defaultversionid_request" :
                            "too_many_versions", "request must identify exactly one newly created Version.");
                    }
                    selected = Identity(created[0]);
                }
                if (!transaction.Entries.ContainsKey(Child(resource + "/versions", selected)))
                {
                    throw new XRegistryRejectionException("unknown_id", "The selected default Version does not exist.");
                }
                meta["defaultversionid"] = selected;
                meta["defaultversionsticky"] = true;
            }
            transaction.Touched.Add(resource + "/meta");
            transaction.Resources.Add(resource);
        }

        private static readonly HashSet<string> s_ignoredAspects = new(StringComparer.Ordinal)
        {
            "capabilities", "defaultversionid", "defaultversionsticky", "epoch", "id", "modelsource", "readonly"
        };

        private static readonly HashSet<string> s_singleValueFlags = new(StringComparer.Ordinal)
        {
            "specversion", "collections", "doc", "binary", "epoch", "setdefaultversionid", "limit", "cursor"
        };
    }
}
