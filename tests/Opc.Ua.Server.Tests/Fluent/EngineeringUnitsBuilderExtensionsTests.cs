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
    /// Tests for <see cref="EngineeringUnitsBuilderExtensions"/> — the
    /// fluent <c>WithEngineeringUnits</c> / <c>WithEURange</c> /
    /// <c>WithUnits</c> helpers on <see cref="IVariableBuilder{TValue}"/>.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class EngineeringUnitsBuilderExtensionsTests
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
            AnalogItemState<double> AnalogVar, BaseDataVariableState NonAnalogVar)
            CreateBuilderWithAnalog()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var analog = AnalogItemState<double>.With<VariantBuilder>(root);
            analog.NodeId = new NodeId("Root.Temp", kNs);
            analog.BrowseName = new QualifiedName("Temp", kNs);
            analog.DisplayName = new LocalizedText("Temp");
            analog.DataType = DataTypeIds.Double;
            analog.ValueRank = ValueRanks.Scalar;
            root.AddChild(analog);

            var nonAnalog = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Root.PlainVar", kNs),
                BrowseName = new QualifiedName("PlainVar", kNs),
                DisplayName = new LocalizedText("PlainVar"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            root.AddChild(nonAnalog);

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [analog.NodeId] = analog,
                [nonAnalog.NodeId] = nonAnalog
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, root, analog, nonAnalog);
        }

        [Test]
        public void WithEngineeringUnitsSetsValueOnAnalogVariable()
        {
            (NodeManagerBuilder b, _, AnalogItemState<double> analog, _) = CreateBuilderWithAnalog();
            IVariableBuilder<double> vb = b.Variable<double>(new NodeId("Root.Temp", kNs));

            var units = new EUInformation("K", "Kelvin", "http://www.opcfoundation.org/UA/units/un/cefact");
            IVariableBuilder<double> chain = vb.WithEngineeringUnits(units);

            Assert.That(chain, Is.SameAs(vb), "Builder is returned for chaining.");
            Assert.That(analog.EngineeringUnits, Is.Not.Null,
                "EngineeringUnits property must be created on demand.");
            Assert.That(analog.EngineeringUnits!.Value, Is.SameAs(units));
        }

        [Test]
        public void WithEURangeSetsRangeOnAnalogVariable()
        {
            (NodeManagerBuilder b, _, AnalogItemState<double> analog, _) = CreateBuilderWithAnalog();
            IVariableBuilder<double> vb = b.Variable<double>(new NodeId("Root.Temp", kNs));

            vb.WithEURange(min: 233.15, max: 473.15);

            Assert.That(analog.EURange, Is.Not.Null);
            Assert.That(analog.EURange!.Value, Is.Not.Null);
            Assert.That(analog.EURange.Value.Low, Is.EqualTo(233.15));
            Assert.That(analog.EURange.Value.High, Is.EqualTo(473.15));
        }

        [Test]
        public void WithUnitsAppliesBoth()
        {
            (NodeManagerBuilder b, _, AnalogItemState<double> analog, _) = CreateBuilderWithAnalog();
            IVariableBuilder<double> vb = b.Variable<double>(new NodeId("Root.Temp", kNs));

            var units = new EUInformation("Pa", "Pascal", "ns");
            vb.WithUnits(units, min: 0, max: 1_000_000);

            Assert.That(analog.EngineeringUnits!.Value, Is.SameAs(units));
            Assert.That(analog.EURange!.Value.Low, Is.Zero);
            Assert.That(analog.EURange.Value.High, Is.EqualTo(1_000_000));
        }

        [Test]
        public void WithEngineeringUnitsOnNonAnalogThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithAnalog();
            IVariableBuilder<int> vb = b.Variable<int>(new NodeId("Root.PlainVar", kNs));

            var units = new EUInformation("K", "Kelvin", "ns");
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => vb.WithEngineeringUnits(units));
            Assert.That(ex!.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WithEURangeOnNonAnalogThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithAnalog();
            IVariableBuilder<int> vb = b.Variable<int>(new NodeId("Root.PlainVar", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => vb.WithEURange(0, 100));
            Assert.That(ex!.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void WithEURangeMinGreaterThanMaxThrowsArgumentException()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithAnalog();
            IVariableBuilder<double> vb = b.Variable<double>(new NodeId("Root.Temp", kNs));

            Assert.Throws<ArgumentException>(() => vb.WithEURange(min: 100, max: 0));
        }

        [Test]
        public void WithEngineeringUnitsNullUnitsThrowsArgumentNullException()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilderWithAnalog();
            IVariableBuilder<double> vb = b.Variable<double>(new NodeId("Root.Temp", kNs));

            Assert.Throws<ArgumentNullException>(
                () => vb.WithEngineeringUnits(null!));
        }

        [Test]
        public void WithEngineeringUnitsOnNullBuilderThrowsArgumentNullException()
        {
            IVariableBuilder<double> vb = null!;
            var units = new EUInformation("K", "Kelvin", "ns");
            Assert.Throws<ArgumentNullException>(() => vb.WithEngineeringUnits(units));
        }

        [Test]
        public void WithEngineeringUnitsCalledTwiceReusesProperty()
        {
            (NodeManagerBuilder b, _, AnalogItemState<double> analog, _) = CreateBuilderWithAnalog();
            IVariableBuilder<double> vb = b.Variable<double>(new NodeId("Root.Temp", kNs));

            var unitsA = new EUInformation("K", "Kelvin", "ns");
            var unitsB = new EUInformation("Pa", "Pascal", "ns");

            vb.WithEngineeringUnits(unitsA);
            PropertyState<EUInformation>? first = analog.EngineeringUnits;
            vb.WithEngineeringUnits(unitsB);

            Assert.That(analog.EngineeringUnits, Is.SameAs(first),
                "EngineeringUnits property must not be replaced on a second call.");
            Assert.That(analog.EngineeringUnits!.Value, Is.SameAs(unitsB));
        }
    }
}
