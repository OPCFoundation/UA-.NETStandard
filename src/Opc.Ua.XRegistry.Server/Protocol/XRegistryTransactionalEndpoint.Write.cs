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
        private void Mutate(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target,
            JsonObject? input, XRegistryAction action, ByteString document)
        {
            if (target.Kind == XRegistryEntityKind.Special)
            {
                if (target.Singular != "modelsource" ||
                    action is not (XRegistryAction.Replace or XRegistryAction.Merge))
                {
                    throw new XRegistryRejectionException(
                        "action_not_supported", "This registry aspect is read-only.", 405);
                }
                JsonObject updated = input ??
                    throw new XRegistryRejectionException("bad_request", "A model is required.");
                if (action == XRegistryAction.Merge)
                {
                    updated = MergeModel(XRegistryModelRules.Object(transaction.Snapshot["modelsource"]), updated);
                }
                var proposed = new XRegistryModelRules((JsonObject)updated.DeepClone());
                foreach ((string path, JsonNode? value) in transaction.Entries)
                {
                    XRegistryTarget entity = proposed.Resolve(path);
                    JsonObject existingEntry = XRegistryModelRules.Object(value);
                    if (entity.Kind == XRegistryEntityKind.Version &&
                        !XRegistryModelRules.Boolean(entity.Definition["hasdocument"], true) &&
                        (existingEntry["document"] is not null ||
                            XRegistryModelRules.Boolean(model.Resolve(path).Definition["hasdocument"], true)))
                    {
                        throw new XRegistryRejectionException("hasdocument_violation",
                            "The model cannot remove domain documents from existing versions.");
                    }
                    JsonObject metadata = Metadata(existingEntry);
                    var attributes = entity.Definition[
                        entity.Kind == XRegistryEntityKind.Meta ? "metaattributes" : "attributes"] as JsonObject;
                    string kind = entity.Kind switch
                    {
                        XRegistryEntityKind.Registry => "registry",
                        XRegistryEntityKind.Group => "group",
                        XRegistryEntityKind.Meta => "meta",
                        _ => "version"
                    };
                    foreach ((string name, _) in metadata)
                    {
                        if (!XRegistryModelRules.IsManaged(name) &&
                            !XRegistryModelRules.IsStandard(name, kind) &&
                            name != entity.Singular + "id" &&
                            !(entity.Kind == XRegistryEntityKind.Version && name == "versionid") &&
                            attributes?.ContainsKey(name) != true &&
                            attributes?.ContainsKey("*") != true)
                        {
                            throw new XRegistryRejectionException("invalid_model",
                                "The proposed model would leave an existing attribute undefined.");
                        }
                    }
                    var checkedMetadata = (JsonObject)metadata.DeepClone();
                    XRegistryModelRules.ValidateObject(checkedMetadata, attributes, kind);
                    foreach ((string name, JsonNode? checkedValue) in checkedMetadata)
                    {
                        if (!XRegistryModelRules.IsManaged(name) &&
                            !JsonNode.DeepEquals(checkedValue, metadata[name]))
                        {
                            throw new XRegistryRejectionException("invalid_model",
                                "Populate required/defaulted attributes before changing their model.");
                        }
                    }
                    if (entity.Kind == XRegistryEntityKind.Meta)
                    {
                        transaction.Resources.Add(Parent(path));
                    }
                }
                transaction.Snapshot["modelsource"] = updated.DeepClone();
                model.Model.Clear();
                foreach ((string name, JsonNode? value) in proposed.Model)
                {
                    model.Model[name] = value?.DeepClone();
                }
                transaction.Touched.Add("/");
                return;
            }
            if (target.IsCollection)
            {
                if (action == XRegistryAction.Replace)
                {
                    throw new XRegistryRejectionException(
                        "action_not_supported", "PUT cannot target a collection.", 405);
                }
                if (action == XRegistryAction.Delete)
                {
                    IEnumerable<string> ids = input is null
                        ? Children(transaction, target.Path).Select(Identity).ToArray()
                        : [.. input.Select(pair => pair.Key)];
                    foreach (string id in ids)
                    {
                        XRegistryTarget child = model.Resolve(Child(target.Path, id));
                        if (!transaction.Entries.ContainsKey(StoragePath(child)))
                        {
                            continue;
                        }
                        Delete(transaction, child, input is null ? null : XRegistryModelRules.Object(input[id]));
                    }
                    return;
                }
                foreach ((string id, JsonNode? value) in input ??
                    throw new XRegistryRejectionException(
                        "bad_request", "A collection request requires an entity map."))
                {
                    Mutate(transaction, model, model.Resolve(Child(target.Path, id)),
                        XRegistryModelRules.Object(value),
                        action == XRegistryAction.Create ? XRegistryAction.Replace : action, default);
                }
                return;
            }
            if (action == XRegistryAction.Delete)
            {
                Delete(transaction, target, input);
                return;
            }
            input ??= [];
            if (target.Kind is XRegistryEntityKind.Resource or XRegistryEntityKind.Version or XRegistryEntityKind.Meta)
            {
                MutateResource(transaction, model, target, input, action, document);
                return;
            }
            if (!document.IsNull)
            {
                throw new XRegistryRejectionException(
                    "bad_request", "Only document-backed versions accept document bodies.");
            }
            JsonObject entry = EnsureEntry(transaction, target.Path, target.Singular + "id",
                target.Kind == XRegistryEntityKind.Registry ? m_options.RegistryId : Identity(target.Path));
            CheckEpoch(transaction, target.Path, input["epoch"]);
            JsonObject collections = XRegistryModelRules.Object(target.Definition[
                target.Kind == XRegistryEntityKind.Registry ? "groups" : "resources"]);
            var excluded = new HashSet<string>(StringComparer.Ordinal) { "model", "modelsource", "capabilities" };
            foreach ((string name, _) in collections)
            {
                excluded.Add(name);
                excluded.Add(name + "url");
                excluded.Add(name + "count");
            }
            entry["metadata"] = XRegistryModelRules.Apply(Metadata(entry), input,
                target.Definition["attributes"] as JsonObject, action == XRegistryAction.Merge,
                target.Singular + "id",
                target.Kind == XRegistryEntityKind.Registry ? m_options.RegistryId : Identity(target.Path),
                target.Kind == XRegistryEntityKind.Registry ? "registry" : "group", excluded);
            Stamp(transaction, target.Path, input);
            foreach ((string name, _) in collections)
            {
                if (input.TryGetPropertyValue(name, out JsonNode? value))
                {
                    Mutate(transaction, model, model.Resolve(Child(target.Path, name)),
                        XRegistryModelRules.Object(value),
                        action == XRegistryAction.Merge ? action : XRegistryAction.Create,
                        default);
                }
            }
            if (target.Kind == XRegistryEntityKind.Registry && input["modelsource"] is JsonObject modelSource)
            {
                Mutate(transaction, model, model.Resolve("/modelsource"), modelSource, action, default);
            }
        }

        private void Delete(Transaction transaction, XRegistryTarget target, JsonObject? input)
        {
            if (target.Kind is XRegistryEntityKind.Registry or XRegistryEntityKind.Meta)
            {
                throw new XRegistryRejectionException(
                    "action_not_supported", "This entity cannot be deleted independently.", 405);
            }
            string path = StoragePath(target);
            JsonObject existing = GetEntry(transaction, path);
            if (input?[target.Singular + "id"] is JsonNode givenId &&
                XRegistryModelRules.Text(givenId) != Identity(target.Path))
            {
                throw new XRegistryRejectionException(
                    "mismatched_id", "The deletion ID differs from its collection key.");
            }
            JsonNode? guard = input?["epoch"];
            if (target.Kind == XRegistryEntityKind.Resource)
            {
                if (input?["epoch"] is not null && input?["meta"]?["epoch"] is null)
                {
                    throw new XRegistryRejectionException("misplaced_epoch", "Resource deletion requires meta.epoch.");
                }
                guard = input?["meta"]?["epoch"];
            }
            for (int index = 0; index < transaction.Request.Parameters.Count; index++)
            {
                XRegistryParameter parameter = transaction.Request.Parameters[index];
                if (parameter.Name == "epoch" && target.Path == transaction.Request.Path)
                {
                    guard = JsonNode.Parse(parameter.Value ?? "null");
                }
            }
            CheckEpoch(transaction, path, guard);
            if (target.Kind == XRegistryEntityKind.Version)
            {
                string resource = Parent(Parent(path));
                JsonObject meta = Metadata(GetEntry(transaction, resource + "/meta"));
                if (XRegistryModelRules.Boolean(meta["defaultversionsticky"]) &&
                    XRegistryModelRules.Text(meta["defaultversionid"]) == Identity(path) &&
                    Children(transaction, resource + "/versions").Count > 1)
                {
                    throw new XRegistryRejectionException("setdefaultversionsticky_false",
                        "Choose a new default before deleting the sticky default version.");
                }
                transaction.Entries.Remove(path);
                transaction.Touched.Add(resource + "/meta");
                transaction.Resources.Add(resource);
                return;
            }
            _ = existing;
            string prefix = target.Path + "/";
            foreach (string descendant in transaction.Entries.Select(pair => pair.Key)
                .Where(key => key == target.Path || key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                transaction.Entries.Remove(descendant);
            }
            transaction.Touched.Add(Parent(Parent(target.Path)));
        }

        private static JsonObject EnsureEntry(Transaction transaction, string path, string idName, string id)
        {
            if (transaction.Entries[path] is JsonObject found)
            {
                return found;
            }
            var entry = new JsonObject
            {
                ["metadata"] = new JsonObject
                {
                    [idName] = id,
                    ["epoch"] = 0,
                    ["createdat"] = transaction.Timestamp,
                    ["modifiedat"] = transaction.Timestamp
                }
            };
            transaction.Entries[path] = entry;
            transaction.Created.Add(path);
            if (path != "/" && !path.EndsWith("/meta", StringComparison.Ordinal))
            {
                string parent = Parent(Parent(path));
                if (parent != "/" && path.Contains("/versions/", StringComparison.Ordinal))
                {
                    parent += "/meta";
                }
                transaction.Touched.Add(parent);
            }
            return entry;
        }

        private static void CheckEpoch(Transaction transaction, string path, JsonNode? guard)
        {
            if (guard is null || transaction.Created.Contains(path))
            {
                return;
            }
            if (XRegistryModelRules.Unsigned(guard) !=
                XRegistryModelRules.Unsigned(Metadata(GetEntry(transaction, path))["epoch"]))
            {
                throw new XRegistryRejectionException(
                    "mismatched_epoch", "The entity changed since the supplied epoch.");
            }
        }

        private static void Stamp(Transaction transaction, string path, JsonObject input)
        {
            JsonObject metadata = Metadata(GetEntry(transaction, path));
            metadata["createdat"] ??= transaction.Timestamp;
            if (!input.ContainsKey("modifiedat") || input["modifiedat"] is null)
            {
                metadata["modifiedat"] = transaction.Timestamp;
            }
            transaction.Touched.Add(path);
        }

        private static void ApplyCounters(Transaction transaction)
        {
            foreach (string path in transaction.Touched)
            {
                if (!transaction.Created.Contains(path) && transaction.Entries[path] is JsonObject entry)
                {
                    Metadata(entry)["epoch"] = XRegistryModelRules.Unsigned(Metadata(entry)["epoch"]).AddOne();
                }
            }
        }

        private static JsonObject MergeModel(JsonObject old, JsonObject input)
        {
            var result = (JsonObject)old.DeepClone();
            foreach ((string name, JsonNode? value) in input)
            {
                if (value is null)
                {
                    result.Remove(name);
                }
                else if (value is JsonObject nested && result[name] is JsonObject existing)
                {
                    result[name] = MergeModel(existing, nested);
                }
                else
                {
                    result[name] = value.DeepClone();
                }
            }
            return result;
        }
    }
}
