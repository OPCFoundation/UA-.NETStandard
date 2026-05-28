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
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Round-trip and validation tests for the <see cref="ModelDependencyV1"/>
    /// wire format consumed by the cross-assembly model dependency machinery.
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
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Demo/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "PumpType",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "Pump",
                Kind = DependencyNodeKind.ObjectType,
                BaseTypeName = "ComponentType",
                BaseTypeNamespace = "http://opcfoundation.org/UA/DI/",
                NumericId = 5001,
                IsAbstract = false
            });
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "DeviceHealthEnumeration",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "DeviceHealthEnumeration",
                Kind = DependencyNodeKind.DataType,
                BaseTypeName = "Enumeration",
                BaseTypeNamespace = "http://opcfoundation.org/UA/",
                NumericId = 6000,
                IsEnumeration = true,
                Fields =
                [
                    new DependencyDataField("NORMAL", "Int32", "http://opcfoundation.org/UA/", -1),
                    new DependencyDataField("FAILURE", "Int32", "http://opcfoundation.org/UA/", -1),
                    new DependencyDataField("CHECK", "Int32", "http://opcfoundation.org/UA/", -1)
                ]
            });

            string payload = dependency.ToBase64Payload();
            Assert.That(payload, Is.Not.Null.And.Not.Empty);

            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.ModelUri, Is.EqualTo("http://example.org/UA/Demo/"));
            Assert.That(decoded.Nodes, Has.Count.EqualTo(2));

            DependencyNode pump = decoded.Nodes[0];
            Assert.That(pump.SymbolicName, Is.EqualTo("PumpType"));
            Assert.That(pump.Kind, Is.EqualTo(DependencyNodeKind.ObjectType));
            Assert.That(pump.BaseTypeName, Is.EqualTo("ComponentType"));
            Assert.That(pump.BaseTypeNamespace, Is.EqualTo("http://opcfoundation.org/UA/DI/"));
            Assert.That(pump.NumericId, Is.EqualTo(5001u));
            Assert.That(pump.IsAbstract, Is.False);

            DependencyNode dhe = decoded.Nodes[1];
            Assert.That(dhe.SymbolicName, Is.EqualTo("DeviceHealthEnumeration"));
            Assert.That(dhe.Kind, Is.EqualTo(DependencyNodeKind.DataType));
            Assert.That(dhe.IsEnumeration, Is.True);
            Assert.That(dhe.Fields, Has.Count.EqualTo(3));
            Assert.That(dhe.Fields[0].Name, Is.EqualTo("NORMAL"));
            Assert.That(dhe.Fields[2].Name, Is.EqualTo("CHECK"));
        }

        [Test]
        public void WriteThenRead_PreservesUnicodeBrowseNames()
        {
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Unicode/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Größentyp",
                SymbolicNamespace = "http://example.org/UA/Unicode/",
                ClassName = "Größentyp",
                Kind = DependencyNodeKind.ObjectType
            });

            string payload = dependency.ToBase64Payload();
            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].SymbolicName, Is.EqualTo("Größentyp"));
            Assert.That(decoded.Nodes[0].ClassName, Is.EqualTo("Größentyp"));
        }

        [Test]
        public void Read_ReturnsNullForWrongMagic()
        {
            var bogus = new byte[] { 0x00, 0x00, 0x01, 0x01 };
            string payload = Convert.ToBase64String(bogus);
            ModelDependencyV1 result = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForFutureVersion()
        {
            // Header with version=2 (unknown) — reader should refuse.
            var bogus = new byte[] { 0xAA, 0xC7, 0x02, 0x01 };
            string payload = Convert.ToBase64String(bogus);
            ModelDependencyV1 result = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForEmptyString()
        {
            Assert.That(ModelDependencyV1.FromBase64Payload(string.Empty), Is.Null);
            Assert.That(ModelDependencyV1.FromBase64Payload(null), Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForBadBase64()
        {
            Assert.That(ModelDependencyV1.FromBase64Payload("not!valid!base64!@@"), Is.Null);
        }

        [Test]
        public void Write_DeterministicByteForByte()
        {
            ModelDependencyV1 s1 = BuildSampleSnapshot();
            ModelDependencyV1 s2 = BuildSampleSnapshot();
            Assert.That(s1.ToBase64Payload(), Is.EqualTo(s2.ToBase64Payload()));
        }

        [Test]
        public void Read_HandlesNullBaseType()
        {
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Root/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Foo",
                SymbolicNamespace = "http://example.org/UA/Root/",
                ClassName = "Foo",
                Kind = DependencyNodeKind.ObjectType,
                BaseTypeName = null,
                BaseTypeNamespace = null
            });
            string payload = dependency.ToBase64Payload();
            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].BaseTypeName, Is.Null);
            Assert.That(decoded.Nodes[0].BaseTypeNamespace, Is.Null);
        }

        [Test]
        public void WriteThenRead_RoundTripsChildren()
        {
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/WithChildren/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "DeviceType",
                SymbolicNamespace = "http://example.org/UA/WithChildren/",
                ClassName = "Device",
                Kind = DependencyNodeKind.ObjectType,
                Children =
                [
                    new DependencyChild
                    {
                        BrowseName = "Manufacturer",
                        SymbolicName = "Manufacturer",
                        TypeDefinitionName = "PropertyType",
                        TypeDefinitionNamespace = "http://opcfoundation.org/UA/",
                        DataTypeName = "LocalizedText",
                        DataTypeNamespace = "http://opcfoundation.org/UA/",
                        ValueRank = -1,
                        ModellingRule = 1,
                        InstanceKind = 3
                    },
                    new DependencyChild
                    {
                        BrowseName = "SerialNumber",
                        SymbolicName = "SerialNumber",
                        TypeDefinitionName = "PropertyType",
                        TypeDefinitionNamespace = "http://opcfoundation.org/UA/",
                        DataTypeName = "String",
                        DataTypeNamespace = "http://opcfoundation.org/UA/",
                        ValueRank = -1,
                        ModellingRule = 2,
                        InstanceKind = 3
                    },
                    new DependencyChild
                    {
                        BrowseName = "<GroupIdentifier>",
                        SymbolicName = "GroupIdentifier",
                        TypeDefinitionName = "FunctionalGroupType",
                        TypeDefinitionNamespace = "http://opcfoundation.org/UA/DI/",
                        ModellingRule = 3,
                        InstanceKind = 1
                    }
                ]
            });

            string payload = dependency.ToBase64Payload();
            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes, Has.Count.EqualTo(1));
            Assert.That(decoded.Nodes[0].Children, Has.Count.EqualTo(3));

            DependencyChild mfg = decoded.Nodes[0].Children[0];
            Assert.That(mfg.BrowseName, Is.EqualTo("Manufacturer"));
            Assert.That(mfg.TypeDefinitionName, Is.EqualTo("PropertyType"));
            Assert.That(mfg.TypeDefinitionNamespace, Is.EqualTo("http://opcfoundation.org/UA/"));
            Assert.That(mfg.DataTypeName, Is.EqualTo("LocalizedText"));
            Assert.That(mfg.ValueRank, Is.EqualTo(-1));
            Assert.That(mfg.ModellingRule, Is.EqualTo((byte)1));
            Assert.That(mfg.InstanceKind, Is.EqualTo((byte)3));

            DependencyChild grp = decoded.Nodes[0].Children[2];
            Assert.That(grp.BrowseName, Is.EqualTo("<GroupIdentifier>"));
            Assert.That(grp.ModellingRule, Is.EqualTo((byte)3));
            Assert.That(grp.TypeDefinitionNamespace, Is.EqualTo("http://opcfoundation.org/UA/DI/"));
        }

        [Test]
        public void WriteThenRead_RoundTripsMethodArgs()
        {
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/WithMethod/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "ServiceType",
                SymbolicNamespace = "http://example.org/UA/WithMethod/",
                ClassName = "Service",
                Kind = DependencyNodeKind.ObjectType,
                Children =
                [
                    new DependencyChild
                    {
                        BrowseName = "InitLock",
                        SymbolicName = "InitLock",
                        TypeDefinitionName = "InitLockMethodType",
                        TypeDefinitionNamespace = "http://opcfoundation.org/UA/DI/",
                        ModellingRule = 1,
                        InstanceKind = 4,
                        InputArguments =
                        [
                            new DependencyMethodArg("Context", "String", "http://opcfoundation.org/UA/", -1)
                        ],
                        OutputArguments =
                        [
                            new DependencyMethodArg("InitLockStatus",
                                "Int32", "http://opcfoundation.org/UA/", -1)
                        ]
                    }
                ]
            });

            string payload = dependency.ToBase64Payload();
            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            DependencyChild method = decoded.Nodes[0].Children[0];
            Assert.That(method.InstanceKind, Is.EqualTo((byte)4));
            Assert.That(method.InputArguments, Has.Count.EqualTo(1));
            Assert.That(method.InputArguments[0].Name, Is.EqualTo("Context"));
            Assert.That(method.InputArguments[0].DataTypeName, Is.EqualTo("String"));
            Assert.That(method.OutputArguments, Has.Count.EqualTo(1));
            Assert.That(method.OutputArguments[0].Name, Is.EqualTo("InitLockStatus"));
        }

        private static ModelDependencyV1 BuildSampleSnapshot()
        {
            var s = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Demo/" };
            s.Nodes.Add(new DependencyNode
            {
                SymbolicName = "TypeA",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "TypeA",
                Kind = DependencyNodeKind.ObjectType,
                NumericId = 100
            });
            s.Nodes.Add(new DependencyNode
            {
                SymbolicName = "TypeB",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "TypeB",
                Kind = DependencyNodeKind.DataType,
                NumericId = 101,
                IsEnumeration = true
            });
            return s;
        }
    }
}
