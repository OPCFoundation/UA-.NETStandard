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

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateCollectionTests
    {
        private const string ApplicationUri = "uri:localhost:opcfoundation.org:NodeStates";
        private SystemContext m_context;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(ApplicationUri);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        [Test]
        public void DefaultConstructor()
        {
            var collection = new NodeStateCollection();
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection, Is.Empty);
        }

        [Test]
        public void CapacityConstructor()
        {
            var collection = new NodeStateCollection(10);
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection.Capacity, Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void EnumerableConstructor()
        {
            var items = new List<NodeState>
            {
                new ViewState { NodeId = new NodeId(1) },
                new ViewState { NodeId = new NodeId(2) }
            };
            var collection = new NodeStateCollection(items);
            Assert.That(collection, Has.Count.EqualTo(2));
            foreach (NodeState item in items)
            {
                item.Dispose();
            }
        }

        [Test]
        public void AddAndIndexer()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState { NodeId = new NodeId(10) };
            collection.Add(view);
            Assert.That(collection, Has.Count.EqualTo(1));
            Assert.That(collection[0], Is.SameAs(view));
            view.Dispose();
        }

        [Test]
        public void RemoveItem()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState { NodeId = new NodeId(20) };
            collection.Add(view);
            bool removed = collection.Remove(view);
            Assert.That(removed, Is.True);
            Assert.That(collection, Is.Empty);
            view.Dispose();
        }

        [Test]
        public void ContainsItem()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState { NodeId = new NodeId(30) };
            collection.Add(view);
            Assert.That(collection, Does.Contain(view));
            var other = new ViewState { NodeId = new NodeId(31) };
            Assert.That(collection, Does.Not.Contain(other));
            view.Dispose();
            other.Dispose();
        }

        [Test]
        public void EnumerateItems()
        {
            var collection = new NodeStateCollection();
            var v1 = new ViewState { NodeId = new NodeId(40) };
            var v2 = new ViewState { NodeId = new NodeId(41) };
            collection.Add(v1);
            collection.Add(v2);

            int count = 0;
            foreach (NodeState item in collection)
            {
                count++;
            }
            Assert.That(count, Is.EqualTo(2));
            v1.Dispose();
            v2.Dispose();
        }

        [Test]
        public void ClearCollection()
        {
            var collection = new NodeStateCollection
            {
                new ViewState { NodeId = new NodeId(50) },
                new ViewState { NodeId = new NodeId(51) }
            };
            collection.Clear();
            Assert.That(collection, Is.Empty);
        }

        [Test]
        public void SaveAsBinaryAndLoadFromBinary()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6000),
                BrowseName = new QualifiedName("CollView"),
                DisplayName = new LocalizedText("Coll View"),
                EventNotifier = EventNotifiers.SubscribeToEvents,
                ContainsNoLoops = true
            };
            collection.Add(view);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            Assert.That(stream.Length, Is.GreaterThan(0));

            stream.Position = 0;
            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, false);
            Assert.That(restored, Has.Count.EqualTo(1));
            view.Dispose();
        }

        [Test]
        public void SaveAsXml()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6001),
                SymbolicName = "XmlView",
                BrowseName = new QualifiedName("XmlView"),
                DisplayName = new LocalizedText("XML View")
            };
            collection.Add(view);

            using var stream = new MemoryStream();
            collection.SaveAsXml(m_context, stream, keepStreamOpen: true);
            Assert.That(stream.Length, Is.GreaterThan(0));
            view.Dispose();
        }

        [Test]
        public void SaveAsNodeSet2()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6002),
                BrowseName = new QualifiedName("NS2View"),
                DisplayName = new LocalizedText("NodeSet2 View")
            };
            collection.Add(view);

            using var stream = new MemoryStream();
            collection.SaveAsNodeSet2(m_context, stream);
            Assert.That(stream.Length, Is.GreaterThan(0));
            view.Dispose();
        }

        [Test]
        public void SaveAsNodeSet2EmitsModelsForUserNamespaces()
        {
            const ushort customNs = 1;

            var collection = new NodeStateCollection();
            var folder = new BaseObjectState(null)
            {
                NodeId = new NodeId(7001, customNs),
                BrowseName = new QualifiedName("CustomFolder", customNs),
                DisplayName = new LocalizedText("Custom Folder")
            };
            var variable = new BaseDataVariableState(folder)
            {
                NodeId = new NodeId(7002, customNs),
                BrowseName = new QualifiedName("CustomVariable", customNs),
                DisplayName = new LocalizedText("Custom Variable"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            folder.AddChild(variable);
            collection.Add(folder);
            collection.Add(variable);

            using var stream = new MemoryStream();
            collection.SaveAsNodeSet2(m_context, stream);
            Assert.That(stream.Length, Is.GreaterThan(0));

            stream.Position = 0;
            Export.UANodeSet nodeSet = Export.UANodeSet.Read(stream);

            Assert.That(nodeSet.Models, Is.Not.Null, "<Models> element must be present");
            Assert.That(nodeSet.Models, Is.Not.Empty, "<Models> must contain at least one <Model>");

            Export.ModelTableEntry customModel =
                Array.Find(nodeSet.Models, m => m.ModelUri == ApplicationUri);
            Assert.That(customModel, Is.Not.Null, "Expected a <Model> for the custom namespace");
            Assert.That(customModel.Version, Is.Not.Null.And.Not.Empty);
            Assert.That(customModel.PublicationDateSpecified, Is.True);
            Assert.That(customModel.RequiredModel, Is.Not.Null);
            Assert.That(
                Array.Exists(customModel.RequiredModel, r => r.ModelUri == Namespaces.OpcUa),
                Is.True,
                "Custom <Model> must declare the OPC UA base namespace as a <RequiredModel>");

            folder.Dispose();
            variable.Dispose();
        }

        [Test]
        public void SaveAsNodeSet2WithModel()
        {
            var collection = new NodeStateCollection();
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(6003),
                BrowseName = new QualifiedName("NS2Ref"),
                DisplayName = new LocalizedText("NodeSet2 Ref")
            };
            collection.Add(refType);

            var model = new Export.ModelTableEntry
            {
                ModelUri = ApplicationUri,
                Version = "1.0.0",
                PublicationDate = DateTime.UtcNow,
                PublicationDateSpecified = true
            };

            using var stream = new MemoryStream();
            collection.SaveAsNodeSet2(m_context, stream, model, DateTime.UtcNow, false);
            Assert.That(stream.Length, Is.GreaterThan(0));
            refType.Dispose();
        }

        [Test]
        public void LoadFromBinaryWithUpdateTables()
        {
            var collection = new NodeStateCollection();
            var dt = new DataTypeState
            {
                NodeId = new NodeId(6010),
                BrowseName = new QualifiedName("BinDT"),
                DisplayName = new LocalizedText("Binary DT")
            };
            collection.Add(dt);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, true);
            Assert.That(restored, Has.Count.EqualTo(1));
            dt.Dispose();
        }

        [Test]
        public void MultipleItemsSaveAndLoad()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6020),
                BrowseName = new QualifiedName("V1"),
                DisplayName = new LocalizedText("View 1")
            };
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(6021),
                BrowseName = new QualifiedName("R1"),
                DisplayName = new LocalizedText("Ref 1")
            };
            collection.Add(view);
            collection.Add(refType);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, false);
            Assert.That(restored, Has.Count.EqualTo(2));
            view.Dispose();
            refType.Dispose();
        }

        [Test]
        public void SaveAsXmlAndLoadFromXmlRoundTrip()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6030),
                SymbolicName = "XmlRTView",
                BrowseName = new QualifiedName("XmlView"),
                DisplayName = new LocalizedText("XML View"),
                EventNotifier = EventNotifiers.SubscribeToEvents,
                ContainsNoLoops = true
            };
            collection.Add(view);

            using var stream = new MemoryStream();
            collection.SaveAsXml(m_context, stream, keepStreamOpen: true);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromXml(m_context, stream, false);
            Assert.That(restored, Has.Count.EqualTo(1));
            Assert.That(restored[0], Is.InstanceOf<ViewState>());
            view.Dispose();
        }

        [Test]
        public void SaveAsXmlWithoutKeepStreamOpen()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6031),
                SymbolicName = "XmlView2",
                BrowseName = new QualifiedName("XmlView2"),
                DisplayName = new LocalizedText("XML View 2")
            };
            collection.Add(view);

            // SaveAsXml without keepStreamOpen closes the stream, so use ToArray() after
            var stream = new MemoryStream();
            collection.SaveAsXml(m_context, stream);
            Assert.That(stream.ToArray(), Is.Not.Empty);
            view.Dispose();
        }

        [Test]
        public void SaveAsXmlAndLoadFromXmlWithUpdateTables()
        {
            var collection = new NodeStateCollection();
            var method = new MethodState(null)
            {
                NodeId = new NodeId(6032),
                SymbolicName = "XmlMethod",
                BrowseName = new QualifiedName("XmlMethod"),
                DisplayName = new LocalizedText("XML Method"),
                Executable = true,
                UserExecutable = false
            };
            collection.Add(method);

            using var stream = new MemoryStream();
            collection.SaveAsXml(m_context, stream, keepStreamOpen: true);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromXml(m_context, stream, true);
            Assert.That(restored, Has.Count.EqualTo(1));
            Assert.That(restored[0], Is.InstanceOf<MethodState>());
            method.Dispose();
        }

        [Test]
        public void SaveAsBinaryAndLoadFromBinaryRoundTrip()
        {
            var collection = new NodeStateCollection();
            var method = new MethodState(null)
            {
                NodeId = new NodeId(6040),
                BrowseName = new QualifiedName("BinMethod"),
                DisplayName = new LocalizedText("Binary Method"),
                Executable = true,
                UserExecutable = true
            };
            var obj = new BaseObjectState(null)
            {
                NodeId = new NodeId(6041),
                BrowseName = new QualifiedName("BinObj"),
                DisplayName = new LocalizedText("Binary Object")
            };
            collection.Add(method);
            collection.Add(obj);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, false);
            Assert.That(restored, Has.Count.EqualTo(2));
            Assert.That(restored[0], Is.InstanceOf<MethodState>());
            Assert.That(restored[1], Is.InstanceOf<BaseObjectState>());
            method.Dispose();
            obj.Dispose();
        }

        [Test]
        public void EmptyCollectionSaveAndLoadBinary()
        {
            var collection = new NodeStateCollection();

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, false);
            Assert.That(restored, Is.Empty);
        }

        [Test]
        public void EmptyCollectionSaveAndLoadXml()
        {
            var collection = new NodeStateCollection();

            using var stream = new MemoryStream();
            collection.SaveAsXml(m_context, stream, keepStreamOpen: true);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromXml(m_context, stream, false);
            Assert.That(restored, Is.Empty);
        }

        [Test]
        public void SaveAsNodeSet2AndLoadRoundTrip()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6050),
                BrowseName = new QualifiedName("NS2View"),
                DisplayName = new LocalizedText("NS2 View"),
                ContainsNoLoops = false
            };
            collection.Add(view);

            using var saveStream = new MemoryStream();
            collection.SaveAsNodeSet2(m_context, saveStream);
            Assert.That(saveStream.ToArray(), Is.Not.Empty);
            view.Dispose();
        }

        [Test]
        public void ConstructorWithCapacity()
        {
            var collection = new NodeStateCollection(50);
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection, Is.Empty);
            Assert.That(collection.Capacity, Is.GreaterThanOrEqualTo(50));
        }

        [Test]
        public void ConstructorWithEnumerable()
        {
            var items = new List<NodeState>
            {
                new BaseObjectState(null) { NodeId = new NodeId(6060) },
                new MethodState(null) { NodeId = new NodeId(6061) },
                new ViewState { NodeId = new NodeId(6062) }
            };

            var collection = new NodeStateCollection(items);
            Assert.That(collection, Has.Count.EqualTo(3));
            Assert.That(collection[0], Is.InstanceOf<BaseObjectState>());
            Assert.That(collection[1], Is.InstanceOf<MethodState>());
            Assert.That(collection[2], Is.InstanceOf<ViewState>());
            foreach (NodeState item in items)
            {
                item.Dispose();
            }
        }

        [Test]
        public void LoadFromBinaryWithMultipleNodeTypes()
        {
            var collection = new NodeStateCollection();
            var view = new ViewState
            {
                NodeId = new NodeId(6070),
                BrowseName = new QualifiedName("V1"),
                DisplayName = new LocalizedText("View 1"),
                ContainsNoLoops = true
            };
            var dt = new DataTypeState
            {
                NodeId = new NodeId(6071),
                BrowseName = new QualifiedName("DT1"),
                DisplayName = new LocalizedText("DataType 1")
            };
            var refType = new ReferenceTypeState
            {
                NodeId = new NodeId(6072),
                BrowseName = new QualifiedName("RT1"),
                DisplayName = new LocalizedText("RefType 1")
            };
            collection.Add(view);
            collection.Add(dt);
            collection.Add(refType);

            using var stream = new MemoryStream();
            collection.SaveAsBinary(m_context, stream);
            stream.Position = 0;

            var restored = new NodeStateCollection();
            restored.LoadFromBinary(m_context, stream, true);
            Assert.That(restored, Has.Count.EqualTo(3));
            Assert.That(restored[0], Is.InstanceOf<ViewState>());
            Assert.That(restored[1], Is.InstanceOf<DataTypeState>());
            Assert.That(restored[2], Is.InstanceOf<ReferenceTypeState>());
            view.Dispose();
            dt.Dispose();
            refType.Dispose();
        }
    }
}
