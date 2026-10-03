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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    public sealed partial class XRegistrySynchronizer
    {
        private async ValueTask CopyAssignedVersionAsync(
            Pass pass, XRegistrySyncObservation selected, XRegistrySyncSide side, CancellationToken ct)
        {
            XRegistrySyncInventory origin = pass.Inventory(Other(side));
            XRegistrySyncInventory target = pass.Inventory(side);
            IXRegistryEndpoint endpoint = side == XRegistrySyncSide.OpcUa ? m_opcUa : m_http;
            string resource = XRegistrySyncModel.ResourcePath(selected.Path);
            if (!pass.Complete ||
                !Qualified(target) ||
                endpoint is not IXRegistryPreparedEndpoint ||
                target.Description is not { SupportsPreparedMutations: true })
            {
                Unsupported(pass, selected.Path,
                    "Assigned Version IDs require a prepared response that pins the assigned identity before commit.");
                return;
            }
            XRegistrySyncObservation? source = selected.Kind == XRegistrySyncEntityKind.Version ? selected :
                origin.Entries.Values.Where(value => value.Kind == XRegistrySyncEntityKind.Version &&
                    Within(value.Path, resource) &&
                    Find(target, value.Path) is null)
                    .OrderBy(value => value.Path, StringComparer.Ordinal)
                    .FirstOrDefault(value => AssignedAncestorAvailable(pass, side, value));
            XRegistrySyncObservation? meta = Find(origin, resource + "/meta");
            if (source is null ||
                meta is null ||
                meta.Metadata.TryGetProperty("xref", out _) ||
                !AssignedAncestorAvailable(pass, side, source) ||
                pass.State.VersionCorrespondences.ContainsKey(source.Path))
            {
                Unsupported(
                    pass, selected.Path,
                    "Assigned identities need a known ancestor and cannot implicitly rebind " +
                    "an existing correspondence.");
                return;
            }
            string parentPath = XRegistryPath.FromSegments(XRegistryPath.GetSegments(resource).Span[..2].ToArray());
            XRegistrySyncObservation? parent = Find(target, parentPath);
            if (parent is null)
            {
                Unsupported(pass, source.Path, "Reconcile the Group before creating an assigned Version.");
                return;
            }
            XRegistrySyncObservation? targetMeta = Find(target, resource + "/meta");
            if (targetMeta is not null &&
                targetMeta.Fingerprint != meta.Fingerprint &&
                (!pass.State.Baselines.TryGetValue(targetMeta.Path, out XRegistrySyncBaseline? baseline) ||
                    targetMeta.Fingerprint != (side == XRegistrySyncSide.OpcUa ? baseline.OpcUa : baseline.Http)
                        .Fingerprint))
            {
                AddConflict(pass, targetMeta.Path, "dependency_conflict",
                    "An independently changed Resource Meta blocks assigned creation.");
                return;
            }
            if (!TakeOperation(pass, source.Path))
            {
                return;
            }
            if (pass.DryRun)
            {
                pass.Records.Add(new XRegistrySyncRecord(source.Path, XRegistrySyncRecordKind.Planned,
                    "Would prepare an assigned Version and retain its verified correspondence before commit."));
                return;
            }
            JsonObject body = Payload(origin.Model!, source);
            string id = XRegistryPath.GetSegments(source.Path)[5];
            if (body["ancestorid"]?.GetValue<string>() == id)
            {
                body["ancestorid"] = "request";
            }
            JsonObject metaBody = XRegistrySyncJson.Object(meta.Metadata);
            bool sticky = metaBody["defaultversionsticky"]?.GetValue<bool>() == true;
            string? desiredDefault = metaBody["defaultversionid"]?.GetValue<string>();
            metaBody.Remove("defaultversionid");
            metaBody["defaultversionsticky"] = false;
            if (targetMeta is not null)
            {
                metaBody["epoch"] = JsonNode.Parse(targetMeta.Epoch);
            }
            body["meta"] = metaBody;
            XRegistrySyncDefinition definition = origin.Model!.Resolve(source.Path);
            var parameters = new List<XRegistryParameter>();
            if (definition.HasDocument)
            {
                parameters.Add(new XRegistryParameter("inline", definition.Singular));
                parameters.Add(new XRegistryParameter("binary", null));
            }
            if (sticky && desiredDefault == id)
            {
                parameters.Add(new XRegistryParameter("setdefaultversionid", "request"));
            }
            var request = new XRegistryRequest(XRegistryAction.Create, resource)
            {
                View = XRegistryView.Metadata,
                Metadata = XRegistrySyncJson.Element(body, m_options.MaximumStateBytes, m_options.MaximumJsonDepth),
                Parameters = [.. parameters]
            };
            await PerformAsync(pass, source.Path, side, XRegistrySyncIntentKind.Create, request, [source],
                [parent, .. target.Entries.Values.Where(value => Within(value.Path, resource))],
                ct, preparedWrite: true, assignsVersion: true).ConfigureAwait(false);
        }

        private static bool AssignedAncestorAvailable(
            Pass pass, XRegistrySyncSide side, XRegistrySyncObservation source)
        {
            string id = XRegistryPath.GetSegments(source.Path)[5];
            string? ancestor =
                source.Metadata.TryGetProperty("ancestorid", out JsonElement value) ? value.GetString() : null;
            return ancestor is null ||
                ancestor == id ||
                Find(pass.Inventory(side), XRegistrySyncModel.Child(
                    XRegistrySyncModel.ResourcePath(source.Path) + "/versions", ancestor)) is not null;
        }

        private static bool CaptureAssignedCorrespondence(
            Pass pass, XRegistrySyncIntent intent, XRegistryResponse preview)
        {
            if (preview.Metadata.ValueKind != JsonValueKind.Object ||
                !preview.Metadata.TryGetProperty("versionid", out JsonElement id) ||
                id.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string actual = XRegistrySyncModel.Child(intent.Request.Path + "/versions", id.GetString()!);
            string canonical = intent.Source[0].Path;
            if (pass.State.VersionCorrespondences.ContainsKey(canonical) ||
                intent.DestinationBefore.ToList().Any(value => new XRegistrySyncCorrespondence(
                    pass.State.VersionCorrespondences, intent.Destination).Address(value.Path) == actual))
            {
                return false;
            }
            var mapping = new XRegistryVersionCorrespondence(canonical,
                intent.Destination == XRegistrySyncSide.OpcUa ? actual : canonical,
                intent.Destination == XRegistrySyncSide.Http ? actual : canonical);
            if (pass.State.VersionCorrespondences.Values.Any(value =>
                value.OpcUaPath == mapping.OpcUaPath || value.HttpPath == mapping.HttpPath))
            {
                return false;
            }
            var tentative = new Dictionary<string, XRegistryVersionCorrespondence>(
                pass.State.VersionCorrespondences, StringComparer.Ordinal)
            { [canonical] = mapping };
            var translator = new XRegistrySyncCorrespondence(tentative, intent.Destination);
            translator.SetDescription(pass.Inventory(intent.Destination).Description!);
            JsonElement metadata = translator.Metadata(canonical, preview.Metadata, false);
            XRegistrySyncModel model = pass.Inventory(intent.Destination).Model!;
            XRegistrySyncDefinition definition = model.Resolve(canonical);
            ByteString document = default;
            if (definition.HasDocument && !metadata.TryGetProperty(definition.Singular + "url", out _))
            {
                if (!metadata.TryGetProperty(definition.Singular + "base64", out JsonElement encoded))
                {
                    return false;
                }
                document = ByteString.From(Convert.FromBase64String(encoded.GetString()!));
            }
            if (model.Observe(canonical, metadata, document).Fingerprint != intent.Source[0].Fingerprint)
            {
                return false;
            }
            pass.State.VersionCorrespondences.Add(canonical, mapping);
            pass.Dirty = true;
            return true;
        }
    }
}
