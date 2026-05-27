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
using NUnit.Framework;
using Opc.Ua.SourceGeneration.Snapshot;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Round-trip and validation tests for the <see cref="ModelSnapshotV1"/>
    /// wire format consumed by the cross-assembly model snapshot machinery.
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ModelSnapshotV1Tests
    {
        [Test]
        public void WriteThenRead_RoundTripsExactly()
        {
            var snapshot = new ModelSnapshotV1 { ModelUri = "http://example.org/UA/Demo/" };
            snapshot.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "PumpType",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "Pump",
                Kind = SnapshotNodeKind.ObjectType,
                BaseTypeName = "ComponentType",
                BaseTypeNamespace = "http://opcfoundation.org/UA/DI/",
                NumericId = 5001,
                IsAbstract = false
            });
            snapshot.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "DeviceHealthEnumeration",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "DeviceHealthEnumeration",
                Kind = SnapshotNodeKind.DataType,
                BaseTypeName = "Enumeration",
                BaseTypeNamespace = "http://opcfoundation.org/UA/",
                NumericId = 6000,
                IsEnumeration = true,
                Fields =
                [
                    new SnapshotDataField("NORMAL", "Int32", "http://opcfoundation.org/UA/", -1),
                    new SnapshotDataField("FAILURE", "Int32", "http://opcfoundation.org/UA/", -1),
                    new SnapshotDataField("CHECK", "Int32", "http://opcfoundation.org/UA/", -1)
                ]
            });

            string payload = snapshot.ToBase64Payload();
            Assert.That(payload, Is.Not.Null.And.Not.Empty);

            ModelSnapshotV1 decoded = ModelSnapshotV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.ModelUri, Is.EqualTo("http://example.org/UA/Demo/"));
            Assert.That(decoded.Nodes, Has.Count.EqualTo(2));

            SnapshotNode pump = decoded.Nodes[0];
            Assert.That(pump.SymbolicName, Is.EqualTo("PumpType"));
            Assert.That(pump.Kind, Is.EqualTo(SnapshotNodeKind.ObjectType));
            Assert.That(pump.BaseTypeName, Is.EqualTo("ComponentType"));
            Assert.That(pump.BaseTypeNamespace, Is.EqualTo("http://opcfoundation.org/UA/DI/"));
            Assert.That(pump.NumericId, Is.EqualTo(5001u));
            Assert.That(pump.IsAbstract, Is.False);

            SnapshotNode dhe = decoded.Nodes[1];
            Assert.That(dhe.SymbolicName, Is.EqualTo("DeviceHealthEnumeration"));
            Assert.That(dhe.Kind, Is.EqualTo(SnapshotNodeKind.DataType));
            Assert.That(dhe.IsEnumeration, Is.True);
            Assert.That(dhe.Fields, Has.Count.EqualTo(3));
            Assert.That(dhe.Fields[0].Name, Is.EqualTo("NORMAL"));
            Assert.That(dhe.Fields[2].Name, Is.EqualTo("CHECK"));
        }

        [Test]
        public void WriteThenRead_PreservesUnicodeBrowseNames()
        {
            var snapshot = new ModelSnapshotV1 { ModelUri = "http://example.org/UA/Unicode/" };
            snapshot.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "Größentyp",
                SymbolicNamespace = "http://example.org/UA/Unicode/",
                ClassName = "Größentyp",
                Kind = SnapshotNodeKind.ObjectType
            });

            string payload = snapshot.ToBase64Payload();
            ModelSnapshotV1 decoded = ModelSnapshotV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].SymbolicName, Is.EqualTo("Größentyp"));
            Assert.That(decoded.Nodes[0].ClassName, Is.EqualTo("Größentyp"));
        }

        [Test]
        public void Read_ReturnsNullForWrongMagic()
        {
            var bogus = new byte[] { 0x00, 0x00, 0x01, 0x01 };
            string payload = Convert.ToBase64String(bogus);
            ModelSnapshotV1 result = ModelSnapshotV1.FromBase64Payload(payload);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForFutureVersion()
        {
            // Header with version=2 (unknown) — reader should refuse.
            var bogus = new byte[] { 0xAA, 0xC7, 0x02, 0x01 };
            string payload = Convert.ToBase64String(bogus);
            ModelSnapshotV1 result = ModelSnapshotV1.FromBase64Payload(payload);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForEmptyString()
        {
            Assert.That(ModelSnapshotV1.FromBase64Payload(string.Empty), Is.Null);
            Assert.That(ModelSnapshotV1.FromBase64Payload(null), Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForBadBase64()
        {
            Assert.That(ModelSnapshotV1.FromBase64Payload("not!valid!base64!@@"), Is.Null);
        }

        [Test]
        public void Write_DeterministicByteForByte()
        {
            ModelSnapshotV1 s1 = BuildSampleSnapshot();
            ModelSnapshotV1 s2 = BuildSampleSnapshot();
            Assert.That(s1.ToBase64Payload(), Is.EqualTo(s2.ToBase64Payload()));
        }

        [Test]
        public void Read_HandlesNullBaseType()
        {
            var snapshot = new ModelSnapshotV1 { ModelUri = "http://example.org/UA/Root/" };
            snapshot.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "Foo",
                SymbolicNamespace = "http://example.org/UA/Root/",
                ClassName = "Foo",
                Kind = SnapshotNodeKind.ObjectType,
                BaseTypeName = null,
                BaseTypeNamespace = null
            });
            string payload = snapshot.ToBase64Payload();
            ModelSnapshotV1 decoded = ModelSnapshotV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].BaseTypeName, Is.Null);
            Assert.That(decoded.Nodes[0].BaseTypeNamespace, Is.Null);
        }

        [Test]
        public void WriteThenRead_RoundTripsChildren()
        {
            var snapshot = new ModelSnapshotV1 { ModelUri = "http://example.org/UA/WithChildren/" };
            snapshot.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "DeviceType",
                SymbolicNamespace = "http://example.org/UA/WithChildren/",
                ClassName = "Device",
                Kind = SnapshotNodeKind.ObjectType,
                Children =
                [
                    new SnapshotChild("Manufacturer", "Manufacturer", modellingRule: 1, instanceKind: 3),
                    new SnapshotChild("SerialNumber", "SerialNumber", modellingRule: 2, instanceKind: 3),
                    new SnapshotChild("<GroupIdentifier>", "GroupIdentifier", modellingRule: 3, instanceKind: 1)
                ]
            });

            string payload = snapshot.ToBase64Payload();
            ModelSnapshotV1 decoded = ModelSnapshotV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes, Has.Count.EqualTo(1));
            Assert.That(decoded.Nodes[0].Children, Has.Count.EqualTo(3));
            Assert.That(decoded.Nodes[0].Children[0].BrowseName, Is.EqualTo("Manufacturer"));
            Assert.That(decoded.Nodes[0].Children[0].ModellingRule, Is.EqualTo((byte)1));
            Assert.That(decoded.Nodes[0].Children[0].InstanceKind, Is.EqualTo((byte)3));
            Assert.That(decoded.Nodes[0].Children[2].BrowseName, Is.EqualTo("<GroupIdentifier>"));
            Assert.That(decoded.Nodes[0].Children[2].ModellingRule, Is.EqualTo((byte)3));
        }

        private static ModelSnapshotV1 BuildSampleSnapshot()
        {
            var s = new ModelSnapshotV1 { ModelUri = "http://example.org/UA/Demo/" };
            s.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "TypeA",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "TypeA",
                Kind = SnapshotNodeKind.ObjectType,
                NumericId = 100
            });
            s.Nodes.Add(new SnapshotNode
            {
                SymbolicName = "TypeB",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "TypeB",
                Kind = SnapshotNodeKind.DataType,
                NumericId = 101,
                IsEnumeration = true
            });
            return s;
        }
    }
}
