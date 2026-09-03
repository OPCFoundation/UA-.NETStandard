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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// A scriptable Moq <see cref="ISession"/> that behaves like a
    /// minimal in-memory WoT Connectivity 1.1 registry: it materializes
    /// groups/resources created through the registry Methods, serves
    /// <c>TranslateBrowsePaths</c> and <c>HasTypeDefinition</c>
    /// <c>Browse</c> resolution, and commits <c>FileType</c>
    /// <c>Open</c>/<c>Write</c>/<c>Close</c> uploads as new resource
    /// versions. Used to exercise <see cref="WotRegistryClient"/> and its
    /// wrappers end-to-end without a live server.
    /// </summary>
    internal sealed class WotRegistrySessionMock
    {
        public WotRegistrySessionMock()
        {
            var telemetryMock = new Mock<ITelemetryContext>();
            var messageContext = ServiceMessageContext.Create(telemetryMock.Object);
            WotConNs = messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
            XRegistryNs = messageContext.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            RegistryNodeId = new NodeId("WoTRegistry", WotConNs);

            m_thingModelGroupType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.ThingModelGroupType, messageContext.NamespaceUris);
            m_thingDescriptionGroupType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.ThingDescriptionGroupType, messageContext.NamespaceUris);

            m_sessionMock = new Mock<ISession>(MockBehavior.Strict);
            m_sessionMock.SetupGet(s => s.MessageContext).Returns(messageContext);
            m_sessionMock.SetupGet(s => s.NamespaceUris).Returns(messageContext.NamespaceUris);

            m_sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                    {
                        var results = new BrowsePathResult[paths.Count];
                        for (int i = 0; i < paths.Count; i++)
                        {
                            results[i] = ResolvePath(paths[i]);
                        }
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = results.ToArrayOf(),
                                DiagnosticInfos = default
                            });
                    });

            m_sessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, descriptions, _) =>
                    {
                        var results = new BrowseResult[descriptions.Count];
                        for (int i = 0; i < descriptions.Count; i++)
                        {
                            results[i] = ResolveBrowse(descriptions[i]);
                        }
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });

            m_sessionMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, requests, _) =>
                    {
                        var values = new DataValue[requests.Count];
                        for (int i = 0; i < requests.Count; i++)
                        {
                            values[i] = ResolveRead(requests[i]);
                        }
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = values.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });

            m_sessionMock
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        if (ReturnEmptyCallResultsOnce)
                        {
                            ReturnEmptyCallResultsOnce = false;
                            return new ValueTask<CallResponse>(new CallResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [],
                                DiagnosticInfos = default
                            });
                        }
                        var results = new CallMethodResult[requests.Count];
                        for (int i = 0; i < requests.Count; i++)
                        {
                            CallMethodRequest req = requests[i];
                            Capture.Add(req);
                            results[i] = Dispatch(req);
                        }
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });

            NodeId Method(ExpandedNodeId id) => ExpandedNodeId.ToNodeId(id, messageContext.NamespaceUris);
            m_methods[Method(XRegistry.MethodIds.RegistryType_CreateGroup)] = OnCreateGroup;
            m_methods[Method(XRegistry.MethodIds.RegistryType_GetOrCreateGroup)] = OnGetOrCreateGroup;
            m_methods[Method(XRegistry.MethodIds.GroupType_CreateResource)] = OnCreateResource;
            m_methods[Method(XRegistry.MethodIds.GroupType_GetOrCreateResource)] = OnGetOrCreateResource;
            m_methods[Method(XRegistry.MethodIds.GroupType_Delete)] = OnDeleteGroup;
            m_methods[Method(XRegistry.MethodIds.ResourceType_Delete)] = OnDeleteResource;
            m_methods[Method(MethodIds.WoTDocumentType_Validate)] = OnValidate;
            m_methods[Method(MethodIds.WoTDocumentType_SetEnabled)] = OnSetEnabled;
            m_methods[Method(MethodIds.WoTDocumentType_SetDefaultVersion)] = OnSetDefaultVersion;
            m_methods[Method(MethodIds.WoTRegistryType_Refresh)] = OnRefresh;
            m_methods[new NodeId(Ua.Methods.FileType_Open, 0)] = OnOpen;
            m_methods[new NodeId(Ua.Methods.FileType_Write, 0)] = OnWrite;
            m_methods[new NodeId(Ua.Methods.FileType_Close, 0)] = OnClose;
            m_methods[new NodeId(Ua.Methods.FileType_Read, 0)] = OnRead;
        }

        public ISession Session => m_sessionMock.Object;

        public ushort WotConNs { get; }

        public ushort XRegistryNs { get; }

        public NodeId RegistryNodeId { get; }

        /// <summary>
        /// Every <c>CallMethodRequest</c> dispatched, in call order. Used
        /// to assert bulk-load dependency ordering.
        /// </summary>
        public List<CallMethodRequest> Capture { get; } = [];

        /// <summary>
        /// Resource ids that <c>Refresh</c> reports as
        /// <see cref="WoTOutcomeEnum.Failed"/>.
        /// </summary>
        public HashSet<string> InvalidResourceIds { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// When set, the next matching mutation call fails with this status.
        /// </summary>
        public Dictionary<NodeId, StatusCode> FailNextCallOn { get; } = [];

        /// <summary>
        /// When set, the next service call returns no method results.
        /// </summary>
        public bool ReturnEmptyCallResultsOnce { get; set; }

        /// <summary>
        /// When set, the next <c>HasTypeDefinition</c> Browse reports no
        /// references, as a server that does not expose the group's type
        /// would.
        /// </summary>
        public bool ReturnNoTypeDefinitionOnce { get; set; }

        /// <summary>
        /// Whether the mock server exposes the additive content-state capability.
        /// </summary>
        public bool ExposeContentDigest { get; set; } = true;

        /// <summary>
        /// Whether the exposed content-state field returns an unknown null value.
        /// </summary>
        public bool ReturnNullContentDigest { get; set; }

        /// <summary>
        /// Whether duplicate <c>CreateResource</c> calls can atomically claim
        /// an existing content-less Version for writing.
        /// </summary>
        public bool SupportsAtomicContentlessFill { get; set; } = true;

        /// <summary>
        /// When set, <c>HasTypeDefinition</c> Browse reports this
        /// ObjectType instead of the one matching the group's kind.
        /// </summary>
        public NodeId TypeDefinitionOverride { get; set; }

        /// <summary>
        /// Replaces the handler for <paramref name="methodId"/> so a test
        /// can script an output argument shape the in-memory registry
        /// would never produce on its own.
        /// </summary>
        public void OverrideMethod(NodeId methodId, Func<CallMethodRequest, Variant[]> handler)
        {
            m_methods[methodId] = handler;
        }

        /// <summary>
        /// Resolves <paramref name="methodId"/> against the mock session's
        /// namespace table.
        /// </summary>
        public NodeId ResolveMethodId(ExpandedNodeId methodId)
        {
            return ExpandedNodeId.ToNodeId(methodId, Session.NamespaceUris);
        }

        public ByteString ContentFor(NodeId resourceNodeId)
        {
            (_, ResourceState resource) = FindResource(resourceNodeId);
            return ByteString.From(resource.Content);
        }

        private BrowsePathResult ResolvePath(BrowsePath path)
        {
            NodeId current = path.StartingNode;
            foreach (RelativePathElement element in path.RelativePath.Elements)
            {
                NodeId next = ResolveChild(current, element.TargetName.Name ?? string.Empty);
                if (next.IsNull)
                {
                    return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch, Targets = [] };
                }
                current = next;
            }
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = new[]
                {
                    new BrowsePathTarget
                    {
                        TargetId = current,
                        RemainingPathIndex = uint.MaxValue
                    }
                }.ToArrayOf()
            };
        }

        private NodeId ResolveChild(NodeId parent, string name)
        {
            if (parent == Ua.ObjectIds.Server && name == "WoTRegistry")
            {
                return RegistryNodeId;
            }
            if (parent == RegistryNodeId && m_groups.TryGetValue(name, out GroupState? group))
            {
                return group.NodeId;
            }
            foreach (GroupState candidate in m_groups.Values)
            {
                if (parent == candidate.NodeId &&
                    candidate.Resources.TryGetValue(name, out ResourceState? resource))
                {
                    return resource.NodeId;
                }
                foreach (ResourceState versionResource in candidate.Versions)
                {
                    if (ExposeContentDigest &&
                        parent == versionResource.NodeId &&
                        string.Equals(
                            name,
                            Opc.Ua.WotCon.BrowseNames.ContentDigest,
                            StringComparison.Ordinal))
                    {
                        return versionResource.ContentDigestNodeId;
                    }
                }
            }
            return NodeId.Null;
        }

        private DataValue ResolveRead(ReadValueId request)
        {
            foreach (GroupState group in m_groups.Values)
            {
                foreach (ResourceState resource in group.Versions)
                {
                    if (ExposeContentDigest &&
                        request.NodeId == resource.ContentDigestNodeId &&
                        request.AttributeId == Attributes.Value)
                    {
                        ByteString digest = ReturnNullContentDigest
                            ? default
                            : resource.Content.Length == 0
                                ? ByteString.Empty
                                : ByteString.From(new byte[] { 1 });
                        return new DataValue(new Variant(digest), StatusCodes.Good);
                    }
                }
            }
            return new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown);
        }

        private BrowseResult ResolveBrowse(BrowseDescription description)
        {
            if (ReturnNoTypeDefinitionOnce)
            {
                ReturnNoTypeDefinitionOnce = false;
                return new BrowseResult { StatusCode = StatusCodes.Good, References = [] };
            }
            foreach (GroupState group in m_groups.Values)
            {
                if (group.NodeId == description.NodeId)
                {
                    if (description.ReferenceTypeId != Ua.ReferenceTypeIds.HasTypeDefinition)
                    {
                        return new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References = group.Resources.Values
                                .Select(resource => new ReferenceDescription
                                {
                                    ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                                    IsForward = true,
                                    NodeId = resource.NodeId,
                                    BrowseName = new QualifiedName(
                                        resource.ResourceId,
                                        WotConNs),
                                    DisplayName = new LocalizedText(resource.ResourceId),
                                    NodeClass = NodeClass.Object
                                })
                                .ToArray()
                                .ToArrayOf()
                        };
                    }
                    NodeId kindTypeId = group.Kind == WoTDocumentKindEnum.ThingModel
                        ? m_thingModelGroupType
                        : m_thingDescriptionGroupType;
                    NodeId typeId = TypeDefinitionOverride.IsNull
                        ? kindTypeId
                        : TypeDefinitionOverride;
                    return new BrowseResult
                    {
                        StatusCode = StatusCodes.Good,
                        References = new[]
                        {
                            new ReferenceDescription { NodeId = typeId }
                        }.ToArrayOf()
                    };
                }
            }
            return new BrowseResult { StatusCode = StatusCodes.Good, References = [] };
        }

        private CallMethodResult Dispatch(CallMethodRequest req)
        {
            if (FailNextCallOn.TryGetValue(req.MethodId, out StatusCode status))
            {
                FailNextCallOn.Remove(req.MethodId);
                return new CallMethodResult
                {
                    StatusCode = status,
                    InputArgumentResults = [],
                    OutputArguments = []
                };
            }
            if (!m_methods.TryGetValue(req.MethodId, out Func<CallMethodRequest, Variant[]>? handler))
            {
                throw new InvalidOperationException(
                    $"WotRegistrySessionMock: no handler registered for method id {req.MethodId}.");
            }
            Variant[] outputs;
            try
            {
                outputs = handler(req);
            }
            catch (ServiceResultException ex)
            {
                return new CallMethodResult
                {
                    StatusCode = ex.StatusCode,
                    InputArgumentResults = [],
                    OutputArguments = []
                };
            }
            return new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                InputArgumentResults = [],
                OutputArguments = outputs.ToArrayOf()
            };
        }

        private Variant[] OnCreateGroup(CallMethodRequest req)
        {
            req.InputArguments[0].TryGetValue(out string groupId);
            GroupState group = EnsureGroup(groupId);
            return [new Variant(group.NodeId)];
        }

        private Variant[] OnGetOrCreateGroup(CallMethodRequest req)
        {
            req.InputArguments[0].TryGetValue(out string groupId);
            bool existed = m_groups.ContainsKey(groupId);
            GroupState group = EnsureGroup(groupId);
            return [new Variant(group.NodeId), new Variant(!existed)];
        }

        private GroupState EnsureGroup(string groupId)
        {
            if (!m_groups.TryGetValue(groupId, out GroupState? group))
            {
                WoTDocumentKindEnum kind = string.Equals(groupId, "thingmodels", StringComparison.Ordinal)
                    ? WoTDocumentKindEnum.ThingModel
                    : WoTDocumentKindEnum.ThingDescription;
                group = new GroupState
                {
                    NodeId = new NodeId("WoTRegistry/groups/" + groupId, WotConNs),
                    GroupId = groupId,
                    Kind = kind
                };
                m_groups[groupId] = group;
            }
            return group;
        }

        private Variant[] OnCreateResource(CallMethodRequest req)
        {
            GroupState group = FindGroup(req.ObjectId);
            req.InputArguments[0].TryGetValue(out string resourceId);
            req.InputArguments[1].TryGetValue(out string versionId);
            req.InputArguments[2].TryGetValue(out bool requestFileOpen);
            ResourceState? existing = string.IsNullOrEmpty(versionId)
                ? null
                : FindVersion(group, resourceId, versionId);
            if (existing is not null)
            {
                if (!requestFileOpen ||
                    !SupportsAtomicContentlessFill ||
                    existing.Content.Length != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                }
                uint existingHandle = OpenWriteHandle(existing);
                return
                [
                    new Variant(existing.NodeId),
                    new Variant(existing.VersionId),
                    new Variant(existingHandle)
                ];
            }
            string assignedVersionId = string.IsNullOrEmpty(versionId)
                ? NextVersionId(group, resourceId)
                : versionId;
            ResourceState resource = CreateResource(group, resourceId, assignedVersionId);
            uint fileHandle = requestFileOpen ? OpenWriteHandle(resource) : 0;
            return [new Variant(resource.NodeId), new Variant(resource.VersionId), new Variant(fileHandle)];
        }

        private Variant[] OnGetOrCreateResource(CallMethodRequest req)
        {
            GroupState group = FindGroup(req.ObjectId);
            req.InputArguments[0].TryGetValue(out string resourceId);
            req.InputArguments[1].TryGetValue(out string versionId);
            req.InputArguments[2].TryGetValue(out bool requestFileOpen);
            ResourceState? resource = string.IsNullOrEmpty(versionId)
                ? group.Resources.GetValueOrDefault(resourceId)
                : FindVersion(group, resourceId, versionId);
            bool existed = resource is not null;
            if (resource is null)
            {
                string assignedVersionId = string.IsNullOrEmpty(versionId)
                    ? NextVersionId(group, resourceId)
                    : versionId;
                resource = CreateResource(group, resourceId, assignedVersionId);
            }
            uint fileHandle = requestFileOpen ? OpenWriteHandle(resource) : 0;
            return
            [
                new Variant(resource.NodeId),
                new Variant(resource.VersionId),
                new Variant(fileHandle),
                new Variant(!existed)
            ];
        }

        private ResourceState CreateResource(
            GroupState group,
            string resourceId,
            string versionId)
        {
            var resource = new ResourceState
            {
                NodeId = new NodeId(
                    "WoTRegistry/groups/" + group.GroupId + "/resources/" + resourceId +
                    "/versions/" + versionId,
                    group.NodeId.NamespaceIndex),
                ContentDigestNodeId = new NodeId(
                    "WoTRegistry/groups/" + group.GroupId + "/resources/" + resourceId +
                    "/versions/" + versionId + "/ContentDigest",
                    group.NodeId.NamespaceIndex),
                ResourceId = resourceId,
                VersionId = versionId
            };
            group.Versions.Add(resource);
            if (!group.Resources.ContainsKey(resourceId))
            {
                group.Resources[resourceId] = resource;
            }
            return resource;
        }

        private static ResourceState? FindVersion(
            GroupState group,
            string resourceId,
            string versionId)
        {
            return group.Versions.FirstOrDefault(candidate =>
                string.Equals(candidate.ResourceId, resourceId, StringComparison.Ordinal) &&
                string.Equals(candidate.VersionId, versionId, StringComparison.Ordinal));
        }

        private static string NextVersionId(GroupState group, string resourceId)
        {
            int next = 1;
            foreach (ResourceState candidate in group.Versions)
            {
                if (string.Equals(candidate.ResourceId, resourceId, StringComparison.Ordinal) &&
                    int.TryParse(candidate.VersionId, out int value))
                {
                    next = Math.Max(next, value + 1);
                }
            }
            return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private GroupState FindGroup(NodeId groupNodeId)
        {
            foreach (GroupState group in m_groups.Values)
            {
                if (group.NodeId == groupNodeId)
                {
                    return group;
                }
            }
            throw new InvalidOperationException("WotRegistrySessionMock: unknown group object id.");
        }

        private Variant[] OnDeleteGroup(CallMethodRequest req)
        {
            GroupState group = FindGroup(req.ObjectId);
            m_groups.Remove(group.GroupId);
            return [];
        }

        private Variant[] OnDeleteResource(CallMethodRequest req)
        {
            (GroupState group, ResourceState resource) = FindResource(req.ObjectId);
            group.Resources.Remove(resource.ResourceId);
            group.Versions.RemoveAll(candidate =>
                string.Equals(
                    candidate.ResourceId,
                    resource.ResourceId,
                    StringComparison.Ordinal));
            return [];
        }

        private Variant[] OnValidate(CallMethodRequest req)
        {
            (_, ResourceState resource) = FindResource(req.ObjectId);
            var outcome = new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = InvalidResourceIds.Contains(resource.ResourceId)
                    ? WoTOutcomeEnum.Failed
                    : WoTOutcomeEnum.Success
            };
#pragma warning disable CS0618 // Validate generated proxy expects a direct structure Variant.
            return [new Variant(outcome)];
#pragma warning restore CS0618
        }

        private Variant[] OnSetEnabled(CallMethodRequest req)
        {
            (_, ResourceState resource) = FindResource(req.ObjectId);
            req.InputArguments[0].TryGetValue(out bool enabled);
            resource.Enabled = enabled;
            return [];
        }

        private Variant[] OnSetDefaultVersion(CallMethodRequest req)
        {
            (GroupState group, ResourceState resource) = FindResource(req.ObjectId);
            req.InputArguments[0].TryGetValue(out string versionId);
            ResourceState? selected = FindVersion(group, resource.ResourceId, versionId);
            if (selected is not null)
            {
                group.Resources[resource.ResourceId] = selected;
            }
            return [];
        }

        private (GroupState, ResourceState) FindResource(NodeId resourceNodeId)
        {
            foreach (GroupState group in m_groups.Values)
            {
                foreach (ResourceState resource in group.Versions)
                {
                    if (resource.NodeId == resourceNodeId)
                    {
                        return (group, resource);
                    }
                }
            }
            throw new InvalidOperationException("WotRegistrySessionMock: unknown resource object id.");
        }

        private uint OpenWriteHandle(ResourceState resource)
        {
            uint handle = ++m_nextHandle;
            m_writeBuffers[handle] = (resource, new List<byte>());
            return handle;
        }

        private Variant[] OnOpen(CallMethodRequest req)
        {
            (_, ResourceState resource) = FindResource(req.ObjectId);
            req.InputArguments[0].TryGetValue(out byte mode);
            uint handle = ++m_nextHandle;
            if (mode == kWriteEraseExistingMode)
            {
                m_writeBuffers[handle] = (resource, new List<byte>());
            }
            else
            {
                m_readBuffers[handle] = (resource, 0);
            }
            return [new Variant(handle)];
        }

        private Variant[] OnWrite(CallMethodRequest req)
        {
            req.InputArguments[0].TryGetValue(out uint handle);
            req.InputArguments[1].TryGetValue(out ByteString data);
            (ResourceState _, List<byte> buffer) = m_writeBuffers[handle];
            buffer.AddRange(data.Span.ToArray());
            return [];
        }

        private Variant[] OnRead(CallMethodRequest req)
        {
            req.InputArguments[0].TryGetValue(out uint handle);
            req.InputArguments[1].TryGetValue(out int length);
            (ResourceState resource, int position) = m_readBuffers[handle];
            int take = Math.Min(length, resource.Content.Length - position);
            take = Math.Max(take, 0);
            byte[] chunk = new byte[take];
            Array.Copy(resource.Content, position, chunk, 0, take);
            m_readBuffers[handle] = (resource, position + take);
            return [new Variant(chunk.ToByteString())];
        }

        private Variant[] OnClose(CallMethodRequest req)
        {
            req.InputArguments[0].TryGetValue(out uint handle);
            if (m_writeBuffers.TryGetValue(handle, out (ResourceState Resource, List<byte> Buffer) entry))
            {
                entry.Resource.Content = [.. entry.Buffer];
                entry.Resource.Epoch++;
                m_writeBuffers.Remove(handle);
            }
            else
            {
                m_readBuffers.Remove(handle);
            }
            return [];
        }

        private Variant[] OnRefresh(CallMethodRequest req)
        {
            req.InputArguments[3].TryGetValue(out string requestId);
            var results = new List<WoTResourceLoadResultDataType>();
            uint succeeded = 0;
            uint failed = 0;
            foreach (GroupState group in m_groups.Values)
            {
                foreach (ResourceState resource in group.Resources.Values)
                {
                    bool isFailed = InvalidResourceIds.Contains(resource.ResourceId);
                    if (isFailed)
                    {
                        failed++;
                    }
                    else
                    {
                        succeeded++;
                    }
                    results.Add(new WoTResourceLoadResultDataType
                    {
                        GroupId = group.GroupId,
                        ResourceId = resource.ResourceId,
                        VersionId = resource.VersionId,
                        Kind = group.Kind,
                        Outcome = isFailed ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Success,
                        LoadState = isFailed ? WoTLoadStateEnum.Failed : WoTLoadStateEnum.Active
                    });
                }
            }
            uint generation = ++m_generation;
            var summary = new WoTRefreshSummaryDataType
            {
                RequestId = requestId,
                Generation = generation,
                Outcome = failed > 0 ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Success,
                Total = succeeded + failed,
                Succeeded = succeeded,
                Failed = failed
            };
            return
            [
                Variant.FromStructure(summary),
                Variant.FromStructure(results.ToArrayOf()),
                new Variant(generation)
            ];
        }

        private sealed class GroupState
        {
            public NodeId NodeId;
            public string GroupId = string.Empty;
            public WoTDocumentKindEnum Kind;
            public readonly Dictionary<string, ResourceState> Resources = new(StringComparer.Ordinal);
            public readonly List<ResourceState> Versions = [];
        }

        private sealed class ResourceState
        {
            public NodeId NodeId;
            public NodeId ContentDigestNodeId;
            public string ResourceId = string.Empty;
            public string VersionId = string.Empty;
            public byte[] Content = [];
            public uint Epoch;
            public bool Enabled = true;
        }

        private readonly Mock<ISession> m_sessionMock;
        private readonly Dictionary<NodeId, Func<CallMethodRequest, Variant[]>> m_methods = [];
        private readonly Dictionary<string, GroupState> m_groups = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, (ResourceState Resource, List<byte> Buffer)> m_writeBuffers = [];
        private readonly Dictionary<uint, (ResourceState Resource, int Position)> m_readBuffers = [];
        private readonly NodeId m_thingModelGroupType;
        private readonly NodeId m_thingDescriptionGroupType;
        private const byte kWriteEraseExistingMode = 2 | 4;
        private uint m_nextHandle;
        private uint m_generation;
    }
}
