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
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using static Opc.Ua.EndpointRegistry.Tests.NativeTestSupport;

namespace Opc.Ua.EndpointRegistry.Tests
{
    /// <summary>
    /// Checks the generated native catalog against the compiled model and replays every Endpoint row of the
    /// specification's native-field coverage ledger through its named native field.
    /// </summary>
    [TestFixture]
    [Category("EndpointRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class EndpointRegistryNativeCatalogTests
    {
        private static RegistryNativeCatalog Catalog => EndpointRegistryNativeCatalog.Catalog;

        public static IEnumerable<TestCaseData> ConstructibleTypes()
        {
            foreach (RegistryNativeTypeDescriptor type in EndpointRegistryNativeCatalog.Catalog.Types.ToArray()!)
            {
                if (type.Factory is not null && !IsAdapterOwned(type.Name))
                {
                    yield return new TestCaseData(type.Name).SetArgDisplayNames(type.Name);
                }
            }
        }

        [TestCaseSource(nameof(ConstructibleTypes))]
        public void PublishedLayoutMatchesTheGeneratedEncoding(string name)
        {
            Assert.That(Catalog.TryGetType(name, out RegistryNativeTypeDescriptor? type), Is.True);
            IEncodeable instance = type!.Factory!();

            ArrayOf<RegistryNativeField> encoded = Fields(instance);

            ArrayOf<RegistryNativeFieldDescriptor> published = Catalog.GetFields(name);
            Assert.Multiple(() =>
            {
                Assert.That(instance.GetType().Name, Is.EqualTo(name));
                Assert.That(instance.TypeId, Is.EqualTo(type.DataTypeId));
                Assert.That(encoded.ToArray()!.Select(field => field.Name),
                    Is.EqualTo(published.ToArray()!.Select(field => field.Name)));
                Assert.That(type.IsAbstract, Is.False);
            });
        }

        [Test]
        public void EveryGeneratedRecordAndMapIsPublished()
        {
            Type[] generated = [.. typeof(EndpointDataType).Assembly.GetExportedTypes()
                .Where(type => typeof(RegistryRecordDataType).IsAssignableFrom(type) ||
                    (typeof(IEncodeable).IsAssignableFrom(type) && type.GetProperty("Entries") is not null))];
            string[] published = [.. Catalog.Types.ToArray()!
                .Where(type => type.DataTypeId.NamespaceUri == Namespaces.EndpointRegistry &&
                    (Catalog.IsSubtype(type.Name, RegistryNativeCatalog.RecordType) ||
                        Catalog.TryGetMap(type.Name, out _)))
                .Select(type => type.Name)];

            Assert.Multiple(() =>
            {
                Assert.That(generated.Select(type => type.Name), Is.EquivalentTo(published));
                foreach (Type type in generated)
                {
                    Assert.That(Catalog.TryGetType(type.Name, out RegistryNativeTypeDescriptor? descriptor), Is.True,
                        type.Name);
                    if (descriptor?.Factory is not null)
                    {
                        Assert.That(descriptor.Factory().GetType(), Is.EqualTo(type), type.Name);
                    }
                }
            });
        }

        [Test]
        public void SchemaContentIsTheOnlyAdapterOwnedFamily()
        {
            string[] adapterOwned = [.. Catalog.Types.ToArray()!
                .Where(type => IsAdapterOwned(type.Name))
                .Select(type => type.Name)];

            Assert.Multiple(() =>
            {
                Assert.That(adapterOwned, Is.EquivalentTo(s_schemaContentTypes));
                Assert.That(Catalog.TryGetChoiceFamily(nameof(SchemaContentDataType),
                    out RegistryNativeChoiceFamily? family), Is.True);
                Assert.That(family!.Selector, Is.EqualTo("dataschemaformat"));
                Assert.That(family.Comparison, Is.EqualTo(RegistrySelectorComparison.CaseFold));
                Assert.That(family.Fallback, Is.EqualTo(nameof(ExtensionSchemaContentDataType)));
                Assert.That(new SchemaContentValueAdapter([]).DataTypes.ToArray(),
                    Is.EquivalentTo(s_schemaContentTypes.Skip(1)));
            });
        }

        public static IEnumerable<TestCaseData> LedgerRows()
        {
            foreach ((string path, string type, string field, string sourceType, string nativeType, bool isArray)
                in EndpointRegistryNativeCoverage.Rows)
            {
                yield return new TestCaseData(path, type, field, sourceType, nativeType, isArray)
                    .SetArgDisplayNames(path, type);
            }
        }

        [TestCaseSource(nameof(LedgerRows))]
        public void LedgerRowReplaysThroughItsNamedNativeField(
            string path,
            string type,
            string field,
            string sourceType,
            string nativeType,
            bool isArray)
        {
            var ledger = new LedgerPath(path);
            RegistryValueDataType document = Json(ledger.BuildDocument());

            RegistryRecordDataType record = Mapper.Project(document, ledger.RootType);

            var leaf = (RegistryRecordDataType)ledger.Navigate(record);
            string actual = LedgerPath.TypeName(leaf);
            Assert.Multiple(() =>
            {
                Assert.That(Catalog.IsSubtype(actual, type), Is.True, actual + " is not a " + type);
                if (field == "AdditionalFields")
                {
                    Assert.That(sourceType, Is.EqualTo("object").Or.EqualTo("any"));
                    Assert.That(leaf.AdditionalFields.ToArray()!.Select(member => member.Name),
                        Is.EqualTo(s_ledgerMember));
                    Assert.That(leaf.PresentFields.ToArray(), Is.Empty);
                }
                else
                {
                    RegistryNativeFieldDescriptor descriptor = Catalog.GetFields(actual).ToArray()!
                        .Single(item => item.Name == field);
                    Assert.That(descriptor.Source, Is.EqualTo(ledger.LeafSource));
                    Assert.That((descriptor.DataType, descriptor.IsArray), Is.EqualTo((nativeType, isArray)));
                    Assert.That(leaf.PresentFields.ToArray(), Is.EqualTo(new[] { field }));
                    Assert.That(leaf.AdditionalFields.ToArray(), Is.Empty);
                    Assert.That(IsUnused(Field(leaf, field)), Is.False, "The authored field is unused.");
                }
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(record)), Is.True);
            });
        }

        [Test]
        public void VendorSubtypeWithPublishedMetadataIsSelectedAndRestoredExactly()
        {
            RegistryRecordMapper mapper = VendorMapper();
            RegistryValueDataType document = Json(
                "{\"protocol\":\"URN:Vendor:Proto/1\",\"protocoloptions\":{\"secure\":true,\"deployed\":true," +
                "\"priority\":\"high\",\"x-other\":{\"n\":1.50}}}");

            var endpoint = (EndpointDataType)mapper.Project(document, DataTypeIds.EndpointDataType);

            var options = (VendorProtocolOptionsDataType)endpoint.ProtocolOptions;
            var unregistered = (EndpointDataType)Mapper.Project(document, DataTypeIds.EndpointDataType);
            Assert.Multiple(() =>
            {
                Assert.That(options.PresentFields.ToArray(), Is.EqualTo(s_vendorPresent));
                Assert.That(options.AdditionalFields.ToArray()!.Select(member => member.Name),
                    Is.EqualTo(s_vendorExtensions));
                Assert.That((options.Deployed, options.Priority, options.Secure), Is.EqualTo((true, "high", true)));
                Assert.That(RegistryValues.Identical(document, mapper.Restore(endpoint)), Is.True);
                Assert.That(unregistered.ProtocolOptions, Is.TypeOf<EndpointProtocolOptionsExtensionDataType>());
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(unregistered)), Is.True);
            });
        }

        [Test]
        public void VendorRegistrationIsValidatedWhenTheCatalogIsBuilt()
        {
            RegistryNativeTypeDescriptor vendor = VendorDescriptor();

            Assert.Multiple(() =>
            {
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder().AddType(vendor)
                        .RegisterChoice(nameof(EndpointProtocolOptionsDataType), "mqtt", vendor.Name).Build(),
                    Throws.InvalidOperationException.With.Message.Contains("collides"));
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder().AddType(vendor)
                        .RegisterChoice(nameof(EndpointProtocolOptionsDataType), "urn:vendor",
                            nameof(EndpointProtocolOptionsExtensionDataType)).Build(),
                    Throws.InvalidOperationException);
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder().AddType(vendor)
                        .RegisterChoice(nameof(EndpointEnvelopeOptionsDataType), "urn:vendor", vendor.Name).Build(),
                    Throws.InvalidOperationException);
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder().AddType(vendor)
                        .RegisterChoice(nameof(EndpointProtocolOptionsDataType), "urn:v\u00E9ndor", vendor.Name)
                        .Build(),
                    Throws.InvalidOperationException);
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder().AddType(vendor with
                        {
                            Fields = [new RegistryNativeFieldDescriptor("Priority", "String") { Source = "deployed" }]
                        }).Build(),
                    Throws.InvalidOperationException);
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder().AddType(vendor with { Factory = null })
                        .Build(),
                    Throws.InvalidOperationException);
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder()
                        .AddType(vendor with { DataTypeId = DataTypeIds.EndpointDataType }),
                    Throws.ArgumentException);
                Assert.That(() => EndpointRegistryNativeCatalog.CreateBuilder()
                        .RegisterChoice(nameof(EndpointDataType), "x", vendor.Name),
                    Throws.ArgumentException);
            });
        }

        [Test]
        public void VendorLayoutThatDisagreesWithItsEncodingIsRejected()
        {
            RegistryNativeTypeDescriptor swapped = VendorDescriptor() with
            {
                Fields =
                [
                    new RegistryNativeFieldDescriptor("Secure", "Boolean") { Source = "secure" },
                    new RegistryNativeFieldDescriptor("Priority", "String") { Source = "priority" }
                ]
            };
            var mapper = new RegistryRecordMapper(
                EndpointRegistryNativeCatalog.CreateBuilder().AddType(swapped)
                    .RegisterChoice(nameof(EndpointProtocolOptionsDataType), "urn:vendor:proto/1", swapped.Name)
                    .Build(),
                Context);

            RegistryRecordMappingException error = Assert.Throws<RegistryRecordMappingException>(() => mapper.Project(
                Json("{\"protocol\":\"urn:vendor:proto/1\",\"protocoloptions\":{\"priority\":\"high\"}}"),
                DataTypeIds.EndpointDataType))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void UnpublishedSubtypeIsRejectedInsteadOfDropped()
        {
            var vendor = (EndpointDataType)VendorMapper().Project(
                Json("{\"protocol\":\"urn:vendor:proto/1\",\"protocoloptions\":{\"priority\":\"high\"}}"),
                DataTypeIds.EndpointDataType);
            var unpublished = (EndpointDataType)Mapper.Project(
                Json("{\"protocol\":\"MQTT\",\"protocoloptions\":{\"deployed\":true}}"),
                DataTypeIds.EndpointDataType);
            unpublished.ProtocolOptions = new UnpublishedProtocolOptionsDataType
            {
                PresentFields = ["Deployed"],
                AdditionalFields = [],
                Authorization = [],
                Deployed = true
            };

            Assert.Multiple(() =>
            {
                foreach (EndpointDataType record in new[] { vendor, unpublished })
                {
                    RegistryRecordMappingException error = Assert.Throws<RegistryRecordMappingException>(
                        () => Mapper.Restore(record))!;
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                    Assert.That(error.Path.ToArray(), Is.EqualTo(s_protocolOptionsPath));
                }
            });
        }

        [TestCase("JsonSchema/2020-12", JsonObjectSchema, typeof(JsonSchemaContentDataType))]
        [TestCase("JSONSCHEMA/2020-12", JsonObjectSchema, typeof(JsonSchemaContentDataType))]
        [TestCase("J\u017FonSchema/2020-12", JsonObjectSchema, typeof(JsonSchemaContentDataType))]
        [TestCase("JsonSchema/2020-12 ", JsonObjectSchema, typeof(ExtensionSchemaContentDataType))]
        [TestCase("JsonSchema", JsonObjectSchema, typeof(ExtensionSchemaContentDataType))]
        [TestCase("avro/1.11", AvroRecordSchema, typeof(AvroSchemaContentDataType))]
        [TestCase("AVRO/1.11", AvroRecordSchema, typeof(AvroSchemaContentDataType))]
        public void SchemaFormatSelectorUsesUnicodeCaseFolding(string format, string schema, Type expected)
        {
            RegistryValueDataType document = Json(
                "{\"dataschemaformat\":\"" + format + "\",\"dataschema\":" + schema + "}");

            var message = (MessageDefinitionDataType)Mapper.Project(document, DataTypeIds.MessageDefinitionDataType);

            Assert.Multiple(() =>
            {
                Assert.That(message.DataSchema, Is.TypeOf(expected));
                Assert.That(message.DataSchemaFormat, Is.EqualTo(format));
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(message)), Is.True);
            });
        }

        [TestCase("nat\u017F", "EndpointProtocolOptionsNATSDataType")]
        [TestCase("r\u0131st-main/2024", "EndpointProtocolOptionsRISTMain2024DataType")]
        [TestCase("ka\u017Fka", "EndpointProtocolOptionsExtensionDataType")]
        [TestCase("\u212Aafka", "EndpointProtocolOptionsExtensionDataType")]
        [TestCase("amqp/1.0", "EndpointProtocolOptionsAMQP10DataType")]
        [TestCase("AMQP", "EndpointProtocolOptionsAMQP10DataType")]
        [TestCase("AMQP/1", "EndpointProtocolOptionsExtensionDataType")]
        [TestCase("", "EndpointProtocolOptionsExtensionDataType")]
        public void ProtocolSelectorUsesUnicodeUpperCase(string protocol, string expected)
        {
            Assert.That(Catalog.SelectType(nameof(EndpointProtocolOptionsDataType), protocol), Is.EqualTo(expected));
        }

        private static RegistryRecordMapper VendorMapper()
        {
            RegistryNativeTypeDescriptor vendor = VendorDescriptor();
            RegistryNativeCatalog catalog = EndpointRegistryNativeCatalog.Catalog.ToBuilder()
                .AddType(vendor)
                .RegisterChoice(nameof(EndpointProtocolOptionsDataType), "urn:vendor:proto/1", vendor.Name)
                .Build();
            return new RegistryRecordMapper(catalog, Context);
        }

        private static RegistryNativeTypeDescriptor VendorDescriptor()
        {
            return new RegistryNativeTypeDescriptor(
                nameof(VendorProtocolOptionsDataType),
                VendorProtocolOptionsDataType.DataTypeId,
                nameof(EndpointProtocolOptionsDataType))
            {
                Fields =
                [
                    new RegistryNativeFieldDescriptor("Priority", "String") { Source = "priority" },
                    new RegistryNativeFieldDescriptor("Secure", "Boolean") { Source = "secure" }
                ],
                Factory = static () => new VendorProtocolOptionsDataType()
            };
        }

        private static bool IsAdapterOwned(string name)
        {
            return Catalog.IsSubtype(name, nameof(SchemaContentDataType));
        }

        private static bool IsUnused(Variant value)
        {
            if (value.IsNull)
            {
                return true;
            }
            return value.TypeInfo.ValueRank != ValueRanks.Scalar
                ? (value.TryGetValue(out ArrayOf<string> texts) && texts.Count == 0) ||
                    (value.TryGetValue(out ArrayOf<bool> flags) && flags.Count == 0) ||
                    (value.TryGetValue(out ArrayOf<ExtensionObject> items) && items.Count == 0)
                : (value.TryGetValue(out string text) && text.Length == 0) ||
                    (value.TryGetValue(out bool flag) && !flag) ||
                    (value.TryGetValue(out ExtensionObject extension) && extension.IsNull);
        }

        /// <summary>
        /// Builds the smallest document that authors one ledger path and navigates the projected record
        /// along the same path. A path is "/collection/*/member", "//member" for the registry root,
        /// "/messages/*/member" inside a Message Group, "Type/member" for a model member, "member[selector]"
        /// for a selector subtype and a trailing "*" for an unknown member.
        /// </summary>
        private sealed class LedgerPath
        {
            public LedgerPath(string path)
            {
                string relative;
                if (path[0] != '/')
                {
                    int slash = path.IndexOf('/', StringComparison.Ordinal);
                    RootType = path.Substring(0, slash);
                    relative = path.Substring(slash + 1);
                }
                else if (path.StartsWith("//", StringComparison.Ordinal))
                {
                    RootType = nameof(EndpointRegistryDocumentDataType);
                    relative = path.Substring(2);
                }
                else
                {
                    RootType = path.StartsWith("/messages/", StringComparison.Ordinal)
                        ? nameof(MessageGroupDataType)
                        : nameof(EndpointRegistryDocumentDataType);
                    relative = path.Substring(1);
                }
                m_segments = Segments(relative);
                LeafSource = Split(m_segments[m_segments.Length - 1]).Source;
            }

            public string RootType { get; }

            public string LeafSource { get; }

            public static string TypeName(IEncodeable value)
            {
                return Catalog.TryGetType(value.TypeId, out RegistryNativeTypeDescriptor? type)
                    ? type.Name
                    : throw new AssertionException("Unpublished native DataType " + value.TypeId);
            }

            public string BuildDocument()
            {
                var text = new StringBuilder();
                BuildRecord(text, RootType, 0);
                return text.ToString();
            }

            public IEncodeable Navigate(IEncodeable record)
            {
                IEncodeable current = record;
                int index = 0;
                while (index < m_segments.Length - 1)
                {
                    RegistryNativeFieldDescriptor field =
                        FindBySource(TypeName(current), Split(m_segments[index]).Source);
                    Variant value = NativeTestSupport.Field(current, field.Name);
                    index++;
                    if (Catalog.TryGetMap(field.DataType, out _))
                    {
                        IEncodeable map = Body(value);
                        IEncodeable entry = Body(new Variant(Items(NativeTestSupport.Field(map, "Entries"))[0]));
                        value = NativeTestSupport.Field(entry, "Value");
                        index++;
                    }
                    else if (field.IsArray)
                    {
                        value = new Variant(Items(value)[0]);
                        index++;
                    }
                    current = Body(value);
                }
                return current;
            }

            private void BuildRecord(StringBuilder text, string typeName, int index)
            {
                string segment = m_segments[index];
                if (segment == "*")
                {
                    text.Append("{\"").Append(LedgerMember).Append("\":[1,\"a\",null]}");
                    return;
                }
                (string source, string? selector) = Split(segment);
                RegistryNativeFieldDescriptor field = FindBySource(typeName, source);
                text.Append("{\"").Append(source).Append("\":");
                if (index == m_segments.Length - 1)
                {
                    text.Append(Sample(field));
                    text.Append('}');
                    return;
                }
                int next = index + 1;
                string element = field.DataType;
                bool map = Catalog.TryGetMap(field.DataType, out RegistryNativeMapDescriptor? mapType);
                if (map || field.IsArray)
                {
                    Assert.That(m_segments[next], Is.EqualTo("*"), "A collection is followed by an element.");
                    element = map ? mapType!.ValueType : field.DataType;
                    next++;
                }
                string? selectorMember = null;
                if (Catalog.TryGetChoiceFamily(element, out RegistryNativeChoiceFamily? family))
                {
                    selector ??= FirstChoiceDeclaring(family, m_segments[next]);
                    element = Catalog.SelectType(element, selector);
                    selectorMember = selector is null ? null : family.Selector;
                }
                text.Append(map ? "{\"k\":" : field.IsArray ? "[" : string.Empty);
                BuildRecord(text, element, next);
                text.Append(map ? "}" : field.IsArray ? "]" : string.Empty);
                if (selectorMember is not null)
                {
                    text.Append(",\"").Append(selectorMember).Append("\":\"").Append(selector).Append('"');
                }
                text.Append('}');
            }

            private static string? FirstChoiceDeclaring(RegistryNativeChoiceFamily family, string segment)
            {
                if (segment == "*")
                {
                    return null;
                }
                string source = Split(segment).Source;
                foreach (RegistryNativeChoice choice in family.Choices)
                {
                    if (Catalog.GetFields(choice.DataType).ToArray()!.Any(field => field.Source == source))
                    {
                        return choice.Key;
                    }
                }
                throw new AssertionException("No selector subtype of " + family.Root + " declares " + source);
            }

            private static string Sample(RegistryNativeFieldDescriptor field)
            {
                string item = SampleOf(field.DataType);
                return field.IsArray ? "[" + item + "]" : item;
            }

            private static string SampleOf(string dataType)
            {
                if (Catalog.TryGetMap(dataType, out RegistryNativeMapDescriptor? map))
                {
                    return "{\"k\":" + SampleOf(map.ValueType) + "}";
                }
                return dataType switch
                {
                    "String" => "\"s\"",
                    "Boolean" => "true",
                    nameof(RegistryNumberValueDataType) => "1",
                    RegistryNativeCatalog.ValueType => "[1,\"a\",null,{\"b\":true}]",
                    nameof(SchemaContentDataType) => "{\"type\":\"string\"}",
                    _ when Catalog.IsSubtype(dataType, RegistryNativeCatalog.RecordType) => "{}",
                    _ => throw new AssertionException("No sample for " + dataType)
                };
            }

            private static RegistryNativeFieldDescriptor FindBySource(string typeName, string source)
            {
                RegistryNativeFieldDescriptor? field = Catalog.GetFields(typeName).ToArray()!
                    .SingleOrDefault(item => item.Source == source);
                return field ?? throw new AssertionException(typeName + " declares no source member " + source);
            }

            /// <summary>
            /// Splits a ledger path at the slashes outside a selector, which may contain slashes itself.
            /// </summary>
            private static string[] Segments(string path)
            {
                var segments = new List<string>();
                int depth = 0;
                int start = 0;
                for (int index = 0; index < path.Length; index++)
                {
                    switch (path[index])
                    {
                        case '[':
                            depth++;
                            break;
                        case ']':
                            depth--;
                            break;
                        case '/' when depth == 0:
                            segments.Add(path.Substring(start, index - start));
                            start = index + 1;
                            break;
                    }
                }
                segments.Add(path.Substring(start));
                return [.. segments];
            }

            private static (string Source, string? Selector) Split(string segment)
            {
                int open = segment.IndexOf('[', StringComparison.Ordinal);
                return open < 0 || segment[segment.Length - 1] != ']'
                    ? (segment, null)
                    : (segment.Substring(0, open), segment.Substring(open + 1, segment.Length - open - 2));
            }

            private static IEncodeable Body(Variant value)
            {
                return value.TryGetValue(out ExtensionObject extension) &&
                    extension.TryGetValue(out IEncodeable? body, Context) && body is not null
                    ? body
                    : throw new AssertionException("A structure is required along the ledger path.");
            }

            private static ArrayOf<ExtensionObject> Items(Variant value)
            {
                return value.TryGetValue(out ArrayOf<ExtensionObject> items) && items.Count > 0
                    ? items
                    : throw new AssertionException("An element is required along the ledger path.");
            }

            private readonly string[] m_segments;
        }

        private const string JsonObjectSchema = "{\"type\":\"object\",\"required\":[\"a\"]}";
        private const string AvroRecordSchema = "{\"type\":\"record\",\"name\":\"R\",\"fields\":[]}";
        private const string LedgerMember = "x-ledger";
        private static readonly string[] s_ledgerMember = [LedgerMember];
        private static readonly string[] s_schemaContentTypes =
        [
            nameof(SchemaContentDataType),
            nameof(JsonSchemaContentDataType),
            nameof(AvroSchemaContentDataType),
            nameof(ArrowSchemaContentDataType),
            nameof(ExtensionSchemaContentDataType),
            nameof(ArrowIpcSchemaContentDataType)
        ];
        private static readonly string[] s_vendorPresent = ["Deployed", "Priority", "Secure"];
        private static readonly string[] s_vendorExtensions = ["x-other"];
        private static readonly string[] s_protocolOptionsPath = ["protocoloptions"];
    }

    /// <summary>
    /// A vendor protocol option subtype with its own DataType NodeId, published through catalog metadata.
    /// </summary>
    public sealed class VendorProtocolOptionsDataType : EndpointProtocolOptionsDataType
    {
        public const string VendorNamespace = "urn:opcfoundation:tests:vendor-protocol";
        public static readonly ExpandedNodeId DataTypeId = new(5001u, 0, VendorNamespace);
        public static readonly ExpandedNodeId EncodingId = new(5002u, 0, VendorNamespace);

        public string Priority { get; set; } = string.Empty;

        public bool Secure { get; set; }

        public override ExpandedNodeId TypeId => DataTypeId;

        public override ExpandedNodeId BinaryEncodingId => EncodingId;

        public override ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);
            encoder.WriteString("Priority", Priority);
            encoder.WriteBoolean("Secure", Secure);
        }

        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);
            Priority = decoder.ReadString("Priority") ?? string.Empty;
            Secure = decoder.ReadBoolean("Secure");
        }

        public override bool IsEqual(IEncodeable encodeable)
        {
            return encodeable is VendorProtocolOptionsDataType other && base.IsEqual(other) &&
                string.Equals(Priority, other.Priority, StringComparison.Ordinal) && Secure == other.Secure;
        }
    }

    /// <summary>
    /// A subtype whose DataType is not published by any catalog; it must never be silently mapped.
    /// </summary>
    public sealed class UnpublishedProtocolOptionsDataType : EndpointProtocolOptionsDataType
    {
        public override ExpandedNodeId TypeId => new(5003u, 0, VendorProtocolOptionsDataType.VendorNamespace);
    }
}
