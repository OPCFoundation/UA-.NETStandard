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
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ObjectLayoutInspector;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BuiltInTypeTests
    {
        [Test]
        public void BoolTests()
        {
            Assert.That(Unsafe.SizeOf<bool>(), Is.EqualTo(1));
        }

        [Test]
        public void DateTimeTests()
        {
            var layout = TypeLayout.GetLayout<DateTime>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<DateTime>(), Is.EqualTo(Unsafe.SizeOf<ulong>()));
            Assert.That(layout.Fields, Has.Length.EqualTo(1));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(ulong)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
        }

        [Test]
        public void StatusCodeTests()
        {
            var layout = TypeLayout.GetLayout<StatusCode>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<StatusCode>(), Is.EqualTo(16));
            Assert.That(layout.Fields, Has.Length.EqualTo(3)); // Todo: Remove padding

            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(string)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(uint)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(4));
        }

        [Test]
        public void NodeIdSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<NodeId>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<NodeId>(), Is.EqualTo(16));
            Assert.That(layout.Fields, Has.Length.EqualTo(2));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(object)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(NodeId.Inner)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
        }

        [Test]
        public void ExpandedNodeIdSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<ExpandedNodeId>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ExpandedNodeId>(), Is.EqualTo(32));
            Assert.That(layout.Fields, Has.Length.EqualTo(2));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(NodeId)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(16));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(ExpandedNodeId.Inner)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(16));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(16));
        }

        [Test]
        public void ExpandedNodeIdInnerOffsetTests()
        {
            var layout = TypeLayout.GetLayout<ExpandedNodeId.Inner>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ExpandedNodeId.Inner>(), Is.EqualTo(16));
            Assert.That(layout.Fields, Has.Length.EqualTo(3));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(object)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(uint)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(4));
            Assert.That(((FieldLayout)layout.Fields[2]).FieldInfo.FieldType, Is.EqualTo(typeof(uint)));
            Assert.That(layout.Fields[2].Offset, Is.EqualTo(12));
            Assert.That(layout.Fields[2].Size, Is.EqualTo(4));
        }

        [Test]
        public void TypeInfoOffsetsTests()
        {
            var layout = TypeLayout.GetLayout<TypeInfo>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<TypeInfo>(), Is.EqualTo(4));
            Assert.That(layout.Fields, Has.Length.EqualTo(3));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(short)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(2));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(byte)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(2));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(1));
            Assert.That(((FieldLayout)layout.Fields[2]).FieldInfo.FieldType, Is.EqualTo(typeof(byte)));
            Assert.That(layout.Fields[2].Offset, Is.EqualTo(3));
            Assert.That(layout.Fields[2].Size, Is.EqualTo(1));
        }

        [Test]
        public void VariantSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<Variant>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<Variant>(), Is.EqualTo(24));

            Assert.That(layout.Fields, Has.Length.EqualTo(4)); // TODO: Remove 4 byte padding field
            Assert.That(layout.Fields[3].Offset, Is.EqualTo(20));

            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(object)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(Variant.Union)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[2]).FieldInfo.FieldType, Is.EqualTo(typeof(TypeInfo)));
            Assert.That(layout.Fields[2].Offset, Is.EqualTo(16));
            Assert.That(layout.Fields[2].Size, Is.EqualTo(4));
        }

        [Test]
        public void ExtensionObjectSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<ExtensionObject>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ExtensionObject>(), Is.EqualTo(40));
            Assert.That(layout.Fields, Has.Length.EqualTo(2));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(object)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(ExpandedNodeId)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(32));
        }

        [Test]
        public void VariantUnionSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<Variant.Union>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<Variant.Union>(), Is.EqualTo(8));
        }

        [Test]
        public void MatrixOfIntSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<MatrixOf<int>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<MatrixOf<int>>(), Is.EqualTo(24));
        }

        [Test]
        public void MatrixByteStringSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<MatrixOf<ByteString>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<MatrixOf<ByteString>>(), Is.EqualTo(24));
        }

        [Test]
        public void MatrixNodeIdSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<MatrixOf<NodeId>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<MatrixOf<NodeId>>(), Is.EqualTo(24));
        }

        [Test]
        public void MatrixVariantSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<MatrixOf<Variant>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<MatrixOf<Variant>>(), Is.EqualTo(24));
            Assert.That(Unsafe.SizeOf<Variant>(), Is.EqualTo(Unsafe.SizeOf<MatrixOf<Variant>>()));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(int[])));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(ReadOnlyMemory<Variant>)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(16));
        }

        [Test]
        public void ReadOnlyMemoryEqualsSizeOfRomOfTTests()
        {
            var layout = TypeLayout.GetLayout<ReadOnlyMemory<Variant>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ReadOnlyMemory<Variant>>(), Is.EqualTo(Unsafe.SizeOf<ReadOnlyMemory>()));
        }

        [Test]
        public void ByteStringSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<ByteString>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ByteString>(), Is.EqualTo(16));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(ReadOnlyMemory<byte>)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(16));
        }

        [Test]
        public void LocalizedTextSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<LocalizedText>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<LocalizedText>(), Is.EqualTo(24));
            Assert.That(layout.Fields, Has.Length.EqualTo(3));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(string)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(string)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[2]).FieldInfo.FieldType, Is.EqualTo(typeof(LocalizedTextFormatAndTranslation)));
            Assert.That(layout.Fields[2].Offset, Is.EqualTo(16));
            Assert.That(layout.Fields[2].Size, Is.EqualTo(8));
        }

        [Test]
        public void QualifiedNameSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<QualifiedName>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<QualifiedName>(), Is.EqualTo(16));
            Assert.That(layout.Fields, Has.Length.EqualTo(3)); // TODO: Remove padding
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(string)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(8));
            Assert.That(((FieldLayout)layout.Fields[1]).FieldInfo.FieldType, Is.EqualTo(typeof(ushort)));
            Assert.That(layout.Fields[1].Offset, Is.EqualTo(8));
            Assert.That(layout.Fields[1].Size, Is.EqualTo(2));
            Assert.That(layout.Fields[2].Size, Is.EqualTo(6));
        }

        [Test]
        public void ArrayOfIntSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<ArrayOf<int>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ArrayOf<int>>(), Is.EqualTo(16));
        }

        [Test]
        public void ArrayOfByteStringSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<ArrayOf<ByteString>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ArrayOf<ByteString>>(), Is.EqualTo(16));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(ReadOnlyMemory<ByteString>)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(16));
        }

        [Test]
        public void ArrayOfNodeIdSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<ArrayOf<NodeId>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ArrayOf<NodeId>>(), Is.EqualTo(16));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(ReadOnlyMemory<NodeId>)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(16));
        }

        [Test]
        public void ArrayOfVariantSizeOfTests()
        {
            var layout = TypeLayout.GetLayout<ArrayOf<Variant>>();
            TestContext.Out.WriteLine(layout.ToString(true));
            Assert.That(Unsafe.SizeOf<ArrayOf<Variant>>(), Is.EqualTo(16));
            Assert.That(((FieldLayout)layout.Fields[0]).FieldInfo.FieldType, Is.EqualTo(typeof(ReadOnlyMemory<Variant>)));
            Assert.That(layout.Fields[0].Offset, Is.EqualTo(0));
            Assert.That(layout.Fields[0].Size, Is.EqualTo(16));
        }
    }
}
