/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

#if NET10_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using RegistryTypes = Opc.Ua.XRegistry.ObjectTypeIds;
using WotTypes = Opc.Ua.WotCon.ObjectTypeIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// An address-space service seam below the real named-session manager and generated clients.
    /// Unconfigured method calls fail, so automatic follow-up operations cannot pass unnoticed.
    /// </summary>
    internal sealed class RemoteModelTestSession : IAsyncDisposable
    {
        private RemoteModelTestSession(
            bool includeWot, bool includeRegistry, MessageSecurityMode mode, bool emptyFactory)
        {
            var services = new ServiceCollection();
            Options = new OpcUaMcpOptions();
            services.AddOpcUaMcpCore(Options);
            services.AddTransient<WotTools>();
            services.AddTransient<XRegistryTools>();
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaWotConTools(McpToolProfile.Wot)
                .WithOpcUaXRegistryTools(McpToolProfile.XRegistry);
            Services = services.BuildServiceProvider();
            Manager = Services.GetRequiredService<OpcUaSessionManager>();
            Context = emptyFactory
                ? ServiceMessageContext.CreateEmpty(Manager.Telemetry)
                : ServiceMessageContext.Create(Manager.Telemetry);
            if (includeRegistry)
            {
                Context.NamespaceUris.GetIndexOrAppend(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri);
            }
            if (includeWot)
            {
                Context.NamespaceUris.GetIndexOrAppend(WotCon.Namespaces.WotCon);
            }
            InstanceNamespace = Context.NamespaceUris.GetIndexOrAppend("urn:test:remote-model");
            Session = new Mock<ISession>(MockBehavior.Strict);
            Cache = new Mock<INodeCache>(MockBehavior.Strict);
            Session.SetupGet(value => value.MessageContext).Returns(Context);
            Session.SetupGet(value => value.NamespaceUris).Returns(Context.NamespaceUris);
            Session.SetupGet(value => value.NodeCache).Returns(Cache.Object);
            Session.SetupGet(value => value.Connected).Returns(true);
            Session.SetupGet(value => value.SessionName).Returns("remote-model-test");
            Session.SetupGet(value => value.SessionId).Returns(Id("session"));
            Session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null, new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://unused.invalid:4840",
                    SecurityMode = mode,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }, EndpointConfiguration.Create()));
            Cache.Setup(value => value.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId actual, NodeId expected, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(actual == expected || m_subtypes.Contains((actual, expected)));
                });
            SetupServices();
        }

        public static async Task<RemoteModelTestSession> CreateAsync(
            bool includeWot = true,
            bool includeRegistry = true,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            bool emptyFactory = false)
        {
            var fixture = new RemoteModelTestSession(includeWot, includeRegistry, mode, emptyFactory);
            await fixture.Manager.RegisterExistingSessionAsync(
                "selected", fixture.Session.Object, "Anonymous").ConfigureAwait(false);
            return fixture;
        }

        public ServiceProvider Services { get; }
        public OpcUaSessionManager Manager { get; }
        public OpcUaMcpOptions Options { get; }
        public ServiceMessageContext Context { get; }
        public Mock<ISession> Session { get; }
        public Mock<INodeCache> Cache { get; }
        public ushort InstanceNamespace { get; }
        public WotTools Wot => Services.GetRequiredService<WotTools>();
        public XRegistryTools Registry => Services.GetRequiredService<XRegistryTools>();
        public List<CallMethodRequest> Calls { get; } = [];
        public List<CancellationToken> CallTokens { get; } = [];
        public List<BrowseDescription> Browses { get; } = [];
        public List<BrowsePath> Paths { get; } = [];
        public List<ReadValueId> Reads { get; } = [];
        public List<TimestampsToReturn> ReadTimestamps { get; } = [];
        public List<string> Operations { get; } = [];
        public List<(bool Release, ByteString Point, CancellationToken Token)> Continuations { get; } = [];
        public Queue<BrowseResult> NextPages { get; } = new();
        public Dictionary<(NodeId Parent, QualifiedName Name), BrowsePathResult> Translations { get; } = [];
        public Dictionary<(NodeId Node, uint Attribute), DataValue> Values { get; } = [];
        public Dictionary<(NodeId Node, NodeId Reference, BrowseDirection Direction), BrowseResult> BrowseResults
        { get; } = [];

        public Func<CallMethodRequest, CallMethodResult> OnCall { get; set; } =
            request => throw new InvalidOperationException($"Unexpected method: {request.ObjectId}/{request.MethodId}");

        public Func<ReadValueId, DataValue>? OnRead { get; set; }
        public StatusCode CallServiceStatus { get; set; } = StatusCodes.Good;

        public NodeId Id(string name)
        {
            return new(name, InstanceNamespace);
        }

        public NodeId Model(ExpandedNodeId id)
        {
            return ExpandedNodeId.ToNodeId(id, Context.NamespaceUris);
        }

        public QualifiedName Name(string uri, string name)
        {
            return new(name, checked((ushort)Context.NamespaceUris.GetIndex(uri)));
        }

        public void Subtype(NodeId actual, ExpandedNodeId expected)
        {
            m_subtypes.Add((actual, Model(expected)));
        }

        public void Type(NodeId node, ExpandedNodeId type)
        {
            Browse(node, ReferenceTypeIds.HasTypeDefinition,
                [new ReferenceDescription { NodeId = Model(type), NodeClass = NodeClass.ObjectType }]);
        }

        public void Browse(
            NodeId node, NodeId reference, ArrayOf<ReferenceDescription> entries,
            BrowseDirection direction = BrowseDirection.Forward, ByteString continuation = default)
        {
            NodeId defaultReference = reference == ReferenceTypeIds.HierarchicalReferences
                ? ReferenceTypeIds.Organizes : reference;
            BrowseResults[(node, reference, direction)] = new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = entries.ConvertAll(entry => BindReference(entry, defaultReference, direction)),
                ContinuationPoint = continuation
            };
        }

        public ReferenceDescription Reference(NodeId node, ExpandedNodeId type, string name = "entry")
        {
            return new ReferenceDescription
            {
                NodeId = node,
                TypeDefinition = Model(type),
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName(name, InstanceNamespace),
                DisplayName = new LocalizedText("en", name),
                IsForward = true
            };
        }

        public void Child(NodeId parent, QualifiedName name, NodeId child)
        {
            Translations[(parent, name)] = new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = [new BrowsePathTarget { TargetId = child, RemainingPathIndex = uint.MaxValue }]
            };
        }

        public NodeId Property(NodeId parent, QualifiedName name, Variant value)
        {
            return Property(parent, name, new DataValue(value));
        }

        public NodeId Property(NodeId parent, QualifiedName name, DataValue value)
        {
            NodeId child = Id($"{parent}/{name}");
            Child(parent, name, child);
            Values[(child, Attributes.Value)] = value;
            return child;
        }

        public void RegistryHierarchy()
        {
            NodeId registry = Id("registry");
            NodeId group = Id("group");
            NodeId resource = Id("resource");
            NodeId versions = Id("versions");
            NodeId version = Id("version-v7");
            Type(registry, RegistryTypes.RegistryType);
            Type(group, RegistryTypes.GroupType);
            Type(resource, RegistryTypes.ResourceType);
            Type(versions, RegistryTypes.ResourceVersionsType);
            Type(version, RegistryTypes.ResourceType);
            Browse(resource, ReferenceTypeIds.Organizes,
                [Reference(group, RegistryTypes.GroupType)], BrowseDirection.Inverse);
            Browse(version, ReferenceTypeIds.Organizes,
                [Reference(versions, RegistryTypes.ResourceVersionsType)], BrowseDirection.Inverse);
            Browse(versions, ReferenceTypeIds.HasComponent,
                [Reference(resource, RegistryTypes.ResourceType)], BrowseDirection.Inverse);
            ReferenceDescription versionsReference = Reference(
                versions, RegistryTypes.ResourceVersionsType, "Versions");
            versionsReference.BrowseName = Name(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Versions");
            Browse(resource, ReferenceTypeIds.HasComponent,
                [versionsReference]);
            Browse(registry, ReferenceTypeIds.Organizes, [Reference(group, RegistryTypes.GroupType, "things")]);
            Browse(group, ReferenceTypeIds.Organizes, [Reference(resource, RegistryTypes.ResourceType, "pump")]);
            Browse(versions, ReferenceTypeIds.Organizes, [Reference(version, RegistryTypes.ResourceType, "v7")]);
            Child(resource, Name(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Versions"), versions);
            Child(resource, Name(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "MetaLabels"), Id("meta-labels"));
            Child(version, Name(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Labels"), Id("version-labels"));
            Child(group, Name(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Labels"), Id("group-labels"));
            Child(registry, Name(XRegistry.XRegistryWellKnown.XRegistryNamespaceUri, "Labels"), Id("registry-labels"));
        }

        public void WotRoots(bool legacy = true, bool registry = true)
        {
            if (legacy)
            {
                Child(ObjectIds.ObjectsFolder, Name(WotCon.Namespaces.WotCon, "WoTAssetConnectionManagement"),
                    Id("management"));
                Type(Id("management"), WotTypes.WoTAssetConnectionManagementType);
            }
            if (registry)
            {
                Child(ObjectIds.Server, Name(WotCon.Namespaces.WotCon, "WoTRegistry"), Id("wot-registry"));
                Type(Id("wot-registry"), WotTypes.WoTRegistryType);
            }
            Type(Id("document"), WotTypes.WoTDocumentType);
        }

        public void Asset()
        {
            Child(Id("asset"), Name(WotCon.Namespaces.WotCon, "WoTFile"), Id("td-file"));
            Values[(Id("asset"), Attributes.DisplayName)] = new DataValue(
                Variant.From(new LocalizedText("en", "Pump asset")));
            Type(Id("asset"), ObjectTypeIds.BaseObjectType);
            Property(Id("td-file"), new QualifiedName("Size"), Variant.From(73UL));
            Property(Id("td-file"), new QualifiedName("Writable"), Variant.From(true));
            Property(Id("td-file"), new QualifiedName("UserWritable"), Variant.From(false));
            Property(Id("td-file"), new QualifiedName("OpenCount"), Variant.From((ushort)0));
        }

        public void Reply(StatusCode status, params Variant[] output)
        {
            OnCall = _ => new CallMethodResult { StatusCode = status, OutputArguments = output.ToArrayOf() };
        }

        public void AssertCall(NodeId owner, ExpandedNodeId method, params Variant[] arguments)
        {
            Assert.That(Calls, Has.Count.EqualTo(1));
            Assert.That(Calls[0].ObjectId, Is.EqualTo(owner));
            Assert.That(Calls[0].MethodId, Is.EqualTo(Model(method)));
            Assert.That(Calls[0].InputArguments.ToArray(), Is.EqualTo(arguments));
        }

        public static JsonElement Payload(CallToolResult result)
        {
            Assert.That(result.StructuredContent.HasValue, Is.True, "A tool must return its structured envelope.");
            JsonElement json = result.StructuredContent!.Value;
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo(json.GetRawText()));
            return json;
        }

        public static void Error(CallToolResult result, StatusCode status)
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(Payload(result).GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(status.Code));
        }

        public static void ArgumentError(CallToolResult result)
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(Payload(result).GetProperty("errorType").GetString(),
                Is.AnyOf(
                    nameof(ArgumentException), nameof(ArgumentOutOfRangeException), nameof(ArgumentNullException)));
        }

        public async Task<CallToolResult> InvokeAsync(string name, JsonObject arguments)
        {
            arguments["sessionName"] = "selected";
            using var json = JsonDocument.Parse(arguments.ToJsonString());
            McpServerTool tool = Services.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == name);
            var request = new RequestContext<CallToolRequestParams>(
                Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("remote-test") },
                new CallToolRequestParams
                {
                    Name = name,
                    Arguments = json.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone())
                })
            {
                MatchedPrimitive = tool
            };
            return await tool.InvokeAsync(request, CancellationToken.None).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            return Services.DisposeAsync();
        }

        private void SetupServices()
        {
            Session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<BrowsePath> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Operations.Add("translate");
                    var results = new BrowsePathResult[requests.Count];
                    for (int index = 0; index < requests.Count; index++)
                    {
                        BrowsePath path = requests[index];
                        Paths.Add(path);
                        Assert.That(path.RelativePath.Elements.Count, Is.EqualTo(1));
                        results[index] = Translations.TryGetValue(
                            (path.StartingNode, path.RelativePath.Elements[0].TargetName), out BrowsePathResult? result)
                            ? result
                            : new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                    }
                    return ValueTask.FromResult(new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results.ToArrayOf()
                    });
                });
            Session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ViewDescription? _, uint _, ArrayOf<BrowseDescription> requests,
                    CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Operations.Add("browse");
                    var results = new BrowseResult[requests.Count];
                    for (int index = 0; index < requests.Count; index++)
                    {
                        BrowseDescription request = requests[index];
                        Browses.Add(request);
                        results[index] = BrowseResponseFor(request);
                    }
                    return ValueTask.FromResult(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results.ToArrayOf()
                    });
                });
            Session.Setup(value => value.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, bool release, ArrayOf<ByteString> points, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(points.Count, Is.EqualTo(1));
                    Continuations.Add((release, points[0], token));
                    return ValueTask.FromResult(new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [release ? new BrowseResult { StatusCode = StatusCodes.Good } : NextPages.Dequeue()]
                    });
                });
            Session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, double _, TimestampsToReturn timestamps, ArrayOf<ReadValueId> requests,
                    CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Operations.Add("read");
                    ReadTimestamps.Add(timestamps);
                    var values = new DataValue[requests.Count];
                    for (int index = 0; index < requests.Count; index++)
                    {
                        ReadValueId request = requests[index];
                        Reads.Add(request);
                        values[index] = OnRead is not null ? OnRead(request) : Read(request);
                    }
                    return ValueTask.FromResult(new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = values.ToArrayOf()
                    });
                });
            Session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Operations.Add("call");
                    Assert.That(requests.Count, Is.EqualTo(1), "A tool must not batch hidden business operations.");
                    Calls.Add(requests[0]);
                    CallTokens.Add(token);
                    return ValueTask.FromResult(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = CallServiceStatus },
                        Results = [OnCall(requests[0])]
                    });
                });
        }

        private DataValue Read(ReadValueId request)
        {
            if (Values.TryGetValue((request.NodeId, request.AttributeId), out DataValue value))
            {
                return value;
            }
            return request.AttributeId switch
            {
                Attributes.NodeId => new DataValue(Variant.From(request.NodeId)),
                Attributes.NodeClass => new DataValue(Variant.From((int)NodeClass.Object)),
                Attributes.BrowseName => new DataValue(Variant.From(new QualifiedName("Asset", InstanceNamespace))),
                Attributes.DisplayName or Attributes.Description =>
                    new DataValue(Variant.From(new LocalizedText("en", "Pump asset"))),
                Attributes.WriteMask or Attributes.UserWriteMask => new DataValue(Variant.From(0u)),
                Attributes.EventNotifier => new DataValue(Variant.From((byte)0)),
                _ => DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid)
            };
        }

        private BrowseResult BrowseResponseFor(BrowseDescription request)
        {
            if (BrowseResults.TryGetValue(
                (request.NodeId, request.ReferenceTypeId, request.BrowseDirection), out BrowseResult? exact))
            {
                return exact;
            }
            var response = new BrowseResult { StatusCode = StatusCodes.Good, References = [] };
            if (request.ReferenceTypeId != ReferenceTypeIds.HierarchicalReferences)
            {
                return response;
            }
            var references = new List<ReferenceDescription>();
            foreach (KeyValuePair<(NodeId Node, NodeId Reference, BrowseDirection Direction), BrowseResult> entry
                in BrowseResults)
            {
                if (entry.Key.Node != request.NodeId ||
                    entry.Key.Direction != request.BrowseDirection ||
                    !IsHierarchicalReference(entry.Key.Reference))
                {
                    continue;
                }
                BrowseResult result = entry.Value;
                if (StatusCode.IsBad(result.StatusCode))
                {
                    return result;
                }
                if (result.StatusCode != StatusCodes.Good)
                {
                    response.StatusCode = result.StatusCode;
                }
                if (!result.ContinuationPoint.IsEmpty)
                {
                    if (!response.ContinuationPoint.IsEmpty && response.ContinuationPoint != result.ContinuationPoint)
                    {
                        throw new InvalidOperationException(
            "Provide an explicit HierarchicalReferences page when typed fixtures have different continuation points.");
                    }
                    response.ContinuationPoint = result.ContinuationPoint;
                }
                for (int index = 0; index < result.References.Count; index++)
                {
                    ReferenceDescription reference = result.References[index];
                    if (request.NodeClassMask == 0 || (request.NodeClassMask & (uint)reference.NodeClass) != 0)
                    {
                        references.Add(BindReference(reference, entry.Key.Reference, request.BrowseDirection));
                    }
                }
            }
            response.References = references.ToArrayOf();
            return response;
        }

        private bool IsHierarchicalReference(NodeId reference)
        {
            return reference == ReferenceTypeIds.Organizes ||
                reference == ReferenceTypeIds.HasComponent ||
                reference == ReferenceTypeIds.HasNotifier ||
                reference == ReferenceTypeIds.HasEventSource ||
                reference == ReferenceTypeIds.HasProperty ||
                m_subtypes.Contains((reference, ReferenceTypeIds.HierarchicalReferences)) ||
                m_subtypes.Contains((reference, ReferenceTypeIds.Organizes)) ||
                m_subtypes.Contains((reference, ReferenceTypeIds.HasComponent)) ||
                m_subtypes.Contains((reference, ReferenceTypeIds.HasNotifier));
        }

        private static ReferenceDescription BindReference(
            ReferenceDescription reference, NodeId defaultType, BrowseDirection direction)
        {
            return new ReferenceDescription
            {
                NodeId = reference.NodeId,
                TypeDefinition = reference.TypeDefinition,
                BrowseName = reference.BrowseName,
                DisplayName = reference.DisplayName,
                NodeClass = reference.NodeClass,
                ReferenceTypeId = reference.ReferenceTypeId.IsNull ? defaultType : reference.ReferenceTypeId,
                IsForward = direction == BrowseDirection.Forward
            };
        }

        private readonly HashSet<(NodeId Actual, NodeId Expected)> m_subtypes = [];
    }
}
#endif
