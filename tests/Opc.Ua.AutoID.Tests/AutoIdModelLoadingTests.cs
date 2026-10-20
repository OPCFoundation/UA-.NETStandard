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
using NUnit.Framework;
using Opc.Ua.Di;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Loads the AutoID model with its DI dependency and checks the generated node states.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdModelLoadingTests
    {
        /// <summary>
        /// Loading registers DI and AutoID and preserves all 305 numeric AutoID declarations.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LoaderRegistersNamespacesAndPreservesEveryDeclaration(bool reverseNamespaces)
        {
            SystemContext context = AutoIdTestContext.CreateSystemContext(reverseNamespaces: reverseNamespaces);
            var roots = new NodeStateCollection();
            NodeStateCollection returned = roots.AddAutoIdTypeSystem(context);
            Dictionary<NodeId, NodeState> nodes = AutoIdTestContext.IndexNodes(context, roots);
            int autoId = context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);
            Dictionary<uint, string> declarations = AutoIdTestContext.ReadDeclarations();
            uint[] loaded =
            [
                .. nodes.Keys
                    .Where(id => id.NamespaceIndex == autoId && id.TryGetValue(out uint _))
                    .Select(id => id.TryGetValue(out uint value) ? value : 0u)
            ];

            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(roots));
                Assert.That(autoId, Is.GreaterThan(0));
                Assert.That(context.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi), Is.GreaterThan(0));
                Assert.That(loaded, Is.EquivalentTo(declarations.Keys));
                Assert.That(nodes.ContainsKey(NodeId.Create(
                    Di.ObjectTypes.DeviceType, Di.Namespaces.OpcUaDi, context.NamespaceUris)), Is.True,
                    "The DI dependency is loaded.");
            });
            foreach (KeyValuePair<uint, string> declaration in declarations)
            {
                NodeState node = nodes[new NodeId(declaration.Key, (ushort)autoId)];
                Assert.That(node.NodeClass.ToString(), Is.EqualTo(declaration.Value), $"NodeClass of {node.NodeId}");
            }
        }

        /// <summary>
        /// Loading twice, or after the DI model was loaded on its own, adds no duplicate roots.
        /// </summary>
        [Test]
        public void RepeatedAndComposedLoadingIsIdempotent()
        {
            SystemContext context = AutoIdTestContext.CreateSystemContext(registerModel: false);
            context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            var roots = new NodeStateCollection();
            roots.AddOpcUaDi(context);
            int diOnly = roots.Count;
            roots.AddAutoIdTypeSystem(context);
            int once = roots.Count;
            roots.AddAutoIdTypeSystem(context);
            Assert.Multiple(() =>
            {
                Assert.That(once, Is.GreaterThan(diOnly));
                Assert.That(roots, Has.Count.EqualTo(once));
                Assert.That(roots.Select(node => node.NodeId).Distinct().Count(), Is.EqualTo(once));
                Assert.That(context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri), Is.GreaterThan(0));
            });
        }

        /// <summary>
        /// The registration helpers reject null arguments.
        /// </summary>
        [Test]
        public void RegistrationRejectsNullArguments()
        {
            SystemContext context = AutoIdTestContext.CreateSystemContext(registerModel: false);
            Assert.Multiple(() =>
            {
                Assert.That(() => ((NodeStateCollection)null!).AddAutoIdTypeSystem(context),
                    Throws.ArgumentNullException);
                Assert.That(() => new NodeStateCollection().AddAutoIdTypeSystem(null!),
                    Throws.ArgumentNullException);
                Assert.That(() => ((IEncodeableFactoryBuilder)null!).AddAutoIdTypeSystem(),
                    Throws.ArgumentNullException);
                Assert.That(() => ((INodeStateFactoryBuilder)null!).AddAutoIdTypeSystem(),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// The device types derive from the DI DeviceType state of the referenced DI assembly.
        /// </summary>
        [Test]
        public void GeneratedDeviceTypesUseTheDiAssembly()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(AutoIdDeviceState).BaseType, Is.EqualTo(typeof(DeviceState)));
                Assert.That(typeof(AutoIdDeviceState).Assembly, Is.Not.SameAs(typeof(DeviceState).Assembly));
                Assert.That(typeof(RfidReaderDeviceState).BaseType, Is.EqualTo(typeof(AutoIdDeviceState)));
                Assert.That(typeof(OpticalVerifierDeviceState).BaseType, Is.EqualTo(typeof(OpticalReaderDeviceState)));
            });
        }

        /// <summary>
        /// Every ObjectType has a typed factory at its canonical numeric identity.
        /// </summary>
        [TestCase(1001u, typeof(AutoIdDeviceState))]
        [TestCase(1002u, typeof(OcrReaderDeviceState))]
        [TestCase(1003u, typeof(RfidReaderDeviceState))]
        [TestCase(1004u, typeof(AutoIdScanEventState))]
        [TestCase(1005u, typeof(OcrScanEventState))]
        [TestCase(1006u, typeof(RfidScanEventState))]
        [TestCase(1008u, typeof(OpticalReaderDeviceState))]
        [TestCase(1009u, typeof(OpticalScanEventState))]
        [TestCase(1010u, typeof(AutoIdDiagnosticsEventState))]
        [TestCase(1011u, typeof(OpticalVerifierDeviceState))]
        [TestCase(1012u, typeof(RtlsDeviceState))]
        [TestCase(1013u, typeof(OpticalVerifierScanEventState))]
        [TestCase(1014u, typeof(RtlsLocationEventState))]
        [TestCase(1015u, typeof(AutoIdAccessEventState))]
        [TestCase(1016u, typeof(RfidAccessEventState))]
        [TestCase(1017u, typeof(AutoIdLogEntryEventState))]
        [TestCase(1018u, typeof(AutoIdPresenceEventState))]
        public void ObjectFactoriesRetainCanonicalTypes(uint identifier, Type expectedType)
        {
            SystemContext context = AutoIdTestContext.CreateSystemContext(reverseNamespaces: true);
            var factory = new NodeStateFactory();
            factory.AddAutoIdTypeSystem();
            var typeId = NodeId.Create(identifier, AutoIdModel.NamespaceUri, context.NamespaceUris);
            NodeState? created = factory.CreateInstance(
                context,
                null,
                NodeClass.Object,
                new QualifiedName("AutoIdComponent", 1),
                ReferenceTypeIds.HasComponent,
                typeId);
            Assert.That(created, Is.TypeOf(expectedType));
        }

        /// <summary>
        /// A scan or access event subtype that refines the DataType of its result property exposes
        /// the refined property once, with the refined value type and DataType.
        /// </summary>
        [Test]
        public void RefinedResultPropertiesAreTypedAndEnumeratedOnce()
        {
            SystemContext context = AutoIdTestContext.CreateSystemContext();
            var rfidScan = new RfidScanEventState(null);
            rfidScan.Create(context, NodeId.Null, new QualifiedName("Scan", 1), LocalizedText.Null, true);
            var verifierScan = new OpticalVerifierScanEventState(null);
            verifierScan.Create(context, NodeId.Null, new QualifiedName("Scan", 1), LocalizedText.Null, true);
            var rfidAccess = new RfidAccessEventState(null);
            rfidAccess.Create(context, NodeId.Null, new QualifiedName("Access", 1), LocalizedText.Null, true);

            Assert.Multiple(() =>
            {
                AssertSingleResultChild(context, rfidScan, rfidScan.ScanResult, DataTypes.RfidScanResult);
                AssertSingleResultChild(
                    context, verifierScan, verifierScan.ScanResult, DataTypes.OpticalVerifierScanResult);
                AssertSingleResultChild(context, rfidAccess, rfidAccess.AccessResult, DataTypes.RfidAccessResult);
                Assert.That(((AutoIdScanEventState)rfidScan).ScanResult, Is.Null,
                    "The supertype's field stays empty.");
            });

            var sighting = new RfidSighting { Antenna = 2, Strength = -48 };
            rfidScan.ScanResult!.Value = [new RfidScanResult { CodeType = "EPC", Sighting = [sighting] }];
            Assert.That(rfidScan.ScanResult.Value[0].Sighting[0].Strength, Is.EqualTo(-48));

            var copy = (RfidScanEventState)rfidScan.Clone();
            Assert.That(copy.ScanResult, Is.Not.Null.And.Not.SameAs(rfidScan.ScanResult));
            Assert.That(copy.ScanResult!.Value[0].IsEqual(rfidScan.ScanResult.Value[0]), Is.True);
        }

        private static void AssertSingleResultChild(
            SystemContext context,
            BaseEventState parent,
            BaseVariableState? child,
            uint dataType)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            Assert.That(child, Is.Not.Null, parent.GetType().Name);
            Assert.That(children.Count(candidate => candidate.BrowseName == child!.BrowseName), Is.EqualTo(1),
                parent.GetType().Name);
            Assert.That(children, Does.Contain(child));
            Assert.That(child!.DataType, Is.EqualTo(NodeId.Create(dataType, AutoIdModel.NamespaceUri,
                context.NamespaceUris)));
            Assert.That(child.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(parent.FindChild(context, child.BrowseName), Is.SameAs(child));
        }
    }
}
