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
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Extrusion.Line;

namespace Opc.Ua.PlasticsRubber.Tests
{
    /// <summary>
    /// Exercises every generated structure with populated binary, JSON and XML payloads.
    /// </summary>
    [TestFixture]
    [Category("PlasticsRubber")]
    public sealed class ModelEncodingTests
    {
        /// <summary>
        /// Gets all thirteen structures crossed with the three standard encodings.
        /// </summary>
        private static IEnumerable<TestCaseData> StructureCases
        {
            get
            {
                string[] names =
                [
                    nameof(ActiveErrorDataType), nameof(ClassifiedActiveErrorDataType),
                    nameof(ConfigurationParameterType), nameof(JobListElementType),
                    nameof(CyclicJobListElementType), nameof(PageEntryDataType),
                    nameof(ParameterSettingType), nameof(PIDParametersDataType),
                    nameof(ProductionDatasetInformationType), nameof(ProductionDatasetReadOptionsType),
                    nameof(ProductionDatasetWriteOptionsType), nameof(StandstillReasonType),
                    nameof(MaterialMappingType)
                ];
                foreach (string name in names)
                {
                    foreach (string format in new[] { "Binary", "Json", "Xml" })
                    {
                        yield return new TestCaseData(name, format);
                    }
                }
            }
        }

        /// <summary>
        /// Round-trips non-default fields through the generated factory registrations.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The encoding format is not supported.</exception>
        [TestCaseSource(nameof(StructureCases))]
        public void PopulatedStructuresRoundTrip(string name, string format)
        {
            IEncodeable value = CreateValue(name);
            ServiceMessageContext context = CompanionModels.CreateMessageContext();
            IEncodeable decoded;
            switch (format)
            {
                case "Binary":
                    using (var stream = new MemoryStream())
                    {
                        using (var encoder = new BinaryEncoder(stream, context, true))
                        {
                            encoder.WriteEncodeable("Value", value, value.TypeId);
                        }
                        stream.Position = 0;
                        using var decoder = new BinaryDecoder(stream, context, true);
                        decoded = decoder.ReadEncodeable<IEncodeable>("Value", value.TypeId);
                        Assert.That(decoder.Position, Is.EqualTo(stream.Length));
                    }
                    break;
                case "Json":
                    string json;
                    using (var encoder = new JsonEncoder(context))
                    {
                        encoder.EncodeMessage(value, value.TypeId);
                        json = encoder.CloseAndReturnText();
                    }
                    using (var decoder = new JsonDecoder(json, context))
                    {
                        decoded = decoder.DecodeMessage<IEncodeable>();
                    }
                    break;
                case "Xml":
                    string xml;
                    using (var encoder = new XmlEncoder(context))
                    {
                        encoder.PushNamespace(kTestNamespace);
                        encoder.WriteEncodeable("Value", value, value.TypeId);
                        encoder.PopNamespace();
                        xml = encoder.CloseAndReturnText()!;
                    }
                    using (var text = new StringReader(xml))
                    using (var reader = XmlReader.Create(text))
                    using (var decoder = new XmlDecoder(reader, context))
                    {
                        decoder.PushNamespace(kTestNamespace);
                        decoded = decoder.ReadEncodeable<IEncodeable>("Value", value.TypeId);
                        decoder.PopNamespace();
                    }
                    using (var parser = new XmlParser(xml, context))
                    {
                        parser.PushNamespace(kTestNamespace);
                        IEncodeable parsed = parser.ReadEncodeable<IEncodeable>("Value", value.TypeId);
                        parser.PopNamespace();
                        Assert.That(value.IsEqual(parsed), Is.True, "Span-based XML decoder");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
            Assert.Multiple(() =>
            {
                Assert.That(decoded.GetType(), Is.EqualTo(value.GetType()));
                Assert.That(decoded.TypeId, Is.EqualTo(value.TypeId));
                Assert.That(value.IsEqual(decoded), Is.True);
            });
        }

        /// <summary>
        /// Pins the new type's standard identifiers and inherited binary field order.
        /// </summary>
        [Test]
        public void ClassifiedActiveErrorUsesStandardIdentifiersAndWireOrder()
        {
            var value = (ClassifiedActiveErrorDataType)CreateValue(nameof(ClassifiedActiveErrorDataType));
            ServiceMessageContext context = CompanionModels.CreateMessageContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Value", value);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            Assert.Multiple(() =>
            {
                Assert.That(value.TypeId, Is.EqualTo(new ExpandedNodeId(3003, Namespaces.PlasticsRubber)));
                Assert.That(value.BinaryEncodingId, Is.EqualTo(new ExpandedNodeId(5065, Namespaces.PlasticsRubber)));
                Assert.That(value.XmlEncodingId, Is.EqualTo(new ExpandedNodeId(5066, Namespaces.PlasticsRubber)));
                Assert.That(decoder.ReadString("Id"), Is.EqualTo(value.Id));
                Assert.That(decoder.ReadUInt16("Severity"), Is.EqualTo(value.Severity));
                Assert.That(decoder.ReadLocalizedText("Message"), Is.EqualTo(value.Message));
                Assert.That(decoder.ReadNodeIdArray("SourceNodes"), Is.EqualTo(value.SourceNodes));
                Assert.That(decoder.ReadUInt16("Classification"), Is.EqualTo(value.Classification));
                Assert.That(decoder.Position, Is.EqualTo(stream.Length));
            });
        }

        /// <summary>
        /// Resolves the authored derived type through its binary ExtensionObject identifier.
        /// </summary>
        [Test]
        public void ClassifiedActiveErrorExtensionObjectPreservesDerivedFields()
        {
            var value = (ClassifiedActiveErrorDataType)CreateValue(nameof(ClassifiedActiveErrorDataType));
            ServiceMessageContext context = CompanionModels.CreateMessageContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteExtensionObject("Error", new ExtensionObject(value));
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            ExtensionObject extension = decoder.ReadExtensionObject("Error");
            Assert.That(extension.TryGetValue(out ClassifiedActiveErrorDataType? decoded), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(decoded!.Id, Is.EqualTo(value.Id));
                Assert.That(decoded.SourceNodes, Is.EqualTo(value.SourceNodes));
                Assert.That(decoded.Classification, Is.EqualTo(value.Classification));
            });
        }

        /// <summary>
        /// Rejects truncated derived payloads rather than silently defaulting the last field.
        /// </summary>
        [Test]
        public void TruncatedClassifiedActiveErrorIsRejected()
        {
            var value = (ClassifiedActiveErrorDataType)CreateValue(nameof(ClassifiedActiveErrorDataType));
            ServiceMessageContext context = CompanionModels.CreateMessageContext();
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, true))
            {
                encoder.WriteEncodeable("Value", value);
            }
            stream.SetLength(stream.Length - 1);
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context, true);
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<ClassifiedActiveErrorDataType>("Value"))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        /// <summary>
        /// Does not resolve standard numeric identifiers from an unrelated namespace.
        /// </summary>
        [Test]
        public void FactoryRejectsAnIdentifierInTheWrongModelNamespace()
        {
            ServiceMessageContext context = CompanionModels.CreateMessageContext();
            Assert.Multiple(() =>
            {
                Assert.That(
                    context.Factory.TryGetEncodeableType(
                        new ExpandedNodeId(3003, "urn:companion-model-tests:unknown"), out _),
                    Is.False);
                Assert.That(
                    context.Factory.TryGetEncodeableType(
                        new ExpandedNodeId(5065, "urn:companion-model-tests:unknown"), out _),
                    Is.False);
            });
        }

        /// <summary>
        /// Supplies deterministic non-default values, including arrays and inherited fields.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The structure name has no test fixture.</exception>
        private static IEncodeable CreateValue(string name)
        {
            return name switch
            {
                nameof(ActiveErrorDataType) => new ActiveErrorDataType
                {
                    Id = "process-overheat",
                    Severity = 500,
                    Message = new LocalizedText("en", "Temperature above setpoint")
                },
                nameof(ClassifiedActiveErrorDataType) => new ClassifiedActiveErrorDataType
                {
                    Id = "material-flow",
                    Severity = 450,
                    Message = new LocalizedText("en", "Material flow interrupted"),
                    SourceNodes = [new NodeId(42, 1), new NodeId("drive", 1)],
                    Classification = 17
                },
                nameof(ConfigurationParameterType) => new ConfigurationParameterType
                {
                    Id = 7,
                    Description = new LocalizedText("en", "Synthetic setpoint"),
                    DefaultValue = new Variant(180.5),
                    Unit = new EUInformation
                    {
                        NamespaceUri = "urn:companion-model-tests:units",
                        UnitId = 1,
                        DisplayName = new LocalizedText("K"),
                        Description = new LocalizedText("Temperature unit")
                    }
                },
                nameof(JobListElementType) => new JobListElementType
                {
                    JobName = "batch-a",
                    JobDescription = "Synthetic validation batch",
                    JobClassification = "validation",
                    CustomerName = "test",
                    ProductionDatasetName = "recipe-a",
                    ProductionDatasetDescription = "Validation settings",
                    Material = ["polymer", "additive"],
                    ProductName = ["pellet"],
                    ProductDescription = ["Synthetic product"],
                    JobPriority = "normal",
                    PlannedStart = s_timestamp,
                    PlannedProductionTime = 120000,
                    LatestEnd = s_timestamp + TimeSpan.FromMinutes(5)
                },
                nameof(CyclicJobListElementType) => new CyclicJobListElementType
                {
                    JobName = "cyclic-batch",
                    Material = ["polymer"],
                    ProductName = ["part"],
                    PlannedStart = s_timestamp,
                    PlannedProductionTime = 300000,
                    NominalParts = 200,
                    NominalBoxParts = 20,
                    ExpectedCycleTime = 750,
                    MouldId = "mould-a",
                    NumCavities = 4
                },
                nameof(PageEntryDataType) => new PageEntryDataType
                {
                    Id = "overview",
                    Title = new LocalizedText("en", "Overview")
                },
                nameof(ParameterSettingType) => new ParameterSettingType
                {
                    Id = 7,
                    Value = new Variant(123.25)
                },
                nameof(PIDParametersDataType) => new PIDParametersDataType
                {
                    P = 1.5,
                    I = 0.3,
                    D = 0.07
                },
                nameof(ProductionDatasetInformationType) => new ProductionDatasetInformationType
                {
                    Name = "recipe-a",
                    Description = "Synthetic validation data",
                    MESId = "validation",
                    CreationTimestamp = s_timestamp,
                    LastModificationTimestamp = s_timestamp + TimeSpan.FromMinutes(1),
                    LastSaveTimestamp = s_timestamp + TimeSpan.FromMinutes(2),
                    UserName = "operator",
                    Components = [1, 3],
                    Manufacturer = "test",
                    SerialNumber = "synthetic-1",
                    Model = "validation",
                    ControllerName = "controller",
                    UserMachineName = "line",
                    LocationName = "test-cell",
                    ProductName = ["pellet"],
                    MouldId = "mould-b",
                    NumCavities = 2
                },
                nameof(ProductionDatasetReadOptionsType) => new ProductionDatasetReadOptionsType
                {
                    Storage = StorageEnumeration.PREPARATION,
                    Name = "recipe-a"
                },
                nameof(ProductionDatasetWriteOptionsType) => new ProductionDatasetWriteOptionsType
                {
                    Storage = StorageEnumeration.PRODUCTION,
                    Name = "recipe-b",
                    Components = [2, 4]
                },
                nameof(StandstillReasonType) => new StandstillReasonType
                {
                    Id = "quality-hold",
                    Text = new LocalizedText("en", "Awaiting review"),
                    LockedByMES = true
                },
                nameof(MaterialMappingType) => new MaterialMappingType
                {
                    MaterialId = "polymer",
                    MaterialLot = "lot-a",
                    HopperId = "hopper-1"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }

        /// <summary>The XML envelope namespace used by the independent encoding tests.</summary>
        private const string kTestNamespace = "urn:companion-model-tests:encoding";

        /// <summary>The deterministic source time for dataset and job fields.</summary>
        private static readonly DateTimeUtc s_timestamp = new(new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc));
    }
}
