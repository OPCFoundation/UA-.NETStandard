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

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private async ValueTask PrepareModelMutationAsync(
            Transaction transaction, XRegistryTarget target, JsonObject? body, CancellationToken ct)
        {
            if (transaction.Request.Action is not (XRegistryAction.Replace or XRegistryAction.Merge))
            {
                return;
            }
            if (target.Kind == XRegistryEntityKind.Registry &&
                body?.ContainsKey("modelsource") == true &&
                body["modelsource"] is not JsonObject)
            {
                throw new XRegistryRejectionException("invalid_model", "modelsource must be a JSON object.");
            }
            JsonObject? source = target.Kind == XRegistryEntityKind.Special && target.Singular == "modelsource" ? body :
                target.Kind == XRegistryEntityKind.Registry ? body?["modelsource"] as JsonObject : null;
            if (source is null)
            {
                return;
            }
            source = transaction.Request.Action == XRegistryAction.Merge
                ? MergeModel(XRegistryModelRules.Object(transaction.Snapshot["modelsource"]), source)
                : (JsonObject)source.DeepClone();
            var expansion = new XRegistryModelExpansion(m_options);
            transaction.ProposedModel = await expansion.ExpandAsync(source, ct)
                .ConfigureAwait(false);
            transaction.ProposedSource = source;
            transaction.ProposedOrigins = expansion.ResourceOrigins;
        }

        private static void ApplyProposedModel(Transaction transaction, XRegistryModelRules model)
        {
            if (transaction.ProposedModel is null)
            {
                return;
            }
            transaction.Snapshot["modelsource"] = transaction.ProposedSource!.DeepClone();
            transaction.Snapshot["resolvedmodel"] = transaction.ProposedModel.DeepClone();
            transaction.Snapshot["resourceorigins"] = transaction.ProposedOrigins!.DeepClone();
            model.Model.Clear();
            foreach ((string name, JsonNode? value) in transaction.ProposedModel)
            {
                model.Model[name] = value?.DeepClone();
            }
            foreach ((string path, _) in transaction.Entries)
            {
                if (model.Resolve(path).Kind == XRegistryEntityKind.Meta)
                {
                    transaction.Resources.Add(Parent(path));
                }
            }
            transaction.Touched.Add("/");
        }

        private static void ValidateModelState(
            Transaction transaction, JsonObject previous, XRegistryModelRules model)
        {
            if (transaction.ProposedModel is null)
            {
                return;
            }
            var prior = new XRegistryModelRules(SnapshotModel(previous));
            foreach ((string path, JsonNode? value) in transaction.Entries)
            {
                XRegistryTarget entity = model.Resolve(path);
                JsonObject entry = XRegistryModelRules.Object(value);
                if (entity.Kind == XRegistryEntityKind.Version &&
                    !XRegistryModelRules.Boolean(entity.Definition["hasdocument"], true) &&
                    (entry["document"] is not null ||
                        entry["blob"] is not null ||
                        (transaction.OriginalEntries.ContainsKey(path) &&
                            XRegistryModelRules.Boolean(prior.Resolve(path).Definition["hasdocument"], true))))
                {
                    throw new XRegistryRejectionException("hasdocument_violation",
                        "The model cannot remove domain documents from existing Versions.");
                }
                JsonObject metadata = Metadata(entry);
                if (entity.Kind == XRegistryEntityKind.Meta)
                {
                    transaction.Resources.Add(Parent(path));
                    if (metadata["xref"] is not null)
                    {
                        continue;
                    }
                }
                JsonObject? attributes = entity.Definition[
                    entity.Kind == XRegistryEntityKind.Meta ? "metaattributes" : "attributes"] as JsonObject;
                JsonObject? active = XRegistryModelRules.EffectiveAttributes(attributes, metadata);
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
                        active?.ContainsKey(name) != true &&
                        active?.ContainsKey("*") != true)
                    {
                        throw new XRegistryRejectionException("invalid_model",
                            "The resulting state has an attribute that the proposed model does not define.");
                    }
                }
                var checkedMetadata = (JsonObject)metadata.DeepClone();
                XRegistryModelRules.ValidateObject(checkedMetadata, attributes, kind);
                foreach ((string name, JsonNode? checkedValue) in checkedMetadata)
                {
                    if (!XRegistryModelRules.IsManaged(name) && !JsonNode.DeepEquals(checkedValue, metadata[name]))
                    {
                        throw new XRegistryRejectionException("invalid_model",
                            "Populate required/defaulted attributes in the same request as their model change.");
                    }
                }
            }
        }

        private static JsonObject SnapshotModel(JsonObject snapshot)
        {
            return (JsonObject)(snapshot["resolvedmodel"] ?? snapshot["modelsource"]!).DeepClone();
        }

        private static bool HasModelIncludes(JsonNode node)
        {
            return node switch
            {
                JsonObject obj => obj.ContainsKey("$include") ||
                    obj.ContainsKey("$includes") ||
                    obj.Any(pair => pair.Value is not null && HasModelIncludes(pair.Value)),
                JsonArray array => array.Any(value => value is not null && HasModelIncludes(value)),
                _ => false
            };
        }
    }
}
