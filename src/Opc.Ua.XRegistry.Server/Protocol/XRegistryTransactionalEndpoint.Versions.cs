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
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private void MutateResource(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target,
            JsonObject input, XRegistryAction action, ByteString document)
        {
            string resource = target.Kind switch
            {
                XRegistryEntityKind.Resource => target.Path,
                XRegistryEntityKind.Meta => Parent(target.Path),
                _ => Parent(Parent(target.Path))
            };
            string groupPath = Parent(Parent(resource));
            XRegistryTarget groupTarget = model.Resolve(groupPath);
            _ = EnsureEntry(transaction, groupPath, groupTarget.Singular + "id", Identity(groupPath));
            bool created = !transaction.Entries.ContainsKey(resource + "/meta");
            JsonObject metaEntry = EnsureEntry(
                transaction, resource + "/meta", target.Singular + "id", Identity(resource));
            if (created)
            {
                Metadata(metaEntry)["defaultversionsticky"] = false;
                transaction.Touched.Add(groupPath);
            }
            transaction.Resources.Add(resource);

            if (target.Kind == XRegistryEntityKind.Version)
            {
                UpdateVersion(transaction, target, input, action, document, false);
                return;
            }

            JsonObject? metaInput = target.Kind == XRegistryEntityKind.Meta
                ? input : input["meta"] is null ? null : XRegistryModelRules.Object(input["meta"]);
            string? priorDefault = Metadata(metaEntry)["defaultversionid"] is JsonNode defaultId
                ? XRegistryModelRules.Text(defaultId) : null;
            if (target.Kind == XRegistryEntityKind.Resource)
            {
                var versions = input["versions"] as JsonObject;
                if (input.ContainsKey("versions") && versions is null)
                {
                    throw new XRegistryRejectionException("bad_request", "versions must be a map.");
                }
                if (versions is not null)
                {
                    foreach ((string id, JsonNode? version) in
                        versions.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        UpdateVersion(transaction, model.Resolve(Child(resource + "/versions", id)),
                            XRegistryModelRules.Object(version), action, default, false);
                    }
                }
                string? requested = input["versionid"] is JsonNode value ? XRegistryModelRules.Text(value) : null;
                string? versionId = created
                    ? requested ??
                        (metaInput?["defaultversionid"] is JsonNode selected
                            ? XRegistryModelRules.Text(selected) : null)
                    : action == XRegistryAction.Create ? requested : priorDefault;
                if (!created && action != XRegistryAction.Create && requested is not null && requested != priorDefault)
                {
                    throw new XRegistryRejectionException("mismatched_id",
                        "The logical resource addresses the prior default version, not another version ID.");
                }
                bool generated = false;
                if (versionId is null && (versions is null || versions.Count == 0 || action == XRegistryAction.Create))
                {
                    versionId = AssignVersion(transaction, resource);
                    generated = true;
                }
                if (versionId is not null && versions?.ContainsKey(versionId) != true)
                {
                    UpdateVersion(transaction, model.Resolve(Child(resource + "/versions", versionId)),
                        input, action, document, generated);
                }
            }
            else if (created)
            {
                string id = AssignVersion(transaction, resource);
                UpdateVersion(transaction, model.Resolve(Child(resource + "/versions", id)),
                    [], XRegistryAction.Replace, default, true);
            }

            if (metaInput is not null)
            {
                CheckEpoch(transaction, resource + "/meta", metaInput["epoch"]);
                if (metaInput["xref"] is not null)
                {
                    throw new XRegistryRejectionException("action_not_supported",
                        "Cross-reference resources require an endpoint with qualified reference resolution.", 405);
                }
                if (metaInput["compatibility"] is not null)
                {
                    throw new XRegistryRejectionException("action_not_supported",
                        "This endpoint has no domain compatibility validator.", 405);
                }
                metaEntry["metadata"] = XRegistryModelRules.Apply(Metadata(metaEntry), metaInput,
                    target.Definition["metaattributes"] as JsonObject, action == XRegistryAction.Merge,
                    target.Singular + "id", Identity(resource), "meta");
                JsonObject metadata = Metadata(metaEntry);
                if (metaInput.ContainsKey("defaultversionid") && !metaInput.ContainsKey("defaultversionsticky"))
                {
                    metadata["defaultversionsticky"] = metaInput["defaultversionid"] is not null;
                }
                metadata["defaultversionsticky"] ??= false;
                Stamp(transaction, resource + "/meta", metaInput);
            }
        }

        private void UpdateVersion(
            Transaction transaction, XRegistryTarget target, JsonObject input,
            XRegistryAction action, ByteString document, bool assigned)
        {
            bool creating = !transaction.Entries.ContainsKey(target.Path);
            string resource = Parent(Parent(target.Path));
            string id = Identity(target.Path);
            if (creating && !assigned && !XRegistryModelRules.Boolean(target.Definition["setversionid"], true))
            {
                throw new XRegistryRejectionException(
                    "versionid_not_allowed", "Version IDs are server-assigned for this type.");
            }
            JsonObject entry = EnsureEntry(transaction, target.Path, "versionid", id);
            CheckEpoch(transaction, target.Path, input["epoch"]);
            if (input[target.Singular + "id"] is JsonNode resourceId &&
                XRegistryModelRules.Text(resourceId) != Identity(resource))
            {
                throw new XRegistryRejectionException("mismatched_id", "The resource ID differs from its path.");
            }
            bool patch = action == XRegistryAction.Merge || !document.IsNull;
            var excluded = new HashSet<string>(StringComparer.Ordinal)
            {
                "meta", "versions", "metaurl", "versionsurl", "versionscount",
                target.Singular + "id", target.Singular, target.Singular + "base64", target.Singular + "url"
            };
            entry["metadata"] = XRegistryModelRules.Apply(Metadata(entry), input,
                target.Definition["attributes"] as JsonObject, patch, "versionid", id, "version", excluded);
            Metadata(entry)[target.Singular + "id"] = Identity(resource);
            Stamp(transaction, target.Path, input);
            if (creating &&
                Metadata(entry)["ancestorid"] is null &&
                XRegistryModelRules.Text(target.Definition["versionmode"]) == "manual")
            {
                List<string> existing = OrderedVersions(transaction, resource, target.Definition);
                existing.Remove(target.Path);
                var ancestors = new HashSet<string>(StringComparer.Ordinal);
                foreach (string path in existing)
                {
                    JsonNode? ancestor = Metadata(GetEntry(transaction, path))["ancestorid"];
                    if (ancestor is not null && XRegistryModelRules.Text(ancestor) != Identity(path))
                    {
                        ancestors.Add(XRegistryModelRules.Text(ancestor));
                    }
                }
                string? latest = existing.LastOrDefault(path => !ancestors.Contains(Identity(path)));
                Metadata(entry)["ancestorid"] = latest is null ? id : Identity(latest);
            }
            SetDocument(transaction, target, input, document, entry, creating, patch);
        }

        private void SetDocument(
            Transaction transaction, XRegistryTarget target, JsonObject input, ByteString bytes,
            JsonObject entry, bool creating, bool patch)
        {
            string name = target.Singular;
            string[] forms = [name, name + "base64", name + "url"];
            int count = forms.Count(input.ContainsKey);
            bool hasDocument = XRegistryModelRules.Boolean(target.Definition["hasdocument"], true);
            if ((!hasDocument && (!bytes.IsNull || count != 0)) || count > 1)
            {
                throw new XRegistryRejectionException(
                    "one_resource", "The resource document representation is invalid.");
            }
            if (!hasDocument)
            {
                return;
            }
            if (creating && entry["document"] is null)
            {
                entry["document"] = string.Empty;
            }
            if (!bytes.IsNull)
            {
                if (transaction.Request.ContentType is null)
                {
                    Metadata(entry).Remove("contenttype");
                }
                else
                {
                    Metadata(entry)["contenttype"] = transaction.Request.ContentType;
                }
            }
            else if (count != 0)
            {
                if (input[name + "url"] is not null)
                {
                    throw new XRegistryRejectionException("action_not_supported",
                        "Remote document URLs require a qualified document-resolution provider.", 405);
                }
                if (input[name + "base64"] is JsonNode encoded)
                {
                    try
                    {
                        bytes = ByteString.From(Convert.FromBase64String(XRegistryModelRules.Text(encoded)));
                    }
                    catch (FormatException exception)
                    {
                        throw new XRegistryRejectionException("bad_request", exception.Message);
                    }
                }
                else if (input[name] is JsonNode inline)
                {
                    bytes = ByteString.From(Encoding.UTF8.GetBytes(inline.ToJsonString()));
                }
                else
                {
                    bytes = ByteString.Empty;
                }
                if (!input.ContainsKey("contenttype") && (!patch || Metadata(entry)["contenttype"] is null))
                {
                    Metadata(entry)["contenttype"] = transaction.Request.ContentType ?? "application/json";
                }
            }
            if (!bytes.IsNull)
            {
                if (bytes.Length > m_options.MaxDocumentBytes)
                {
                    throw new XRegistryRejectionException(
                        "bad_request", "The document exceeds its configured byte limit.", 413);
                }
                entry["document"] = Base64(bytes);
            }
        }

        private void FinalizeResources(Transaction transaction, XRegistryModelRules model)
        {
            foreach (string resource in transaction.Resources.ToArray())
            {
                if (transaction.Entries[resource + "/meta"] is not JsonObject metaEntry)
                {
                    continue;
                }
                JsonObject definition = model.Resolve(resource).Definition;
                List<string> versions = OrderedVersions(transaction, resource, definition);
                JsonObject meta = Metadata(metaEntry);
                if (versions.Count == 0)
                {
                    transaction.Entries.Remove(resource + "/meta");
                    transaction.Touched.Add(Parent(Parent(resource)));
                    continue;
                }
                FixAncestry(transaction, versions, definition);
                SelectDefault(transaction, versions, meta, definition);
                BigInteger maxVersions = XRegistryModelRules.Unsigned(definition["maxversions"]);
                while (maxVersions > 0 && versions.Count > maxVersions)
                {
                    string? oldest = versions.FirstOrDefault(path =>
                        maxVersions == 1 || Identity(path) != XRegistryModelRules.Text(meta["defaultversionid"])) ??
                        throw new XRegistryRejectionException(
                            "bad_request",
                            "No version is eligible for retention pruning.");
                    transaction.Entries.Remove(oldest);
                    versions.Remove(oldest);
                    transaction.Touched.Add(resource + "/meta");
                    FixAncestry(transaction, versions, definition, allowRemovedAncestors: true);
                    SelectDefault(transaction, versions, meta, definition);
                }
                ValidateMatchingAttributes(transaction, versions, definition["attributes"] as JsonObject);
                ValidateGroupConstraints(transaction, model, resource, versions);
            }
        }

        private static List<string> OrderedVersions(Transaction transaction, string resource, JsonObject definition)
        {
            string mode = XRegistryModelRules.Text(definition["versionmode"]);
            string timestamp = mode == "modifiedat" ? "modifiedat" : "createdat";
            return [.. Children(transaction, resource + "/versions")
                .OrderBy(path => XRegistryModelRules.Text(Metadata(GetEntry(transaction, path))[timestamp]),
                    StringComparer.Ordinal)
                .ThenBy(Identity, StringComparer.OrdinalIgnoreCase).ThenBy(Identity, StringComparer.Ordinal)];
        }

        private static void FixAncestry(
            Transaction transaction, List<string> versions, JsonObject definition, bool allowRemovedAncestors = false)
        {
            bool manual = XRegistryModelRules.Text(definition["versionmode"]) == "manual";
            var ids = new HashSet<string>(versions.Select(Identity), StringComparer.Ordinal);
            string? prior = null;
            foreach (string path in versions)
            {
                JsonObject metadata = Metadata(GetEntry(transaction, path));
                string id = Identity(path);
                if (!manual || metadata["ancestorid"] is null)
                {
                    metadata["ancestorid"] = prior ?? id;
                }
                string ancestor = XRegistryModelRules.Text(metadata["ancestorid"]);
                if (!ids.Contains(ancestor))
                {
                    if (!allowRemovedAncestors &&
                        (transaction.Created.Contains(path) || transaction.Touched.Contains(path)))
                    {
                        throw new XRegistryRejectionException(
                            "invalid_ancestor", "The requested ancestor does not exist.");
                    }
                    metadata["ancestorid"] = id;
                }
                prior = id;
            }
            int roots = 0;
            foreach (string path in versions)
            {
                string current = path;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    string id = Identity(current);
                    string ancestor = XRegistryModelRules.Text(Metadata(GetEntry(transaction, current))["ancestorid"]);
                    if (ancestor == id)
                    {
                        if (current == path)
                        {
                            roots++;
                        }
                        break;
                    }
                    if (!seen.Add(current))
                    {
                        throw new XRegistryRejectionException(
                            "invalid_ancestor", "Version ancestry contains a cycle.");
                    }
                    current = Child(Parent(path), ancestor);
                }
            }
            if (XRegistryModelRules.Boolean(definition["singleversionroot"]) && roots > 1)
            {
                throw new XRegistryRejectionException(
                    "multiple_roots", "This resource type permits only one ancestor root.");
            }
        }

        private static void SelectDefault(
            Transaction transaction, List<string> versions, JsonObject meta, JsonObject definition)
        {
            bool sticky = XRegistryModelRules.Boolean(meta["defaultversionsticky"]);
            if (XRegistryModelRules.Unsigned(definition["maxversions"]) == 1 && sticky)
            {
                throw new XRegistryRejectionException(
                    "setdefaultversionsticky_false", "maxversions=1 requires a non-sticky default.");
            }
            if (sticky && meta["defaultversionid"] is JsonNode selected)
            {
                if (!versions.Any(path => Identity(path) == XRegistryModelRules.Text(selected)))
                {
                    throw new XRegistryRejectionException(
                        "invalid_defaultversionid", "The selected default version does not exist.");
                }
                return;
            }
            string newest = versions[^1];
            if (XRegistryModelRules.Text(definition["versionmode"]) == "manual")
            {
                var ancestors = new HashSet<string>(StringComparer.Ordinal);
                foreach (string path in versions)
                {
                    string ancestor = XRegistryModelRules.Text(Metadata(GetEntry(transaction, path))["ancestorid"]);
                    if (ancestor != Identity(path))
                    {
                        ancestors.Add(ancestor);
                    }
                }
                newest = versions.Last(path => !ancestors.Contains(Identity(path)));
            }
            meta["defaultversionid"] = Identity(newest);
        }

        private static void ValidateMatchingAttributes(
            Transaction transaction, List<string> versions, JsonObject? definitions, string prefix = "")
        {
            if (definitions is null)
            {
                return;
            }
            foreach ((string name, JsonNode? rule) in definitions)
            {
                if (XRegistryModelRules.Boolean(rule?["matchversions"]))
                {
                    JsonNode? expected = FindAttribute(Metadata(GetEntry(transaction, versions[0])), prefix + name);
                    foreach (string path in versions.Skip(1))
                    {
                        if (!JsonNode.DeepEquals(expected,
                            FindAttribute(Metadata(GetEntry(transaction, path)), prefix + name)))
                        {
                            throw new XRegistryRejectionException("mismatched_version_attribute",
                                "Version attributes do not satisfy matchversions.");
                        }
                    }
                }
                if (rule?["attributes"] is JsonObject nested)
                {
                    ValidateMatchingAttributes(transaction, versions, nested, prefix + name + ".");
                }
            }
        }

        private static void ValidateGroupConstraints(
            Transaction transaction, XRegistryModelRules model, string resource, List<string> versions)
        {
            string group = Parent(Parent(resource));
            string resourceType = Identity(Parent(resource));
            JsonObject groupMetadata = Metadata(GetEntry(transaction, group));
            JsonObject? constraints = groupMetadata["constraints"]?[resourceType] as JsonObject ??
                model.Resolve(group).Definition["constraints"]?[resourceType] as JsonObject;
            if (constraints is null)
            {
                return;
            }
            foreach ((string name, JsonNode? value) in constraints)
            {
                JsonObject constraint = XRegistryModelRules.Object(value);
                foreach (string path in versions)
                {
                    JsonNode? actual = FindAttribute(Metadata(GetEntry(transaction, path)), name);
                    if (constraint["enum"] is JsonArray choices &&
                        choices.Count > 0 &&
                        !choices.Any(choice => JsonNode.DeepEquals(actual, choice)))
                    {
                        throw new XRegistryRejectionException(
                            "invalid_attribute", "A group constraint rejected a version value.");
                    }
                    if (constraint["equals"] is JsonNode reference &&
                        !JsonNode.DeepEquals(
                            actual, FindAttribute(groupMetadata, XRegistryModelRules.Text(reference))))
                    {
                        throw new XRegistryRejectionException(
                            "invalid_attribute", "A group equality constraint is not satisfied.");
                    }
                }
            }
        }

        private static JsonNode? FindAttribute(JsonObject value, string path)
        {
            JsonNode? current = value;
            foreach (string name in path.Split('.'))
            {
                current = current is JsonObject obj ? obj[name] : null;
            }
            return current;
        }

        private static string AssignVersion(Transaction transaction, string resource)
        {
            int next = 1;
            while (transaction.Entries.ContainsKey(
                Child(resource + "/versions", next.ToString(CultureInfo.InvariantCulture))))
            {
                next++;
            }
            return next.ToString(CultureInfo.InvariantCulture);
        }
    }
}
