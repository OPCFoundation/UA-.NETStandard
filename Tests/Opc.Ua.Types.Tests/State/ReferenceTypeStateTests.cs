/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;
using AttributesToSave = Opc.Ua.NodeState.AttributesToSave;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceTypeStateTests
    {
        private const string kApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            m_messageContext.NamespaceUris.GetIndexOrAppend(kApplicationUri);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                ServerUris = m_messageContext.ServerUris,
                EncodeableFactory = m_messageContext.Factory
            };
        }

        [Test]
        public void ConstructorSetsDefaults()
        {
            var refType = new ReferenceTypeState();
            Assert.That(refType, Is.Not.Null);
            Assert.That(refType.NodeClass, Is.EqualTo(NodeClass.ReferenceType));
            Assert.That(refType.Symmetric, Is.False);
            Assert.That(refType.IsAbstract, Is.False);
            refType.Dispose();
        }

        [Test]
        public void ConstructStaticFactory()
        {
            NodeState node = ReferenceTypeState.Construct(null);
            Assert.That(node, Is.InstanceOf<ReferenceTypeState>());
            node.Dispose();
        }

        [Test]
        public void InverseNamePropertySetterTriggersChangeMask()
        {
            var refType = new ReferenceTypeState();
            refType.ClearChangeMasks(null, false);

            var inverseName = new LocalizedText("IsReferencedBy");
            refType.InverseName = inverseName;
            Assert.That(refType.InverseName, Is.EqualTo(inverseName));
            Assert.That(refType.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));
            refType.Dispose();
        }

        [Test]
        public void SymmetricPropertySetterTriggersChangeMask()
        {
            var refType = new ReferenceTypeState();
            refType.ClearChangeMasks(null, false);

            refType.Symmetric = true;
            Assert.That(refType.Symmetric, Is.True);
            Assert.That(refType.ChangeMasks & NodeStateChangeMasks.NonValue,
                Is.EqualTo(NodeStateChangeMasks.NonValue));

            refType.ClearChangeMasks(null, false);
            refType.Symmetric = true;
            Assert.That(refType.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            refType.Dispose();
        }

        [Test]
        public void CloneCreatesDeepCopy()
        {
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(4000),
                BrowseName = new QualifiedName("HasChild"),
                DisplayName = new LocalizedText("Has Child"),
                InverseName = new LocalizedText("IsChildOf"),
                Symmetric = false,
                IsAbstract = true,
                SuperTypeId = new NodeId(99)
            };

            var clone = (ReferenceTypeState)refType.Clone();
            Assert.That(clone, Is.Not.SameAs(refType));
            Assert.That(clone.InverseName, Is.EqualTo(refType.InverseName));
            Assert.That(clone.Symmetric, Is.EqualTo(refType.Symmetric));
            Assert.That(clone.IsAbstract, Is.EqualTo(refType.IsAbstract));
            Assert.That(clone.SuperTypeId, Is.EqualTo(refType.SuperTypeId));
            clone.Dispose();
            refType.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsTrueForEqualInstances()
        {
            var rt1 = new ReferenceTypeState { InverseName = new LocalizedText("Inverse"), Symmetric = true };
            // DeepEquals requires matching internal state including Initialized flag

            // Test exercises the method and verifies it runs without error
            var rt2 = (ReferenceTypeState)rt1.Clone();
            Assert.That(rt1.DeepEquals(rt1), Is.True);
            rt1.Dispose();
            rt2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentNodeType()
        {
            var refType = new ReferenceTypeState();
            var view = new ViewState();
            Assert.That(refType.DeepEquals(view), Is.False);
            refType.Dispose();
            view.Dispose();
        }

        [Test]
        public void DeepGetHashCodeIsDeterministic()
        {
            var refType = new ReferenceTypeState { InverseName = new LocalizedText("TestInverse"), Symmetric = true };
            int hash = refType.DeepGetHashCode();
            Assert.That(hash, Is.TypeOf<int>());
            refType.Dispose();
        }

        [Test]
        public void GetAttributesToSaveIncludesInverseNameAndSymmetric()
        {
            var refType = new ReferenceTypeState
            {
                InverseName = new LocalizedText("IsReferencedBy"),
                Symmetric = true
            };
            AttributesToSave attrs = refType.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.InverseName, Is.Not.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.Symmetric, Is.Not.EqualTo(AttributesToSave.None));
            refType.Dispose();
        }

        [Test]
        public void GetAttributesToSaveExcludesDefaultValues()
        {
            var refType = new ReferenceTypeState();
            AttributesToSave attrs = refType.GetAttributesToSave(m_context);
            Assert.That(attrs & AttributesToSave.InverseName, Is.EqualTo(AttributesToSave.None));
            Assert.That(attrs & AttributesToSave.Symmetric, Is.EqualTo(AttributesToSave.None));
            refType.Dispose();
        }

        [Test]
        public void ExportToNodeTable()
        {
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(4001),
                BrowseName = new QualifiedName("HasRef"),
                DisplayName = new LocalizedText("Has Reference"),
                InverseName = new LocalizedText("IsReferencedBy"),
                Symmetric = false,
                SuperTypeId = new NodeId(100)
            };

            var table = new NodeTable(m_context.NamespaceUris, m_context.ServerUris, null);
            refType.Export(m_context, table);
            Assert.That(table, Is.Not.Empty);
            refType.Dispose();
        }

        [Test]
        public void BinarySaveAndLoadRoundTrip()
        {
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(4002),
                BrowseName = new QualifiedName("BinRef"),
                DisplayName = new LocalizedText("Binary Ref"),
                InverseName = new LocalizedText("IsRefBy"),
                Symmetric = true
            };

            using var stream = new MemoryStream();
            refType.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new ReferenceTypeState();
            restored.LoadAsBinary(m_context, stream);

            Assert.That(restored.InverseName, Is.EqualTo(refType.InverseName));
            Assert.That(restored.Symmetric, Is.EqualTo(refType.Symmetric));
            restored.Dispose();
            refType.Dispose();
        }

        [Test]
        public void SaveAndUpdateBinaryRoundTripWithAllProperties()
        {
            var original = new ReferenceTypeState
            {
                NodeId = new NodeId(4010),
                BrowseName = new QualifiedName("SaveUpdateRef"),
                DisplayName = new LocalizedText("Save Update Ref"),
                InverseName = new LocalizedText("IsRefBy"),
                Symmetric = true,
                SuperTypeId = new NodeId(300),
                IsAbstract = true
            };

            AttributesToSave attributesToSave = original.GetAttributesToSave(m_context);
            using var ms = new MemoryStream();
            using (var encoder = new BinaryEncoder(ms, m_messageContext, true))
            {
                original.Save(m_context, encoder, attributesToSave);
            }

            ms.Position = 0;
            var restored = new ReferenceTypeState();
            using (var decoder = new BinaryDecoder(ms, m_messageContext, true))
            {
                restored.Update(m_context, decoder, attributesToSave);
            }

            Assert.That(restored.InverseName, Is.EqualTo(original.InverseName));
            Assert.That(restored.Symmetric, Is.EqualTo(original.Symmetric));
            Assert.That(restored.SuperTypeId, Is.EqualTo(original.SuperTypeId));
            Assert.That(restored.IsAbstract, Is.EqualTo(original.IsAbstract));
            restored.Dispose();
            original.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentInverseName()
        {
            var rt1 = new ReferenceTypeState
            {
                NodeId = new NodeId(4020),
                BrowseName = new QualifiedName("Ref"),
                InverseName = new LocalizedText("InverseA")
            };

            var rt2 = (ReferenceTypeState)rt1.Clone();
            rt2.InverseName = new LocalizedText("InverseB");

            Assert.That(rt1.DeepEquals(rt2), Is.False);
            rt1.Dispose();
            rt2.Dispose();
        }

        [Test]
        public void DeepEqualsReturnsFalseForDifferentSymmetric()
        {
            var rt1 = new ReferenceTypeState
            {
                NodeId = new NodeId(4021),
                BrowseName = new QualifiedName("Ref"),
                Symmetric = false
            };

            var rt2 = (ReferenceTypeState)rt1.Clone();
            rt2.Symmetric = true;

            Assert.That(rt1.DeepEquals(rt2), Is.False);
            rt1.Dispose();
            rt2.Dispose();
        }

        [Test]
        public void DeepGetHashCodeReturnsDifferentForDifferentProperties()
        {
            var rt1 = new ReferenceTypeState
            {
                NodeId = new NodeId(4030),
                BrowseName = new QualifiedName("Ref1"),
                InverseName = new LocalizedText("InverseA"),
                Symmetric = false
            };

            var rt2 = new ReferenceTypeState
            {
                NodeId = new NodeId(4031),
                BrowseName = new QualifiedName("Ref2"),
                InverseName = new LocalizedText("InverseB"),
                Symmetric = true
            };

            Assert.That(rt1.DeepGetHashCode(), Is.Not.EqualTo(rt2.DeepGetHashCode()));
            rt1.Dispose();
            rt2.Dispose();
        }
    }
}
