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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Di;

namespace Opc.Ua.LADS.Tests.Model
{
    /// <summary>
    /// Loads the LADS model with its dependencies through <c>AddLadsTypeSystem</c>.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    public sealed class LadsModelLoadingTests
    {
        /// <summary>
        /// Loading registers the closure and preserves every numeric LADS declaration.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LoaderRegistersNamespacesAndPreservesEveryDeclaration(bool reverseNamespaces)
        {
            SystemContext context = LadsTestContext.CreateSystemContext(reverseNamespaces: reverseNamespaces);
            var roots = new NodeStateCollection();
            NodeStateCollection returned = roots.AddLadsTypeSystem(context);
            Dictionary<NodeId, NodeState> nodes = LadsTestContext.IndexNodes(context, roots);
            int lads = context.NamespaceUris.GetIndex(LadsModel.NamespaceUri);
            Dictionary<uint, string> declarations = LadsTestContext.ReadDeclarations();
            uint[] loaded =
            [
                .. nodes.Keys
                    .Where(id => id.NamespaceIndex == lads && id.TryGetValue(out uint _))
                    .Select(id => id.TryGetValue(out uint value) ? value : 0u)
            ];
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(roots));
                Assert.That(lads, Is.GreaterThan(0));
                foreach (string uri in LadsTestContext.ExpectedNamespaceUris)
                {
                    Assert.That(context.NamespaceUris.GetIndex(uri), Is.GreaterThan(0), uri);
                }
                Assert.That(loaded, Is.EquivalentTo(declarations.Keys));
                Assert.That(nodes.ContainsKey(NodeId.Create(
                    Di.ObjectTypes.DeviceType, Di.Namespaces.OpcUaDi, context.NamespaceUris)), Is.True);
                Assert.That(nodes.ContainsKey(NodeId.Create(
                    AMB.ObjectTypes.MaintenanceEventStateMachineType, AMB.Namespaces.AMB, context.NamespaceUris)),
                    Is.True);
            });
            foreach (KeyValuePair<uint, string> declaration in declarations)
            {
                NodeState node = nodes[new NodeId(declaration.Key, (ushort)lads)];
                Assert.That(node.NodeClass.ToString(), Is.EqualTo(declaration.Value), $"NodeClass of {node.NodeId}");
            }
        }

        /// <summary>
        /// Loading twice, or after a dependency was loaded on its own, adds no duplicate roots.
        /// </summary>
        [Test]
        public void RepeatedAndComposedLoadingIsIdempotent()
        {
            SystemContext context = LadsTestContext.CreateSystemContext(registerModel: false);
            context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            var roots = new NodeStateCollection();
            roots.AddOpcUaDi(context);
            int diOnly = roots.Count;
            roots.AddLadsTypeSystem(context);
            int once = roots.Count;
            roots.AddLadsTypeSystem(context);
            Assert.Multiple(() =>
            {
                Assert.That(once, Is.GreaterThan(diOnly));
                Assert.That(roots, Has.Count.EqualTo(once));
                Assert.That(roots.Select(node => node.NodeId).Distinct().Count(), Is.EqualTo(once));
                Assert.That(context.NamespaceUris.GetIndex(LadsModel.NamespaceUri), Is.GreaterThan(0));
            });
        }

        /// <summary>
        /// The registration helpers reject null arguments.
        /// </summary>
        [Test]
        public void RegistrationRejectsNullArguments()
        {
            SystemContext context = LadsTestContext.CreateSystemContext(registerModel: false);
            Assert.Multiple(() =>
            {
                Assert.That(() => ((NodeStateCollection)null!).AddLadsTypeSystem(context),
                    Throws.ArgumentNullException);
                Assert.That(() => new NodeStateCollection().AddLadsTypeSystem(null!),
                    Throws.ArgumentNullException);
                Assert.That(() => ((IEncodeableFactoryBuilder)null!).AddLadsTypeSystem(),
                    Throws.ArgumentNullException);
                Assert.That(() => ((INodeStateFactoryBuilder)null!).AddLadsTypeSystem(),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// The generated device and functional unit states derive from the referenced model assemblies.
        /// </summary>
        [Test]
        public void GeneratedTypesUseTheDependencyAssemblies()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(LADSDeviceState).IsSubclassOf(typeof(Di.DeviceState)), Is.True);
                Assert.That(typeof(LADSDeviceState).Assembly, Is.Not.SameAs(typeof(Di.DeviceState).Assembly));
                Assert.That(typeof(LADSComponentsState).IsSubclassOf(typeof(Machinery.MachineComponentsState)),
                    Is.True);
                Assert.That(typeof(MaintenanceTaskState).IsSubclassOf(typeof(Di.MaintenanceRequiredAlarmState)),
                    Is.True);
            });
        }
    }
}
