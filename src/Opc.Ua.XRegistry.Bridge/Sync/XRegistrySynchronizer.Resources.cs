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
        private static bool RequiresResourceClosure(
            XRegistrySyncDefinition definition, XRegistrySyncObservation source, XRegistrySyncObservation? destination)
        {
            if (definition.Kind is not (XRegistrySyncEntityKind.ResourceMeta or XRegistrySyncEntityKind.Version))
            {
                return false;
            }
            return XRegistrySyncJson.String(definition.Model, "versionmode") != "manual" ||
                definition.Model.GetProperty("maxversions").GetRawText() != "0" ||
                source.Metadata.TryGetProperty("xref", out _) ||
                destination?.Metadata.TryGetProperty("xref", out _) == true ||
                (definition.Kind == XRegistrySyncEntityKind.Version &&
                    destination is not null &&
                    !XRegistrySyncModel.AttributeEqual(source.Metadata, destination.Metadata, "ancestorid")) ||
                HasMatchingRule(definition.Model);
        }

        private static bool HasMatchingRule(JsonElement model)
        {
            if (model.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in model.EnumerateObject())
                {
                    if ((property.Name == "matchversions" && property.Value.ValueKind == JsonValueKind.True) ||
                        HasMatchingRule(property.Value))
                    {
                        return true;
                    }
                }
            }
            else if (model.ValueKind == JsonValueKind.Array)
            {
                return model.EnumerateArray().Any(HasMatchingRule);
            }
            return false;
        }

        private async ValueTask CopyResourceAsync(
            Pass pass, XRegistrySyncObservation selected, XRegistrySyncSide destinationSide, CancellationToken ct)
        {
            XRegistrySyncInventory origin = pass.Inventory(Other(destinationSide));
            XRegistrySyncInventory destination = pass.Inventory(destinationSide);
            IXRegistryEndpoint endpoint = destinationSide == XRegistrySyncSide.OpcUa ? m_opcUa : m_http;
            string resource = XRegistrySyncModel.ResourcePath(selected.Path);
            string metaPath = resource + "/meta";
            if (!pass.Complete ||
                !Qualified(destination) ||
                endpoint is not IXRegistryPreparedEndpoint ||
                destination.Description is not { SupportsPreparedMutations: true })
            {
                Unsupported(pass, selected.Path,
                    "This dependency closure requires a complete inventory and globally invalidated preparation.");
                return;
            }
            XRegistrySyncObservation? sourceMeta = Find(origin, metaPath);
            if (sourceMeta is null)
            {
                Unsupported(pass, selected.Path, "The source Resource Meta is unavailable.");
                return;
            }
            bool reference = sourceMeta.Metadata.TryGetProperty("xref", out _);
            ArrayOf<XRegistrySyncObservation> sources =
            [
                sourceMeta,
                .. origin.Entries.Values.Where(value => value.Kind == XRegistrySyncEntityKind.Version &&
                    Within(value.Path, resource)).OrderBy(value => value.Path, StringComparer.Ordinal)
            ];
            XRegistrySyncDefinition definition = destination.Model!.Resolve(metaPath);
            if (!reference && (sources.Count < 2 || !definition.Model.GetProperty("setversionid").GetBoolean()))
            {
                Unsupported(pass, selected.Path,
                    "The Resource needs complete Versions and explicit or mapped destination identities.");
                return;
            }
            XRegistrySyncObservation[] existing =
                [.. destination.Entries.Values.Where(value => Within(value.Path, resource))];
            Dictionary<string, XRegistrySyncObservation> desired =
                sources.ToList().ToDictionary(value => value.Path, StringComparer.Ordinal);
            foreach (XRegistrySyncObservation value in existing)
            {
                if (!desired.TryGetValue(value.Path, out XRegistrySyncObservation? source))
                {
                    Unsupported(pass, selected.Path,
                        "An extra destination Version must be reconciled separately before this closure.");
                    return;
                }
                if (value.Path != selected.Path &&
                    source.Fingerprint != value.Fingerprint &&
                    (!pass.State.Baselines.TryGetValue(value.Path, out XRegistrySyncBaseline? baseline) ||
                        value.Fingerprint != (destinationSide == XRegistrySyncSide.OpcUa
                            ? baseline.OpcUa : baseline.Http).Fingerprint))
                {
                    AddConflict(pass, value.Path, "dependency_conflict",
                        "An independently changed dependency cannot be overwritten by another Version's update.");
                    return;
                }
            }
            string parentPath = XRegistryPath.FromSegments(XRegistryPath.GetSegments(resource).Span[..2].ToArray());
            XRegistrySyncObservation? parent = Find(destination, parentPath);
            if (parent is null)
            {
                Unsupported(pass, selected.Path, "The destination Group must be reconciled before its Resource.");
                return;
            }
            if (!TakeOperation(pass, selected.Path))
            {
                return;
            }
            if (pass.DryRun)
            {
                pass.Records.Add(new XRegistrySyncRecord(selected.Path, XRegistrySyncRecordKind.Planned,
                    "Would prepare and verify the complete Resource dependency closure."));
                foreach (XRegistrySyncObservation source in sources)
                {
                    pass.Processed.Add(source.Path);
                }
                return;
            }
            XRegistrySyncObservation? beforeMeta = Find(destination, metaPath);
            JsonObject body = reference
                ? new JsonObject { ["meta"] = XRegistrySyncJson.Object(sourceMeta.Metadata) }
                : ResourcePayload(destination.Model, sources);
            JsonObject meta = body["meta"]!.AsObject();
            if (beforeMeta is not null && !beforeMeta.Metadata.TryGetProperty("xref", out _))
            {
                meta["epoch"] = JsonNode.Parse(beforeMeta.Epoch);
            }
            if (!reference && beforeMeta?.Metadata.TryGetProperty("xref", out _) == true)
            {
                meta["xref"] = null;
            }
            if (!reference)
            {
                JsonObject versions = body["versions"]!.AsObject();
                foreach (XRegistrySyncObservation prior in existing.Where(
                    value => value.Kind == XRegistrySyncEntityKind.Version))
                {
                    string id = XRegistryPath.GetSegments(prior.Path)[5];
                    versions[id]!["epoch"] = JsonNode.Parse(prior.Epoch);
                }
            }
            bool documents = definition.Model.GetProperty("hasdocument").GetBoolean();
            var request = new XRegistryRequest(XRegistryAction.Replace, resource)
            {
                View = XRegistryView.Metadata,
                Metadata = XRegistrySyncJson.Element(body, m_options.MaximumStateBytes, m_options.MaximumJsonDepth),
                Parameters = reference ? [new("inline", "meta")] :
                    [new("inline", documents ? "meta,versions." + definition.Singular : "meta,versions"),
                        new("binary", null)]
            };
            await PerformAsync(pass, metaPath, destinationSide,
                beforeMeta is null ? XRegistrySyncIntentKind.Create : XRegistrySyncIntentKind.Replace,
                request, sources, [parent, .. existing], ct, preparedWrite: true).ConfigureAwait(false);
        }

        private static bool PreparedResourceMatches(Pass pass, XRegistrySyncIntent intent, XRegistryResponse preview)
        {
            if (preview.Metadata.ValueKind != JsonValueKind.Object ||
                !preview.Metadata.TryGetProperty("meta", out JsonElement meta))
            {
                return false;
            }
            XRegistrySyncModel model = pass.Inventory(intent.Destination).Model!;
            XRegistrySyncObservation expectedMeta = intent.Source[0];
            if (model.Observe(expectedMeta.Path, meta, default).Fingerprint != expectedMeta.Fingerprint)
            {
                return false;
            }
            if (expectedMeta.Metadata.TryGetProperty("xref", out _))
            {
                return true;
            }
            if (!preview.Metadata.TryGetProperty("versions", out JsonElement versions) ||
                versions.ValueKind != JsonValueKind.Object ||
                versions.EnumerateObject().Count() != intent.Source.Count - 1)
            {
                return false;
            }
            foreach (XRegistrySyncObservation source in intent.Source.ToList().Skip(1))
            {
                string id = XRegistryPath.GetSegments(source.Path)[5];
                if (!versions.TryGetProperty(id, out JsonElement version))
                {
                    return false;
                }
                XRegistrySyncDefinition definition = model.Resolve(source.Path);
                ByteString bytes = default;
                if (definition.HasDocument && !version.TryGetProperty(definition.Singular + "url", out _))
                {
                    if (!version.TryGetProperty(definition.Singular + "base64", out JsonElement encoded))
                    {
                        return false;
                    }
                    bytes = ByteString.From(Convert.FromBase64String(encoded.GetString()!));
                }
                if (model.Observe(source.Path, version, bytes).Fingerprint != source.Fingerprint)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
