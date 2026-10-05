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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Runs the same encode/decode cases through the default complex type
    /// builder and the Reflection.Emit builder, which must produce the same
    /// wire format (OPC 10000-6 5.1.7 subtyped values, OPC 10000-3 8.40
    /// Structure-backed OptionSet subtypes).
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexTypeBuilderParityTests
    {
        /// <summary>
        /// The complex type builder under test.
        /// </summary>
        public enum BuilderKind
        {
            /// <summary>
            /// The default complex type builder.
            /// </summary>
            Default,

            /// <summary>
            /// The Reflection.Emit complex type builder.
            /// </summary>
            Emit
        }

        /// <summary>
        /// OPC 10000-6 5.1.7: in a StructureWithSubtypedValues a field with
        /// IsOptional=FALSE is encoded directly as its declared type and only
        /// a field with IsOptional=TRUE is serialized as an ExtensionObject,
        /// which may carry a subtype of the declared DataType.
        /// </summary>
        [Test]
        public async Task StructureWithSubtypedValuesBinaryEncodingFollowsIsOptionalAsync(
            [Values] BuilderKind builder)
        {
            TypeModel model = await LoadModelAsync(builder).ConfigureAwait(false);
            IStructure value = CreateSubtypedStructure(model);

            byte[] actual = EncodeBinary(model, (IEncodeable)value);

            byte[] expected;
            using (var encoder = new BinaryEncoder(model.Context))
            {
                encoder.WriteInt32(null, 7); // Fixed: Inner encoded directly
                encoder.WriteExtensionObject(
                    null,
                    new ExtensionObject(CreateInnerSub(model, 8, "x"))); // Any: subtype
                encoder.WriteInt32(null, 9); // Number
                expected = encoder.CloseAndReturnBuffer();
            }
            Assert.That(actual, Is.EqualTo(expected));

            var decoded = (IStructure)DecodeBinary(model, model.SubtypedId, actual);
            AssertSubtypedStructure(decoded);
        }

        /// <summary>
        /// OPC 10000-6 5.1.7 applies to UnionWithSubtypedValues as well: the
        /// selected field with IsOptional=FALSE is encoded directly, the one
        /// with IsOptional=TRUE as an ExtensionObject.
        /// </summary>
        [Test]
        public async Task UnionWithSubtypedValuesBinaryEncodingFollowsIsOptionalAsync(
            [Values] BuilderKind builder)
        {
            TypeModel model = await LoadModelAsync(builder).ConfigureAwait(false);

            IStructure fixedUnion = CreateInstance(model, model.UnionId);
            fixedUnion["Fixed"] = Variant.FromStructure(CreateInner(model, 5));
            byte[] fixedBytes = EncodeBinary(model, (IEncodeable)fixedUnion);
            byte[] expectedFixed;
            using (var encoder = new BinaryEncoder(model.Context))
            {
                encoder.WriteUInt32(null, 1);
                encoder.WriteInt32(null, 5);
                expectedFixed = encoder.CloseAndReturnBuffer();
            }
            Assert.That(fixedBytes, Is.EqualTo(expectedFixed));

            IStructure anyUnion = CreateInstance(model, model.UnionId);
            anyUnion["Any"] = Variant.FromStructure(CreateInnerSub(model, 6, "y"));
            byte[] anyBytes = EncodeBinary(model, (IEncodeable)anyUnion);
            byte[] expectedAny;
            using (var encoder = new BinaryEncoder(model.Context))
            {
                encoder.WriteUInt32(null, 2);
                encoder.WriteExtensionObject(
                    null,
                    new ExtensionObject(CreateInnerSub(model, 6, "y")));
                expectedAny = encoder.CloseAndReturnBuffer();
            }
            Assert.That(anyBytes, Is.EqualTo(expectedAny));

            var decodedFixed = (IStructure)DecodeBinary(model, model.UnionId, fixedBytes);
            var decodedAny = (IStructure)DecodeBinary(model, model.UnionId, anyBytes);
            IStructure fixedInner = decodedFixed["Fixed"].GetStructure<IEncodeable>() as IStructure;
            IStructure anyInner = decodedAny["Any"].GetStructure<IEncodeable>() as IStructure;
            Assert.Multiple(() =>
            {
                Assert.That(fixedInner, Is.Not.Null);
                Assert.That(fixedInner["Value"].GetInt32(), Is.EqualTo(5));
                Assert.That(anyInner, Is.Not.Null);
                Assert.That(((IEncodeable)anyInner).TypeId, Is.EqualTo(model.InnerSubTypeId));
                Assert.That(anyInner["Extra"].GetString(), Is.EqualTo("y"));
            });
        }

        /// <summary>
        /// A Structure-backed OptionSet subtype is created by both builders and
        /// a structure field of that type is encoded as the inherited
        /// Value/ValidBits ByteStrings.
        /// </summary>
        [Test]
        public async Task OptionSetSubtypeFieldBinaryEncodingIsValueAndValidBitsAsync(
            [Values] BuilderKind builder)
        {
            TypeModel model = await LoadModelAsync(builder).ConfigureAwait(false);

            Assert.That(model.TypeSystem.GetDefinedDataTypeIds(), Has.Member(model.OptionsTypeId));
            Assert.That(model.Factory.GetTypes(), Has.Some.InstanceOf<Encoders.OptionSet>());

            IStructure holder = CreateInstance(model, model.HolderId);
            Encoders.OptionSet options = CreateOptions(model);
            holder["Options"] = Variant.FromStructure(options);

            byte[] actual = EncodeBinary(model, (IEncodeable)holder);
            byte[] expected;
            using (var encoder = new BinaryEncoder(model.Context))
            {
                // Value: Read and Execute set; ValidBits: all three bits assigned
                encoder.WriteByteString(null, ByteString.From([0x05]));
                encoder.WriteByteString(null, ByteString.From([0x07]));
                expected = encoder.CloseAndReturnBuffer();
            }
            Assert.That(actual, Is.EqualTo(expected));

            var decoded = (IStructure)DecodeBinary(model, model.HolderId, actual);
            var decodedOptions = decoded["Options"].GetStructure<IEncodeable>() as Encoders.OptionSet;
            Assert.That(decodedOptions, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(decodedOptions.TypeId, Is.EqualTo(model.OptionsTypeId));
                Assert.That(decodedOptions["Read"], Is.True);
                Assert.That(decodedOptions["Write"], Is.False);
                Assert.That(decodedOptions["Execute"], Is.True);
            });
        }

        /// <summary>
        /// The XML and JSON encodings of both builders are identical and round
        /// trip the subtyped structure, the subtyped union and the OptionSet.
        /// </summary>
        [Test]
        public async Task XmlAndJsonEncodingsMatchBetweenBuildersAsync(
            [Values(EncodingType.Xml, EncodingType.Json)] EncodingType encodingType)
        {
            TypeModel defaultModel = await LoadModelAsync(BuilderKind.Default).ConfigureAwait(false);
            TypeModel emitModel = await LoadModelAsync(BuilderKind.Emit).ConfigureAwait(false);

            foreach (Func<TypeModel, IEncodeable> create in new Func<TypeModel, IEncodeable>[]
            {
                m => (IEncodeable)CreateSubtypedStructure(m),
                m =>
                {
                    IStructure union = CreateInstance(m, m.UnionId);
                    union["Any"] = Variant.FromStructure(CreateInnerSub(m, 3, "z"));
                    return (IEncodeable)union;
                },
                m =>
                {
                    IStructure holder = CreateInstance(m, m.HolderId);
                    holder["Options"] = Variant.FromStructure(CreateOptions(m));
                    return (IEncodeable)holder;
                }
            })
            {
                IEncodeable defaultValue = create(defaultModel);
                IEncodeable emitValue = create(emitModel);
                string defaultText = EncodeText(defaultModel, encodingType, defaultValue);
                string emitText = EncodeText(emitModel, encodingType, emitValue);
                if (encodingType == EncodingType.Json)
                {
                    Assert.That(emitText, Is.EqualTo(defaultText));
                }
                else
                {
                    // the XML of the default builder names the body element
                    // after the runtime class, so only the content is compared
                    Assert.That(
                        StripXml(emitText),
                        Is.EqualTo(StripXml(defaultText)),
                        emitText + Environment.NewLine + defaultText);
                }

                IEncodeable defaultDecoded = DecodeText(defaultModel, encodingType, defaultText);
                IEncodeable emitDecoded = DecodeText(emitModel, encodingType, emitText);
                Assert.Multiple(() =>
                {
                    Assert.That(defaultDecoded.IsEqual(defaultValue), Is.True, defaultText);
                    Assert.That(emitDecoded.IsEqual(emitValue), Is.True, emitText);
                });
            }
        }

        /// <summary>
        /// A type the builder cannot create (here an OptionSet subtype) is
        /// skipped and does not abort the load of the other types.
        /// </summary>
        [Test]
        public async Task UnsupportedOptionSetSubtypeDoesNotAbortTheLoadAsync()
        {
            TypeModel model = await LoadModelAsync(
                BuilderKind.Emit,
                f => new NoOptionSetFactory(f)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    model.TypeSystem.GetDefinedDataTypeIds(),
                    Has.Member(model.SubtypedTypeId));
                Assert.That(
                    model.TypeSystem.GetDefinedDataTypeIds(),
                    Has.No.Member(model.OptionsTypeId));
                Assert.That(model.Context.Factory.TryGetEncodeableType(
                    model.SubtypedTypeId, out _), Is.True);
            });
        }

        /// <summary>
        /// The element structure of an XML document without namespaces and
        /// without the names of the elements inside an ExtensionObject Body.
        /// </summary>
        private static string StripXml(string xml)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(
                new System.IO.StringReader(xml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                document.Load(reader);
            }
            var builder = new StringBuilder();
            foreach (System.Xml.XmlElement element in
                document.SelectNodes("//*").OfType<System.Xml.XmlElement>())
            {
                string name = element.ParentNode?.LocalName == "Body" ? "*" : element.LocalName;
                builder.Append('<').Append(name).Append('>');
                if (!element.HasChildNodes || element.FirstChild is XmlText)
                {
                    builder.Append(element.InnerText);
                }
            }
            return builder.ToString();
        }

        private static void AssertSubtypedStructure(IStructure decoded)
        {
            IStructure fixedInner = decoded["Fixed"].GetStructure<IEncodeable>() as IStructure;
            IStructure anyInner = decoded["Any"].GetStructure<IEncodeable>() as IStructure;
            Assert.Multiple(() =>
            {
                Assert.That(fixedInner, Is.Not.Null);
                Assert.That(fixedInner["Value"].GetInt32(), Is.EqualTo(7));
                Assert.That(anyInner, Is.Not.Null);
                Assert.That(anyInner["Value"].GetInt32(), Is.EqualTo(8));
                Assert.That(anyInner["Extra"].GetString(), Is.EqualTo("x"));
                Assert.That(decoded["Number"].GetInt32(), Is.EqualTo(9));
            });
        }

        private static IStructure CreateSubtypedStructure(TypeModel model)
        {
            IStructure value = CreateInstance(model, model.SubtypedId);
            value["Fixed"] = Variant.FromStructure(CreateInner(model, 7));
            value["Any"] = Variant.FromStructure(CreateInnerSub(model, 8, "x"));
            value["Number"] = Variant.From(9);
            return value;
        }

        private static IEncodeable CreateInner(TypeModel model, int value)
        {
            IStructure inner = CreateInstance(model, model.InnerId);
            inner["Value"] = Variant.From(value);
            return (IEncodeable)inner;
        }

        private static IEncodeable CreateInnerSub(TypeModel model, int value, string extra)
        {
            IStructure inner = CreateInstance(model, model.InnerSubId);
            inner["Value"] = Variant.From(value);
            inner["Extra"] = Variant.From(extra);
            return (IEncodeable)inner;
        }

        private static Encoders.OptionSet CreateOptions(TypeModel model)
        {
            Assert.That(
                model.Context.Factory.TryGetEncodeableType(model.OptionsTypeId, out IEncodeableType type),
                Is.True);
            var options = (Encoders.OptionSet)type.CreateInstance();
            options["Read"] = true;
            options["Write"] = false;
            options["Execute"] = true;
            return options;
        }

        private static IStructure CreateInstance(TypeModel model, NodeId typeId)
        {
            Assert.That(
                model.Context.Factory.TryGetEncodeableType(
                    NodeId.ToExpandedNodeId(typeId, model.Context.NamespaceUris),
                    out IEncodeableType type),
                Is.True,
                $"Type {typeId} was not loaded.");
            return (IStructure)type.CreateInstance();
        }

        private static byte[] EncodeBinary(TypeModel model, IEncodeable value)
        {
            using var encoder = new BinaryEncoder(model.Context);
            encoder.WriteEncodeable(null, value, value.TypeId);
            return encoder.CloseAndReturnBuffer();
        }

        private static IEncodeable DecodeBinary(TypeModel model, NodeId typeId, byte[] buffer)
        {
            using var decoder = new BinaryDecoder(buffer, model.Context);
            return decoder.ReadEncodeable<IEncodeable>(
                null,
                NodeId.ToExpandedNodeId(typeId, model.Context.NamespaceUris));
        }

        private static string EncodeText(TypeModel model, EncodingType encodingType, IEncodeable value)
        {
            if (encodingType == EncodingType.Xml)
            {
                using var encoder = new XmlEncoder(model.Context);
                encoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);
                encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                encoder.PopNamespace();
                return encoder.CloseAndReturnText();
            }
            using var jsonEncoder = new JsonEncoder(model.Context);
            jsonEncoder.WriteExtensionObject("Value", new ExtensionObject(value));
            return jsonEncoder.CloseAndReturnText();
        }

        private static IEncodeable DecodeText(TypeModel model, EncodingType encodingType, string text)
        {
            ExtensionObject result;
            if (encodingType == EncodingType.Xml)
            {
                using var decoder = new XmlParser(text, model.Context);
                decoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);
                result = decoder.ReadExtensionObject("Value");
                decoder.PopNamespace();
            }
            else
            {
                using var decoder = new JsonDecoder(text, model.Context);
                result = decoder.ReadExtensionObject("Value");
            }
            Assert.That(result.TryGetValue(out IEncodeable body), Is.True, text);
            return body;
        }

        private static async Task<TypeModel> LoadModelAsync(
            BuilderKind builder,
            Func<IComplexTypeFactory, IComplexTypeFactory> wrap = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var resolver = new MockResolver();
            ushort ns = resolver.NamespaceUris.GetIndexOrAppend(Namespaces.MockResolverUrl);
            uint nodeId = 8100;

            NodeId innerId = AddStructure(resolver, "Inner", ns, ref nodeId, new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = [Field("Value", DataTypeIds.Int32)]
            });
            NodeId innerSubId = AddStructure(resolver, "InnerSub", ns, ref nodeId, new StructureDefinition
            {
                BaseDataType = innerId,
                StructureType = StructureType.Structure,
                Fields = [Field("Value", DataTypeIds.Int32), Field("Extra", DataTypeIds.String)]
            });
            NodeId subtypedId = AddStructure(resolver, "Subtyped", ns, ref nodeId, new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.StructureWithSubtypedValues,
                Fields =
                [
                    Field("Fixed", innerId),
                    Field("Any", innerId, isOptional: true),
                    Field("Number", DataTypeIds.Int32)
                ]
            });
            NodeId unionId = AddStructure(resolver, "SubtypedUnion", ns, ref nodeId, new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.UnionWithSubtypedValues,
                Fields = [Field("Fixed", innerId), Field("Any", innerId, isOptional: true)]
            });

            // the abstract OptionSet DataType and a Structure-backed subtype of it
            resolver.DataTypeNodes[DataTypeIds.OptionSet] = new DataTypeNode
            {
                NodeId = DataTypeIds.OptionSet,
                NodeClass = NodeClass.DataType,
                BrowseName = QualifiedName.From(BrowseNames.OptionSet),
                DisplayName = LocalizedText.From(BrowseNames.OptionSet),
                IsAbstract = true
            };
            resolver.SuperTypes[DataTypeIds.OptionSet] = DataTypeIds.Structure;
            var optionsNode = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, ns),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("Options", ns),
                DisplayName = LocalizedText.From("Options"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(new EnumDefinition
                {
                    Fields =
                    [
                        new EnumField { Name = "Read", Value = 0 },
                        new EnumField { Name = "Write", Value = 1 },
                        new EnumField { Name = "Execute", Value = 2 }
                    ]
                })
            };
            AddEncodingNodes(resolver, optionsNode, ns, ref nodeId);
            resolver.DataTypeNodes[optionsNode.NodeId] = optionsNode;
            resolver.SuperTypes[optionsNode.NodeId] = DataTypeIds.OptionSet;

            NodeId holderId = AddStructure(resolver, "Holder", ns, ref nodeId, new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = [Field("Options", optionsNode.NodeId)]
            });

            IComplexTypeFactory factory = builder == BuilderKind.Emit
                ? new ComplexTypeBuilderFactory()
                : new DefaultComplexTypeFactory();
            if (wrap != null)
            {
                factory = wrap(factory);
            }
            var typeSystem = new ComplexTypeSystem(resolver, factory, telemetry);
            bool loaded = await typeSystem.LoadAsync().ConfigureAwait(false);

            var context = new ServiceMessageContext(telemetry, resolver.Factory)
            {
                NamespaceUris = resolver.NamespaceUris
            };
            return new TypeModel
            {
                Context = context,
                Factory = factory,
                TypeSystem = typeSystem,
                Loaded = loaded,
                InnerId = innerId,
                InnerSubId = innerSubId,
                InnerSubTypeId = NodeId.ToExpandedNodeId(innerSubId, resolver.NamespaceUris),
                SubtypedId = subtypedId,
                SubtypedTypeId = NodeId.ToExpandedNodeId(subtypedId, resolver.NamespaceUris),
                UnionId = unionId,
                OptionsId = optionsNode.NodeId,
                OptionsTypeId = NodeId.ToExpandedNodeId(optionsNode.NodeId, resolver.NamespaceUris),
                HolderId = holderId
            };
        }

        private static StructureField Field(string name, NodeId dataType, bool isOptional = false)
        {
            return new StructureField
            {
                Name = name,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                IsOptional = isOptional
            };
        }

        private static NodeId AddStructure(
            MockResolver resolver,
            string name,
            ushort ns,
            ref uint nodeId,
            StructureDefinition definition)
        {
            var node = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, ns),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = LocalizedText.From(name),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(definition)
            };
            AddEncodingNodes(resolver, node, ns, ref nodeId);
            resolver.DataTypeNodes[node.NodeId] = node;
            return node.NodeId;
        }

        private static void AddEncodingNodes(
            MockResolver resolver,
            DataTypeNode dataTypeNode,
            ushort ns,
            ref uint nodeId)
        {
            foreach (string browseName in new[] { BrowseNames.DefaultBinary, BrowseNames.DefaultXml })
            {
                var description = new ReferenceDescription
                {
                    NodeId = new NodeId(nodeId++, ns),
                    ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                    BrowseName = QualifiedName.From(browseName),
                    DisplayName = LocalizedText.From(browseName),
                    IsForward = true,
                    NodeClass = NodeClass.Object
                };
                var encoding = new Node(description);
                resolver.DataTypeNodes[encoding.NodeId] = encoding;
                dataTypeNode.References += new ReferenceNode
                {
                    ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                    IsInverse = false,
                    TargetId = description.NodeId
                };
            }
        }

        private sealed class TypeModel
        {
            public ServiceMessageContext Context { get; init; }
            public IComplexTypeFactory Factory { get; init; }
            public ComplexTypeSystem TypeSystem { get; init; }
            public bool Loaded { get; init; }
            public NodeId InnerId { get; init; }
            public NodeId InnerSubId { get; init; }
            public ExpandedNodeId InnerSubTypeId { get; init; }
            public NodeId SubtypedId { get; init; }
            public ExpandedNodeId SubtypedTypeId { get; init; }
            public NodeId UnionId { get; init; }
            public NodeId OptionsId { get; init; }
            public ExpandedNodeId OptionsTypeId { get; init; }
            public NodeId HolderId { get; init; }
        }

        /// <summary>
        /// A factory whose builders cannot create OptionSet subtypes.
        /// </summary>
        private sealed class NoOptionSetFactory : IComplexTypeFactory
        {
            public NoOptionSetFactory(IComplexTypeFactory inner)
            {
                m_inner = inner;
            }

            public IComplexTypeBuilder Create(
                string targetNamespace,
                int targetNamespaceIndex,
                string moduleName = null)
            {
                return new Builder(m_inner.Create(targetNamespace, targetNamespaceIndex, moduleName));
            }

            public IReadOnlyList<IType> GetTypes()
            {
                return m_inner.GetTypes();
            }

            private sealed class Builder : IComplexTypeBuilder
            {
                public Builder(IComplexTypeBuilder inner)
                {
                    m_inner = inner;
                }

                public string TargetNamespace => m_inner.TargetNamespace;

                public int TargetNamespaceIndex => m_inner.TargetNamespaceIndex;

                public IEnumeratedType AddEnumType(QualifiedName typeName, EnumDefinition enumDefinition)
                {
                    return m_inner.AddEnumType(typeName, enumDefinition);
                }

                public IEncodeableType AddOptionSetType(
                    QualifiedName typeName,
                    ExpandedNodeId typeId,
                    ExpandedNodeId binaryEncodingId,
                    ExpandedNodeId xmlEncodingId,
                    EnumDefinition enumDefinition)
                {
                    throw new NotSupportedException("OptionSet subtypes are not supported.");
                }

                public IComplexTypeFieldBuilder AddStructuredType(
                    QualifiedName name,
                    StructureDefinition structureDefinition)
                {
                    return m_inner.AddStructuredType(name, structureDefinition);
                }

                private readonly IComplexTypeBuilder m_inner;
            }

            private readonly IComplexTypeFactory m_inner;
        }
    }
}
