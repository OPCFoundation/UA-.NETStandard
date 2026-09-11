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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryBridgeNodeManager
    {
        internal void ConfigureGroup(GroupState node, XRegistryNativeGroup group)
        {
            m_entities[node.NodeId] = group.Path;
            m_entityNodes[node.NodeId] = node;
            node.EventNotifier = EventNotifiers.None;
            node.GroupId!.Value = group.Id;
            node.Xid!.Value = group.Path;
            node.BrowseName = new QualifiedName(group.Path, InstanceNamespaceIndex);
            BindProperty(node.GroupId, group.Path,
                XRegistryNativeJson.RequiredText(group.Definition, "singular") + "id");
            BindEntityProperties(node.Name, node.Description, node.Epoch, node.CreatedAt, node.ModifiedAt, group.Path);
            BindLabels(node.Labels, group.Path);
            node.CreateResource!.OnCallAsync = null;
            node.CreateResource.OnCall = null;
            node.CreateResource.OnCallMethod2Async =
                (c, m, o, i, output, ct) => CreateResourceAsync(c, group, i, output, false, ct);
            node.GetOrCreateResource!.OnCallAsync = null;
            node.GetOrCreateResource.OnCall = null;
            node.GetOrCreateResource.OnCallMethod2Async =
                (c, m, o, i, output, ct) => CreateResourceAsync(c, group, i, output, true, ct);
            BindDelete(node.Delete, group.Path);
            BindMetadataFile(node, group.Path);
        }

        internal void ConfigureResource(ResourceState node, XRegistryNativeResource resource)
        {
            m_resourceMetadata[node.NodeId] = resource;
            bool logical = node.Versions is not null;
            string path = logical ? resource.Path : resource.VersionPath;
            m_entities[node.NodeId] = path;
            m_entityNodes[node.NodeId] = node;
            node.EventNotifier = EventNotifiers.None;
            node.ResourceId!.Value = resource.Id;
            node.VersionId!.Value = resource.Version;
            node.Xid!.Value = path;
            node.BrowseName = new QualifiedName(logical ? resource.Path : resource.Version, InstanceNamespaceIndex);
            if (logical)
            {
                node.Versions!.BrowseName = new QualifiedName(
                    BrowseNames.Versions,
                    Server.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri));
                BindProperty(node.MetaEpoch, resource.Path + "/meta", "epoch");
                BindProperty(node.MetaCreatedAt, resource.Path + "/meta", "createdat");
                BindProperty(node.MetaModifiedAt, resource.Path + "/meta", "modifiedat");
                BindLabels(node.MetaLabels, resource.Path + "/meta");
                BindResourceFile(node, resource, true);
            }
            BindProperty(node.ResourceId, path,
                XRegistryNativeJson.RequiredText(resource.Definition, "singular") + "id");
            BindProperty(node.VersionId, path, "versionid");
            BindProperty(node.Format, path, "format", true);
            BindProperty(node.ContentType, path, "contenttype", true);
            BindEntityProperties(node.Name, node.Description, node.Epoch, node.CreatedAt, node.ModifiedAt, path);
            BindLabels(node.Labels, path);
            BindDelete(node.Delete, path);
            BindMetadataFile(node, path);
        }

        internal XRegistryNativeFile BindResourceFile(
            ResourceState node, XRegistryNativeResource resource, bool logical)
        {
            m_resourceMetadata[node.NodeId] = resource;
            string path = logical ? resource.Path : resource.VersionPath;
            if (m_files.TryGetValue(node.NodeId, out XRegistryNativeFile? existing))
            {
                return existing;
            }
            node.AddMimeType(SystemContext).AddMaxByteStringLength(SystemContext);
            node.MaxByteStringLength!.Value = (uint)m_options.ChunkSize;
            var file = new XRegistryNativeFile(node, m_options, m_budget, m_options.MaxDocumentBytes,
                (context, mode, ct) => OpenResourceAsync(
                    context, path, logical, HasDocument(node.NodeId), mode, ct),
                CloseResourceAsync,
                mutatesEndpoint: true);
            node.Writable!.OnSimpleReadValueAsync = async (context, n, ct) =>
            {
                _ = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
                bool writable = HasDocument(node.NodeId) &&
                    Qualified(await InspectAsync(context, ct).ConfigureAwait(false));
                return new AttributeSimpleReadResult(ServiceResult.Good, Variant.From(writable));
            };
            node.UserWritable!.OnSimpleReadValueAsync = node.Writable.OnSimpleReadValueAsync;
            node.Size!.OnSimpleReadValueAsync = async (context, n, ct) =>
            {
                XRegistryFileSnapshot snapshot = await OpenResourceAsync(
                    context, path, logical, HasDocument(node.NodeId), 1, ct).ConfigureAwait(false);
                return new AttributeSimpleReadResult(ServiceResult.Good, Variant.From((ulong)snapshot.Bytes.Length));
            };
            m_files[node.NodeId] = file;
            return file;
        }

        private void ConfigureRegistry(XRegistryNativeSnapshot snapshot)
        {
            RegistryState node = m_registry!;
            m_entities[node.NodeId] = "/";
            m_entityNodes[node.NodeId] = node;
            node.EventNotifier = EventNotifiers.None;
            node.RegistryId!.Value = snapshot.Description.RegistryId;
            node.SpecVersion!.Value = XRegistryNativeJson.Text(snapshot.Root, "specversion");
            node.Xid!.Value = "/";
            node.Epoch!.Value = XRegistryNativeJson.Epoch(snapshot.Root);
            node.CreatedAt!.Value = (DateTimeUtc)XRegistryNativeJson.Timestamp(snapshot.Root, "createdat");
            node.ModifiedAt!.Value = (DateTimeUtc)XRegistryNativeJson.Timestamp(snapshot.Root, "modifiedat");
            BindProperty(node.RegistryId, "/", "registryid");
            BindProperty(node.SpecVersion, "/", "specversion");
            BindProperty(node.Epoch, "/", "epoch");
            BindProperty(node.CreatedAt, "/", "createdat");
            BindProperty(node.ModifiedAt, "/", "modifiedat");
            BindLabels(node.Labels, "/");
            BindJsonFile(node.Model!, "/model");
            BindJsonFile(node.Capabilities!, "/capabilities");
            node.CapabilitiesInfo!.OnSimpleReadValueAsync = async (context, n, ct) =>
            {
                _ = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
                JsonElement capabilities = await ReadMetadataAsync(context, "/capabilities", ct).ConfigureAwait(false);
                var summary = new RegistryCapabilitiesDataType
                {
                    Flags = StringArray(capabilities, "flags"),
                    Mutable = StringArray(capabilities, "mutable"),
                    Apis = StringArray(capabilities, "apis"),
                    SpecVersions = StringArray(capabilities, "specversions"),
                    Schemas = StringArray(capabilities, "schemas"),
                    Pagination = Boolean(capabilities, "pagination"),
                    ShortSelf = Boolean(capabilities, "shortself"),
                    StickyVersions = Boolean(capabilities, "stickyversions"),
                    EnforceCompatibility = Boolean(capabilities, "enforcecompatibility")
                };
                return new AttributeSimpleReadResult(ServiceResult.Good, Variant.FromStructure(summary));
            };
            node.CreateGroup!.OnCall = null;
            node.CreateGroup.OnCallAsync = null;
            node.CreateGroup.OnCallMethod2Async =
                (c, m, o, i, output, ct) => CreateGroupAsync(c, i, output, false, ct);
            node.GetOrCreateGroup!.OnCall = null;
            node.GetOrCreateGroup.OnCallAsync = null;
            node.GetOrCreateGroup.OnCallMethod2Async =
                (c, m, o, i, output, ct) => CreateGroupAsync(c, i, output, true, ct);
            BindMetadataFile(node, "/");
        }

        private void BindEntityProperties(
            PropertyState<string>? name, PropertyState<string>? description, PropertyState<uint>? epoch,
            PropertyState<DateTimeUtc>? created, PropertyState<DateTimeUtc>? modified, string path)
        {
            BindProperty(name, path, "name", true);
            BindProperty(description, path, "description", true);
            BindProperty(epoch, path, "epoch");
            BindProperty(created, path, "createdat");
            BindProperty(modified, path, "modifiedat");
        }

        private void BindProperty(
            BaseVariableState? property, string path, string attribute, bool writable = false, string? label = null)
        {
            if (property is null)
            {
                return;
            }
            property.StatusCode = StatusCodes.Good;
            bool canWrite = writable &&
                m_options.ProjectionContext.IsAuthenticated &&
                Qualified(m_strategy!.Snapshot.Description);
            property.AccessLevel = canWrite ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;
            property.UserAccessLevel = property.AccessLevel;
            property.OnSimpleReadValueAsync = async (context, node, ct) =>
            {
                _ = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
                JsonElement metadata = await ReadMetadataAsync(context, path, ct).ConfigureAwait(false);
                if (label is not null && !metadata.TryGetProperty("labels", out metadata))
                {
                    return new AttributeSimpleReadResult(StatusCodes.BadNoData, Variant.Null);
                }
                string name = label ?? attribute;
                if (!metadata.TryGetProperty(name, out JsonElement value) ||
                    value.ValueKind == JsonValueKind.Null)
                {
                    return new AttributeSimpleReadResult(StatusCodes.BadNoData, Variant.Null);
                }
                Variant result;
                if (property.DataType == Ua.DataTypeIds.UInt32)
                {
                    result = Variant.From(XRegistryNativeJson.Epoch(metadata));
                }
                else if (property.DataType == Ua.DataTypeIds.DateTime)
                {
                    result = Variant.From((DateTimeUtc)XRegistryNativeJson.Timestamp(metadata, attribute));
                }
                else if (value.ValueKind == JsonValueKind.String)
                {
                    result = Variant.From(value.GetString()!);
                }
                else
                {
                    return new AttributeSimpleReadResult(StatusCodes.BadTypeMismatch, Variant.Null);
                }
                return new AttributeSimpleReadResult(ServiceResult.Good, result);
            };
            property.OnSimpleWriteValueAsync = async (context, node, value, ct) =>
            {
                if (!canWrite || !value.TryGetValue(out string text))
                {
                    return new AttributeWriteResult(
                        canWrite ? StatusCodes.BadTypeMismatch : StatusCodes.BadNotWritable);
                }
                JsonObject body = label is null
                    ? new JsonObject { [attribute] = text }
                    : new JsonObject { ["labels"] = new JsonObject { [label] = text } };
                XRegistryResponse response = await ExecuteAsync(context,
                    new XRegistryRequest(XRegistryAction.Merge, path)
                    {
                        View = XRegistryView.Metadata,
                        Metadata = XRegistryNativeJson.Element(body)
                    }, ct).ConfigureAwait(false);
                return new AttributeWriteResult(XRegistryNativeJson.Status(response));
            };
        }

        private void BindLabels(AttributesState? labels, string path)
        {
            if (labels is null)
            {
                return;
            }
            labels.AddAddAttribute(SystemContext).AddRemoveAttribute(SystemContext);
            labels.AddAttribute!.OnCall = null;
            labels.AddAttribute.OnCallAsync = null;
            labels.AddAttribute.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 3 ||
                    !i[0].TryGetValue(out string key) ||
                    !i[1].TryGetValue(out string value) ||
                    !i[2].TryGetValue(out uint epoch))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                return await UpdateLabelAsync(c, path, key, value, epoch, ct).ConfigureAwait(false);
            };
            labels.RemoveAttribute!.OnCall = null;
            labels.RemoveAttribute.OnCallAsync = null;
            labels.RemoveAttribute.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 2 || !i[0].TryGetValue(out string key) || !i[1].TryGetValue(out uint epoch))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                return await UpdateLabelAsync(c, path, key, null, epoch, ct).ConfigureAwait(false);
            };
            var children = new List<BaseInstanceState>();
            labels.GetChildren(SystemContext, children);
            foreach (BaseVariableState property in children.OfType<BaseVariableState>())
            {
                BindProperty(property, path, "labels", true, property.BrowseName.Name);
            }
        }

        private async ValueTask<ServiceResult> UpdateLabelAsync(
            ISystemContext context, string path, string key, string? value, uint epoch, CancellationToken ct)
        {
            var metadata = new JsonObject { ["labels"] = new JsonObject { [key] = value } };
            if (epoch != 0)
            {
                metadata["epoch"] = epoch;
            }
            XRegistryResponse response = await ExecuteAsync(context, new XRegistryRequest(XRegistryAction.Merge, path)
            {
                View = XRegistryView.Metadata,
                Metadata = XRegistryNativeJson.Element(metadata)
            }, ct).ConfigureAwait(false);
            return XRegistryNativeJson.Status(response);
        }

        private void BindDelete(DeleteMethodState? method, string path)
        {
            if (method is null)
            {
                return;
            }
            method.OnCall = null;
            method.OnCallAsync = null;
            method.OnCallMethod2Async = async (context, m, o, input, output, ct) =>
            {
                if (input.Count != 1 || !input[0].TryGetValue(out uint epoch))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                XRegistryResponse response = await ExecuteAsync(context,
                    new XRegistryRequest(XRegistryAction.Delete, path)
                    {
                        Parameters = epoch == 0 ? [] :
                            [new XRegistryParameter("epoch", epoch.ToString(CultureInfo.InvariantCulture))]
                    }, ct).ConfigureAwait(false);
                return XRegistryNativeJson.Status(response);
            };
        }

        private async ValueTask<ServiceResult> CreateGroupAsync(
            ISystemContext context, ArrayOf<Variant> input, List<Variant> output,
            bool getOrCreate, CancellationToken ct)
        {
            _ = await AuthorizeAsync(context, true, ct).ConfigureAwait(false);
            if (input.Count != 1 || !input[0].TryGetValue(out string id))
            {
                return StatusCodes.BadInvalidArgument;
            }
            JsonProperty[] definitions = [.. XRegistryNativeJson.RequiredObject(m_strategy!.Snapshot.Description.Model,
                "groups").EnumerateObject()];
            if (definitions.Length != 1)
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported,
                    "CreateGroup has no collection selector; use the experimental collection-aware operation.");
            }
            string collection = definitions[0].Name;
            string path = XRegistryPath.FromSegments([collection, id]);
            JsonElement root = await ReadMetadataAsync(context, "/", ct).ConfigureAwait(false);
            XRegistryResponse found = await ReadResponseAsync(context, path, ct).ConfigureAwait(false);
            bool created = found.StatusCode == 404;
            if (!created)
            {
                XRegistryNativeJson.EnsureSuccess(found);
                if (!getOrCreate)
                {
                    return StatusCodes.BadAlreadyExists;
                }
                await RefreshAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var body = new JsonObject
                {
                    ["epoch"] = XRegistryNativeJson.Epoch(root),
                    [collection] = new JsonObject { [id] = new JsonObject() }
                };
                XRegistryResponse response = await ExecuteAsync(context,
                    new XRegistryRequest(XRegistryAction.Merge, "/")
                    {
                        View = XRegistryView.Metadata,
                        Metadata = XRegistryNativeJson.Element(body)
                    }, ct).ConfigureAwait(false);
                XRegistryNativeJson.EnsureSuccess(response);
            }
            output[0] = Variant.From(FindEntity(path));
            if (getOrCreate)
            {
                output[1] = Variant.From(created);
            }
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> CreateResourceAsync(
            ISystemContext context, XRegistryNativeGroup group, ArrayOf<Variant> input,
            List<Variant> output, bool getOrCreate, CancellationToken ct)
        {
            _ = await AuthorizeAsync(context, true, ct).ConfigureAwait(false);
            if (input.Count != 3 ||
                !input[0].TryGetValue(out string id) ||
                !input[1].TryGetValue(out string version) ||
                !input[2].TryGetValue(out bool open))
            {
                return StatusCodes.BadInvalidArgument;
            }
            JsonProperty[] definitions = [.. XRegistryNativeJson.RequiredObject(group.Definition,
                "resources").EnumerateObject()];
            if (definitions.Length != 1)
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported,
                    "CreateResource has no collection selector; use the experimental operation.");
            }
            if (open &&
                definitions[0].Value.TryGetProperty("hasdocument", out JsonElement hasDocument) &&
                hasDocument.ValueKind == JsonValueKind.False)
            {
                return StatusCodes.BadNotSupported;
            }
            string path = group.Path + XRegistryPath.FromSegments([definitions[0].Name, id]);
            JsonElement parent = await ReadMetadataAsync(context, group.Path, ct).ConfigureAwait(false);
            XRegistryResponse meta = await ReadResponseAsync(context, path + "/meta", ct).ConfigureAwait(false);
            bool newResource = meta.StatusCode == 404;
            if (!newResource)
            {
                XRegistryNativeJson.EnsureSuccess(meta);
            }
            string versionPath = version.Length == 0
                ? path
                : path + "/versions" + XRegistryPath.FromSegments([version]);
            XRegistryResponse existing = newResource || version.Length == 0
                ? new XRegistryResponse(404)
                : await ReadResponseAsync(context, versionPath, ct).ConfigureAwait(false);
            bool created = existing.StatusCode == 404;
            if (!created)
            {
                XRegistryNativeJson.EnsureSuccess(existing);
                if (!getOrCreate)
                {
                    return StatusCodes.BadAlreadyExists;
                }
                await RefreshAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var versionBody = new JsonObject();
                if (version.Length != 0)
                {
                    versionBody["versionid"] = version;
                }
                XRegistryRequest request;
                if (newResource)
                {
                    request = new XRegistryRequest(XRegistryAction.Merge, group.Path)
                    {
                        View = XRegistryView.Metadata,
                        Metadata = XRegistryNativeJson.Element(new JsonObject
                        {
                            ["epoch"] = XRegistryNativeJson.Epoch(parent),
                            [definitions[0].Name] = new JsonObject { [id] = versionBody }
                        })
                    };
                }
                else
                {
                    var preservedMeta = (JsonObject)JsonNode.Parse(meta.Metadata.GetRawText())!;
                    foreach (string generated in new[]
                    {
                        "self", "xid", "defaultversionurl", "readonly", "metaurl", "versionsurl", "versionscount"
                    })
                    {
                        preservedMeta.Remove(generated);
                    }
                    versionBody["meta"] = preservedMeta;
                    request = new XRegistryRequest(XRegistryAction.Create, path)
                    {
                        View = XRegistryView.Metadata,
                        Metadata = XRegistryNativeJson.Element(versionBody)
                    };
                }
                XRegistryResponse result = await ExecuteAsync(context, request, ct).ConfigureAwait(false);
                XRegistryNativeJson.EnsureSuccess(result);
                if (version.Length == 0)
                {
                    JsonElement updated = await ReadMetadataAsync(context, path, ct).ConfigureAwait(false);
                    version = XRegistryNativeJson.RequiredText(updated, "versionid");
                }
                versionPath = path + "/versions" + XRegistryPath.FromSegments([version]);
            }
            NodeId nodeId = FindEntity(versionPath);
            uint handle = open
                ? await m_files[nodeId].OpenAsync(context, 2, ct).ConfigureAwait(false)
                : 0;
            output[0] = Variant.From(nodeId);
            output[1] = Variant.From(version);
            output[2] = Variant.From(handle);
            if (getOrCreate)
            {
                output[3] = Variant.From(created);
            }
            return ServiceResult.Good;
        }

        private async ValueTask<XRegistryFileSnapshot> OpenResourceAsync(
            ISystemContext context, string path, bool logical, bool hasDocument, byte mode, CancellationToken ct)
        {
            XRegistryCallContext caller = await AuthorizeAsync(context, (mode & 2) != 0, ct).ConfigureAwait(false);
            if (!hasDocument)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "This resource type has no document.");
            }
            if ((mode & 2) != 0 && !Qualified(await InspectAsync(context, ct).ConfigureAwait(false)))
            {
                throw new ServiceResultException(StatusCodes.BadNotWritable);
            }
            string pinned = path;
            if (logical)
            {
                JsonElement meta = await ReadMetadataAsync(context, path + "/meta", ct).ConfigureAwait(false);
                pinned += "/versions" +
                    XRegistryPath.FromSegments(
                        [XRegistryNativeJson.RequiredText(meta, "defaultversionid")]);
            }
            XRegistryResponse response = await m_endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, pinned)
                {
                    Context = caller
                }, ct).ConfigureAwait(false);
            XRegistryNativeJson.EnsureSuccess(response);
            if (response.Document.IsNull || response.Document.Length > m_options.MaxDocumentBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The endpoint did not return a bounded document.");
            }
            uint epoch = XRegistryNativeJson.Epoch(response.Metadata);
            var guard = new JsonObject { ["epoch"] = epoch };
            if (response.Metadata.TryGetProperty("createdat", out JsonElement created))
            {
                guard["createdat"] = JsonNode.Parse(created.GetRawText());
            }
            return new XRegistryFileSnapshot(response.Document,
                new XRegistryRequest(XRegistryAction.Replace, pinned)
                {
                    View = XRegistryView.Default,
                    Metadata = XRegistryNativeJson.Element(guard),
                    ContentType = response.ContentType
                });
        }

        private async ValueTask<ServiceResult> CloseResourceAsync(
            ISystemContext context, XRegistryFileSnapshot snapshot, ByteString bytes, CancellationToken ct)
        {
            _ = await AuthorizeAsync(context, true, ct).ConfigureAwait(false);
            XRegistryRequest baseline = snapshot.Baseline ??
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            JsonElement current = await ReadMetadataAsync(context, baseline.Path, ct).ConfigureAwait(false);
            if (XRegistryNativeJson.Text(current, "createdat") !=
                XRegistryNativeJson.Text(baseline.Metadata, "createdat"))
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "The pinned version incarnation changed.");
            }
            XRegistryResponse response = await ExecuteAsync(context, baseline with { Document = bytes }, ct)
                .ConfigureAwait(false);
            return XRegistryNativeJson.Status(response);
        }

        private void BindMetadataFile(BaseObjectState parent, string path)
        {
            var name = new QualifiedName("Metadata",
                Server.NamespaceUris.GetIndexOrAppend(XRegistryBridgeNativeOptions.ExperimentalNamespaceUri));
            if (parent.FindChild(SystemContext, name) is FileState)
            {
                return;
            }
            FileState file = SystemContext.CreateInstanceOfFileType(parent, name);
            file.NodeId = New(SystemContext, file);
            file.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            parent.AddChild(file);
            BindJsonFile(file, path);
        }

        private void BindJsonFile(FileState node, string path)
        {
            if (m_files.ContainsKey(node.NodeId))
            {
                return;
            }
            m_files[node.NodeId] = new XRegistryNativeFile(node, m_options, m_budget, m_options.MaxMessageBytes,
                async (context, mode, ct) =>
                {
                    _ = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
                    JsonElement metadata = await ReadMetadataAsync(context, path, ct).ConfigureAwait(false);
                    return new XRegistryFileSnapshot(XRegistryNativeJson.Bytes(metadata));
                }, null);
            node.Size!.OnSimpleReadValueAsync = async (context, n, ct) =>
            {
                JsonElement metadata = await ReadMetadataAsync(context, path, ct).ConfigureAwait(false);
                return new AttributeSimpleReadResult(ServiceResult.Good,
                    Variant.From((ulong)XRegistryNativeJson.Bytes(metadata).Length));
            };
        }

        private async ValueTask<XRegistryResponse> ReadResponseAsync(
            ISystemContext context, string path, CancellationToken ct)
        {
            XRegistryCallContext caller = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
            return await m_endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, path)
            {
                View = XRegistryView.Metadata,
                Context = caller
            }, ct).ConfigureAwait(false);
        }

        private async ValueTask<JsonElement> ReadMetadataAsync(
            ISystemContext context, string path, CancellationToken ct)
        {
            XRegistryResponse response = await ReadResponseAsync(context, path, ct).ConfigureAwait(false);
            XRegistryNativeJson.EnsureSuccess(response);
            XRegistryNativeJson.RequireObject(response.Metadata);
            return response.Metadata;
        }

        private NodeId FindEntity(string path)
        {
            foreach (KeyValuePair<NodeId, string> entity in m_entities)
            {
                if (entity.Value == path)
                {
                    return entity.Key;
                }
            }
            throw new ServiceResultException(StatusCodes.BadInvalidState, "The committed entity was not projected.");
        }

        private bool HasDocument(NodeId id)
        {
            return m_resourceMetadata.TryGetValue(id, out XRegistryNativeResource? resource)
                ? resource.HasDocument
                : throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
        }

        private static ArrayOf<string> StringArray(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                return [];
            }
            if (value.ValueKind != JsonValueKind.Array ||
                value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A capability string array is invalid.");
            }
            return [.. value.EnumerateArray().Select(item => item.GetString()!)];
        }

        private static bool Boolean(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                return false;
            }
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A capability Boolean is invalid.");
            }
            return value.GetBoolean();
        }

        private readonly ConcurrentDictionary<NodeId, BaseObjectState> m_entityNodes = new();
        private readonly ConcurrentDictionary<NodeId, XRegistryNativeResource> m_resourceMetadata = new();
    }
}
