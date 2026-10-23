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
#if NETFRAMEWORK
using System.Collections.Generic;
#endif
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private static bool MutateReference(
            Transaction transaction, XRegistryModelRules model, XRegistryTarget target, JsonObject input,
            XRegistryAction action, ByteString document, JsonObject metaEntry, ref bool created)
        {
            string resource = target.Kind == XRegistryEntityKind.Resource ? target.Path :
                target.Kind == XRegistryEntityKind.Meta ? Parent(target.Path) : Parent(Parent(target.Path));
            JsonObject stored = Metadata(metaEntry);
            string? previous = stored["xref"] is JsonNode oldReference ? XRegistryModelRules.Text(oldReference) : null;
            JsonObject? supplied = target.Kind == XRegistryEntityKind.Meta ? input : input["meta"] as JsonObject;
            if (supplied?["xref"] is JsonNode newReference)
            {
                string reference =
                    ReferenceTarget(transaction, model, resource, XRegistryModelRules.Text(newReference));
                if (target.Kind is not (XRegistryEntityKind.Resource or XRegistryEntityKind.Meta) ||
                    !document.IsNull ||
                    HasFlag(transaction.Request, "setdefaultversionid"))
                {
                    throw new XRegistryRejectionException("extra_xref_attribute",
                        "A cross-reference declaration cannot include document or Version changes.");
                }
                string idName = target.Singular + "id";
                bool epochAllowed = previous is null && !created;
                if (supplied.Any(pair => pair.Key != "xref" &&
                    pair.Key != idName &&
                    !(epochAllowed && pair.Key == "epoch")) ||
                    (target.Kind == XRegistryEntityKind.Resource &&
                        input.Any(pair => pair.Key != "meta" && pair.Key != idName)))
                {
                    throw new XRegistryRejectionException("extra_xref_attribute",
                        "Only source identity, xref and an existing normal Resource's meta.epoch are permitted.");
                }
                foreach (JsonObject fields in new[] { input, supplied })
                {
                    if (fields[idName] is JsonNode id && XRegistryModelRules.Text(id) != Identity(resource))
                    {
                        throw new XRegistryRejectionException(
                            "mismatched_id", "The cross-reference source ID differs from its path.");
                    }
                }
                CheckEpoch(transaction, resource + "/meta", supplied["epoch"]);
                foreach (string version in Children(transaction, resource + "/versions"))
                {
                    transaction.Entries.Remove(version);
                }
                metaEntry["metadata"] = new JsonObject
                {
                    [idName] = Identity(resource),
                    ["epoch"] = stored["epoch"]!.DeepClone(),
                    ["createdat"] = stored["createdat"]!.DeepClone(),
                    ["modifiedat"] = transaction.Timestamp,
                    ["xref"] = reference
                };
                transaction.Touched.Add(resource + "/meta");
                transaction.Stamped.Add(resource + "/meta");
                return true;
            }
            if (previous is null)
            {
                return false;
            }
            if (target.Kind is not (XRegistryEntityKind.Resource or XRegistryEntityKind.Meta) ||
                (action == XRegistryAction.Merge && supplied?.ContainsKey("xref") != true))
            {
                throw new XRegistryRejectionException(
                    "readonly", "Referenced content must be changed at its owning Resource.");
            }
            var referenced = transaction.Entries[previous + "/meta"] as JsonObject;
            BigInteger remoteEpoch = referenced is not null && Metadata(referenced)["xref"] is null
                ? XRegistryModelRules.Unsigned(Metadata(referenced)["epoch"]) : BigInteger.Zero;
            if (supplied?["epoch"] is JsonNode guard && XRegistryModelRules.Unsigned(guard) != remoteEpoch)
            {
                throw new XRegistryRejectionException(
                    "mismatched_epoch", "The referenced Resource changed before conversion.");
            }
            supplied?.Remove("epoch");
            metaEntry["metadata"] = new JsonObject
            {
                [target.Singular + "id"] = Identity(resource),
                ["epoch"] = JsonNode.Parse(BigInteger.Max(remoteEpoch, XRegistryModelRules.Unsigned(stored["epoch"]))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ["createdat"] = stored["createdat"]!.DeepClone(),
                ["modifiedat"] = transaction.Timestamp,
                ["defaultversionsticky"] = false
            };
            transaction.Touched.Add(resource + "/meta");
            transaction.Stamped.Add(resource + "/meta");
            created = true;
            return false;
        }

        private static string ReferenceTarget(
            Transaction transaction, XRegistryModelRules model, string resource, string reference)
        {
            try
            {
                string targetPath = XRegistryPath.Normalize(reference);
                if (model.Resolve(targetPath).Kind != XRegistryEntityKind.Resource ||
                    ResourceOrigin(transaction, resource) != ResourceOrigin(transaction, targetPath))
                {
                    throw new XRegistryRejectionException("malformed_xref",
                        "Cross-references require the same Resource model type, including imported type identity.");
                }
                return targetPath;
            }
            catch (ArgumentException)
            {
                throw new XRegistryRejectionException(
                    "malformed_xref", "A cross-reference requires a valid Resource XID.");
            }
            catch (XRegistryRejectionException exception) when (exception.Code == "not_found")
            {
                throw new XRegistryRejectionException("malformed_xref", "The referenced Resource type is not defined.");
            }
        }

        private static string ResourceOrigin(Transaction transaction, string resource)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(resource);
            if (segments.Count != 4)
            {
                throw new XRegistryRejectionException("malformed_xref", "A cross-reference must address a Resource.");
            }
            string type = XRegistryPath.FromSegments([segments[0], segments[2]]);
            return transaction.Snapshot["resourceorigins"]?[type] is JsonNode origin
                ? XRegistryModelRules.Text(origin) : type;
        }

        private Transaction ReferenceView(Transaction transaction, XRegistryModelRules model)
        {
            string[] aliases = [.. transaction.Entries.Where(pair =>
                model.Resolve(pair.Key).Kind == XRegistryEntityKind.Meta &&
                pair.Value?["metadata"]?["xref"] is not null).Select(pair => pair.Key)];
            if (aliases.Length == 0)
            {
                return transaction;
            }
            var snapshot = (JsonObject)transaction.Snapshot.DeepClone();
            Transaction view = CreateReadView(transaction, snapshot);
            foreach (string alias in aliases)
            {
                string resource = Parent(alias);
                string reference = XRegistryModelRules.Text(transaction.Entries[alias]!["metadata"]!["xref"]);
                _ = ReferenceTarget(transaction, model, resource, reference);
                string singular = model.Resolve(resource).Singular;
                JsonObject source = GetEntry(view, alias);
                if (transaction.Entries[reference + "/meta"] is JsonObject target &&
                    Metadata(target)["xref"] is null)
                {
                    source["metadata"] = Metadata(target).DeepClone();
                    Metadata(source)[singular + "id"] = Identity(resource);
                    Metadata(source)["xref"] = reference;
                    Metadata(source)["readonly"] = true;
                    foreach (string path in Children(transaction, reference + "/versions"))
                    {
                        var version = (JsonObject)GetEntry(transaction, path).DeepClone();
                        Metadata(version)[singular + "id"] = Identity(resource);
                        string projectedPath = Child(resource + "/versions", Identity(path));
                        if (!view.Entries.ContainsKey(projectedPath) && view.Entries.Count >= m_options.MaxEntities)
                        {
                            throw new XRegistryRejectionException("too_large",
                                "The expanded reference view exceeds the entity quota.", 413);
                        }
                        view.Entries[projectedPath] = version;
                    }
                }
                else
                {
                    source["metadata"] =
                        new JsonObject { [singular + "id"] = Identity(resource), ["xref"] = reference };
                }
            }
            return view;
        }

        private void ValidateReferences(Transaction transaction, XRegistryModelRules model)
        {
            Transaction view = ReferenceView(transaction, model);
            foreach ((string path, JsonNode? value) in transaction.Entries)
            {
                if (model.Resolve(path).Kind == XRegistryEntityKind.Meta && value?["metadata"]?["xref"] is not null)
                {
                    string resource = Parent(path);
                    ValidateGroupConstraints(view, model, resource, Children(view, resource + "/versions"));
                }
            }
        }

        private static bool IgnoreReadonly(Transaction transaction, XRegistryTarget target)
        {
            if (!transaction.IgnoredAspects.Contains("readonly") ||
                target.Kind is not (XRegistryEntityKind.Resource or XRegistryEntityKind.Meta
                    or XRegistryEntityKind.Version or XRegistryEntityKind.Versions))
            {
                return false;
            }
            string resource = target.Kind switch
            {
                XRegistryEntityKind.Resource => target.Path,
                XRegistryEntityKind.Meta or XRegistryEntityKind.Versions => Parent(target.Path),
                _ => Parent(Parent(target.Path))
            };
            if (transaction.Entries[resource + "/meta"] is not JsonObject entry ||
                (Metadata(entry)["xref"] is null && !XRegistryModelRules.Boolean(Metadata(entry)["readonly"])))
            {
                return false;
            }
            if (target.Path == transaction.Request.Path)
            {
                throw new XRegistryRejectionException(
                    "bad_flag", "Ignoring the only addressed read-only entity invalidates the request.");
            }
            transaction.IgnoredPaths.Add(target.Path);
            return true;
        }
    }
}
