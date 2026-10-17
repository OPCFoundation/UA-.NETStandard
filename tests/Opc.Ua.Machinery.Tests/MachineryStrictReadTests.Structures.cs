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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Result;
using JobNames = Opc.Ua.Machinery.Jobs.BrowseNames;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryStrictReadTests
    {
        /// <summary>
        /// Job-list evidence distinguishes a present list, Good-null, typed-null, empty and absent.
        /// </summary>
        [TestCase("Present")]
        [TestCase("Binary")]
        [TestCase("GoodNull")]
        [TestCase("NullArray")]
        [TestCase("Empty")]
        [TestCase("Absent")]
        public async Task StrictJobOrderSnapshotPreservesArrayShapeAndEvidence(string kind)
        {
            var harness = new ReadHarness();
            Variant value = kind switch
            {
                "GoodNull" => Variant.Null,
                "NullArray" => Variant.From(default(ArrayOf<ExtensionObject>)),
                "Empty" => Variant.FromStructure(ArrayOf<V2.ISA95JobOrderAndStateDataType>.Empty),
                "Binary" => Variant.From([BinaryStructure(harness, Order("J-1"))]),
                _ => Variant.FromStructure([Order("J-1")])
            };
            DataValue original = TimedValue(value, StatusCodes.Good);
            NodeId source = kind == "Absent" ? NodeId.Null : AddJobList(harness, original);
            MachineryJobOrdersSnapshot snapshot = await harness.Client().ReadJobOrdersSnapshotAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(snapshot.JobOrders.Count, Is.EqualTo(kind is "Present" or "Binary" ? 1 : 0));
            Assert.That(snapshot.Values.Count, Is.EqualTo(1));
            Assert.That(snapshot.Values[0].SourceNodeId, Is.EqualTo(source));
            Assert.That(snapshot.Values[0].IsAbsent, Is.EqualTo(kind == "Absent"));
            if (kind == "Absent")
            {
                Assert.That(snapshot.Values[0].Value.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            }
            else
            {
                Assert.That(snapshot.Values[0].Value, Is.EqualTo(original));
                Assert.That(snapshot.JobOrders.IsNull, Is.EqualTo(kind is "GoodNull" or "NullArray"));
            }
            if (snapshot.JobOrders.Count != 0)
            {
                Assert.That(snapshot.JobOrders[0].JobOrder.JobOrderID, Is.EqualTo("J-1"));
            }
        }

        /// <summary>
        /// The structured array limit is checked before allocating a decoded element array.
        /// </summary>
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task StrictJobArrayHasZeroAtAndOverLimitBoundaries(int count)
        {
            var harness = new ReadHarness();
            var orders = new V2.ISA95JobOrderAndStateDataType[count];
            for (int ii = 0; ii < count; ii++)
            {
                orders[ii] = Order($"J-{ii}");
            }
            AddJobList(harness, new DataValue(Variant.FromStructure(orders.ToArrayOf())));
            MachineryClient client = harness.Client(new MachineryClientOptions { MaximumReadArrayItems = 2 });
            if (count > 2)
            {
                Assert.That(
                    Assert.ThrowsAsync<MachineryReadException>(
                        async () => await client.ReadJobOrdersAsync(harness.Machine).ConfigureAwait(false))!.StatusCode,
                    Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            else
            {
                Assert.That((await client.ReadJobOrdersAsync(harness.Machine).ConfigureAwait(false)).Count,
                    Is.EqualTo(count));
            }
        }

        /// <summary>
        /// Wrong structures, duplicate identifiers and exact UInt32 mistakes fail rather than becoming empty lists.
        /// </summary>
        [TestCase("Scalar", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("WrongElement", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("NullElement", nameof(StatusCodes.BadDecodingError))]
        [TestCase("MissingId", nameof(StatusCodes.BadDecodingError))]
        [TestCase("DuplicateId", nameof(StatusCodes.BadDecodingError))]
        [TestCase("WrongUInt32", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("NullParameter", nameof(StatusCodes.BadDecodingError))]
        [TestCase("DuplicateParameter", nameof(StatusCodes.BadDecodingError))]
        public void StrictJobSnapshotRejectsMalformedStructures(string kind, string statusName)
        {
            StatusCode expectedStatus = TestStatus(statusName);
            var harness = new ReadHarness();
            V2.ISA95JobOrderAndStateDataType order = Order(kind == "MissingId" ? string.Empty : "J-1");
            if (kind == "WrongUInt32")
            {
                order.JobOrder.JobOrderParameters =
                    [new V2.ISA95ParameterDataType { ID = "RunsPlanned", Value = Variant.From(new StatusCode(5)) }];
            }
            if (kind == "NullParameter")
            {
                order.JobOrder.JobOrderParameters = [null!];
            }
            if (kind == "DuplicateParameter")
            {
                order.JobOrder.JobOrderParameters =
                [
                    new V2.ISA95ParameterDataType { ID = "RunsPlanned", Value = Variant.From(5u) },
                    new V2.ISA95ParameterDataType { ID = "RunsPlanned", Value = Variant.From(6u) }
                ];
            }
            Variant value = kind switch
            {
                "Scalar" => Variant.FromStructure(order),
                "WrongElement" => Variant.FromStructure([new EUInformation()]),
                "NullElement" => Variant.From([ExtensionObject.Null]),
                "DuplicateId" => Variant.FromStructure([order, Order("J-1")]),
                _ => Variant.FromStructure([order])
            };
            DataValue original = TimedValue(value, StatusCodes.Good);
            NodeId source = AddJobList(harness, original);
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadJobOrdersSnapshotAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(error.ReadValue.Value, Is.EqualTo(original));
        }

        /// <summary>
        /// Nested arrays and cyclic in-memory structures cannot evade the aggregate bound.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StrictJobSnapshotBoundsNestedArraysAndCycles(bool cycle)
        {
            var harness = new ReadHarness();
            V2.ISA95JobOrderAndStateDataType order = Order("J-1");
            var parameter = new V2.ISA95ParameterDataType
            {
                ID = "JobName",
                Value = Variant.From((ArrayOf<LocalizedText>)[new("one"), new("two")])
            };
            if (cycle)
            {
                parameter.Value = Variant.Null;
                parameter.Subparameters = [parameter];
            }
            order.JobOrder.JobOrderParameters = [parameter];
            NodeId source = AddJobList(harness,
                new DataValue(Variant.FromStructure([order])));
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(async () =>
                await harness.Client(new MachineryClientOptions { MaximumReadArrayItems = 3 })
                    .ReadJobOrdersSnapshotAsync(harness.Machine)
                    .ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
        }

        /// <summary>
        /// Job response convenience projections use the evidenced list in strict mode, not a method call.
        /// </summary>
        [Test]
        public async Task StrictJobResponseParametersRetainListEvidenceWithoutCallingMethods()
        {
            var harness = new ReadHarness();
            var response = new V2.ISA95JobResponseDataType
            {
                JobResponseID = "R-1",
                JobOrderID = "J-1",
                JobResponseData =
                [
                    new V2.ISA95ParameterDataType { ID = "RunsCompleted", Value = Variant.From(4u) }
                ]
            };
            DataValue original = TimedValue(
                Variant.FromStructure([response]), StatusCodes.Good);
            NodeId source = AddJobList(harness, original, responses: true);
            MachineryClient client = harness.Client();
            MachineryJobResponsesSnapshot snapshot = await client.ReadJobResponsesSnapshotAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(snapshot.JobResponses[0].JobResponseID, Is.EqualTo("R-1"));
            MachineryJobResponseParameters? parameters =
                await client.ReadJobResponseParametersAsync(harness.Machine, "J-1").ConfigureAwait(false);
            Assert.That(parameters!.RunsCompleted, Is.EqualTo(4u));
            Assert.That(parameters.Values[0].SourceNodeId, Is.EqualTo(source));
            Assert.That(parameters.Values[0].Value, Is.EqualTo(original));
            Assert.That(harness.Session.Invocations.Any(i => i.Method.Name == "CallAsync"), Is.False);
        }

        /// <summary>
        /// Result snapshots retain present Good-null values instead of silently dropping them.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StrictSelectedResultReadRetainsOriginalEvidence(bool goodNull)
        {
            var harness = new ReadHarness();
            Variant value = goodNull ? Variant.Null : Variant.FromStructure(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType
                {
                    ResultId = "R-1",
                    ResultUri = ["https://not-contacted.invalid/result"]
                },
                ResultContent = [Variant.From(12.0)]
            });
            DataValue original = TimedValue(value, StatusCodes.Good);
            harness.Values[harness.Machine] = original;
            MachineryResultSnapshot snapshot = await harness.Client().ReadPublishedResultAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(snapshot.Result?.ResultMetaData.ResultId, Is.EqualTo(goodNull ? null : "R-1"));
            Assert.That(snapshot.Values[0].SourceNodeId, Is.EqualTo(harness.Machine));
            Assert.That(snapshot.Values[0].Value, Is.EqualTo(original));
            Assert.That(snapshot.Values[0].IsAbsent, Is.False);
            Assert.That(harness.ReadTimestamps, Has.Count.EqualTo(1));
            Assert.That(harness.BrowseLimits, Is.Empty);
            Assert.That(harness.Session.Invocations.Any(i => i.Method.Name == "CallAsync"), Is.False);
        }

        /// <summary>
        /// Result content and metadata arrays are bounded, and malformed typed results fail explicitly.
        /// </summary>
        [TestCase("WrongType", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("MissingId", nameof(StatusCodes.BadDecodingError))]
        [TestCase("ContentLimit", nameof(StatusCodes.BadEncodingLimitsExceeded))]
        [TestCase("UriLimit", nameof(StatusCodes.BadEncodingLimitsExceeded))]
        [TestCase("NestedArrayLimit", nameof(StatusCodes.BadEncodingLimitsExceeded))]
        public void StrictResultSnapshotRejectsMalformedOrOversizedValues(string kind, string statusName)
        {
            StatusCode status = TestStatus(statusName);
            var harness = new ReadHarness();
            var result = new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = kind == "MissingId" ? string.Empty : "R-1" },
                ResultContent = kind == "ContentLimit"
                    ? [Variant.From(1), Variant.From(2), Variant.From(3)]
                    : []
            };
            if (kind == "UriLimit")
            {
                result.ResultMetaData.ResultUri = ["one", "two", "three"];
            }
            if (kind == "NestedArrayLimit")
            {
                result.ResultContent = [Variant.From([1.0, 2.0])];
            }
            DataValue original = new(
                kind == "WrongType" ? Variant.From("not a result") : Variant.FromStructure(result));
            harness.Values[harness.Machine] = original;
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(async () =>
                await harness.Client(new MachineryClientOptions { MaximumReadArrayItems = 2 })
                    .ReadPublishedResultAsync(harness.Machine)
                    .ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(status));
            Assert.That(error.ReadValue!.Value, Is.EqualTo(original));
        }

        /// <summary>
        /// Stopping after one result snapshot does not read the remaining variables.
        /// </summary>
        [Test]
        public async Task PublishedResultSnapshotsReadOnlyTheConsumedVariables()
        {
            var harness = new ReadHarness();
            QualifiedName management = harness.Name(
                Result.BrowseNames.ResultManagement, Result.Namespaces.MachineryResult);
            QualifiedName results = harness.Name(
                Result.BrowseNames.Results, Result.Namespaces.MachineryResult);
            NodeId folder = harness.Add(harness.Machine, [management, results]);
            ReferenceDescription first = Reference(harness, 1, NodeClass.Variable);
            ReferenceDescription second = Reference(harness, 2, NodeClass.Variable);
            var firstNode = ExpandedNodeId.ToNodeId(first.NodeId, harness.Namespaces);
            harness.Values[firstNode] = new DataValue(Variant.FromStructure(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "R-1" }
            }));
            harness.Browse = _ => Page([first, second]);
            IAsyncEnumerable<MachineryResultSnapshot> stream = harness.Client()
                .ReadPublishedResultSnapshotsAsync(harness.Machine);
            Assert.That(harness.ReadTimestamps, Is.Empty);
            var found = new List<MachineryResultSnapshot>();
            await foreach (MachineryResultSnapshot snapshot in stream)
            {
                found.Add(snapshot);
                break;
            }
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Result!.ResultMetaData.ResultId, Is.EqualTo("R-1"));
            Assert.That(found[0].Values[0].SourceNodeId, Is.EqualTo(firstNode));
            Assert.That(found[0].Values[0].StartingNodeId, Is.EqualTo(folder));
            Assert.That(found[0].Values[0].BrowsePath.ToArray(), Is.EqualTo([first.BrowseName]));
            Assert.That(harness.ReadTimestamps, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A malformed known-type body keeps the original binary value and source evidence.
        /// </summary>
        [Test]
        public void MalformedBinaryResultRetainsSourceAndReportsDecodingFailure()
        {
            var harness = new ReadHarness();
            var encoded = new ExtensionObject(
                new ResultDataType().BinaryEncodingId, Token(1));
            DataValue original = TimedValue(Variant.From(encoded), StatusCodes.Good);
            harness.Values[harness.Machine] = original;
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadPublishedResultAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(harness.Machine));
            Assert.That(error.ReadValue.Value, Is.EqualTo(original));
        }

        /// <summary>
        /// Strict job response projections decode binary parameter arrays without losing source evidence.
        /// </summary>
        [Test]
        public async Task StrictJobResponseProjectsBinaryParameterStructures()
        {
            var harness = new ReadHarness();
            var bom = new Jobs.BOMInformationDataType
            {
                Identification = new Jobs.OutputInformationDataType { ItemNumber = "Part-1" }
            };
            var response = new V2.ISA95JobResponseDataType
            {
                JobResponseID = "R-1",
                JobOrderID = "J-1",
                JobResponseData =
                [
                    new V2.ISA95ParameterDataType
                    {
                        ID = "AsBuiltBOM",
                        Value = Variant.From([BinaryStructure(harness, bom)])
                    }
                ]
            };
            NodeId source = AddJobList(harness,
                new DataValue(Variant.FromStructure([response])), responses: true);
            MachineryJobResponseParameters? result = await harness.Client()
                .ReadJobResponseParametersAsync(harness.Machine, "J-1")
                .ConfigureAwait(false);
            Assert.That(result!.AsBuiltBOM.Count, Is.EqualTo(1));
            Assert.That(result.AsBuiltBOM[0].Identification.ItemNumber, Is.EqualTo("Part-1"));
            Assert.That(result.Values[0].SourceNodeId, Is.EqualTo(source));
        }

        /// <summary>
        /// Distinct partial responses remain visible, but selecting an ambiguous response does not pick the first.
        /// </summary>
        [Test]
        public async Task StrictJobResponseSelectionRejectsAmbiguityWithoutDroppingListRows()
        {
            var harness = new ReadHarness();
            AddJobList(harness,
                new DataValue(Variant.FromStructure((ArrayOf<V2.ISA95JobResponseDataType>)
                [
                    new() { JobResponseID = "R-1", JobOrderID = "J-1" },
                    new() { JobResponseID = "R-2", JobOrderID = "J-1" }
                ])), responses: true);
            MachineryClient client = harness.Client();
            Assert.That(
                (await client.ReadJobResponsesSnapshotAsync(harness.Machine).ConfigureAwait(false)).JobResponses.Count,
                Is.EqualTo(2));
            Assert.That(
                Assert.ThrowsAsync<MachineryReadException>(async () =>
                    await client.ReadJobResponseParametersAsync(harness.Machine, "J-1")
                        .ConfigureAwait(false))!.StatusCode,
                Is.EqualTo(StatusCodes.BadDecodingError));
        }

        private static V2.ISA95JobOrderAndStateDataType Order(string id)
        {
            return new V2.ISA95JobOrderAndStateDataType
            {
                JobOrder = new V2.ISA95JobOrderDataType { JobOrderID = id }
            };
        }

        private static NodeId AddJobList(ReadHarness harness, in DataValue value, bool responses = false)
        {
            return harness.Add(harness.Machine,
                [
                    harness.Name(JobNames.JobManagement, Jobs.Namespaces.MachineryJobs),
                    harness.Name(responses ? JobNames.JobOrderResults : JobNames.JobOrderControl,
                        Jobs.Namespaces.MachineryJobs),
                    harness.Name(responses ? V2.BrowseNames.JobOrderResponseList : V2.BrowseNames.JobOrderList,
                        V2.Namespaces.ISA95JobControlV2)
                ],
                value);
        }

        private static ExtensionObject BinaryStructure<T>(ReadHarness harness, T value) where T : IEncodeable
        {
            using var encoder = new BinaryEncoder(harness.Context);
            value.Encode(encoder);
            return new ExtensionObject(value.BinaryEncodingId, new ByteString(encoder.CloseAndReturnBuffer()!));
        }
    }
}
