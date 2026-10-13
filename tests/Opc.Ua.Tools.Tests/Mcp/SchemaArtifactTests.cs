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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Schema;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Structural artifact assertions against the real SDK generators and bounded file publication.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class SchemaArtifactTests
    {
        /// <summary>
        /// Every advertised format preserves the declared type namespace and one-dimensional field shape.
        /// </summary>
        [TestCase(UaSchemaFormat.Xsd)]
        [TestCase(UaSchemaFormat.Bsd)]
        [TestCase(UaSchemaFormat.JsonCompact)]
        [TestCase(UaSchemaFormat.JsonVerbose)]
        public async Task EverySdkFormatProducesStructuralSchemaAsync(UaSchemaFormat format)
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Sample", SchemaTestContext.Structure(
                SchemaTestContext.Field("Count", DataTypeIds.Int32),
                SchemaTestContext.Field("Readings", DataTypeIds.Double, ValueRanks.OneDimension)));

            JsonObject result = SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString(), Format = format }, "sample.schema")
                .ConfigureAwait(false));
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "sample.schema"))
                .ConfigureAwait(false);

            Assert.That(result["format"]!.GetValue<string>(), Is.EqualTo(format.ToString()));
            Assert.That(result["targetNamespace"]!.GetValue<string>(), Is.EqualTo(SchemaTestContext.TypeNamespaceUri));
            Assert.That(result["bytesTransferred"]!.GetValue<long>(), Is.EqualTo(bytes.Length));
            Assert.That(result["sha256"]!.GetValue<string>(), Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes))));
            if (format is UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose)
            {
                JsonObject document = JsonNode.Parse(bytes)!.AsObject();
                JsonNode definition = document["$defs"]!["Sample"]!;
                Assert.That(document["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/Sample"));
                Assert.That(definition["properties"]!["Count"]!["type"]!.GetValue<string>(), Is.EqualTo("integer"));
                JsonNode readings = definition["properties"]!["Readings"]!;
                if (format == UaSchemaFormat.JsonVerbose)
                {
                    Assert.That(definition["required"]!.AsArray().Select(value => value!.GetValue<string>()),
                        Does.Contain("Readings"));
                    readings = readings["anyOf"]![0]!;
                }
                Assert.That(readings["type"]!.GetValue<string>(), Is.EqualTo("array"));
            }
            else
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var document = XDocument.Load(stream);
                Assert.That(document.Root!.Attribute("TargetNamespace")?.Value ??
                    document.Root.Attribute("targetNamespace")?.Value, Is.EqualTo(SchemaTestContext.TypeNamespaceUri));
                Assert.That(document.Descendants().Any(element => element.Attribute("Name")?.Value == "Readings" ||
                    element.Attribute("name")?.Value == "Readings"), Is.True);
                if (format == UaSchemaFormat.Bsd)
                {
                    XElement field = document.Descendants().Single(element =>
                        element.Name.LocalName == "Field" && element.Attribute("Name")?.Value == "Readings");
                    Assert.That(field.Attribute("LengthField")?.Value, Is.EqualTo("NoOfReadings"));
                }
                else
                {
                    Assert.That(document.Descendants().Any(element =>
                        element.Attribute("maxOccurs")?.Value == "unbounded"), Is.True);
                }
            }
        }

        /// <summary>
        /// Type scope excludes an unrelated sibling; Namespace scope contains it without guessing an alias namespace.
        /// </summary>
        [Test]
        public async Task NamespaceScopeIncludesSiblingButTypeScopeDoesNotAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Root", SchemaTestContext.Structure());
            fixture.Register("Sibling", SchemaTestContext.Structure());
            var input = new SchemaGenerateInput { TypeId = root.ToString() };
            SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(input, "type.json").ConfigureAwait(false));
            input.Scope = UaSchemaScope.Namespace;
            SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(input, "namespace.json").ConfigureAwait(false));
            JsonObject type = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "type.json"))
                .ConfigureAwait(false))!.AsObject();
            JsonObject all = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "namespace.json"))
                .ConfigureAwait(false))!.AsObject();

            Assert.That(type["$defs"]!.AsObject().Select(entry => entry.Key), Is.EqualTo(s_rootOnly));
            Assert.That(all["$defs"]!.AsObject().Select(entry => entry.Key),
                Is.EquivalentTo(s_namespaceTypes));
            Assert.That(all.ContainsKey("$ref"), Is.False);
        }

        /// <summary>
        /// Namespace limits are enforced before provider expansion and never bypassed through the host resolver.
        /// </summary>
        [Test]
        public async Task RegisteredNamespaceCannotBypassTypeLimitAsync()
        {
            await using var fixture = new SchemaTestContext(new McpSchemaOptions { MaxTypes = 1 });
            NodeId root = fixture.Register("Root", SchemaTestContext.Structure());
            fixture.Register("Sibling", SchemaTestContext.Structure());

            JsonObject result = SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString(), Scope = UaSchemaScope.Namespace }, "too-many.json")
                .ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// Local listing has meaningful empty, first-page and last-page states without an active session.
        /// </summary>
        [Test]
        public async Task RegisteredListPreservesEmptyAndPagingStatesAsync()
        {
            await using var fixture = new SchemaTestContext();
            var query = new SchemaListInput { NamespaceUri = SchemaTestContext.TypeNamespaceUri, MaxResults = 1 };
            JsonObject empty = SchemaTestContext.Payload(await fixture.Tools.ListTypesAsync(query)
                .ConfigureAwait(false));
            NodeId first = fixture.Register("First", SchemaTestContext.Structure());
            NodeId second = fixture.Register("Second", SchemaTestContext.Structure());
            JsonObject page = SchemaTestContext.Payload(await fixture.Tools.ListTypesAsync(query)
                .ConfigureAwait(false));
            query.Offset = 1;
            JsonObject last = SchemaTestContext.Payload(await fixture.Tools.ListTypesAsync(query)
                .ConfigureAwait(false));

            Assert.That(empty["items"]!.AsArray(), Is.Empty);
            Assert.That(empty["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(page["items"]![0]!["typeId"]!.GetValue<string>(), Is.EqualTo(first.ToString()));
            Assert.That(page["nextOffset"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(last["items"]![0]!["typeId"]!.GetValue<string>(), Is.EqualTo(second.ToString()));
            Assert.That(last["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(last["nextOffset"], Is.Null);
        }

        /// <summary>
        /// Factory-backed discovery deduplicates encoding aliases and filters by the actual DataType namespace.
        /// </summary>
        [Test]
        public async Task FactoryDiscoveryReturnsCanonicalTypesAndFiltersEncodingNamespacesAsync()
        {
            await using var fixture = new SchemaTestContext();
            Assert.That(fixture.Context.Factory.TryGetEncodeableType(
                DataTypeIds.Argument, out IEncodeableType? type), Is.True);
            var context = ServiceMessageContext.CreateEmpty(fixture.Context.Telemetry);
            const string encodingNamespace = "urn:test:schema:encodings";
            ushort index = context.NamespaceUris.GetIndexOrAppend(encodingNamespace);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            builder.AddEncodeableType(type!)
                .AddEncodeableType(
                NodeId.ToExpandedNodeId(new NodeId("Alias", index), context.NamespaceUris), type!);
            builder.Commit();
            SchemaTools tools = WithContext(fixture, context);

            JsonObject standard = SchemaTestContext.Payload(await tools.ListTypesAsync(new SchemaListInput())
                .ConfigureAwait(false));
            JsonObject aliases = SchemaTestContext.Payload(await tools.ListTypesAsync(
                new SchemaListInput { NamespaceUri = encodingNamespace }).ConfigureAwait(false));

            Assert.That(standard["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(standard["items"]![0]!["typeId"]!.GetValue<string>(),
                Is.EqualTo(DataTypeIds.Argument.ToString()));
            Assert.That(standard["items"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("Argument"));
            Assert.That(standard["items"]![0]!["namespaceUri"]!.GetValue<string>(), Is.EqualTo(Namespaces.OpcUa));
            Assert.That(aliases["items"]!.AsArray(), Is.Empty);
            Assert.That(aliases["hasMore"]!.GetValue<bool>(), Is.False);
        }

        /// <summary>
        /// Even a namespace with no DataTypes cannot scan an unlimited number of registered encoding identifiers.
        /// </summary>
        [TestCase(4096, false)]
        [TestCase(4097, true)]
        public async Task FactoryIdentifierScanEnforcesExactBoundAsync(int count, bool failure)
        {
            await using var fixture = new SchemaTestContext();
            Assert.That(fixture.Context.Factory.TryGetEncodeableType(
                DataTypeIds.Argument, out IEncodeableType? type), Is.True);
            var context = ServiceMessageContext.CreateEmpty(fixture.Context.Telemetry);
            const string encodingNamespace = "urn:test:schema:encoding-scan";
            ushort index = context.NamespaceUris.GetIndexOrAppend(encodingNamespace);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            for (uint identifier = 1; identifier <= count; identifier++)
            {
                builder.AddEncodeableType(
                    NodeId.ToExpandedNodeId(new NodeId(identifier, index), context.NamespaceUris), type!);
            }
            builder.Commit();

            JsonObject result = SchemaTestContext.Payload(await WithContext(fixture, context).ListTypesAsync(
                new SchemaListInput { NamespaceUri = encodingNamespace }).ConfigureAwait(false), error: failure);

            if (failure)
            {
                Assert.That(result["statusCodeValue"]!.GetValue<uint>(),
                    Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded.Code));
            }
            else
            {
                Assert.That(result["items"]!.AsArray(), Is.Empty);
                Assert.That(result["hasMore"]!.GetValue<bool>(), Is.False);
            }
        }

        /// <summary>
        /// Multidimensional JSON arrays retain their rank rather than collapsing to one dimension.
        /// </summary>
        [Test]
        public async Task JsonGenerationPreservesTwoDimensionalRankAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Matrix", SchemaTestContext.Structure(
                SchemaTestContext.Field("Values", DataTypeIds.Double, 2)));
            SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString() }, "matrix.json").ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "matrix.json"))
                .ConfigureAwait(false))!;
            JsonNode values = document["$defs"]!["Matrix"]!["properties"]!["Values"]!;

            Assert.That(values["type"]!.GetValue<string>(), Is.EqualTo("array"));
            Assert.That(values["items"]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
            Assert.That(values["items"]!["items"]!["type"]!.AsArray().Select(value => value!.GetValue<string>()),
                Is.EquivalentTo(s_numberRepresentations));
        }

        /// <summary>
        /// Structure-backed OptionSets use Value and ValidBits, not ordinal enum values.
        /// </summary>
        [Test]
        public async Task StructureOptionSetGeneratesValueAndValidBitsAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Flags", new EnumDefinition
            {
                IsOptionSet = true,
                Fields = [new EnumField { Name = "Enabled", Value = 0 }]
            }, structureOptionSet: true);

            SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString() }, "flags.json").ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "flags.json"))
                .ConfigureAwait(false))!;

            Assert.That(document["$defs"]!["Flags"]!["properties"]!.AsObject().Select(property => property.Key),
                Is.EquivalentTo(s_optionSetProperties));
            JsonObject refused = SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString(), Format = UaSchemaFormat.Bsd }, "flags.bsd")
                .ConfigureAwait(false), error: true);
            Assert.That(refused["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(File.Exists(Path.Combine(fixture.Root, "flags.bsd")), Is.False);
        }

        /// <summary>
        /// Provider limitations and unresolved fields are explicit errors and publish nothing.
        /// </summary>
        [Test]
        public async Task UnsupportedAndUnknownDefinitionsLeaveNoArtifactAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Matrix", SchemaTestContext.Structure(
                SchemaTestContext.Field("Values", DataTypeIds.Double, 2)));
            SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString(), Format = UaSchemaFormat.Bsd }, "rank.bsd")
                .ConfigureAwait(false), error: true);
            fixture.Register("Matrix", SchemaTestContext.Structure(
                SchemaTestContext.Field("Unknown", fixture.Id("Missing"))));
            SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString() }, "missing.json")
                .ConfigureAwait(false), error: true);

            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// The stricter schema or transfer budget accepts exactly the serialized length and rejects one less.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublicationEnforcesExactByteBoundaryWithoutPartialFilesAsync(bool schemaLimit)
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Sample", SchemaTestContext.Structure(
                SchemaTestContext.Field("Count", DataTypeIds.Int32)));
            var input = new SchemaGenerateInput { TypeId = root.ToString() };
            JsonObject baseline = SchemaTestContext.Payload(await fixture.Tools.GenerateAsync(input, "baseline.json")
                .ConfigureAwait(false));
            int size = baseline["bytesTransferred"]!.GetValue<int>();
            if (!schemaLimit)
            {
                fixture.Shared.MaxTransferBytes = size;
            }
            SchemaTools exact = schemaLimit ? WithByteLimit(fixture, size) : fixture.Tools;
            SchemaTestContext.Payload(await exact.GenerateAsync(input, "exact.json").ConfigureAwait(false));
            if (!schemaLimit)
            {
                fixture.Shared.MaxTransferBytes = size - 1;
            }
            SchemaTools below = schemaLimit ? WithByteLimit(fixture, size - 1) : fixture.Tools;

            JsonObject refused = SchemaTestContext.Payload(
                await below.GenerateAsync(input, "oversize.json").ConfigureAwait(false), error: true);

            Assert.That(refused["errorType"]!.GetValue<string>(), Is.EqualTo(nameof(IOException)));
            Assert.That(new FileInfo(Path.Combine(fixture.Root, "exact.json")).Length, Is.EqualTo(size));
            Assert.That(Directory.GetFiles(fixture.Root).Select(Path.GetFileName),
                Is.EquivalentTo(s_boundaryArtifacts));
        }

        /// <summary>
        /// Existing artifacts remain byte-identical and root escapes are refused before generation.
        /// </summary>
        [Test]
        public async Task PublicationNeverOverwritesOrEscapesRootAsync()
        {
            await using var fixture = new SchemaTestContext();
            string existing = Path.Combine(fixture.Root, "existing.json");
            await File.WriteAllTextAsync(existing, "original").ConfigureAwait(false);
            var input = new SchemaGenerateInput { TypeId = "i=999999" };

            JsonObject overwrite = SchemaTestContext.Payload(
                await fixture.Tools.GenerateAsync(input, "existing.json").ConfigureAwait(false), error: true);
            JsonObject escape = SchemaTestContext.Payload(
                await fixture.Tools.GenerateAsync(input, "..\\escaped-schema.json").ConfigureAwait(false), error: true);

            Assert.That(overwrite["errorType"]!.GetValue<string>(), Is.EqualTo(nameof(IOException)));
            Assert.That(escape["errorType"]!.GetValue<string>(), Is.EqualTo(nameof(ArgumentException)));
            Assert.That(await File.ReadAllTextAsync(existing).ConfigureAwait(false), Is.EqualTo("original"));
            Assert.That(Directory.GetFiles(fixture.Root).Select(Path.GetFileName),
                Is.EqualTo(s_existingArtifact));
        }

        /// <summary>
        /// Caller cancellation propagates and cannot create a local artifact.
        /// </summary>
        [Test]
        public async Task CancelledGenerationLeavesNoArtifactAsync()
        {
            await using var fixture = new SchemaTestContext();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            Assert.ThrowsAsync<OperationCanceledException>(async () => await fixture.Tools.GenerateOpenApiAsync(
                new SchemaOpenApiInput(), "cancelled.json", cancellation.Token).ConfigureAwait(false));

            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// Cancellation raised by a synchronous SDK serializer is observed before atomic publication.
        /// </summary>
        [Test]
        public async Task CancellationDuringSerializationPublishesNothingAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Sample", SchemaTestContext.Structure());
            using var cancellation = new CancellationTokenSource();
            bool serialized = false;
            var generator = new ControlledGenerator(stream =>
            {
                stream.Write("{}"u8);
                serialized = true;
                cancellation.Cancel();
            });
            SchemaTools tools = WithGenerator(fixture, generator);

            Assert.ThrowsAsync<OperationCanceledException>(async () => await tools.GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString() }, "cancelled.json", ct: cancellation.Token)
                .ConfigureAwait(false));

            Assert.That(serialized, Is.True);
            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// A tightened transfer budget fails through the real atomic writer and cleans its staging file.
        /// </summary>
        [Test]
        public async Task PublicationFailureRemovesStagingArtifactAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Sample", SchemaTestContext.Structure());
            var generator = new ControlledGenerator(stream =>
            {
                stream.Write("{}"u8);
                fixture.Shared.MaxTransferBytes = 1;
            });

            JsonObject result = SchemaTestContext.Payload(await WithGenerator(fixture, generator).GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString() }, "incomplete.json").ConfigureAwait(false),
                error: true);

            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo(nameof(IOException)));
            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// The shared transfer ceiling bounds memory writes themselves, not only the later filesystem copy.
        /// </summary>
        [Test]
        public async Task TransferCeilingStopsSerializerBeforeOverflowWriteAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Sample", SchemaTestContext.Structure());
            fixture.Shared.MaxTransferBytes = 4;
            bool reachedLimit = false;
            bool acceptedOverflow = false;
            var generator = new ControlledGenerator(stream =>
            {
                stream.Write("1234"u8);
                reachedLimit = true;
                stream.WriteByte(0);
                acceptedOverflow = true;
            });

            JsonObject result = SchemaTestContext.Payload(await WithGenerator(fixture, generator).GenerateAsync(
                new SchemaGenerateInput { TypeId = root.ToString() }, "overflow.json").ConfigureAwait(false),
                error: true);

            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo(nameof(IOException)));
            Assert.That(reachedLimit, Is.True);
            Assert.That(acceptedOverflow, Is.False);
            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// Both REST service sets use the existing generator without requiring a UA session.
        /// </summary>
        [TestCase(WebApiServiceSet.AllServices, false)]
        [TestCase(WebApiServiceSet.Sessionless, false)]
        [TestCase(WebApiServiceSet.Sessionless, true)]
        public async Task OpenApiUsesSdkRoutesAndOptionalComponentsAsync(WebApiServiceSet serviceSet, bool components)
        {
            await using var fixture = new SchemaTestContext();
            JsonObject result = SchemaTestContext.Payload(await fixture.Tools.GenerateOpenApiAsync(
                new SchemaOpenApiInput
                {
                    ServiceSet = serviceSet,
                    IncludeSchemas = components,
                    ServerUrl = "https://documentation.invalid/ua"
                }, "openapi.json").ConfigureAwait(false));
            JsonObject document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "openapi.json"))
                .ConfigureAwait(false))!.AsObject();

            Assert.That(document["openapi"]!.GetValue<string>(), Is.EqualTo("3.0.4"));
            Assert.That(document["servers"]![0]!["url"]!.GetValue<string>(),
                Is.EqualTo("https://documentation.invalid/ua"));
            Assert.That(document["paths"]!.AsObject().Select(path => path.Key),
                Is.EquivalentTo(WebApiServiceRoutes.GetRoutes(serviceSet).ToList().Select(route => route.Path)));
            Assert.That(document.ContainsKey("components"), Is.EqualTo(components));
            Assert.That(result["routeCount"]!.GetValue<int>(), Is.EqualTo(document["paths"]!.AsObject().Count));
            if (components)
            {
                Assert.That(document["components"]!["schemas"]!.AsObject().ContainsKey("ReadRequest"), Is.True);
                Assert.That(document["paths"]!["/read"]!["post"]!["requestBody"]!["content"]!
                    .AsObject().Single().Value!["schema"]!["$ref"]!.GetValue<string>(),
                    Is.EqualTo("#/components/schemas/ReadRequest"));
            }
        }

        /// <summary>
        /// Every PubSub envelope is generated by its existing SDK provider with explicit masks.
        /// </summary>
        [TestCase(SchemaPubSubKind.Payload)]
        [TestCase(SchemaPubSubKind.DataSetMessage)]
        [TestCase(SchemaPubSubKind.NetworkMessage)]
        [TestCase(SchemaPubSubKind.MetaData)]
        public async Task PubSubSdkEnvelopesPreserveExplicitMasksAsync(SchemaPubSubKind kind)
        {
            await using var fixture = new SchemaTestContext();
            var input = new SchemaPubSubInput
            {
                Kind = kind,
                Name = "Measurements",
                Fields = [new SchemaPubSubField { Name = "Temperature", DataTypeId = "i=11" }],
                FieldContentMask = kind == SchemaPubSubKind.MetaData ? 0 :
                    (uint)(DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp),
                MessageContentMask = kind is SchemaPubSubKind.DataSetMessage or SchemaPubSubKind.NetworkMessage
                    ? (uint)(JsonDataSetMessageContentMask.DataSetWriterId |
                        JsonDataSetMessageContentMask.SequenceNumber) : 0,
                NetworkContentMask = kind == SchemaPubSubKind.NetworkMessage
                    ? (uint)JsonNetworkMessageContentMask.PublisherId : 0
            };
            SchemaTestContext.Payload(await fixture.Tools.GeneratePubSubAsync(input, "pubsub.json")
                .ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "pubsub.json"))
                .ConfigureAwait(false))!;
            JsonNode payload = document;
            if (kind == SchemaPubSubKind.NetworkMessage)
            {
                Assert.That(document["properties"]!.AsObject().ContainsKey("PublisherId"), Is.True);
                Assert.That(document["properties"]!.AsObject().ContainsKey("WriterGroupName"), Is.False);
                payload = document["$defs"]!["DataSetMessage"]!;
            }
            if (kind is SchemaPubSubKind.DataSetMessage or SchemaPubSubKind.NetworkMessage)
            {
                Assert.That(payload["properties"]!.AsObject().ContainsKey("DataSetWriterId"), Is.True);
                Assert.That(payload["properties"]!.AsObject().ContainsKey("SequenceNumber"), Is.True);
                Assert.That(payload["properties"]!.AsObject().ContainsKey("Timestamp"), Is.False);
                payload = payload["properties"]!["Payload"]!;
            }
            if (kind == SchemaPubSubKind.MetaData)
            {
                Assert.That(document["properties"]!["MessageType"]!["const"]!.GetValue<string>(),
                    Is.EqualTo("ua-metadata"));
                Assert.That(document["properties"]!["MetaData"]!["additionalProperties"]!.GetValue<bool>(), Is.True);
            }
            else
            {
                JsonObject properties = payload["properties"]!["Temperature"]!["properties"]!.AsObject();
                Assert.That(properties.ContainsKey("Value"), Is.True);
                Assert.That(properties.ContainsKey("StatusCode"), Is.True);
                Assert.That(properties.ContainsKey("SourceTimestamp"), Is.True);
                Assert.That(properties.ContainsKey("ServerTimestamp"), Is.False);
            }
        }

        /// <summary>
        /// Unknown, contradictory and unused PubSub bits are errors, not silently ignored settings.
        /// </summary>
        [TestCase(0x80000000u, 0u, 0u)]
        [TestCase((uint)(DataSetFieldContentMask.RawData | DataSetFieldContentMask.StatusCode), 0u, 0u)]
        [TestCase(0u, 1u, 0u)]
        [TestCase(0u, 0u, 1u)]
        public async Task PubSubInvalidMasksAreRefusedAsync(uint fields, uint messages, uint networks)
        {
            await using var fixture = new SchemaTestContext();
            var input = new SchemaPubSubInput
            {
                Kind = SchemaPubSubKind.Payload,
                FieldContentMask = fields,
                MessageContentMask = messages,
                NetworkContentMask = networks
            };

            SchemaTestContext.Payload(await fixture.Tools.GeneratePubSubAsync(input, "invalid.json")
                .ConfigureAwait(false), error: true);

            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// Complex PubSub fields use actual registered definitions rather than a permissive empty schema.
        /// </summary>
        [Test]
        public async Task PubSubCustomFieldsUseBoundedDefinitionClosureAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId root = fixture.Register("Sample", SchemaTestContext.Structure(
                SchemaTestContext.Field("Count", DataTypeIds.Int32)));
            var input = new SchemaPubSubInput
            {
                Fields = [new SchemaPubSubField { Name = "Record", DataTypeId = root.ToString() }],
                FieldContentMask = (uint)DataSetFieldContentMask.RawData,
                MessageContentMask = 0,
                NetworkContentMask = 0
            };

            SchemaTestContext.Payload(await fixture.Tools.GeneratePubSubAsync(input, "complex.json")
                .ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "complex.json"))
                .ConfigureAwait(false))!;

            Assert.That(document["properties"]!["Record"]!["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/Sample"));
            Assert.That(document["$defs"]!["Sample"]!["$defs"]!["Sample"]!["properties"]!["Count"]!["type"]!
                .GetValue<string>(), Is.EqualTo("integer"));
            input.Fields = [new SchemaPubSubField { Name = "Record", DataTypeId = fixture.Id("Missing").ToString() }];
            JsonObject missing = SchemaTestContext.Payload(
                await fixture.Tools.GeneratePubSubAsync(input, "missing.json").ConfigureAwait(false), error: true);
            Assert.That(missing["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadDataTypeIdUnknown));
            Assert.That(File.Exists(Path.Combine(fixture.Root, "missing.json")), Is.False);
        }

        /// <summary>
        /// A genuinely empty DataSet has an empty object schema, not an invented placeholder field.
        /// </summary>
        [Test]
        public async Task EmptyPubSubPayloadHasNoInventedFieldsAsync()
        {
            await using var fixture = new SchemaTestContext();
            SchemaTestContext.Payload(await fixture.Tools.GeneratePubSubAsync(new SchemaPubSubInput
            {
                FieldContentMask = 0,
                MessageContentMask = 0,
                NetworkContentMask = 0
            }, "empty.json").ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "empty.json"))
                .ConfigureAwait(false))!;

            Assert.That(document["type"]!.GetValue<string>(), Is.EqualTo("object"));
            Assert.That(document["properties"]!.AsObject(), Is.Empty);
            Assert.That(document["additionalProperties"]!.GetValue<bool>(), Is.False);
        }

        /// <summary>
        /// Reuses the real bounded publication path with an independently lowered schema limit.
        /// </summary>
        private static SchemaTools WithByteLimit(SchemaTestContext fixture, int limit)
        {
            return new SchemaTools(fixture.Services.GetRequiredService<OpcUaSessionManager>(),
                fixture.Services.GetRequiredService<McpFileTransfers>(), fixture.Shared,
                new McpSchemaOptions { MaxSchemaBytes = limit }, fixture.Context, fixture.Registry);
        }

        /// <summary>
        /// Injects only a controlled serializer while retaining all real preflight, definition and transfer behavior.
        /// </summary>
        private static SchemaTools WithGenerator(SchemaTestContext fixture, IUaSchemaGenerator generator)
        {
            return new SchemaTools(fixture.Services.GetRequiredService<OpcUaSessionManager>(),
                fixture.Services.GetRequiredService<McpFileTransfers>(), fixture.Shared,
                fixture.Options, fixture.Context, fixture.Registry, [generator]);
        }

        /// <summary>
        /// Uses a real isolated SDK factory rather than the fixture's manually populated definition registry.
        /// </summary>
        private static SchemaTools WithContext(SchemaTestContext fixture, IServiceMessageContext context)
        {
            return new SchemaTools(fixture.Services.GetRequiredService<OpcUaSessionManager>(),
                fixture.Services.GetRequiredService<McpFileTransfers>(), fixture.Shared,
                fixture.Options, context);
        }

        /// <summary>The root-only JSON definition set.</summary>
        private static readonly string[] s_rootOnly = ["Root"];

        /// <summary>The complete bounded test namespace.</summary>
        private static readonly string[] s_namespaceTypes = ["Root", "Sibling"];

        /// <summary>The SDK JSON representations of ordinary and special floating-point values.</summary>
        private static readonly string[] s_numberRepresentations = ["number", "string"];

        /// <summary>The structure-backed OptionSet wire members.</summary>
        private static readonly string[] s_optionSetProperties = ["Value", "ValidBits"];

        /// <summary>The only files that can survive the byte-boundary checks.</summary>
        private static readonly string[] s_boundaryArtifacts = ["baseline.json", "exact.json"];

        /// <summary>The pre-existing artifact that must remain untouched.</summary>
        private static readonly string[] s_existingArtifact = ["existing.json"];

        /// <summary>
        /// A deterministic generator that exposes cancellation and file-publication failure seams.
        /// </summary>
        private sealed class ControlledGenerator : IUaSchemaGenerator
        {
            /// <summary>
            /// Captures one synchronous in-memory serializer action.
            /// </summary>
            internal ControlledGenerator(Action<Stream> serialize)
            {
                m_serialize = serialize;
            }

            /// <inheritdoc/>
            public bool CanGenerate(UaSchemaFormat format)
            {
                return format == UaSchemaFormat.JsonCompact;
            }

            /// <inheritdoc/>
            public IUaSchema Generate(
                UaTypeDescription type, IDataTypeDefinitionResolver resolver,
                UaSchemaFormat format, UaSchemaScope scope)
            {
                return new ControlledSchema(m_serialize);
            }

            /// <summary>The serializer action executed only after real bounded preflight.</summary>
            private readonly Action<Stream> m_serialize;
        }

        /// <summary>
        /// A stream-only SDK schema used to observe the actual bounded-memory-to-atomic-file pipeline.
        /// </summary>
        private sealed class ControlledSchema : IUaSchema
        {
            /// <summary>
            /// Captures the test's bounded stream action.
            /// </summary>
            internal ControlledSchema(Action<Stream> serialize)
            {
                m_serialize = serialize;
            }

            /// <inheritdoc/>
            public UaSchemaFormat Format => UaSchemaFormat.JsonCompact;

            /// <inheritdoc/>
            public string MediaType => "application/schema+json";

            /// <inheritdoc/>
            public string TargetNamespace => SchemaTestContext.TypeNamespaceUri;

            /// <inheritdoc/>
            public void WriteTo(Stream stream)
            {
                m_serialize(stream);
            }

            /// <inheritdoc/>
            public void WriteTo(TextWriter writer)
            {
                throw new NotSupportedException("The tool must use the bounded stream serializer.");
            }

            /// <inheritdoc/>
            public string ToSchemaString()
            {
                throw new NotSupportedException("The tool must not allocate an unbounded schema string.");
            }

            /// <summary>The controlled serializer action.</summary>
            private readonly Action<Stream> m_serialize;
        }
    }
}
#endif
