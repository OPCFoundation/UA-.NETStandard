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
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Machinery.Client;
using DiNames = Opc.Ua.Di.BrowseNames;
using PvNames = Opc.Ua.Machinery.ProcessValues.BrowseNames;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryStrictReadTests
    {
        /// <summary>
        /// Both state-machine projections retain original evidence and never synthesize a timestamp.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StrictStateAndModeSnapshotsPreserveQualityAndTime(bool mode)
        {
            var harness = new ReadHarness();
            (NodeId state, NodeId source) = AddState(harness, mode);
            DataValue original = harness.Values[source];
            NodeId number = harness.Add(state,
                [new QualifiedName(Ua.BrowseNames.CurrentState), new QualifiedName(Ua.BrowseNames.Number)],
                new DataValue(Variant.From(2u)));
            MachineryClient client = harness.Client();
            MachineryStateSnapshot? snapshot = mode
                ? await client.ReadOperationModeAsync(harness.Machine).ConfigureAwait(false)
                : await client.ReadItemStateAsync(harness.Machine).ConfigureAwait(false);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.State.StateMachineId, Is.EqualTo(state));
            Assert.That(snapshot.State.CurrentState.Text, Is.EqualTo("Executing"));
            Assert.That(snapshot.State.Timestamp, Is.EqualTo(original.SourceTimestamp.ToDateTime()));
            Assert.That(snapshot.State.Status, Is.EqualTo(original.StatusCode));
            Assert.That(snapshot.CurrentStateNumber, Is.EqualTo(2u));
            Assert.That(snapshot.Values[0].Value, Is.EqualTo(original));
            Assert.That(snapshot.Values[2].SourceNodeId, Is.EqualTo(number));
            Assert.That(snapshot.Values[3].IsAbsent, Is.True);
            Assert.That(snapshot.LastTransitionNumber, Is.Null);
            FiniteStateSnapshot? compatible = mode
                ? await client.GetOperationModeAsync(harness.Machine).ConfigureAwait(false)
                : await client.GetItemStateAsync(harness.Machine).ConfigureAwait(false);
            Assert.That(compatible, Is.EqualTo(snapshot.State));
        }

        /// <summary>
        /// UInt32 metadata refuses the StatusCode value accepted by Variant.TryGetValue.
        /// </summary>
        [Test]
        public void StrictStateNumberRejectsStatusCodeWireType()
        {
            var harness = new ReadHarness();
            (NodeId state, _) = AddState(harness, mode: false);
            var wrong = Variant.From(new StatusCode(5));
            Assert.That(wrong.TryGetValue(out uint _), Is.True, "Exercise the permissive TryGetValue edge.");
            NodeId source = harness.Add(state,
                [new QualifiedName(Ua.BrowseNames.CurrentState), new QualifiedName(Ua.BrowseNames.Number)],
                TimedValue(wrong, StatusCodes.Good));
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().GetItemStateAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(error.ReadValue.Value.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
        }

        /// <summary>
        /// A bad optional transition value is a read failure, not an absent transition.
        /// </summary>
        [TestCase(nameof(StatusCodes.Uncertain))]
        [TestCase(nameof(StatusCodes.BadUserAccessDenied))]
        public void StrictStateRejectsBadTransitionQuality(string statusName)
        {
            StatusCode status = TestStatus(statusName);
            var harness = new ReadHarness();
            (NodeId state, _) = AddState(harness, mode: false);
            DataValue original = TimedValue(Variant.From(new LocalizedText("Started")), status);
            NodeId source = harness.Add(state, [new QualifiedName(Ua.BrowseNames.LastTransition)], original);
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadItemStateAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(status));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(error.ReadValue.Value, Is.EqualTo(original));
        }

        /// <summary>
        /// Health evidence distinguishes a real enum, Good-null, absence and failed values.
        /// </summary>
        [TestCase("Present")]
        [TestCase("GoodNull")]
        [TestCase("Absent")]
        [TestCase("WrongType")]
        [TestCase("Uncertain")]
        [TestCase("Denied")]
        public async Task StrictHealthEvidenceDistinguishesReadOutcomes(string kind)
        {
            var harness = new ReadHarness();
            StatusCode status = kind == "Uncertain" ? StatusCodes.Uncertain :
                kind == "Denied" ? StatusCodes.BadUserAccessDenied : StatusCodes.Good;
            Variant value = kind == "GoodNull" ? Variant.Null :
                kind == "WrongType" ? Variant.From("Normal") : Variant.From(0);
            NodeId source = NodeId.Null;
            if (kind != "Absent")
            {
                source = harness.Add(harness.Machine,
                    [
                        Machinery(harness, BrowseNames.Monitoring),
                        Machinery(harness, BrowseNames.Health),
                        Di(harness, DiNames.DeviceHealth)
                    ],
                    TimedValue(value, status));
            }
            MachineryClient client = harness.Client();
            if (kind is "WrongType" or "Uncertain" or "Denied")
            {
                MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                    async () => await client.ReadDeviceHealthAsync(harness.Machine).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(kind == "WrongType" ? StatusCodes.BadTypeMismatch : status));
                Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(source));
            }
            else
            {
                MachineryReadValue field = await client.ReadDeviceHealthValueAsync(harness.Machine)
                    .ConfigureAwait(false);
                Assert.That(field.SourceNodeId, Is.EqualTo(source));
                Assert.That(field.IsAbsent, Is.EqualTo(kind == "Absent"));
                Assert.That(field.Value.StatusCode,
                    Is.EqualTo(kind == "Absent" ? StatusCodes.BadNoMatch : StatusCodes.Good));
                if (kind == "Present")
                {
                    Assert.That(await client.ReadDeviceHealthAsync(harness.Machine).ConfigureAwait(false),
                        Is.EqualTo(Ua.Di.DeviceHealthEnumeration.NORMAL));
                }
                else
                {
                    Assert.That(await client.ReadDeviceHealthAsync(harness.Machine).ConfigureAwait(false), Is.Null);
                }
            }
        }

        /// <summary>
        /// Optional model absence is reported without invented namespace indexes or service calls.
        /// </summary>
        [Test]
        public async Task MissingModelNamespacesAreExplicitAndDoNotMutateTheTable()
        {
            var harness = new ReadHarness { Namespaces = new NamespaceTable() };
            MachineryClient client = harness.Client();
            var machine = new NodeId(123u);
            string[] original = harness.Namespaces.ToArray();
            Assert.That((await client.DiscoverMachinesAsync().ConfigureAwait(false)).Count, Is.Zero);
            Assert.That(await client.ReadIdentificationAsync(machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.ReadOperationCountersAsync(machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.ReadItemStateAsync(machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.ReadOperationModeAsync(machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.ReadProcessValueAsync(machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.ReadMeteringPointAsync(machine).ConfigureAwait(false), Is.Null);
            MachineryReadValue health = await client.ReadDeviceHealthValueAsync(machine).ConfigureAwait(false);
            Assert.That(health.IsAbsent, Is.True);
            Assert.That(health.MissingNamespaceUri, Is.EqualTo(Namespaces.Machinery));
            MachineryJobOrdersSnapshot jobs = await client.ReadJobOrdersSnapshotAsync(machine).ConfigureAwait(false);
            Assert.That(jobs.Values[0].IsAbsent, Is.True);
            Assert.That(jobs.Values[0].MissingNamespaceUri, Is.EqualTo(Jobs.Namespaces.MachineryJobs));
            Assert.That(harness.ReadTimestamps, Is.Empty);
            Assert.That(harness.BrowseLimits, Is.Empty);
            Assert.That(harness.Namespaces.ToArray(), Is.EqualTo(original));
        }

        /// <summary>
        /// Process values keep source metadata and accept the model's numeric scalar and array forms.
        /// </summary>
        [TestCase("Double")]
        [TestCase("Integer")]
        [TestCase("Array")]
        [TestCase("GoodNull")]
        public async Task StrictProcessValuesRetainSignalAndOptionalFieldEvidence(string kind)
        {
            var harness = new ReadHarness();
            Variant value = kind switch
            {
                "Integer" => Variant.From(5),
                "Array" => Variant.From([5.0, 6.0]),
                "GoodNull" => Variant.Null,
                _ => Variant.From(5.0)
            };
            DataValue original = TimedValue(value, StatusCodes.Good);
            QualifiedName signalName = harness.Name(PADIM.BrowseNames.AnalogSignal, PADIM.Namespaces.PADIM);
            NodeId signal = harness.Add(harness.Machine, [signalName], original);
            QualifiedName unitsName = new(Ua.BrowseNames.EngineeringUnits);
            harness.Add(signal, [unitsName], new DataValue(Variant.FromStructure(new EUInformation
            {
                DisplayName = new LocalizedText("bar"),
                NamespaceUri = "urn:units",
                UnitId = 1
            })));
            MachineryProcessValue? result = await harness.Client().ReadProcessValueAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(result!.SignalNodeId, Is.EqualTo(signal));
            Assert.That(result.Sample, Is.EqualTo(original));
            Assert.That(result.Value, Is.EqualTo(kind is "Array" or "GoodNull" ? (double?)null : 5.0));
            Assert.That(result.EngineeringUnits!.DisplayName.Text, Is.EqualTo("bar"));
            Assert.That(result.Values.Count, Is.EqualTo(11));
            Assert.That(Field(result.Values, signalName).Value, Is.EqualTo(original));
            Assert.That(Field(result.Values, signalName).IsAbsent, Is.False);
            Assert.That(Field(result.Values,
                harness.Name(PvNames.Status, ProcessValues.Namespaces.MachineryProcessValues)).IsAbsent,
                Is.True);
        }

        /// <summary>
        /// Wrong process metadata and oversized numeric arrays fail with the actual source retained.
        /// </summary>
        [TestCase("WrongSignal", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("WrongStatus", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("WrongUnits", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("ArrayLimit", nameof(StatusCodes.BadEncodingLimitsExceeded))]
        [TestCase("MissingSignal", nameof(StatusCodes.BadNoMatch))]
        public void StrictProcessValuesRejectMalformedFields(string kind, string statusName)
        {
            StatusCode expectedStatus = TestStatus(statusName);
            var harness = new ReadHarness();
            QualifiedName signalName = harness.Name(PADIM.BrowseNames.AnalogSignal, PADIM.Namespaces.PADIM);
            if (kind != "MissingSignal")
            {
                Variant value = kind == "WrongSignal" ? Variant.From("5") :
                    kind == "ArrayLimit" ? Variant.From([1.0, 2.0, 3.0]) : Variant.From(5.0);
                NodeId signal = harness.Add(harness.Machine, [signalName], new DataValue(value));
                if (kind == "WrongStatus")
                {
                    harness.Add(harness.Machine,
                        [harness.Name(PvNames.Status, ProcessValues.Namespaces.MachineryProcessValues)],
                        new DataValue(Variant.From(7u)));
                }
                if (kind == "WrongUnits")
                {
                    harness.Add(signal, [new QualifiedName(Ua.BrowseNames.EngineeringUnits)],
                        new DataValue(Variant.FromStructure(new Range { Low = 1, High = 2 })));
                }
            }
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(async () =>
                await harness.Client(new MachineryClientOptions { MaximumReadArrayItems = 2 })
                    .ReadProcessValueAsync(harness.Machine)
                    .ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(error.ReadValue!.BrowsePath.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Meter reads retain the actual measurement source, quality and timestamps.
        /// </summary>
        [Test]
        public async Task StrictMeterSnapshotRetainsApplicationTagAndMeasurementEvidence()
        {
            var harness = new ReadHarness();
            QualifiedName tag = harness.Name(ECM.BrowseNames.ApplicationTag, ECM.Namespaces.ECM);
            harness.Add(harness.Machine, [tag], new DataValue(Variant.Null));
            ReferenceDescription measurement = Reference(harness, 1, NodeClass.Variable);
            var source = ExpandedNodeId.ToNodeId(measurement.NodeId, harness.Namespaces);
            DataValue original = TimedValue(Variant.From(42.5), StatusCodes.Good);
            harness.Values[source] = original;
            harness.Browse = _ => Page([measurement]);
            MachineryMeteringPoint? meter = await harness.Client().ReadMeteringPointAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(meter!.ApplicationTag, Is.Null);
            Assert.That(meter.Values.Count, Is.EqualTo(2));
            Assert.That(meter.Values[0].IsAbsent, Is.False);
            Assert.That(meter.Measurements.Count, Is.EqualTo(1));
            Assert.That(meter.Measurements[0].NodeId, Is.EqualTo(source));
            Assert.That(meter.Measurements[0].Value, Is.EqualTo(original.WrappedValue));
            Assert.That(meter.Measurements[0].Values[0].Value, Is.EqualTo(original));
        }

        /// <summary>
        /// The metering point's direct Read batch is as strict as translated field reads.
        /// </summary>
        [TestCase("Denied", nameof(StatusCodes.BadUserAccessDenied))]
        [TestCase("Uncertain", nameof(StatusCodes.Uncertain))]
        [TestCase("MissingValue", nameof(StatusCodes.BadDecodingError))]
        [TestCase("WrongValue", nameof(StatusCodes.BadTypeMismatch))]
        [TestCase("WrongTag", nameof(StatusCodes.BadTypeMismatch))]
        public void StrictMeterRejectsUnreadableOrMalformedMeasurements(string kind, string statusName)
        {
            StatusCode status = TestStatus(statusName);
            var harness = new ReadHarness();
            QualifiedName tag = harness.Name(ECM.BrowseNames.ApplicationTag, ECM.Namespaces.ECM);
            NodeId tagSource = harness.Add(harness.Machine, [tag],
                new DataValue(kind == "WrongTag" ? Variant.From(7) : Variant.From("Main")));
            ReferenceDescription measurement = Reference(harness, 1, NodeClass.Variable);
            var source = ExpandedNodeId.ToNodeId(measurement.NodeId, harness.Namespaces);
            harness.Values[source] = TimedValue(
                kind == "WrongValue" ? Variant.From("42") : Variant.From(42.0),
                kind is "Denied" or "Uncertain" ? status : StatusCodes.Good);
            harness.Browse = _ => Page([measurement]);
            if (kind == "MissingValue")
            {
                harness.Read = (nodes, _) => Task.FromResult(nodes[0].NodeId == source
                    ? new ReadResponse { ResponseHeader = new ResponseHeader(), Results = [] }
                    : harness.ReadNodes(nodes));
            }
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadMeteringPointAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(status));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(kind == "WrongTag" ? tagSource : source));
        }

        /// <summary>
        /// Lifetime counters retain direct and property evidence while bounding warning arrays.
        /// </summary>
        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task StrictLifetimeCounterBoundsWarningsAndPreservesEvidence(int warningCount)
        {
            var harness = new ReadHarness();
            NodeId folder = harness.Add(harness.Machine, [Machinery(harness, BrowseNames.LifetimeCounters)]);
            ReferenceDescription counter = Reference(harness, 1, NodeClass.Variable);
            NodeId source = harness.Add(folder, [counter.BrowseName], new DataValue(50.0));
            counter.NodeId = source;
            harness.Add(source, [Di(harness, DiNames.StartValue)], new DataValue(100.0));
            harness.Add(source, [Di(harness, DiNames.LimitValue)], new DataValue(0.0));
            harness.Add(source, [Di(harness, DiNames.WarningValues)],
                new DataValue(Variant.From(new double[warningCount].ToArrayOf())));
            harness.Browse = _ => Page([counter]);
            MachineryClient client = harness.Client(new MachineryClientOptions { MaximumReadArrayItems = 2 });
            var results = new List<MachineryLifetimeVariable>();
            async Task ReadAsync()
            {
                await foreach (MachineryLifetimeVariable result in client.ReadLifetimeCountersAsync(harness.Machine))
                {
                    results.Add(result);
                }
            }

            if (warningCount > 2)
            {
                Assert.That(Assert.ThrowsAsync<MachineryReadException>(ReadAsync)!.StatusCode,
                    Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(results, Is.Empty);
            }
            else
            {
                await ReadAsync().ConfigureAwait(false);
                Assert.That(results.Single().Remaining, Is.EqualTo(50.0));
                Assert.That(results.Single().WarningValues.Count, Is.EqualTo(warningCount));
                Assert.That(results.Single().Values.Count, Is.EqualTo(4));
                Assert.That(results.Single().Values[0].SourceNodeId, Is.EqualTo(source));
            }
        }

        /// <summary>
        /// A present Good-null state with no timestamp is not changed into absence or local time.
        /// </summary>
        [Test]
        public async Task StrictStateGoodNullAndMissingTimestampArePreserved()
        {
            var harness = new ReadHarness();
            (_, NodeId source) = AddState(harness, mode: false);
            harness.Values[source] = new DataValue(Variant.Null);
            MachineryStateSnapshot? snapshot = await harness.Client().ReadItemStateAsync(harness.Machine)
                .ConfigureAwait(false);
            Assert.That(snapshot!.State.CurrentState.IsNull, Is.True);
            Assert.That(snapshot.State.Timestamp, Is.EqualTo(DateTimeUtc.MinValue.ToDateTime()));
            Assert.That(snapshot.Values[0].Value.SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(snapshot.Values[0].IsAbsent, Is.False);
            Assert.That(snapshot.Values[0].SourceNodeId, Is.EqualTo(source));
            Assert.That(snapshot.Values[0].Value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Missing optional building blocks return absence without attempting a value read.
        /// </summary>
        [Test]
        public async Task StrictOptionalMissingBlocksDoNotReadValues()
        {
            var harness = new ReadHarness();
            MachineryClient client = harness.Client();
            Assert.That(await client.ReadIdentificationAsync(harness.Machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.ReadOperationCountersAsync(harness.Machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.GetItemStateAsync(harness.Machine).ConfigureAwait(false), Is.Null);
            Assert.That(await client.GetOperationModeAsync(harness.Machine).ConfigureAwait(false), Is.Null);
            Assert.That((await client.ResolveMonitoringAsync(harness.Machine).ConfigureAwait(false)).IsNull, Is.True);
            Assert.That(harness.ReadTimestamps, Is.Empty);
        }

        /// <summary>
        /// A selected resource's required Main meter is not treated like an optional block.
        /// </summary>
        [Test]
        public void StrictMissingMainMeterIsAConformanceFailure()
        {
            var harness = new ReadHarness();
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(
                async () => await harness.Client().ReadMainMeteringPointAsync(harness.Machine).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(error.ReadValue!.StartingNodeId, Is.EqualTo(harness.Machine));
            Assert.That(error.ReadValue.BrowsePath[0].Name, Is.EqualTo(Energy.BrowseNames.Main));
            Assert.That(harness.ReadTimestamps, Is.Empty);
        }

        /// <summary>
        /// Lifetime fields must use their modeled Double types, not coercible Float values.
        /// </summary>
        [TestCase("Remaining")]
        [TestCase("StartValue")]
        [TestCase("LimitValue")]
        [TestCase("WarningValues")]
        public void StrictLifetimeCounterRejectsWrongWireTypes(string field)
        {
            var harness = new ReadHarness();
            NodeId folder = harness.Add(harness.Machine, [Machinery(harness, BrowseNames.LifetimeCounters)]);
            ReferenceDescription counter = Reference(harness, 1, NodeClass.Variable);
            NodeId source = harness.Add(folder, [counter.BrowseName],
                new DataValue(field == "Remaining" ? Variant.From(5.0f) : Variant.From(5.0)));
            counter.NodeId = source;
            NodeId rejected = source;
            if (field != "Remaining")
            {
                rejected = harness.Add(source, [Di(harness, field)],
                    new DataValue(field == "WarningValues"
                        ? Variant.From([1.0f])
                        : Variant.From(1.0f)));
            }
            harness.Browse = _ => Page([counter]);
            MachineryReadException? error = Assert.ThrowsAsync<MachineryReadException>(async () =>
            {
                await foreach (MachineryLifetimeVariable _ in harness.Client()
                    .ReadLifetimeCountersAsync(harness.Machine).ConfigureAwait(false))
                {
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(error.ReadValue!.SourceNodeId, Is.EqualTo(rejected));
            Assert.That(error.ReadValue.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        private static (NodeId State, NodeId Source) AddState(ReadHarness harness, bool mode)
        {
            NodeId state = harness.Add(harness.Machine,
                [
                    Machinery(harness, BrowseNames.Monitoring),
                    Machinery(harness, BrowseNames.Status),
                    Machinery(harness, mode ? BrowseNames.MachineryOperationMode : BrowseNames.MachineryItemState)
                ]);
            NodeId source = harness.Add(state, [new QualifiedName(Ua.BrowseNames.CurrentState)],
                TimedValue(Variant.From(new LocalizedText("Executing")), StatusCodes.GoodClamped));
            harness.Add(state,
                [new QualifiedName(Ua.BrowseNames.CurrentState), new QualifiedName(Ua.BrowseNames.Id)],
                new DataValue(Variant.From(new NodeId(9u))));
            return (state, source);
        }
    }
}
