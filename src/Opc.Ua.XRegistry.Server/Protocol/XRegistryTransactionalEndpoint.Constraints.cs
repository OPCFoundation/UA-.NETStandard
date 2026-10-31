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

using System.Collections.Generic;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Server.Protocol
{
    public sealed partial class XRegistryTransactionalEndpoint
    {
        private static JsonObject? ConstrainedAttributes(Transaction transaction, XRegistryTarget target)
        {
            string resource = Parent(Parent(target.Path));
            string group = Parent(Parent(resource));
            ArrayOf<string> segments = XRegistryPath.GetSegments(resource);
            var model = new XRegistryModelRules(transaction.ProposedModel is null
                ? SnapshotModel(transaction.Snapshot) : (JsonObject)transaction.ProposedModel.DeepClone());
            JsonObject definition = XRegistryModelRules.Object(model.CompleteModel()["groups"]?[segments[0]]);
            return XRegistryConstraintRules.Apply(
                definition, Metadata(GetEntry(transaction, group))["constraints"] as JsonObject,
                segments[2], target.Definition["attributes"] as JsonObject);
        }

        private static JsonObject EffectiveConstraints(JsonObject? declared, JsonObject? instance, string resource)
        {
            return XRegistryConstraintRules.Effective(declared, instance, resource);
        }

        private static void ValidateConstraintDeclarations(Transaction transaction, XRegistryModelRules model)
        {
            JsonObject complete = model.CompleteModel();
            foreach ((string path, JsonNode? value) in transaction.Entries)
            {
                if (model.Resolve(path).Kind == XRegistryEntityKind.Group)
                {
                    string groupType = XRegistryPath.GetSegments(path)[0];
                    JsonNode? constraints = value?["metadata"]?["constraints"];
                    if (constraints is not (null or JsonObject))
                    {
                        throw new XRegistryRejectionException(
                            "constraint_failure", "Group constraints must be an object.");
                    }
                    XRegistryConstraintRules.ValidateGroup(XRegistryModelRules.Object(complete["groups"]?[groupType]),
                        constraints as JsonObject);
                }
            }
        }

        private static void ValidateResourceState(Transaction transaction, XRegistryModelRules model)
        {
            foreach (string resource in transaction.Resources)
            {
                if (transaction.Entries[resource + "/meta"] is not JsonObject entry ||
                    Metadata(entry)["xref"] is not null)
                {
                    continue;
                }
                List<string> versions = Children(transaction, resource + "/versions");
                if (versions.Count != 0)
                {
                    JsonObject definition = model.Resolve(resource).Definition;
                    ValidateMatchingAttributes(transaction, versions, definition["attributes"] as JsonObject);
                    ValidateGroupConstraints(transaction, model, resource, versions);
                }
            }
        }
    }
}
