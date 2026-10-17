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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using DataTypeIds = Opc.Ua.DataTypeIds;
using ObjectTypeIds = Opc.Ua.ObjectTypeIds;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;
using Scales = Opc.Ua.Scales;

namespace UaLens.Tests.Companions
{
    /// <summary>
    /// Service-level test responses for the real Scales client and generated method proxies.
    /// No connection, process, subscription, certificate or physical scale is created.
    /// </summary>
    internal sealed class WeighingTestSession
    {
        /// <summary>
        /// Creates a borrowed-session fixture and an explicitly injected test-only simulation policy.
        /// </summary>
        public WeighingTestSession()
        {
            Services = new IndustrialCompanionTestSession();
            Services.NamespaceUris.GetIndexOrAppend(Scales.Namespaces.Scales);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.PackML.Namespaces.PackML);
            Session.SetupGet(session => session.Connected).Returns(() => Connected);
            Session.SetupGet(session => session.SessionId).Returns(() => SessionId);
            Session.SetupGet(session => session.Identity).Returns(() => Identity);
            Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
            Transport = new Mock<ITransportChannel>(MockBehavior.Strict);
            Transport.SetupGet(channel => channel.MessageContext).Returns(Services.MessageContext);
            Session.SetupGet(session => session.TransportChannel).Returns(Transport.Object);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            cache.Setup(nodeCache => nodeCache.FindSuperTypeAsync(
                    It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId id, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return new ValueTask<NodeId>(m_superTypes.TryGetValue(id, out NodeId parent) ? parent : default);
                });
            Session.SetupGet(session => session.NodeCache).Returns(cache.Object);
            var clock = new Mock<TimeProvider>(MockBehavior.Strict);
            clock.Setup(time => time.GetUtcNow()).Returns(() => UtcNow);
            Clock = clock.Object;
            Grant = new WeighingSimulationGrant(
                new Guid("bdc27922-2d11-45b9-8dfa-d78ec956f960"), "test-revision-1", UtcNow.AddMinutes(5));
            Policy = new Mock<IWeighingSimulationPolicy>(MockBehavior.Strict);
            Policy.Setup(policy => policy.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((CompanionContext _, CompanionTarget _, string _, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return new ValueTask<WeighingSimulationGrant>(Grant);
                });
            Services.ReadHandler = (read, _) => Services.Read(read).WithSourceTimestamp(Timestamp);
            Services.CallHandler = static (_, _) => IndustrialCompanionTestSession.Good();
        }

        /// <summary>
        /// Gets the bounded service response store.
        /// </summary>
        public IndustrialCompanionTestSession Services { get; }

        /// <summary>
        /// Gets the primary session mock.
        /// </summary>
        public Mock<ISession> Session => Services.Session;

        /// <summary>
        /// Gets the borrowed transport mock; closing or disposing it is never permitted.
        /// </summary>
        public Mock<ITransportChannel> Transport { get; }

        /// <summary>
        /// Gets the explicit test-only simulation ownership policy.
        /// </summary>
        public Mock<IWeighingSimulationPolicy> Policy { get; }

        /// <summary>
        /// Gets the deterministic expiry clock.
        /// </summary>
        public TimeProvider Clock { get; }

        /// <summary>
        /// Gets or sets the clock instant without changing the fixed owned-run expiry.
        /// </summary>
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Gets or sets the connected state used by the identity snapshot.
        /// </summary>
        public bool Connected { get; set; } = true;

        /// <summary>
        /// Gets or sets the session identity.
        /// </summary>
        public NodeId SessionId { get; set; } = new(123u);

        /// <summary>
        /// Gets or sets the user identity.
        /// </summary>
        public IUserIdentity Identity { get; set; } = new Mock<IUserIdentity>(MockBehavior.Strict).Object;

        /// <summary>
        /// Gets the endpoint identity under test, not a live endpoint.
        /// </summary>
        public EndpointDescription Endpoint { get; } = new()
        {
            EndpointUrl = "opc.tcp://owned-weighing.example:4840/Simulation",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            Server = new ApplicationDescription { ApplicationUri = "urn:ualens:test:owned-weighing" }
        };

        /// <summary>
        /// Gets or sets the non-secret run identity returned by the explicit test policy.
        /// </summary>
        public WeighingSimulationGrant Grant { get; set; }

        /// <summary>
        /// Gets the fixed source timestamp.
        /// </summary>
        public DateTimeUtc Timestamp { get; } = new(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>
        /// Gets the explicit unit identity used by the sample values.
        /// </summary>
        public EUInformation Unit { get; } = new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = 0x4b474d,
            DisplayName = new LocalizedText("kg"),
            Description = new LocalizedText("kilogram")
        };

        /// <summary>
        /// Creates a context with explicit bounds.
        /// </summary>
        public CompanionContext Context(int maxTargets = 128, int maxFields = 256)
        {
            return Services.Context(maxTargets, maxFields);
        }

        /// <summary>
        /// Creates the real provider with the explicit policy when requested.
        /// </summary>
        public WeighingCompanionProvider Provider(bool authorized = true)
        {
            return new WeighingCompanionProvider(authorized ? Policy.Object : null, Clock);
        }

        /// <summary>
        /// Adds a real model type to both standard discovery roots.
        /// </summary>
        public CompanionTarget AddScale(ScaleKind kind = ScaleKind.Simple, bool system = false, bool roots = true)
        {
            NodeId id = Services.Id("scale-" + ++m_nextNode);
            string name = system ? "Scale system" : kind.ToString();
            ExpandedNodeId type = system ? Scales.ObjectTypeIds.ScaleSystemType :
                new ExpandedNodeId(ScalesModel.ObjectTypeOf(kind), Scales.Namespaces.Scales);
            Services.AddObject(id, type, name);
            if (roots)
            {
                var client = new ScalesClient(Session.Object, Services.Telemetry);
                Services.AddChild(client.Topology.DeviceSetId, id);
                Services.AddChild(client.MachinesFolderId, id);
            }
            return new CompanionTarget("weighing", id, name, name);
        }

        /// <summary>
        /// Adds an object child of the given model type.
        /// </summary>
        public NodeId AddGroup(NodeId parent, string name, ExpandedNodeId type = default)
        {
            NodeId id = Services.Id("group-" + ++m_nextNode);
            Services.AddObject(id, type.IsNull ? ObjectTypeIds.BaseObjectType : type, name);
            Services.AddChild(parent, id, Scales.Namespaces.Scales, name);
            return id;
        }

        /// <summary>
        /// Adds a browsable source variable.
        /// </summary>
        public NodeId AddVariable(
            NodeId parent, string name, Variant value, string? namespaceUri = null, StatusCode status = default)
        {
            string uri = namespaceUri ?? Scales.Namespaces.Scales;
            NodeId id = Services.AddProperty(parent, uri, name, value);
            Services.SetValue(id, value, status: status);
            ArrayOf<ReferenceDescription> previous = Services.Browse(new BrowseDescription
            {
                NodeId = parent,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            }).References;
            Services.SetReferences(parent, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                [
                    .. previous,
                    new ReferenceDescription
                    {
                        NodeId = new ExpandedNodeId(id),
                        NodeClass = NodeClass.Variable,
                        BrowseName = new QualifiedName(name, (ushort)Services.NamespaceUris.GetIndex(uri)),
                        IsForward = true,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent
                    }
                ]);
            return id;
        }

        /// <summary>
        /// Adds a typed weight and its independent source properties.
        /// </summary>
        public NodeId AddWeight(
            NodeId scale, string name = "CurrentWeight", double gross = 25, double net = 19, double tare = 6,
            string? omit = null)
        {
            NodeId weight = AddVariable(scale, name,
                Variant.From(new ExtensionObject(new WeightType { Gross = gross, Net = net, Tare = tare })));
            AddWeightProperty(weight, Scales.BrowseNames.Overload, Variant.From(false), omit);
            AddWeightProperty(weight, Scales.BrowseNames.Underload, Variant.From(false), omit);
            AddWeightProperty(weight, Scales.BrowseNames.TareMode, Variant.From((int)TareMode.None_0), omit);
            AddWeightProperty(weight, Scales.BrowseNames.WeightStable, Variant.From(true), omit);
            AddWeightProperty(weight, Scales.BrowseNames.InsideZero, Variant.From(true), omit);
            AddWeightProperty(weight, Scales.BrowseNames.CurrentRangeId, Variant.From((ushort)1), omit);
            AddWeightProperty(weight, Scales.BrowseNames.WeightId, Variant.From("server-weight-17"), omit);
            AddWeightProperty(weight, Opc.Ua.BrowseNames.EngineeringUnits,
                Variant.From(new ExtensionObject(Unit)), omit, Opc.Ua.Namespaces.OpcUa);
            AddWeightProperty(weight, Opc.Ua.BrowseNames.EURange,
                Variant.From(new ExtensionObject(new Opc.Ua.Range { Low = 0, High = 100 })), omit,
                Opc.Ua.Namespaces.OpcUa);
            return weight;
        }

        /// <summary>
        /// Adds an executable method with the published scalar argument signature.
        /// </summary>
        public NodeId AddMethod(
            NodeId owner, string name, ArrayOf<NodeId> inputs = default, bool returnsNode = false)
        {
            NodeId method = Services.AddProperty(owner, Scales.Namespaces.Scales, name, Variant.Null);
            Services.SetValue(method, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
            Services.SetValue(method, Variant.From(true), Attributes.Executable);
            Services.SetValue(method, Variant.From(true), Attributes.UserExecutable);
            if (inputs.Count != 0)
            {
                Services.AddProperty(method, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.InputArguments,
                    Arguments(inputs));
            }
            if (returnsNode)
            {
                Services.AddProperty(method, Opc.Ua.Namespaces.OpcUa, Opc.Ua.BrowseNames.OutputArguments,
                    Arguments([DataTypeIds.NodeId]));
            }
            return method;
        }

        /// <summary>
        /// Changes a field without changing its source NodeId.
        /// </summary>
        public NodeId Set(
            NodeId parent, string name, Variant value, StatusCode status = default, string? namespaceUri = null)
        {
            NodeId node = Resolve(parent, name, namespaceUri);
            Services.SetValue(node, value, status: status);
            return node;
        }

        /// <summary>
        /// Resolves the exact source field in the fixture.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public NodeId Resolve(NodeId parent, string name, string? namespaceUri = null)
        {
            BrowsePathResult result = Services.Translate(new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            TargetName = new QualifiedName(name, (ushort)Services.NamespaceUris.GetIndex(
                                namespaceUri ?? Scales.Namespaces.Scales))
                        }
                    ]
                }
            });
            return result.Targets.Count == 1
                ? ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Services.NamespaceUris)
                : throw new InvalidOperationException("No fixture field " + name);
        }

        /// <summary>
        /// Registers live ancestry and matching typed-client cache ancestry for a vendor subtype.
        /// </summary>
        public void AddSubtype(NodeId type, NodeId superType)
        {
            m_superTypes[type] = superType;
            Services.SetReferences(type, BrowseDirection.Inverse, ReferenceTypeIds.HasSubtype,
                [
                    new ReferenceDescription
                    {
                        NodeId = new ExpandedNodeId(superType),
                        NodeClass = NodeClass.ObjectType,
                        IsForward = false,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype
                    }
                ]);
        }

        /// <summary>
        /// Creates scalar argument metadata matching the real generated proxies.
        /// </summary>
        public static Variant Arguments(ArrayOf<NodeId> types)
        {
            var arguments = new List<ExtensionObject>();
            for (int index = 0; index < types.Count; index++)
            {
                arguments.Add(new ExtensionObject(new Argument
                {
                    Name = "Argument" + index,
                    DataType = types[index],
                    ValueRank = ValueRanks.Scalar,
                    ArrayDimensions = []
                }));
            }
            ArrayOf<ExtensionObject> result = [.. arguments];
            return Variant.From(result);
        }

        /// <summary>
        /// Supplies exact typed preset-tare editor values.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public ArrayOf<CompanionValue> PresetInput(double value = 5)
        {
            string unitNamespace = Unit.NamespaceUri ??
                throw new InvalidOperationException("Preset input requires an engineering-unit namespace.");
            return
            [
                new("presetTare", Variant.From(value)),
                new("unitNamespace", Variant.From(unitNamespace)),
                new("unitId", Variant.From(Unit.UnitId))
            ];
        }

        private void AddWeightProperty(
            NodeId weight, string name, Variant value, string? omit, string? namespaceUri = null)
        {
            if (name != omit)
            {
                AddVariable(weight, name, value, namespaceUri);
            }
        }

        private readonly Dictionary<NodeId, NodeId> m_superTypes = [];
        private int m_nextNode;
    }
}
