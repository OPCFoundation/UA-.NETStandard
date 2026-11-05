/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.IA.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.IA.Tests
{
    /// <summary>
    /// Checks client discovery and category handling through scripted UA service responses.
    /// </summary>
    [TestFixture]
    public sealed class IndustrialAutomationClientTests
    {
        /// <summary>
        /// Every published category is distinguished, including reusable-device inheritance.
        /// </summary>
        [TestCase(ObjectTypes.DynamicCalibrationTargetCategoryType, IaTargetCategory.Dynamic)]
        [TestCase(ObjectTypes.OneTimeCalibrationTargetCategoryType, IaTargetCategory.OneTime)]
        [TestCase(ObjectTypes.ReusableCalibrationTargetCategoryType, IaTargetCategory.Reusable)]
        [TestCase(ObjectTypes.ReusableDeviceCalibrationTargetCategoryType, IaTargetCategory.ReusableDevice)]
        [TestCase(ObjectTypes.BaseCalibrationTargetCategoryType, IaTargetCategory.Unknown)]
        public async Task CategoryUsesResolvedTypeAndMostSpecificClassification(uint type, IaTargetCategory expected)
        {
            var fixture = new SessionFixture();
            NodeId target = fixture.Instance("Target");
            NodeId category = fixture.Instance("Category");
            NodeId definition = new(type, fixture.IaNamespace);
            fixture.Browse(target, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(category, new QualifiedName(BrowseNames.CalibrationTargetCategory,
                    fixture.IaNamespace), NodeClass.Object, definition)]);
            fixture.Browse(category, Opc.Ua.ReferenceTypeIds.HasTypeDefinition,
                [fixture.Reference(definition, fixture.Name("CategoryType"), NodeClass.ObjectType, default)]);
            fixture.Subtype(new NodeId(ObjectTypes.ReusableDeviceCalibrationTargetCategoryType, fixture.IaNamespace),
                new NodeId(ObjectTypes.ReusableCalibrationTargetCategoryType, fixture.IaNamespace));

            IaTargetCategory actual = await fixture.Client.ReadCalibrationCategoryAsync(target).ConfigureAwait(false);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Vendor subtypes and interfaces are recognized without duplicates or unbounded traversal of cycles.
        /// </summary>
        [TestCase(1, false, 0)]
        [TestCase(2, false, 0)]
        [TestCase(3, true, 0)]
        [TestCase(3, true, 1)]
        [TestCase(3, true, 2)]
        public async Task DiscoveryRecognizesVendorTypesAndEnforcesItsExactNodeBound(
            int limit, bool accepted, int interfaceDepth)
        {
            var fixture = new SessionFixture();
            NodeId root = fixture.Instance("Root");
            NodeId light = fixture.Instance("Lamp");
            NodeId statistics = fixture.Instance("Statistics");
            NodeId vendorLight = fixture.Instance("VendorLightType");
            NodeId vendorRolling = fixture.Instance("VendorRollingInterface");
            NodeId vendorStatistics = fixture.Instance("VendorStatisticsType");
            NodeId parentStatistics = fixture.Instance("ParentStatisticsType");
            ReferenceDescription lamp = fixture.Reference(light, fixture.Name("Lamp"), NodeClass.Object, vendorLight);
            fixture.Browse(root, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [lamp, lamp, fixture.Reference(statistics, fixture.Name("Statistics"), NodeClass.Object,
                    vendorStatistics)]);
            fixture.Browse(statistics, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(root, fixture.Name("Root"), NodeClass.Object, Opc.Ua.ObjectTypeIds.BaseObjectType)]);
            if (interfaceDepth == 0)
            {
                fixture.Browse(statistics, Opc.Ua.ReferenceTypeIds.HasInterface,
                    [fixture.Reference(vendorRolling, fixture.Name("Rolling"), NodeClass.ObjectType, default)]);
            }
            else
            {
                fixture.Interfaces(interfaceDepth == 1 ? vendorStatistics : parentStatistics, [vendorRolling]);
                fixture.Supertype(vendorStatistics, parentStatistics);
                fixture.Supertype(parentStatistics, vendorStatistics);
            }
            fixture.Subtype(vendorLight,
                ExpandedNodeId.ToNodeId(ObjectTypeIds.BasicStacklightType, fixture.Namespaces));
            fixture.Subtype(vendorRolling,
                ExpandedNodeId.ToNodeId(ObjectTypeIds.IRollingStatisticsType, fixture.Namespaces));
            var entries = new List<IaEntry>();
            if (!accepted)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    await foreach (IaEntry entry in fixture.Client.DiscoverAsync(root, limit).ConfigureAwait(false))
                    {
                        entries.Add(entry);
                    }
                });
                Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
                return;
            }
            await foreach (IaEntry entry in fixture.Client.DiscoverAsync(root, limit).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            Assert.That(entries.Select(entry => (entry.NodeId, entry.Concept)), Is.EquivalentTo(new[]
            {
                (light, IaConcept.Stacklight),
                (statistics, IaConcept.RollingStatistics)
            }));
        }

        /// <summary>
        /// An absent model is reported before browsing rather than looking like an empty successful result.
        /// </summary>
        [Test]
        public void DiscoveryReportsAnAbsentIaNamespace()
        {
            var fixture = new SessionFixture();
            fixture.Session.SetupGet(session => session.NamespaceUris).Returns(new NamespaceTable());
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                fixture.Client.DiscoverAsync(fixture.Instance("Root")));
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            fixture.Session.Verify(session => session.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// ObjectType ancestry consumes a separate bounded traversal even when the instance tree is small.
        /// </summary>
        [Test]
        public async Task DiscoveryBoundsTypeAncestryIndependently()
        {
            var fixture = new SessionFixture();
            NodeId root = fixture.Instance("Root");
            NodeId child = fixture.Instance("Child");
            NodeId type = fixture.Instance("Type");
            NodeId parent = fixture.Instance("Parent");
            NodeId grandparent = fixture.Instance("Grandparent");
            fixture.Browse(root, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(child, fixture.Name("Child"), NodeClass.Object, type)]);
            fixture.Supertype(type, parent);
            fixture.Supertype(parent, grandparent);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (IaEntry _ in fixture.Client.DiscoverAsync(root, maximumNodes: 2)
                    .ConfigureAwait(false))
                {
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
        }

        /// <summary>
        /// Feature inspection retains metadata, distinguishes custom objects and counts unique feature identities.
        /// </summary>
        [TestCase(1, false, 0)]
        [TestCase(2, true, 0)]
        [TestCase(3, true, 0)]
        [TestCase(2, true, 1)]
        [TestCase(2, true, 2)]
        [TestCase(2, true, 3)]
        public async Task FeatureInspectionPreservesNativeInformationAndDistinctLimits(
            int limit, bool accepted, int badField)
        {
            var fixture = new SessionFixture();
            NodeId target = fixture.Instance("Target");
            NodeId folder = fixture.Instance("Features");
            NodeId numeric = fixture.Instance("Length");
            NodeId custom = fixture.Instance("Geometry");
            NodeId units = fixture.Instance("Units");
            NodeId resolution = fixture.Instance("Resolution");
            fixture.Browse(target, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(folder, new QualifiedName(BrowseNames.CalibrationTargetFeatures,
                    fixture.IaNamespace), NodeClass.Object, Opc.Ua.ObjectTypeIds.BaseObjectType)]);
            ReferenceDescription length = fixture.Reference(numeric, fixture.Name("Length"), NodeClass.Variable,
                VariableTypeIds.CalibrationValueType);
            fixture.Browse(folder, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [length, length, fixture.Reference(custom, fixture.Name("Geometry"), NodeClass.Object,
                    Opc.Ua.ObjectTypeIds.BaseObjectType)]);
            fixture.Browse(numeric, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
            [
                fixture.Reference(units, new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits), NodeClass.Variable,
                    Opc.Ua.VariableTypeIds.PropertyType),
                fixture.Reference(resolution, new QualifiedName(BrowseNames.Resolution, fixture.IaNamespace),
                    NodeClass.Variable, Opc.Ua.VariableTypeIds.PropertyType)
            ]);
            var unit = new EUInformation { UnitId = 5066068, DisplayName = new LocalizedText("mm") };
            StatusCode valueStatus = badField == 1
                ? StatusCodes.BadNoData : StatusCodes.UncertainLastUsableValue;
            StatusCode unitStatus = badField == 2 ? StatusCodes.BadNoData : StatusCodes.Good;
            StatusCode resolutionStatus = badField == 3 ? StatusCodes.BadNoData : StatusCodes.Good;
            fixture.Value(numeric, new DataValue(Variant.From(25.0), valueStatus, IaTestContext.Timestamp));
            fixture.Value(units, new DataValue(Variant.FromStructure(unit), unitStatus, IaTestContext.Timestamp));
            fixture.Value(resolution, new DataValue(Variant.From(0.1), resolutionStatus, IaTestContext.Timestamp));
            if (!accepted)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Client.ReadCalibrationFeaturesAsync(target, limit).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(fixture.ReadRequestSizes, Is.Empty);
                return;
            }
            ArrayOf<IaCalibrationFeature> features = await fixture.Client.ReadCalibrationFeaturesAsync(target, limit)
                .ConfigureAwait(false);
            Assert.That(features.Count, Is.EqualTo(2));
            Assert.That(features[0].NodeId, Is.EqualTo(numeric));
            Assert.That(features[0].Value.WrappedValue, Is.EqualTo(Variant.From(25.0)));
            Assert.That(features[0].Value.SourceTimestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(features[0].Value.StatusCode, Is.EqualTo(valueStatus));
            Assert.That(features[0].EngineeringUnits.WrappedValue.TryGetStructure<EUInformation>(
                out EUInformation? information), Is.True);
            Assert.That(information!.UnitId, Is.EqualTo(5066068));
            Assert.That(features[0].EngineeringUnits.StatusCode, Is.EqualTo(unitStatus));
            Assert.That(features[0].EngineeringUnits.SourceTimestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(features[0].Resolution.WrappedValue, Is.EqualTo(Variant.From(0.1)));
            Assert.That(features[0].Resolution.StatusCode, Is.EqualTo(resolutionStatus));
            Assert.That(features[0].Resolution.SourceTimestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(features[1].NodeId, Is.EqualTo(custom));
            Assert.That(features[1].DisplayName, Is.EqualTo(new LocalizedText("Geometry")));
            Assert.That(features[1].Value.IsNull, Is.True);
            Assert.That(features[1].EngineeringUnits.IsNull, Is.True);
            Assert.That(features[1].Resolution.IsNull, Is.True);
            Assert.That(fixture.BrowseRequestSizes, Is.EqualTo(new[] { 1, 1, 2 }));
            Assert.That(fixture.ReadRequestSizes, Is.EqualTo(new[] { 3 }));
        }

        /// <summary>
        /// Feature count changes batch sizes rather than introducing a browse and read loop per feature.
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(256, 0)]
        [TestCase(256, 64)]
        public async Task FeatureInspectionBatchesDiscoveryAndNativeReads(int count, int browseLimit)
        {
            var fixture = new SessionFixture();
            fixture.Session.SetupGet(session => session.OperationLimits)
                .Returns(new OperationLimits { MaxNodesPerBrowse = (uint)browseLimit });
            NodeId target = fixture.Instance("Target");
            NodeId folder = fixture.Instance("Features");
            fixture.Browse(target, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(folder, new QualifiedName(BrowseNames.CalibrationTargetFeatures,
                    fixture.IaNamespace), NodeClass.Object, Opc.Ua.ObjectTypeIds.BaseObjectType)]);
            var children = new ReferenceDescription[count];
            for (int i = 0; i < count; i++)
            {
                string suffix = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                NodeId node = fixture.Instance("Feature" + suffix);
                NodeId units = fixture.Instance("Units" + suffix);
                NodeId resolution = fixture.Instance("Resolution" + suffix);
                children[i] = fixture.Reference(node, fixture.Name("Feature" + suffix), NodeClass.Variable,
                    VariableTypeIds.CapacityRangeType);
                fixture.Browse(node, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [
                    fixture.Reference(units, new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits),
                        NodeClass.Variable, Opc.Ua.VariableTypeIds.PropertyType),
                    fixture.Reference(resolution, new QualifiedName(BrowseNames.Resolution, fixture.IaNamespace),
                        NodeClass.Variable, Opc.Ua.VariableTypeIds.PropertyType)
                ]);
                fixture.Value(node, new DataValue(Variant.FromStructure(new Range { Low = i, High = i + 1 }),
                    StatusCodes.BadNoData, IaTestContext.Timestamp));
                fixture.Value(units, new DataValue(Variant.FromStructure(new EUInformation { UnitId = i }),
                    StatusCodes.Good, IaTestContext.Timestamp));
                fixture.Value(resolution, new DataValue(Variant.From((double)i),
                    StatusCodes.UncertainLastUsableValue, IaTestContext.Timestamp));
            }
            fixture.Browse(folder, Opc.Ua.ReferenceTypeIds.HierarchicalReferences, children);
            ArrayOf<IaCalibrationFeature> features = await fixture.Client.ReadCalibrationFeaturesAsync(target)
                .ConfigureAwait(false);
            Assert.That(features.Count, Is.EqualTo(count));
            for (int i = 0; i < features.Count; i++)
            {
                Assert.That(features[i].NodeId,
                    Is.EqualTo(ExpandedNodeId.ToNodeId(children[i].NodeId, fixture.Namespaces)));
                Assert.That(features[i].Value.WrappedValue.TryGetStructure<Range>(out Range? range), Is.True);
                Assert.That(range!.Low, Is.EqualTo(i));
                Assert.That(range.High, Is.EqualTo(i + 1));
                Assert.That(features[i].Value.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
                Assert.That(features[i].Value.SourceTimestamp, Is.EqualTo(IaTestContext.Timestamp));
                Assert.That(features[i].EngineeringUnits.WrappedValue.TryGetStructure<EUInformation>(
                    out EUInformation? units), Is.True);
                Assert.That(units!.UnitId, Is.EqualTo(i));
                Assert.That(features[i].Resolution.WrappedValue, Is.EqualTo(Variant.From((double)i)));
                Assert.That(features[i].Resolution.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
            }
            var expectedBrowses = new List<int> { 1, 1 };
            int batchSize = browseLimit == 0 ? count : browseLimit;
            for (int remaining = count; remaining > 0; remaining -= batchSize)
            {
                expectedBrowses.Add(Math.Min(remaining, batchSize));
            }
            Assert.That(fixture.BrowseRequestSizes, Is.EqualTo(expectedBrowses));
            Assert.That(fixture.ReadRequestSizes, Is.EqualTo(count == 0 ? Array.Empty<int>() : new[] { count * 3 }));
        }

        /// <summary>
        /// Combining property discovery retains rejection of duplicate standard names before reading values.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FeatureInspectionRejectsAmbiguousBatchedProperties(bool resolution)
        {
            var fixture = new SessionFixture();
            NodeId target = fixture.Instance("Target");
            NodeId folder = fixture.Instance("Features");
            NodeId feature = fixture.Instance("Feature");
            fixture.Browse(target, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(folder, new QualifiedName(BrowseNames.CalibrationTargetFeatures,
                    fixture.IaNamespace), NodeClass.Object, Opc.Ua.ObjectTypeIds.BaseObjectType)]);
            fixture.Browse(folder, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                [fixture.Reference(feature, fixture.Name("Feature"), NodeClass.Variable,
                    VariableTypeIds.CalibrationValueType)]);
            QualifiedName name = resolution
                ? new QualifiedName(BrowseNames.Resolution, fixture.IaNamespace)
                : new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits);
            fixture.Browse(feature, Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
            [
                fixture.Reference(fixture.Instance("First"), name, NodeClass.Variable,
                    Opc.Ua.VariableTypeIds.PropertyType),
                fixture.Reference(fixture.Instance("Second"), name, NodeClass.Variable,
                    Opc.Ua.VariableTypeIds.PropertyType)
            ]);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Client.ReadCalibrationFeaturesAsync(target).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(fixture.ReadRequestSizes, Is.Empty);
        }

        /// <summary>
        /// Resolves namespaces and serves fixed browse results without a network or background tasks.
        /// </summary>
        private sealed class SessionFixture
        {
            /// <summary>
            /// Uses a non-default IA namespace index and exact type matching except for declared vendor subtypes.
            /// </summary>
            public SessionFixture()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
                Namespaces = context.NamespaceUris;
                m_instanceNamespace = Namespaces.GetIndexOrAppend("urn:tests:ia:client");
                Namespaces.GetIndexOrAppend("urn:tests:ia:unrelated");
                IaNamespace = Namespaces.GetIndexOrAppend(Opc.Ua.IA.Namespaces.IA);
                Session.SetupGet(session => session.NamespaceUris).Returns(Namespaces);
                Session.SetupGet(session => session.MessageContext).Returns(context);
                Session.SetupGet(session => session.Factory).Returns(context.Factory);
                Session.SetupGet(session => session.OperationLimits).Returns(new OperationLimits());
                Session.SetupGet(session => session.ServerCapabilities).Returns(new ServerCapabilities());
                Session.SetupGet(session => session.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
                var cache = new Mock<INodeCache>();
                Session.SetupGet(session => session.NodeCache).Returns(cache.Object);
                cache.Setup(value => value.IsTypeOfAsync(It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>())).Returns<NodeId, NodeId, CancellationToken>(
                        (subtype, supertype, _) =>
                            new ValueTask<bool>(subtype == supertype || m_subtypes.Contains((subtype, supertype))));
                cache.Setup(value => value.GetReferencesAsync(It.IsAny<NodeId>(),
                    Opc.Ua.ReferenceTypeIds.HasInterface, false, true, It.IsAny<CancellationToken>()))
                    .Returns<NodeId, NodeId, bool, bool, CancellationToken>((node, _, _, _, _) =>
                        new ValueTask<ArrayOf<INode>>(m_interfaces.TryGetValue(node, out ArrayOf<INode> interfaces)
                            ? interfaces : default));
                cache.Setup(value => value.FindSuperTypeAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns<NodeId, CancellationToken>((node, _) =>
                        new ValueTask<NodeId>(m_supertypes.TryGetValue(node, out NodeId supertype)
                            ? supertype : NodeId.Null));
                Session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                        (_, _, _, descriptions, _) =>
                        {
                            BrowseRequestSizes.Add(descriptions.Count);
                            var results = new BrowseResult[descriptions.Count];
                            for (int i = 0; i < descriptions.Count; i++)
                            {
                                BrowseDescription description = descriptions[i];
                                m_browse.TryGetValue((description.NodeId, description.ReferenceTypeId),
                                    out ArrayOf<ReferenceDescription> references);
                                results[i] = new BrowseResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    References = references
                                };
                            }
                            return new ValueTask<BrowseResponse>(new BrowseResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = results
                            });
                        });
                Session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                        (_, _, _, descriptions, _) =>
                        {
                            ReadRequestSizes.Add(descriptions.Count);
                            var values = new DataValue[descriptions.Count];
                            for (int i = 0; i < descriptions.Count; i++)
                            {
                                values[i] = m_values.TryGetValue(descriptions[i].NodeId, out DataValue value)
                                    ? value
                                    : new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown);
                            }
                            return new ValueTask<ReadResponse>(new ReadResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = values
                            });
                        });
                Client = new IndustrialAutomationClient(Session.Object, telemetry);
            }

            /// <summary>
            /// Gets the scripted transport seam.
            /// </summary>
            public Mock<ISession> Session { get; } = new();

            /// <summary>
            /// Gets the session namespace mapping.
            /// </summary>
            public NamespaceTable Namespaces { get; }

            /// <summary>
            /// Gets the deliberately shifted IA namespace.
            /// </summary>
            public ushort IaNamespace { get; }

            /// <summary>
            /// Gets the facade under test.
            /// </summary>
            public IndustrialAutomationClient Client { get; }

            /// <summary>
            /// Gets the number of nodes in each transport browse request.
            /// </summary>
            public List<int> BrowseRequestSizes { get; } = [];

            /// <summary>
            /// Gets the number of nodes in each transport read request.
            /// </summary>
            public List<int> ReadRequestSizes { get; } = [];

            /// <summary>
            /// Creates an application namespace identity.
            /// </summary>
            public NodeId Instance(string name)
            {
                return new NodeId(name, m_instanceNamespace);
            }

            /// <summary>
            /// Creates an application namespace browse name.
            /// </summary>
            public QualifiedName Name(string name)
            {
                return new QualifiedName(name, m_instanceNamespace);
            }

            /// <summary>
            /// Supplies a browse response for one reference family.
            /// </summary>
            public void Browse(NodeId parent, NodeId reference, ArrayOf<ReferenceDescription> children)
            {
                m_browse.Add((parent, reference), children);
            }

            /// <summary>
            /// Declares one intentional subtype relationship.
            /// </summary>
            public void Subtype(NodeId subtype, NodeId supertype)
            {
                m_subtypes.Add((subtype, supertype));
            }

            /// <summary>
            /// Declares interfaces on an ObjectType rather than repeating them on instances.
            /// </summary>
            public void Interfaces(NodeId type, ArrayOf<NodeId> interfaces)
            {
                var nodes = new INode[interfaces.Count];
                for (int i = 0; i < interfaces.Count; i++)
                {
                    nodes[i] = new ObjectTypeNode { NodeId = interfaces[i] };
                }
                m_interfaces.Add(type, nodes);
            }

            /// <summary>
            /// Supplies a direct type-parent link independently of subtype classification.
            /// </summary>
            public void Supertype(NodeId subtype, NodeId supertype)
            {
                m_supertypes.Add(subtype, supertype);
            }

            /// <summary>
            /// Supplies a native value with its original acquisition metadata.
            /// </summary>
            public void Value(NodeId node, in DataValue value)
            {
                m_values.Add(node, value);
            }

            /// <summary>
            /// Describes a scripted forward reference with its actual type.
            /// </summary>
            public ReferenceDescription Reference(
                NodeId node, QualifiedName name, NodeClass nodeClass, ExpandedNodeId type)
            {
                return new ReferenceDescription
                {
                    NodeId = node,
                    BrowseName = name,
                    DisplayName = new LocalizedText(name.Name),
                    NodeClass = nodeClass,
                    TypeDefinition = type,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                    IsForward = true
                };
            }

            /// <summary>The application namespace.</summary>
            private readonly ushort m_instanceNamespace;

            /// <summary>The scripted reference families.</summary>
            private readonly Dictionary<(NodeId Parent, NodeId Reference), ArrayOf<ReferenceDescription>> m_browse = [];

            /// <summary>The explicit vendor subtype relationships.</summary>
            private readonly HashSet<(NodeId Subtype, NodeId Supertype)> m_subtypes = [];

            /// <summary>The interfaces declared on each ObjectType.</summary>
            private readonly Dictionary<NodeId, ArrayOf<INode>> m_interfaces = [];

            /// <summary>The type hierarchy, including deliberately cyclic test input.</summary>
            private readonly Dictionary<NodeId, NodeId> m_supertypes = [];

            /// <summary>The native scripted reads.</summary>
            private readonly Dictionary<NodeId, DataValue> m_values = [];
        }
    }
}
