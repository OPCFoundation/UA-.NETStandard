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
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Reusable mock session harness for exercising the Vision client
    /// facades. Wires namespace tables, message context, browse-path
    /// translation, browse, read and call handlers over dictionary-backed
    /// stores so a caller can populate the address space by BrowseName
    /// and read raw <see cref="Variant"/> values without booting a real
    /// server.
    /// </summary>
    internal sealed class VisionSessionHarness
    {
        private readonly Dictionary<(NodeId Parent, string BrowseName), NodeId> m_children = [];
        private readonly Dictionary<NodeId, List<ReferenceDescription>> m_browse = [];
        private readonly Dictionary<NodeId, Variant> m_values = [];
        private readonly Dictionary<NodeId, StatusCode> m_valueStatus = [];
        private StatusCode m_callStatus = StatusCodes.Good;
        private ArrayOf<Variant> m_callOutput = ArrayOf<Variant>.Empty;

        public VisionSessionHarness()
        {
            Telemetry = new Mock<ITelemetryContext>().Object;
            NamespaceUris.GetIndexOrAppend(Opc.Ua.Namespaces.OpcUa);
            NamespaceUris.GetIndexOrAppend(global::Opc.Ua.Vision.Namespaces.Vision);
            MessageContext = ServiceMessageContext.Create(Telemetry);
            MessageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Namespaces.OpcUa);
            MessageContext.NamespaceUris.GetIndexOrAppend(global::Opc.Ua.Vision.Namespaces.Vision);
            Session.SetupGet(s => s.NamespaceUris).Returns(NamespaceUris);
            Session.SetupGet(s => s.MessageContext).Returns(MessageContext);
            Session.SetupGet(s => s.Factory).Returns(MessageContext.Factory);
            Session.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
            Session.SetupGet(s => s.ServerCapabilities).Returns(new ServerCapabilities());
            Session.SetupGet(s => s.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
            Session.SetupGet(s => s.NodeCache).Returns(NodeCache.Object);
            NodeCache
                .Setup(c => c.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            SetupTranslate();
            SetupBrowse();
            SetupRead();
            SetupCall();
            Client = new VisionClient(Session.Object, Telemetry);
        }

        public Mock<ISession> Session { get; } = new(MockBehavior.Loose);

        public Mock<INodeCache> NodeCache { get; } = new(MockBehavior.Loose);

        public ITelemetryContext Telemetry { get; }

        public NamespaceTable NamespaceUris { get; } = new();

        public ServiceMessageContext MessageContext { get; }

        public VisionClient Client { get; }

        public ushort VisionNamespaceIndex =>
            (ushort)NamespaceUris.GetIndex(global::Opc.Ua.Vision.Namespaces.Vision);

        public NodeId VisionRootId => NodeId.Create(
            Objects.Vision, global::Opc.Ua.Vision.Namespaces.Vision, NamespaceUris);

        public NodeId SensorsFolderId => NodeId.Create(
            Objects.Vision_Sensors, global::Opc.Ua.Vision.Namespaces.Vision, NamespaceUris);

        public NodeId PipelinesFolderId { get; } = new(1001u, 3);

        public NodeId FramesFolderId { get; } = new(1002u, 3);

        public NodeId SensorNodeId { get; } = new(2000u, 3);

        public NodeId PipelineNodeId { get; } = new(3000u, 3);

        public NodeId FeedbackNodeId { get; } = new(3100u, 3);

        public NodeId ResultNodeId { get; } = new(3200u, 3);

        public NodeId FrameNodeId { get; } = new(4000u, 3);

        public NodeId OpticsNodeId { get; } = new(2100u, 3);

        public NodeId IlluminationNodeId { get; } = new(2101u, 3);

        public NodeId CalibrationsFolderId { get; } = new(2200u, 3);

        public NodeId IntrinsicCalibrationNodeId { get; } = new(2201u, 3);

        public NodeId ExtrinsicCalibrationNodeId { get; } = new(2202u, 3);

        public NodeId MediaNodeId { get; } = new(2300u, 3);

        public NodeId StreamEndpointsFolderId { get; } = new(2310u, 3);

        public NodeId ClipEndpointsFolderId { get; } = new(2320u, 3);

        public NodeId StreamEndpointNodeId { get; } = new(2311u, 3);

        public NodeId ClipEndpointNodeId { get; } = new(2321u, 3);

        public NodeId ResultsFolderId { get; } = new(3300u, 3);

        public NodeId InferenceResultNodeId { get; } = new(3301u, 3);

        /// <summary>
        /// Populates the Vision root's Pipelines and Frames folders so
        /// GetPipelinesFolderIdAsync and GetFramesFolderIdAsync return
        /// non-null NodeIds when consumers call them.
        /// </summary>
        public void ConfigureVisionFolders()
        {
            AddChild(VisionRootId, BrowseNames.Pipelines, PipelinesFolderId);
            AddChild(VisionRootId, BrowseNames.Frames, FramesFolderId);
        }

        /// <summary>
        /// Populates a single sensor with the given type definition under
        /// the Vision/Sensors folder, browse-reachable both by browse-path
        /// and by hierarchical browse. Returns the sensor NodeId.
        /// </summary>
        public NodeId AddSensor(uint typeDefinition, string browseName = "Sensor1")
        {
            AddBrowse(SensorsFolderId,
                [Ref(SensorNodeId, browseName, typeDefinition)]);
            return SensorNodeId;
        }

        /// <summary>
        /// Populates a pipeline browse-reachable under Vision/Pipelines.
        /// </summary>
        public NodeId AddPipeline(string browseName = "Pipeline1")
        {
            AddBrowse(PipelinesFolderId,
                [Ref(PipelineNodeId, browseName, ObjectTypes.InferencePipelineType)]);
            return PipelineNodeId;
        }

        /// <summary>
        /// Populates a coordinate frame browse-reachable under Vision/Frames.
        /// </summary>
        public NodeId AddFrame(string browseName = "Frame1")
        {
            AddBrowse(FramesFolderId,
                [Ref(FrameNodeId, browseName, ObjectTypes.CoordinateFrameType)]);
            return FrameNodeId;
        }

        public void ConfigureCall(StatusCode status, params Variant[] outputs)
        {
            m_callStatus = status;
            m_callOutput = outputs?.ToArrayOf() ?? ArrayOf<Variant>.Empty;
        }

        public ReferenceDescription Ref(NodeId nodeId, string browseName, uint typeId)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(nodeId),
                BrowseName = new QualifiedName(browseName, VisionNamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                NodeClass = NodeClass.Object,
                TypeDefinition = new ExpandedNodeId(
                    new NodeId(typeId, VisionNamespaceIndex)),
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IsForward = true
            };
        }

        public void AddBrowse(NodeId folder, IReadOnlyList<ReferenceDescription> references)
        {
            m_browse[folder] = [.. references];
        }

        public void AppendBrowse(NodeId folder, ReferenceDescription reference)
        {
            if (!m_browse.TryGetValue(folder, out List<ReferenceDescription>? list))
            {
                list = [];
                m_browse[folder] = list;
            }
            list.Add(reference);
        }

        public void AddChild(NodeId parent, string browseName, NodeId child)
        {
            m_children[(parent, browseName)] = child;
        }

        public void AddValue(NodeId nodeId, Variant value)
        {
            m_values[nodeId] = value;
        }

        public void AddValueStatus(NodeId nodeId, StatusCode statusCode)
        {
            m_valueStatus[nodeId] = statusCode;
        }

        public void AddValueChild(NodeId parent, string browseName, NodeId nodeId, Variant value)
        {
            AddChild(parent, browseName, nodeId);
            AddValue(nodeId, value);
        }

        private void SetupTranslate()
        {
            Session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>((_, paths, _) =>
                {
                    var results = new List<BrowsePathResult>();
                    for (int ii = 0; ii < paths.Count; ii++)
                    {
                        BrowsePath path = paths[ii];
                        NodeId current = path.StartingNode;
                        bool found = true;
                        for (int jj = 0; jj < path.RelativePath.Elements.Count; jj++)
                        {
                            string name = path.RelativePath.Elements[jj].TargetName.Name ?? string.Empty;
                            if (!m_children.TryGetValue((current, name), out NodeId next))
                            {
                                found = false;
                                break;
                            }
                            current = next;
                        }
                        results.Add(found ? GoodPath(current) : BadPath());
                    }
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                });
        }

        private void SetupBrowse()
        {
            Session.Setup(s => s.BrowseAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ViewDescription>(),
                It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(),
                It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint,
                         ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, descriptions, _) =>
                    {
                        var results = new List<BrowseResult>(descriptions.Count);
                        for (int ii = 0; ii < descriptions.Count; ii++)
                        {
                            BrowseDescription description = descriptions[ii];
                            List<ReferenceDescription> refs = m_browse.TryGetValue(
                                description.NodeId, out List<ReferenceDescription>? value)
                                ? value
                                : [];
                            results.Add(new BrowseResult
                            {
                                StatusCode = StatusCodes.Good,
                                References = refs.ToArrayOf(),
                                ContinuationPoint = default
                            });
                        }
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });
            Session.Setup(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<bool>(),
                It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseNextResponse>(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good, References = [] }],
                    DiagnosticInfos = default
                }));
        }

        private void SetupRead()
        {
            Session.Setup(s => s.ReadAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(),
                It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn,
                         ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, nodes, _) =>
                    {
                        var values = new List<DataValue>();
                        for (int ii = 0; ii < nodes.Count; ii++)
                        {
                            ReadValueId node = nodes[ii];
                            if (node.AttributeId == Attributes.Value)
                            {
                                if (m_valueStatus.TryGetValue(node.NodeId, out StatusCode status) &&
                                    StatusCode.IsBad(status))
                                {
                                    values.Add(new DataValue(Variant.Null, status));
                                }
                                else if (m_values.TryGetValue(node.NodeId, out Variant variant))
                                {
                                    values.Add(new DataValue(variant, StatusCodes.Good,
                                        DateTime.UtcNow, DateTime.UtcNow));
                                }
                                else
                                {
                                    values.Add(new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown));
                                }
                            }
                            else
                            {
                                values.Add(new DataValue(Variant.Null, StatusCodes.BadNotSupported));
                            }
                        }
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = values.ToArrayOf(),
                            DiagnosticInfos = default
                        });
                    });
        }

        private void SetupCall()
        {
            Session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, _, _) =>
                    new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = m_callStatus,
                                OutputArguments = m_callOutput
                            }
                        ],
                        DiagnosticInfos = default
                    }));
        }

        private static BrowsePathResult GoodPath(NodeId nodeId)
        {
            return new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = [new BrowsePathTarget { TargetId = new ExpandedNodeId(nodeId) }]
            };
        }

        private static BrowsePathResult BadPath()
        {
            return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch, Targets = [] };
        }
    }
}
