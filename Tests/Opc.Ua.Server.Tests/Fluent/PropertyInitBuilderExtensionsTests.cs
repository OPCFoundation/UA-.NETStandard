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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="PropertyInitBuilderExtensions"/> — fluent
    /// <c>WithProperty(string|QualifiedName, value)</c> helpers.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class PropertyInitBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        private static SystemContext CreateContext()
        {
            var ns = new NamespaceTable();
            ns.Append(Ua.Namespaces.OpcUa);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = ns
            };
        }

        private static (NodeManagerBuilder Builder, BaseObjectState Root,
            PropertyState<string> StringProp,
            PropertyState<int> IntProp,
            BaseObjectState NonVariableChild)
            CreateBuilderWithObject()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            PropertyState<string> stringProp =
                new PropertyState<string>.Implementation<VariantBuilder>(root)
                {
                    NodeId = new NodeId("Root.Manufacturer", kNs),
                    BrowseName = new QualifiedName("Manufacturer", kNs),
                    DisplayName = new LocalizedText("Manufacturer"),
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.Scalar
                };
            root.AddChild(stringProp);

            PropertyState<int> intProp =
                new PropertyState<int>.Implementation<VariantBuilder>(root)
                {
                    NodeId = new NodeId("Root.Count", kNs),
                    BrowseName = new QualifiedName("Count", kNs),
                    DisplayName = new LocalizedText("Count"),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                };
            root.AddChild(intProp);

            var nonVar = new BaseObjectState(root)
            {
                NodeId = new NodeId("Root.NonVar", kNs),
                BrowseName = new QualifiedName("NonVar", kNs),
                DisplayName = new LocalizedText("NonVar")
            };
            root.AddChild(nonVar);

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [stringProp.NodeId] = stringProp,
                [intProp.NodeId] = intProp,
                [nonVar.NodeId] = nonVar
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, root, stringProp, intProp, nonVar);
        }

        [Test]
        public void WithPropertyStringSetsValue()
        {
            (NodeManagerBuilder b, _, PropertyState<string> sp, _, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder chain = nb.WithProperty("Manufacturer", "SimPump Corp");

            Assert.That(chain, Is.SameAs(nb));
            Assert.That(sp.Value, Is.EqualTo("SimPump Corp"));
        }

        [Test]
        public void WithPropertyIntSetsValue()
        {
            (NodeManagerBuilder b, _, _, PropertyState<int> ip, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty("Count", 42);

            Assert.That(ip.Value, Is.EqualTo(42));
        }

        [Test]
        public void WithPropertyVariantSetsValue()
        {
            (NodeManagerBuilder b, _, PropertyState<string> sp, _, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty("Manufacturer", Variant.From("Vendor X"));

            Assert.That(sp.Value, Is.EqualTo("Vendor X"));
        }

        [Test]
        public void WithPropertyQualifiedNameSetsValue()
        {
            (NodeManagerBuilder b, _, PropertyState<string> sp, _, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty(new QualifiedName("Manufacturer", kNs), Variant.From("Acme"));

            Assert.That(sp.Value, Is.EqualTo("Acme"));
        }

        [Test]
        public void WithPropertyChainsMultipleSetters()
        {
            (NodeManagerBuilder b, _, PropertyState<string> sp, PropertyState<int> ip, _) =
                CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty("Manufacturer", "Vendor A")
              .WithProperty("Count", 7)
              .WithProperty("Manufacturer", "Vendor B");

            Assert.That(sp.Value, Is.EqualTo("Vendor B"));
            Assert.That(ip.Value, Is.EqualTo(7));
        }

        [Test]
        public void WithPropertyUnknownNameThrowsBadNodeIdUnknown()
        {
            (NodeManagerBuilder b, _, _, _, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => nb.WithProperty("DoesNotExist", "x"))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void WithPropertyNonVariableChildThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, _, _, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => nb.WithProperty("NonVar", "x"))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WithPropertyEmptyBrowseNameThrowsArgumentException()
        {
            (NodeManagerBuilder b, _, _, _, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            Assert.Throws<ArgumentException>(
                () => nb.WithProperty(string.Empty, "x"));
        }

        [Test]
        public void WithPropertyOnNullBuilderThrowsArgumentNullException()
        {
            INodeBuilder nb = null!;
            Assert.Throws<ArgumentNullException>(
                () => nb.WithProperty("Manufacturer", "x"));
        }

        [Test]
        public void WithPropertyNullQualifiedNameThrowsArgumentNullException()
        {
            (NodeManagerBuilder b, _, _, _, _) = CreateBuilderWithObject();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            Assert.Throws<ArgumentNullException>(
                () => nb.WithProperty(QualifiedName.Null, Variant.From("x")));
        }

        [Test]
        public void WithPropertyOnTypedBuilderPreservesTypedView()
        {
            (NodeManagerBuilder b, _, _, _, _) = CreateBuilderWithObject();
            INodeBuilder<BaseObjectState> typed = b.Node<BaseObjectState>(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> chain = typed.WithProperty("Manufacturer", Variant.From("X"));

            Assert.That(chain, Is.SameAs(typed));
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Prop)
            CreateBuilderWithDataVariable(string browseName)
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var prop = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Root." + browseName, kNs),
                BrowseName = new QualifiedName(browseName, kNs),
                DisplayName = new LocalizedText(browseName),
                DataType = DataTypeIds.BaseDataType,
                ValueRank = ValueRanks.Scalar
            };
            root.AddChild(prop);

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [prop.NodeId] = prop
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, prop);
        }

        [Test]
        public void WithPropertyBoolSetsValue()
        {
            (NodeManagerBuilder b, BaseDataVariableState prop) =
                CreateBuilderWithDataVariable("Enabled");
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty("Enabled", true);

            Assert.That(prop.WrappedValue.AsBoxedObject(), Is.True);
        }

        [Test]
        public void WithPropertyDoubleSetsValue()
        {
            (NodeManagerBuilder b, BaseDataVariableState prop) =
                CreateBuilderWithDataVariable("Pressure");
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty("Pressure", 3.14);

            Assert.That(prop.WrappedValue.AsBoxedObject(), Is.EqualTo(3.14));
        }

        [Test]
        public void WithPropertyLongSetsValue()
        {
            (NodeManagerBuilder b, BaseDataVariableState prop) =
                CreateBuilderWithDataVariable("BigCount");
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty("BigCount", 1234567890123L);

            Assert.That(prop.WrappedValue.AsBoxedObject(), Is.EqualTo(1234567890123L));
        }

        [Test]
        public void WithPropertyByteSetsValue()
        {
            (NodeManagerBuilder b, BaseDataVariableState prop) =
                CreateBuilderWithDataVariable("Flags");
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.WithProperty("Flags", (byte)0xAB);

            Assert.That(prop.WrappedValue.AsBoxedObject(), Is.EqualTo((byte)0xAB));
        }

        [Test]
        public void WithPropertyNodeIdSetsValue()
        {
            (NodeManagerBuilder b, BaseDataVariableState prop) =
                CreateBuilderWithDataVariable("RefId");
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            var value = new NodeId(42u, kNs);
            nb.WithProperty("RefId", value);

            Assert.That(prop.WrappedValue.AsBoxedObject(), Is.EqualTo(value));
        }

        [Test]
        public void WithPropertyLocalizedTextSetsValue()
        {
            (NodeManagerBuilder b, BaseDataVariableState prop) =
                CreateBuilderWithDataVariable("Description");
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            var value = new LocalizedText("en", "hello");
            nb.WithProperty("Description", value);

            Assert.That(prop.WrappedValue.AsBoxedObject(), Is.EqualTo(value));
        }
    }
}
