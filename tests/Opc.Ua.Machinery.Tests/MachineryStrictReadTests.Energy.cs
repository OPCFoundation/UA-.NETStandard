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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Client;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryStrictReadTests
    {
        /// <summary>
        /// ApplicationTag is mandatory for strict meters without changing the tolerant legacy reader.
        /// </summary>
        [Test]
        public async Task StrictMeterRequiresApplicationTagButLegacyStillToleratesAbsence()
        {
            var harness = new ReadHarness();
            MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadMeteringPointAsync(harness.Machine).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(error.ReadValue!.BrowsePath[0].Name, Is.EqualTo(ECM.BrowseNames.ApplicationTag));
            Assert.That(error.ReadValue.IsAbsent, Is.True);

            MachineryMeteringPoint? legacy = await harness.Client(strict: false)
                .ReadMeteringPointAsync(harness.Machine).ConfigureAwait(false);
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy!.ApplicationTag, Is.Null);
        }

        /// <summary>
        /// StartTime is retained as metadata rather than coerced into a numeric measurement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StrictMeterSeparatesStartTimeMetadata(bool wrongType)
        {
            ReadHarness harness = CreateMeterHarness();
            ReferenceDescription start = Reference(harness, 1, NodeClass.Variable);
            start.BrowseName = harness.Name(IA.BrowseNames.StartTime, IA.Namespaces.IA);
            start.TypeDefinition = VariableTypeIds.PropertyType;
            NodeId startId = ExpandedNodeId.ToNodeId(start.NodeId, harness.Namespaces);
            DataValue original = TimedValue(wrongType
                ? Variant.From("not a timestamp")
                : Variant.From(new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc)), StatusCodes.Good);
            harness.Values[startId] = original;
            harness.Declare(startId, Ua.DataTypeIds.DateTime);
            harness.Browse = _ => Page([start]);

            if (wrongType)
            {
                MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(
                    async () => await harness.Client().ReadMeteringPointAsync(harness.Machine)
                        .ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(error.ReadValue!.Value, Is.EqualTo(original));
            }
            else
            {
                MachineryMeteringPoint? result = await harness.Client().ReadMeteringPointAsync(harness.Machine)
                    .ConfigureAwait(false);
                Assert.That(result!.Measurements.Count, Is.Zero);
                Assert.That(result.Values.Count, Is.EqualTo(2));
                Assert.That(Field(result.Values, start.BrowseName).Value, Is.EqualTo(original));
            }
        }

        /// <summary>
        /// Both ECM three-phase structures are valid measurements, including their binary wire form.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task StrictMeterAcceptsModeledThreePhaseStructures(bool phaseToPhase, bool binary)
        {
            ReadHarness harness = CreateMeterHarness();
            IEncodeable structure = phaseToPhase
                ? new ECM.AcPpDataType { L1L2 = 1, L2L3 = 2, L3L1 = 3 }
                : new ECM.AcPeDataType { L1 = 1, L2 = 2, L3 = 3 };
            Variant value = binary
                ? Variant.From(BinaryStructure(harness, structure))
                : Variant.FromStructure(structure);
            ReferenceDescription measurement = Reference(harness, 1, NodeClass.Variable);
            measurement.BrowseName = harness.Name(
                phaseToPhase ? ECM.BrowseNames.AcVoltagePp : ECM.BrowseNames.AcCurrentPe, ECM.Namespaces.ECM);
            NodeId source = ExpandedNodeId.ToNodeId(measurement.NodeId, harness.Namespaces);
            DataValue original = TimedValue(value, StatusCodes.Good);
            harness.Values[source] = original;
            harness.Declare(source, ExpandedNodeId.ToNodeId(structure.TypeId, harness.Namespaces));
            harness.Browse = _ => Page([measurement]);

            MachineryMeteringPoint? result = await harness.Client().ReadMeteringPointAsync(harness.Machine)
                .ConfigureAwait(false);

            Assert.That(result!.Measurements.Count, Is.EqualTo(1));
            Assert.That(result.Measurements[0].Value, Is.EqualTo(value));
            Assert.That(result.Measurements[0].NodeId, Is.EqualTo(source));
            Assert.That(result.Measurements[0].Values[0].Value, Is.EqualTo(original));
        }

        /// <summary>
        /// Declared measurement data types and ranks reject incompatible values without losing source evidence.
        /// </summary>
        [TestCase("WrongStructure", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("WrongRank", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("MalformedBinary", nameof(StatusCodes.BadDecodingError))]
        [TestCase("DeniedDeclaration", nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase("MalformedDeclaration", nameof(StatusCodes.BadDecodingError))]
        public void StrictMeterValidatesMeasurementDeclarationsAndBodies(string kind, string status)
        {
            ReadHarness harness = CreateMeterHarness();
            var structure = new ECM.AcPeDataType { L1 = 1, L2 = 2, L3 = 3 };
            Variant value = kind switch
            {
                "WrongStructure" => Variant.FromStructure(new ECM.AcPpDataType()),
                "WrongRank" => Variant.FromStructure((ArrayOf<ECM.AcPeDataType>)[structure]),
                "MalformedBinary" => Variant.From(new ExtensionObject(structure.BinaryEncodingId, Token(1))),
                _ => Variant.FromStructure(structure)
            };
            ReferenceDescription measurement = Reference(harness, 1, NodeClass.Variable);
            NodeId source = ExpandedNodeId.ToNodeId(measurement.NodeId, harness.Namespaces);
            DataValue original = TimedValue(value, StatusCodes.Good);
            harness.Values[source] = original;
            harness.Declare(source, new NodeId(ECM.DataTypes.AcPeDataType, harness.Index(ECM.Namespaces.ECM)));
            if (kind == "DeniedDeclaration")
            {
                harness.AttributeValues[(source, Attributes.DataType)] =
                    DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied);
            }
            else if (kind == "MalformedDeclaration")
            {
                harness.AttributeValues[(source, Attributes.DataType)] = new DataValue(Variant.From("wrong"));
            }
            harness.Browse = _ => Page([measurement]);

            MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadMeteringPointAsync(harness.Machine).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(TestStatus(status)));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(error.ReadValue.Value, Is.EqualTo(original));
        }

        /// <summary>
        /// A modeled numeric array is accepted but cannot bypass the decoded-array budget.
        /// </summary>
        [TestCase(2)]
        [TestCase(3)]
        public async Task StrictMeterBoundsModeledMeasurementArrays(int count)
        {
            ReadHarness harness = CreateMeterHarness();
            ReferenceDescription measurement = Reference(harness, 1, NodeClass.Variable);
            NodeId source = ExpandedNodeId.ToNodeId(measurement.NodeId, harness.Namespaces);
            Variant value = Variant.From(new float[count].ToArrayOf());
            harness.Values[source] = new DataValue(value);
            harness.Declare(source, Ua.DataTypeIds.Float, ValueRanks.OneDimension);
            harness.Browse = _ => Page([measurement]);
            MachineryClient client = harness.Client(new MachineryClientOptions { MaximumReadArrayItems = 2 });
            if (count > 2)
            {
                MachineryReadException error = Assert.ThrowsAsync<MachineryReadException>(
                    async () => await client.ReadMeteringPointAsync(harness.Machine).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            else
            {
                MachineryMeteringPoint? result = await client.ReadMeteringPointAsync(harness.Machine)
                    .ConfigureAwait(false);
                Assert.That(result!.Measurements[0].Value, Is.EqualTo(value));
            }
        }

        /// <summary>
        /// Typed metadata properties remain available as evidence but are not mislabeled as measurements.
        /// </summary>
        [Test]
        public async Task StrictMeterRetainsMetadataPropertiesOutsideTheMeasurementList()
        {
            ReadHarness harness = CreateMeterHarness();
            ReferenceDescription metadata = Reference(harness, 1, NodeClass.Variable);
            metadata.TypeDefinition = VariableTypeIds.PropertyType;
            NodeId source = ExpandedNodeId.ToNodeId(metadata.NodeId, harness.Namespaces);
            DataValue original = TimedValue(Variant.From("meter-serial"), StatusCodes.Good);
            harness.Values[source] = original;
            harness.Declare(source, Ua.DataTypeIds.String);
            harness.Browse = _ => Page([metadata]);

            MachineryMeteringPoint? result = await harness.Client().ReadMeteringPointAsync(harness.Machine)
                .ConfigureAwait(false);

            Assert.That(result!.Measurements.Count, Is.Zero);
            Assert.That(Field(result.Values, metadata.BrowseName).Value, Is.EqualTo(original));
        }

        private static ReadHarness CreateMeterHarness()
        {
            var harness = new ReadHarness();
            harness.Add(harness.Machine, [harness.Name(ECM.BrowseNames.ApplicationTag, ECM.Namespaces.ECM)],
                new DataValue(Variant.From("Main")));
            return harness;
        }
    }
}
