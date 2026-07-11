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
using System.Xml;
using NUnit.Framework;
using RuntimeOptionSet = Opc.Ua.Encoders.OptionSet;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class OptionSetTests
    {
        private static readonly byte[] s_readableExecutableBytes = [0x01, 0x04];
        private static readonly string[] s_readableExecutableNames = ["Readable", "Executable"];
        private static readonly string[] s_executableName = ["Executable"];

        [Test]
        public void ConstructorInitializesMetadataAndDefinitionByteLength()
        {
            EnumDefinition definition = CreateDefinition();
            RuntimeOptionSet optionSet = CreateOptionSet(definition);

            Assert.That(optionSet.Type, Is.EqualTo(typeof(RuntimeOptionSet)));
            Assert.That(optionSet.XmlName, Is.EqualTo(new XmlQualifiedName("AccessRights", "urn:test")));
            Assert.That(optionSet.TypeId, Is.EqualTo(new ExpandedNodeId(new NodeId(5001, 2))));
            Assert.That(optionSet.BinaryEncodingId, Is.EqualTo(new ExpandedNodeId(new NodeId(5002, 2))));
            Assert.That(optionSet.XmlEncodingId, Is.EqualTo(new ExpandedNodeId(new NodeId(5003, 2))));
            Assert.That(optionSet.Definition, Is.SameAs(definition));
            Assert.That(optionSet.ByteLength, Is.EqualTo(2));
            Assert.That(optionSet.GetDataTypeDefinition(new NamespaceTable()), Is.SameAs(definition));
        }

        [Test]
        public void ConstructorRejectsMissingXmlNameOrDefinition()
        {
            EnumDefinition definition = CreateDefinition();
            var xmlName = new XmlQualifiedName("AccessRights", "urn:test");

            Assert.Throws<ArgumentNullException>(() =>
                new RuntimeOptionSet(null, NodeId.Null, NodeId.Null, NodeId.Null, definition));
            Assert.Throws<ArgumentNullException>(() =>
                new RuntimeOptionSet(xmlName, NodeId.Null, NodeId.Null, NodeId.Null, null));
        }

        [Test]
        public void FieldNameIndexerSetsValueAndValidBits()
        {
            RuntimeOptionSet optionSet = CreateOptionSet();

            optionSet["Readable"] = true;
            optionSet["Executable"] = true;

            Assert.That(optionSet["Readable"], Is.True);
            Assert.That(optionSet["Executable"], Is.True);
            Assert.That(optionSet["Writable"], Is.False);
            Assert.That(optionSet.Value.Span.ToArray(), Is.EqualTo(s_readableExecutableBytes));
            Assert.That(optionSet.ValidBits.Span.ToArray(), Is.EqualTo(s_readableExecutableBytes));
            Assert.That(optionSet.GetSetFieldNames(), Is.EqualTo(s_readableExecutableNames));
            Assert.That(optionSet.ToString(), Is.EqualTo("AccessRights {Readable, Executable}"));

            optionSet["Readable"] = false;

            Assert.That(optionSet["Readable"], Is.False);
            Assert.That(optionSet.ValidBits.Span.ToArray(), Is.EqualTo(s_readableExecutableBytes));
            Assert.That(optionSet.GetSetFieldNames(), Is.EqualTo(s_executableName));
        }

        [Test]
        public void BitIndexerRejectsOutOfRangeWritesButAllowsSafeReads()
        {
            RuntimeOptionSet optionSet = CreateOptionSet();

            optionSet[15] = true;

            Assert.That(optionSet[15], Is.True);
            Assert.That(optionSet[-1], Is.False);
            Assert.That(optionSet[16], Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => optionSet[-1] = true);
            Assert.Throws<ArgumentOutOfRangeException>(() => optionSet[16] = true);
        }

        [Test]
        public void FieldNameIndexerRejectsUnknownNames()
        {
            RuntimeOptionSet optionSet = CreateOptionSet();

            Assert.Throws<ArgumentException>(() => _ = optionSet["Missing"]);
            Assert.Throws<ArgumentException>(() => optionSet["Missing"] = true);
            Assert.Throws<ArgumentException>(() => _ = optionSet[string.Empty]);
        }

        [Test]
        public void CloneCopiesValuesAndCreateInstanceCopiesOnlyMetadata()
        {
            RuntimeOptionSet optionSet = CreateOptionSet();
            optionSet["Writable"] = true;

            var clone = (RuntimeOptionSet)optionSet.Clone();
            var instance = (RuntimeOptionSet)optionSet.CreateInstance();

            Assert.That(clone, Is.Not.SameAs(optionSet));
            Assert.That(clone["Writable"], Is.True);
            Assert.That(clone.Value.Span.ToArray(), Is.EqualTo(optionSet.Value.Span.ToArray()));
            Assert.That(instance.Definition, Is.SameAs(optionSet.Definition));
            Assert.That(instance.Value.IsEmpty, Is.True);
            Assert.That(instance.ValidBits.IsEmpty, Is.True);

            clone["Writable"] = false;

            Assert.That(optionSet["Writable"], Is.True);
            Assert.That(clone["Writable"], Is.False);
        }

        [Test]
        public void EmptyAndNegativeOnlyDefinitionsHaveNoSetFields()
        {
            var empty = CreateOptionSet(new EnumDefinition());
            var negativeOnly = CreateOptionSet(new EnumDefinition
            {
                Fields = new[]
                {
                    new EnumField { Name = "Reserved", Value = -1 }
                }.ToArrayOf()
            });

            Assert.That(empty.ByteLength, Is.Zero);
            Assert.That(empty.GetSetFieldNames(), Is.Empty);
            Assert.That(empty.ToString(), Is.EqualTo("AccessRights {}"));
            Assert.That(negativeOnly.ByteLength, Is.Zero);
            Assert.That(negativeOnly.GetSetFieldNames(), Is.Empty);
            Assert.Throws<ArgumentException>(() => _ = negativeOnly["Reserved"]);
        }

        private static RuntimeOptionSet CreateOptionSet()
        {
            return CreateOptionSet(CreateDefinition());
        }

        private static RuntimeOptionSet CreateOptionSet(EnumDefinition definition)
        {
            return new RuntimeOptionSet(
                new XmlQualifiedName("AccessRights", "urn:test"),
                new ExpandedNodeId(new NodeId(5001, 2)),
                new ExpandedNodeId(new NodeId(5002, 2)),
                new ExpandedNodeId(new NodeId(5003, 2)),
                definition);
        }

        private static EnumDefinition CreateDefinition()
        {
            return new EnumDefinition
            {
                IsOptionSet = true,
                Fields = new[]
                {
                    new EnumField { Name = "Readable", Value = 0 },
                    new EnumField { Name = "Writable", Value = 1 },
                    new EnumField { Name = "Executable", Value = 10 }
                }.ToArrayOf()
            };
        }
    }
}
