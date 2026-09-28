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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;
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
    public class ModelDependencyV1Tests
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

            var decoded = ModelDependencyV1.FromBase64Payload(payload);
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
        public void WriteThenRead_RoundTripsVariableTypeDataType()
        {
            // A consumer that types a variable with a VariableType supplied by
            // a referenced assembly needs that type's data type restriction to
            // decide whether the generated state class takes a template
            // parameter. Before the restriction was carried here, the consumer
            // resolved a null DataTypeNode and the node state generator threw
            // a bare NullReferenceException (OPC 40001-1 Machinery over the
            // OPC 10000-100 DI LifetimeVariableType).
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Demo/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "LifetimeVariableType",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "LifetimeVariable",
                Kind = DependencyNodeKind.VariableType,
                BaseTypeName = "BaseDataVariableType",
                BaseTypeNamespace = "http://opcfoundation.org/UA/",
                NumericId = 468,
                DataTypeName = "Number",
                DataTypeNamespace = "http://opcfoundation.org/UA/",
                ValueRank = (int)ValueRank.Scalar
            });

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            DependencyNode variableType = decoded.Nodes[0];
            Assert.That(variableType.Kind, Is.EqualTo(DependencyNodeKind.VariableType));
            Assert.That(variableType.DataTypeName, Is.EqualTo("Number"));
            Assert.That(
                variableType.DataTypeNamespace,
                Is.EqualTo("http://opcfoundation.org/UA/"));
            Assert.That(variableType.ValueRank, Is.EqualTo((int)ValueRank.Scalar));
        }

        [Test]
        public void WriteThenRead_OmittedVariableTypeDataTypeStaysNull()
        {
            // Payloads written before the VariableType data type entry existed
            // never set the flag, so a current reader must leave the fields
            // null rather than consuming bytes that are not there.
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Demo/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "PlainType",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "Plain",
                Kind = DependencyNodeKind.ObjectType,
                NumericId = 1
            });

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].DataTypeName, Is.Null);
            Assert.That(decoded.Nodes[0].DataTypeNamespace, Is.Null);
            Assert.That(decoded.Nodes[0].ValueRank, Is.Null);
        }

        [Test]
        public void WriteThenRead_RoundTripsVariableTypeWithoutValueRank()
        {
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Demo/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "LooseVariableType",
                SymbolicNamespace = "http://example.org/UA/Demo/",
                ClassName = "LooseVariable",
                Kind = DependencyNodeKind.VariableType,
                NumericId = 2,
                DataTypeName = "BaseDataType",
                DataTypeNamespace = "http://opcfoundation.org/UA/",
                ValueRank = null
            });

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].DataTypeName, Is.EqualTo("BaseDataType"));
            Assert.That(decoded.Nodes[0].ValueRank, Is.Null);
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
            var decoded = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].SymbolicName, Is.EqualTo("Größentyp"));
            Assert.That(decoded.Nodes[0].ClassName, Is.EqualTo("Größentyp"));
        }

        /// <summary>
        /// Guid and opaque identifiers travel in an optional trailer so that
        /// payloads without them stay byte identical.
        /// </summary>
        [TestCase(null)]
        [TestCase(true)]
        [TestCase(false)]
        public void WriteThenReadRoundTripsExtendedIdentifiers(bool? emitted)
        {
            var dependency = new ModelDependencyV1
            {
                ModelUri = "http://example.org/UA/ExtendedIds/",
                FluentAccessorsEmitted = emitted
            };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "GuidType",
                SymbolicNamespace = "http://example.org/UA/ExtendedIds/",
                ClassName = "GuidType",
                Kind = DependencyNodeKind.ObjectType,
                GuidId = "09087e75-8e5e-499b-954f-f2a9603db28a"
            });
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "OpaqueType",
                SymbolicNamespace = "http://example.org/UA/ExtendedIds/",
                ClassName = "OpaqueType",
                Kind = DependencyNodeKind.ObjectType,
                OpaqueId = "M/RbKBsRVkePCePcx24oRA=="
            });
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "EmptyOpaqueType",
                SymbolicNamespace = "http://example.org/UA/ExtendedIds/",
                ClassName = "EmptyOpaqueType",
                Kind = DependencyNodeKind.ObjectType,
                OpaqueId = string.Empty
            });

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.FluentAccessorsEmitted, Is.EqualTo(emitted));
                Assert.That(
                    decoded.Nodes[0].GuidId,
                    Is.EqualTo("09087e75-8e5e-499b-954f-f2a9603db28a"));
                Assert.That(decoded.Nodes[0].OpaqueId, Is.Null);
                Assert.That(decoded.Nodes[1].GuidId, Is.Null);
                Assert.That(decoded.Nodes[1].OpaqueId, Is.EqualTo("M/RbKBsRVkePCePcx24oRA=="));
                Assert.That(decoded.Nodes[2].GuidId, Is.Null);
                Assert.That(decoded.Nodes[2].OpaqueId, Is.Empty);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WriteThenRead_RoundTripsFluentAccessorCapability(bool emitted)
        {
            var dependency = new ModelDependencyV1
            {
                ModelUri = "http://example.org/UA/Capabilities/",
                FluentAccessorsEmitted = emitted
            };

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.FluentAccessorsEmitted, Is.EqualTo(emitted));
        }

        [Test]
        public void WriteThenRead_OmittedCapabilityRemainsUnknown()
        {
            var dependency = new ModelDependencyV1
            {
                ModelUri = "http://example.org/UA/Legacy/"
            };

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.FluentAccessorsEmitted, Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForWrongMagic()
        {
            byte[] bogus = [0x00, 0x00, 0x01, 0x01];
            string payload = Convert.ToBase64String(bogus);
            var result = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Read_ReturnsNullForFutureVersion()
        {
            byte[] bogus = [0xAA, 0xC7, 0x02, 0x01];
            string payload = Convert.ToBase64String(bogus);
            var result = ModelDependencyV1.FromBase64Payload(payload);
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
            var decoded = ModelDependencyV1.FromBase64Payload(payload);
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
            var decoded = ModelDependencyV1.FromBase64Payload(payload);
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
        public void WriteThenRead_RoundTripsVariableMetadata()
        {
            const string defaultValue = """
                <uax:ListOfString
                    xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                    <uax:String>First</uax:String>
                    <uax:String>Second</uax:String>
                </uax:ListOfString>
                """;
            var dependency = new ModelDependencyV1
            {
                ModelUri = "http://example.org/UA/VariableMetadata/"
            };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "DeviceType",
                SymbolicNamespace = dependency.ModelUri,
                ClassName = "Device",
                Kind = DependencyNodeKind.ObjectType,
                Children =
                [
                    new DependencyChild
                    {
                        BrowseName = "Values",
                        SymbolicName = "Values",
                        InstanceKind = 2,
                        AccessLevel = (byte)AccessLevel.ReadWrite,
                        AccessLevelSpecified = true,
                        RawAccessLevel = 5,
                        RawUserAccessLevel = 1,
                        MinimumSamplingInterval = 0,
                        MinimumSamplingIntervalSpecified = true,
                        Historizing = false,
                        HistorizingSpecified = true,
                        DefaultValueXml = defaultValue
                    }
                ]
            });

            ModelDependencyV1 decoded =
                ModelDependencyV1.FromBase64Payload(
                    dependency.ToBase64Payload());

            Assert.That(decoded, Is.Not.Null);
            DependencyChild child = decoded.Nodes.Single().Children.Single();
            Assert.Multiple(() =>
            {
                Assert.That(child.AccessLevel, Is.EqualTo((byte)AccessLevel.ReadWrite));
                Assert.That(child.AccessLevelSpecified, Is.True);
                Assert.That(child.RawAccessLevel, Is.EqualTo(5u));
                Assert.That(child.RawUserAccessLevel, Is.EqualTo(1u));
                Assert.That(child.MinimumSamplingInterval, Is.Zero);
                Assert.That(child.MinimumSamplingIntervalSpecified, Is.True);
                Assert.That(child.Historizing, Is.False);
                Assert.That(child.HistorizingSpecified, Is.True);
                Assert.That(
                    child.DefaultValueXml?.ReplaceLineEndings(),
                    Is.EqualTo(defaultValue.ReplaceLineEndings()));
            });
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
            var decoded = ModelDependencyV1.FromBase64Payload(payload);
            Assert.That(decoded, Is.Not.Null);
            DependencyChild method = decoded.Nodes[0].Children[0];
            Assert.That(method.InstanceKind, Is.EqualTo((byte)4));
            Assert.That(method.InputArguments, Has.Count.EqualTo(1));
            Assert.That(method.InputArguments[0].Name, Is.EqualTo("Context"));
            Assert.That(method.InputArguments[0].DataTypeName, Is.EqualTo("String"));
            Assert.That(method.OutputArguments, Has.Count.EqualTo(1));
            Assert.That(method.OutputArguments[0].Name, Is.EqualTo("InitLockStatus"));
        }

        /// <summary>
        /// Regression: trailers carry no length, and a method identity trailer of
        /// an unknown (newer) version was left unread - the extended identifier
        /// trailer was then parsed out of its middle, which assigned garbage ids
        /// or threw and lost the whole payload. The reader now stops at the
        /// unknown trailer and keeps everything before it.
        /// </summary>
        [Test]
        public void ReadStopsAtAnUnknownTrailerVersion()
        {
            ModelDependencyV1 dependency = BuildTrailerSnapshot(
                methodStateName: "DoItMethodType",
                guidId: "09087e75-8e5e-499b-954f-f2a9603db28a");
            ModelDependencyV1 bodyOnly = BuildTrailerSnapshot(methodStateName: null, guidId: null);

            byte[] body = Inflate(bodyOnly.ToBase64Payload());
            byte[] full = Inflate(dependency.ToBase64Payload());
            // body: the payload without trailers or capability byte; full[body.Length]
            // is the capability byte and the method identity trailer version follows.
            Assert.That(full[body.Length + 1], Is.EqualTo((byte)1));
            full[body.Length + 1] = 2;

            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(Deflate(full));

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes, Has.Count.EqualTo(1));
            Assert.That(decoded.Nodes[0].SymbolicName, Is.EqualTo("ServiceType"));
            Assert.That(decoded.Nodes[0].Children[0].MethodStateName, Is.Empty);
            Assert.That(decoded.Nodes[0].GuidId, Is.Null, "the trailer after the unknown one is not guessed at");
        }

        /// <summary>
        /// Payloads whose trailers are all known still read completely.
        /// </summary>
        [Test]
        public void ReadKnownTrailersStillRoundTrip()
        {
            ModelDependencyV1 dependency = BuildTrailerSnapshot(
                methodStateName: "DoItMethodType",
                guidId: "09087e75-8e5e-499b-954f-f2a9603db28a");

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded.Nodes[0].Children[0].MethodStateName, Is.EqualTo("DoItMethodType"));
            Assert.That(decoded.Nodes[0].GuidId, Is.EqualTo("09087e75-8e5e-499b-954f-f2a9603db28a"));
        }

        /// <summary>
        /// Regression: an empty (not null) GuidId and a method identity that is
        /// only a namespace were not written, so they read back as null / empty.
        /// </summary>
        [Test]
        public void WriteThenReadRoundTripsEmptyGuidAndNamespaceOnlyMethodIdentity()
        {
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Edge/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "EdgeType",
                SymbolicNamespace = "http://example.org/UA/Edge/",
                ClassName = "EdgeType",
                Kind = DependencyNodeKind.ObjectType,
                GuidId = string.Empty,
                Children =
                [
                    new DependencyChild
                    {
                        BrowseName = "DoIt",
                        SymbolicName = "DoIt",
                        MethodStateNamespace = "http://example.org/UA/Edge/"
                    }
                ]
            });

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded.Nodes[0].GuidId, Is.Empty);
            Assert.That(
                decoded.Nodes[0].Children[0].MethodStateNamespace,
                Is.EqualTo("http://example.org/UA/Edge/"));
        }

        /// <summary>
        /// A2-7: IsOptional, AllowSubTypes and ArrayDimensions of structure
        /// fields round trip through the structure field trailer.
        /// </summary>
        [Test]
        public void WriteThenReadRoundTripsStructureFieldFlags()
        {
            ModelDependencyV1 dependency = BuildStructureSnapshot(withFlags: true, fluent: null);

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            Assert.That(decoded.FluentAccessorsEmitted, Is.Null);
            Assert.That(decoded.Nodes[0].GuidId, Is.Null);
            IReadOnlyList<DependencyDataField> fields = decoded.Nodes[0].Fields;
            Assert.That(fields, Is.EqualTo(dependency.Nodes[0].Fields));
            Assert.That(fields[0].IsOptional, Is.False);
            Assert.That(fields[1].IsOptional, Is.True);
            Assert.That(fields[2].AllowSubTypes, Is.True);
            Assert.That(fields[3].ArrayDimensions, Is.EqualTo("0,0,0"));
        }

        /// <summary>
        /// The structure field trailer only appends to the V1 format: a payload
        /// without flagged fields is byte identical to an old one, and a
        /// payload with them starts with the old payload, so a new reader reads
        /// an old payload (no flags) and an old reader, which ignores the
        /// unknown capability bit and stops after the trailers it knows, reads
        /// a new one - including the "fluent accessors unknown" state.
        /// </summary>
        [Test]
        public void StructureFieldTrailerIsCompatibleWithOldPayloadsAndReaders()
        {
            ModelDependencyV1 withFlags = BuildStructureSnapshot(withFlags: true, fluent: null);
            ModelDependencyV1 withoutFlags = BuildStructureSnapshot(withFlags: false, fluent: null);

            byte[] old = Inflate(withoutFlags.ToBase64Payload());
            byte[] full = Inflate(withFlags.ToBase64Payload());
            Assert.That(full.Take(old.Length), Is.EqualTo(old), "the old payload is a prefix");

            // New reader, old payload: the fields read without flags.
            ModelDependencyV1 fromOld = ModelDependencyV1.FromBase64Payload(Deflate(old));
            Assert.That(fromOld.Nodes[0].Fields, Is.EqualTo(withoutFlags.Nodes[0].Fields));
            Assert.That(fromOld.Nodes[0].Fields.Any(f => f.IsOptional || f.AllowSubTypes), Is.False);

            // Old reader, new payload: an old reader only honours the
            // FluentAccessorsKnown bit when a trailer bit it knows is set.
            byte capabilities = full[old.Length];
            Assert.That(capabilities & 0x20, Is.Not.Zero, "extended identifier trailer bit is set");
            Assert.That(capabilities & 0x40, Is.Zero, "fluent accessors stay unknown");
            byte[] asOldReaderSeesIt = (byte[])full.Clone();
            asOldReaderSeesIt[old.Length] = (byte)(capabilities & ~0x10);
            ModelDependencyV1 oldView = ModelDependencyV1.FromBase64Payload(Deflate(asOldReaderSeesIt));
            Assert.That(oldView, Is.Not.Null);
            Assert.That(oldView.FluentAccessorsEmitted, Is.Null);
            Assert.That(oldView.Nodes[0].GuidId, Is.Null);
            Assert.That(oldView.Nodes[0].Fields, Is.EqualTo(withoutFlags.Nodes[0].Fields));

            // Known fluent capability plus the trailer still round trips.
            ModelDependencyV1 fluent = ModelDependencyV1.FromBase64Payload(
                BuildStructureSnapshot(withFlags: true, fluent: true).ToBase64Payload());
            Assert.That(fluent.FluentAccessorsEmitted, Is.True);
            Assert.That(fluent.Nodes[0].Fields[1].IsOptional, Is.True);
        }

        /// <summary>
        /// The structure field trailer is length prefixed, so a later version
        /// of it is skipped instead of failing the payload.
        /// </summary>
        [Test]
        public void ReadSkipsAnUnknownStructureFieldTrailerVersion()
        {
            ModelDependencyV1 withFlags = BuildStructureSnapshot(withFlags: true, fluent: null);
            byte[] full = Inflate(withFlags.ToBase64Payload());
            // capabilities, extended identifier trailer (version, count, and
            // two null strings per node), then the structure field trailer.
            int structureTrailer = Inflate(BuildStructureSnapshot(withFlags: false, fluent: null)
                .ToBase64Payload()).Length + 1 + 1 + 4 + 2;
            Assert.That(full[structureTrailer], Is.EqualTo((byte)1));
            full[structureTrailer] = 2;

            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(Deflate(full));

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Nodes[0].Fields.Any(f => f.IsOptional), Is.False);
        }

        /// <summary>
        /// A2-5: the namespace table of each default value round trips
        /// through the value namespace trailer; equal tables are shared and
        /// a child without a table reads back without one.
        /// </summary>
        [Test]
        public void WriteThenReadRoundTripsDefaultValueNamespaceTables()
        {
            ModelDependencyV1 dependency = BuildValueSnapshot(withTables: true, withFlags: true);

            var decoded = ModelDependencyV1.FromBase64Payload(dependency.ToBase64Payload());

            IReadOnlyList<DependencyChild> children = decoded.Nodes[1].Children;
            Assert.That(children[0].DefaultValueNamespaceUris, Is.EqualTo(s_producerTable));
            Assert.That(children[1].DefaultValueNamespaceUris, Is.EqualTo(s_nodeSetTable));
            Assert.That(children[2].DefaultValueNamespaceUris, Is.Null);
            Assert.That(children[3].DefaultValueNamespaceUris, Is.EqualTo(s_producerTable));
            Assert.That(decoded.Nodes[0].Children, Is.Empty);
            // The trailers before it still read.
            Assert.That(decoded.Nodes[0].Fields[0].IsOptional, Is.True);
            Assert.That(decoded.FluentAccessorsEmitted, Is.Null);
        }

        /// <summary>
        /// The value namespace trailer only appends to the V1 format: a
        /// payload without tables is byte identical to an old one, a new
        /// reader reads an old payload without tables (the consumer then
        /// resolves the values as before), and an old reader, which ignores
        /// the unknown capability bit and stops after the trailers it knows,
        /// reads a new payload - with and without the structure field
        /// trailer in front of it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ValueNamespaceTrailerIsCompatibleWithOldPayloadsAndReaders(bool withFlags)
        {
            ModelDependencyV1 withTables = BuildValueSnapshot(withTables: true, withFlags);
            ModelDependencyV1 withoutTables = BuildValueSnapshot(withTables: false, withFlags);

            byte[] old = Inflate(withoutTables.ToBase64Payload());
            byte[] full = Inflate(withTables.ToBase64Payload());
            // The capability byte follows the body (the payload without any
            // trailer); apart from the new capability bit the old payload is
            // a prefix of the new one.
            int capabilityOffset = Inflate(BuildValueSnapshot(withTables: false, withFlags: false)
                .ToBase64Payload()).Length;
            byte capabilities = full[capabilityOffset];
            byte[] withoutNewBit = (byte[])full.Clone();
            withoutNewBit[capabilityOffset] = (byte)(capabilities & ~0x08);
            if (old.Length == capabilityOffset)
            {
                // No trailer before: the old payload has no capability byte.
                Assert.That(full.Take(old.Length), Is.EqualTo(old), "the old payload is a prefix");
            }
            else
            {
                Assert.That(withoutNewBit.Take(old.Length), Is.EqualTo(old), "the old payload is a prefix");
            }

            // New reader, old payload.
            ModelDependencyV1 fromOld = ModelDependencyV1.FromBase64Payload(Deflate(old));
            Assert.That(
                fromOld.Nodes.SelectMany(n => n.Children).Select(c => c.DefaultValueNamespaceUris),
                Has.All.Null);
            Assert.That(fromOld.Nodes[1].Children[0].DefaultValueXml, Is.Not.Null);

            // Old reader, new payload.
            Assert.That(capabilities & 0x08, Is.Not.Zero, "value namespace trailer bit is set");
            Assert.That(capabilities & 0x20, Is.Not.Zero, "extended identifier trailer bit is set");
            Assert.That(capabilities & 0x40, Is.Zero, "fluent accessors stay unknown");
            byte[] asOldReaderSeesIt = (byte[])full.Clone();
            asOldReaderSeesIt[capabilityOffset] = (byte)(capabilities & ~0x08);
            ModelDependencyV1 oldView = ModelDependencyV1.FromBase64Payload(Deflate(asOldReaderSeesIt));
            Assert.That(oldView, Is.Not.Null);
            Assert.That(oldView.FluentAccessorsEmitted, Is.Null);
            Assert.That(oldView.Nodes[1].Children, Has.Count.EqualTo(4));
            Assert.That(
                oldView.Nodes.SelectMany(n => n.Children).Select(c => c.DefaultValueNamespaceUris),
                Has.All.Null);
            Assert.That(oldView.Nodes[0].Fields[0].IsOptional, Is.EqualTo(withFlags));
            // A reader that predates the structure field trailer as well.
            asOldReaderSeesIt[capabilityOffset] = (byte)(capabilities & ~0x18);
            oldView = ModelDependencyV1.FromBase64Payload(Deflate(asOldReaderSeesIt));
            Assert.That(oldView, Is.Not.Null);
            Assert.That(oldView.Nodes[0].Fields[0].IsOptional, Is.False);
        }

        /// <summary>
        /// The value namespace trailer is length prefixed, so a later version
        /// of it is skipped instead of failing the payload.
        /// </summary>
        [Test]
        public void ReadSkipsAnUnknownValueNamespaceTrailerVersion()
        {
            ModelDependencyV1 withTables = BuildValueSnapshot(withTables: true, withFlags: false);
            byte[] full = Inflate(withTables.ToBase64Payload());
            // capabilities, extended identifier trailer (version, count, and
            // two null strings per node), then the value namespace trailer.
            int trailer = Inflate(BuildValueSnapshot(withTables: false, withFlags: false)
                .ToBase64Payload()).Length + 1 + 1 + 4 + (2 * 2);
            Assert.That(full[trailer], Is.EqualTo((byte)1));
            full[trailer] = 2;

            ModelDependencyV1 decoded = ModelDependencyV1.FromBase64Payload(Deflate(full));

            Assert.That(decoded, Is.Not.Null);
            Assert.That(
                decoded.Nodes.SelectMany(n => n.Children).Select(c => c.DefaultValueNamespaceUris),
                Has.All.Null);
        }

        private static readonly string[] s_producerTable =
            ["http://opcfoundation.org/UA/", "http://example.org/UA/Values/"];

        private static readonly string[] s_nodeSetTable =
            ["http://opcfoundation.org/UA/", "http://example.org/UA/Other/", "http://example.org/UA/Values/"];

        private static ModelDependencyV1 BuildValueSnapshot(bool withTables, bool withFlags)
        {
            const string ns = "http://example.org/UA/Values/";
            const string qn =
                "<uax:QualifiedName xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                "<uax:NamespaceIndex>1</uax:NamespaceIndex><uax:Name>X</uax:Name></uax:QualifiedName>";
            var dependency = new ModelDependencyV1 { ModelUri = ns };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Measurement",
                SymbolicNamespace = ns,
                ClassName = "Measurement",
                Kind = DependencyNodeKind.DataType,
                Fields =
                [
                    new DependencyDataField("Note", "String", "http://opcfoundation.org/UA/", -1)
                    {
                        IsOptional = withFlags
                    }
                ]
            });
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "DeviceType",
                SymbolicNamespace = ns,
                ClassName = "Device",
                Kind = DependencyNodeKind.ObjectType,
                Children =
                [
                    new DependencyChild
                    {
                        BrowseName = "Label",
                        SymbolicName = "Label",
                        InstanceKind = 3,
                        DefaultValueXml = qn,
                        DefaultValueNamespaceUris = withTables ? s_producerTable : null
                    },
                    new DependencyChild
                    {
                        BrowseName = "Imported",
                        SymbolicName = "Imported",
                        InstanceKind = 3,
                        DefaultValueXml = qn,
                        DefaultValueNamespaceUris = withTables ? s_nodeSetTable : null
                    },
                    new DependencyChild
                    {
                        BrowseName = "Plain",
                        SymbolicName = "Plain",
                        InstanceKind = 1
                    },
                    new DependencyChild
                    {
                        BrowseName = "Other",
                        SymbolicName = "Other",
                        InstanceKind = 3,
                        DefaultValueXml = qn,
                        DefaultValueNamespaceUris = withTables ? [.. s_producerTable] : null
                    }
                ]
            });
            return dependency;
        }

        private static ModelDependencyV1 BuildStructureSnapshot(bool withFlags, bool? fluent)
        {
            const string ns = "http://example.org/UA/Structures/";
            var dependency = new ModelDependencyV1
            {
                ModelUri = ns,
                FluentAccessorsEmitted = fluent
            };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "Measurement",
                SymbolicNamespace = ns,
                ClassName = "Measurement",
                Kind = DependencyNodeKind.DataType,
                BaseTypeName = "Structure",
                BaseTypeNamespace = "http://opcfoundation.org/UA/",
                NumericId = 1,
                Fields =
                [
                    new DependencyDataField("Id", "Int32", "http://opcfoundation.org/UA/", -1),
                    new DependencyDataField("Note", "String", "http://opcfoundation.org/UA/", -1)
                    {
                        IsOptional = withFlags
                    },
                    new DependencyDataField("Payload", "Structure", "http://opcfoundation.org/UA/", -1)
                    {
                        AllowSubTypes = withFlags
                    },
                    new DependencyDataField("Cube", "Double", "http://opcfoundation.org/UA/", 0)
                    {
                        ArrayDimensions = withFlags ? "0,0,0" : null
                    }
                ]
            });
            return dependency;
        }

        private static ModelDependencyV1 BuildTrailerSnapshot(string methodStateName, string guidId)
        {
            var dependency = new ModelDependencyV1 { ModelUri = "http://example.org/UA/Trailers/" };
            dependency.Nodes.Add(new DependencyNode
            {
                SymbolicName = "ServiceType",
                SymbolicNamespace = "http://example.org/UA/Trailers/",
                ClassName = "Service",
                Kind = DependencyNodeKind.ObjectType,
                GuidId = guidId,
                Children =
                [
                    new DependencyChild
                    {
                        BrowseName = "DoIt",
                        SymbolicName = "DoIt",
                        MethodStateName = methodStateName ?? string.Empty,
                        MethodStateNamespace = methodStateName == null
                            ? string.Empty
                            : "http://example.org/UA/Trailers/"
                    }
                ]
            });
            return dependency;
        }

        private static byte[] Inflate(string payload)
        {
            byte[] bytes = Convert.FromBase64String(payload);
            using var input = new MemoryStream(bytes, 4, bytes.Length - 4);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        private static string Deflate(byte[] raw)
        {
            using var output = new MemoryStream();
            output.Write(ModelDependencyV1.Magic, 0, ModelDependencyV1.Magic.Length);
            output.WriteByte(ModelDependencyV1.Version);
            output.WriteByte(ModelDependencyV1.CompressionDeflate);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }
            return Convert.ToBase64String(output.ToArray());
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
