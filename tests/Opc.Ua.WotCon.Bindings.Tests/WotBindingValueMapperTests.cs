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

using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class WotBindingValueMapperTests
    {
        [TestCase("EventId", 0)]
        [TestCase("nsu=http://opcfoundation.org/UA/;EventId", 0)]
        [TestCase("{http://opcfoundation.org/UA/}EventId", 0)]
        [TestCase("nsu=http://example.org/model/;EventId", 2)]
        [TestCase("{http://example.org/model/}EventId", 2)]
        public void ResolvesSelectedFieldNamesWithoutLosingTheirNamespace(string element, int expectedIndex)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:unrelated");
            namespaces.Append("http://example.org/model/");

            QualifiedName name = WotBindingValueMapper.ResolveBrowseName(element, namespaces);

            Assert.That(name.Name, Is.EqualTo("EventId"));
            Assert.That(name.NamespaceIndex, Is.EqualTo(expectedIndex));
            Assert.That(namespaces.Count, Is.EqualTo(3));
        }

        [TestCase("nsu=urn:unknown;EventId")]
        [TestCase("{urn:unknown}EventId")]
        [TestCase("nsu=urn:unknown")]
        [TestCase("{urn:unknown}")]
        public void UnknownOrMalformedSelectedNamespacesFailWithoutExtendingTheTable(string element)
        {
            var namespaces = new NamespaceTable();

            ServiceResultException? failure = Assert.Throws<ServiceResultException>(
                () => WotBindingValueMapper.ResolveBrowseName(element, namespaces));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            Assert.That(namespaces.Count, Is.EqualTo(1));
        }

        [Test]
        public void MapsNestedDataValuesAndMatricesWithoutLosingDimensionsOrStatus()
        {
            ServiceMessageContext source = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            ushort from = source.NamespaceUris.GetIndexOrAppend("urn:shared");
            ServiceMessageContext target = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            target.NamespaceUris.Append("urn:unrelated");
            ushort to = target.NamespaceUris.GetIndexOrAppend("urn:shared");
            var matrix = new NodeId[,] { { new("one", from), new("two", from) } };
            var timestamp = new DateTimeUtc(2026, 8, 1, 0, 0, 0);
            var input = new Variant(new DataValue(
                new Variant(matrix.ToMatrixOf()), StatusCodes.Uncertain, timestamp, timestamp));

            Variant mapped = WotBindingValueMapper.Translate(input, source, target);

            Assert.That(mapped.TryGetValue(out DataValue data), Is.True);
            Assert.That(data.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(data.SourceTimestamp, Is.EqualTo(timestamp));
            Assert.That(data.ServerTimestamp, Is.EqualTo(timestamp));
            Assert.That(data.WrappedValue.TryGetValue(out MatrixOf<NodeId> result), Is.True);
            ArrayOf<NodeId> values = result.ToArrayOf(out int[] dimensions);
            Assert.That(dimensions, Is.EqualTo(s_dimensions));
            Assert.That(values, Is.EqualTo(new[] { new NodeId("one", to), new NodeId("two", to) }));
            Assert.That(matrix[0, 0].NamespaceIndex, Is.EqualTo(from));
        }

        [Test]
        public void UnknownSourceIndexIsNotReinterpretedInTheTargetTable()
        {
            ServiceMessageContext source = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            ServiceMessageContext target = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            target.NamespaceUris.Append("urn:must-not-be-used");
            var value = new Variant(new QualifiedName("Wrong", 1));

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => WotBindingValueMapper.Translate(value, source, target));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(target.NamespaceUris.Count, Is.EqualTo(2));
        }

        [Test]
        public void AbsoluteExpandedNodeIdsRemainPortableWithoutExtendingTheRemoteTable()
        {
            ServiceMessageContext source = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            ServiceMessageContext target = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            var identity = new ExpandedNodeId("foreign", "urn:external");

            Variant mapped = WotBindingValueMapper.Translate(new Variant(identity), source, target);

            Assert.That(mapped.TryGetValue(out ExpandedNodeId result), Is.True);
            Assert.That(result, Is.EqualTo(identity));
            Assert.That(target.NamespaceUris.Count, Is.EqualTo(1));
        }

        private static readonly int[] s_dimensions = [1, 2];
    }
}
