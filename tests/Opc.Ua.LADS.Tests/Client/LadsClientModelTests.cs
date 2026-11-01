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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.LADS.Client;

namespace Opc.Ua.LADS.Tests.Client
{
    /// <summary>
    /// Covers the socket-free parts of the LADS client: options, program
    /// requests, function kinds, snapshots and value conversion.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Category("Client")]
    public sealed class LadsClientModelTests
    {
        [Test]
        public void DefaultOptionsAreValid()
        {
            LadsClientOptions options = LadsClientOptions.Default;

            Assert.Multiple(() =>
            {
                Assert.That(options.MaximumBrowseReferences, Is.EqualTo(4096));
                Assert.That(options.MaximumBrowsePages, Is.EqualTo(128));
                Assert.That(options.MaximumFunctionDepth, Is.EqualTo(8));
                Assert.That(options.MaximumFileSize, Is.EqualTo(64L * 1024 * 1024));
                Assert.That(options.FileChunkSize, Is.EqualTo(64 * 1024));
                Assert.That(options.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
                Assert.That(options.QueueSize, Is.EqualTo(10u));
                Assert.That(options.Validate, Throws.Nothing);
            });
        }

        [Test]
        public void InvalidOptionsAreRejected()
        {
            LadsClientOptions[] invalid =
            [
                new() { MaximumBrowseReferences = 0 },
                new() { MaximumBrowsePages = 0 },
                new() { MaximumFunctionDepth = 0 },
                new() { MaximumFileSize = 0 },
                new() { FileChunkSize = 0 },
                new() { SamplingInterval = TimeSpan.FromMilliseconds(-1) },
                new() { QueueSize = 0 }
            ];

            foreach (LadsClientOptions options in invalid)
            {
                Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>(), options.ToString());
            }
        }

        [Test]
        public void ProgramRequestAccumulatesPropertiesAndSamples()
        {
            LadsProgramRequest request = LadsProgramRequest.For("Measure")
                .WithProperty("Temperature", "37")
                .WithProperty("Volume", "10")
                .WithSample("Plate1", "S1", "A1", "custom")
                .WithSample("Plate1", "S2")
                .WithJob("Job")
                .WithTask("Task");

            Assert.Multiple(() =>
            {
                Assert.That(request.TemplateId, Is.EqualTo("Measure"));
                Assert.That(request.Properties.Count, Is.EqualTo(2));
                Assert.That(request.Properties[0].Key, Is.EqualTo("Temperature"));
                Assert.That(request.Properties[1].Value, Is.EqualTo("10"));
                Assert.That(request.Samples.Count, Is.EqualTo(2));
                Assert.That(request.Samples[0].Position, Is.EqualTo("A1"));
                Assert.That(request.Samples[0].CustomData, Is.EqualTo("custom"));
                Assert.That(request.Samples[1].SampleId, Is.EqualTo("S2"));
                Assert.That(request.SupervisoryJobId, Is.EqualTo("Job"));
                Assert.That(request.SupervisoryTaskId, Is.EqualTo("Task"));
            });
        }

        [Test]
        public void ProgramRequestIsImmutable()
        {
            LadsProgramRequest request = LadsProgramRequest.For("Measure");

            LadsProgramRequest extended = request.WithProperty("Key", "Value").WithSample("C", "S");

            Assert.Multiple(() =>
            {
                Assert.That(request.Properties.Count, Is.Zero);
                Assert.That(request.Samples.Count, Is.Zero);
                Assert.That(extended.Properties.Count, Is.EqualTo(1));
                Assert.That(extended.Samples.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void ProgramRequestRejectsEmptyIds()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => LadsProgramRequest.For(string.Empty));
                Assert.Throws<ArgumentException>(() => LadsProgramRequest.For(null!));
                Assert.Throws<ArgumentException>(
                    () => LadsProgramRequest.For("Measure").WithProperty(string.Empty, "x"));
            });
        }

        [Test]
        public void EveryFunctionKindHasALadsTypeId()
        {
            foreach (LadsFunctionKind kind in LadsFunctionKinds.MostSpecificFirst)
            {
                if (kind == LadsFunctionKind.Unknown)
                {
                    continue;
                }
                ExpandedNodeId typeId = LadsFunctionKinds.TypeIdOf(kind);
                Assert.That(typeId.IsNull, Is.False, kind.ToString());
                Assert.That(typeId.NamespaceUri, Is.EqualTo(Namespaces.LADS), kind.ToString());
            }
        }

        [Test]
        public void MostSpecificFirstListsEveryKindOnce()
        {
            ArrayOf<LadsFunctionKind> kinds = LadsFunctionKinds.MostSpecificFirst;
            var unique = new HashSet<LadsFunctionKind>();
            foreach (LadsFunctionKind kind in kinds)
            {
                Assert.That(unique.Add(kind), Is.True, kind.ToString());
            }

            Assert.Multiple(() =>
            {
                Assert.That(unique, Has.Count.EqualTo(20));
                Assert.That(unique, Does.Not.Contain(LadsFunctionKind.Unknown));
                Assert.That(
                    IndexOf(kinds, LadsFunctionKind.AnalogScalarSensorWithCompensation),
                    Is.LessThan(IndexOf(kinds, LadsFunctionKind.AnalogScalarSensor)));
                Assert.That(
                    IndexOf(kinds, LadsFunctionKind.AnalogScalarSensor),
                    Is.LessThan(IndexOf(kinds, LadsFunctionKind.AnalogSensor)));
                Assert.That(
                    IndexOf(kinds, LadsFunctionKind.AnalogControlWithTotalizer),
                    Is.LessThan(IndexOf(kinds, LadsFunctionKind.AnalogControl)));
                Assert.That(
                    IndexOf(kinds, LadsFunctionKind.AnalogControl),
                    Is.LessThan(IndexOf(kinds, LadsFunctionKind.BaseControl)));
            });
        }

        [TestCase(LadsFunctionKind.BaseSensor, true, false)]
        [TestCase(LadsFunctionKind.AnalogScalarSensor, true, false)]
        [TestCase(LadsFunctionKind.MultiSensor, true, false)]
        [TestCase(LadsFunctionKind.BaseControl, false, true)]
        [TestCase(LadsFunctionKind.TimerControl, false, true)]
        [TestCase(LadsFunctionKind.MultiStateDiscreteControl, false, true)]
        [TestCase(LadsFunctionKind.Cover, false, false)]
        [TestCase(LadsFunctionKind.Unknown, false, false)]
        public void FunctionKindsAreClassified(LadsFunctionKind kind, bool sensor, bool control)
        {
            Assert.Multiple(() =>
            {
                Assert.That(LadsFunctionKinds.IsSensor(kind), Is.EqualTo(sensor));
                Assert.That(LadsFunctionKinds.IsControl(kind), Is.EqualTo(control));
            });
        }

        [Test]
        public void StateSnapshotComparesStateNamesIgnoringCase()
        {
            var state = new LadsStateSnapshot(new NodeId(1u, 2), "Running", NodeId.Null);

            Assert.Multiple(() =>
            {
                Assert.That(state.IsIn("running"), Is.True);
                Assert.That(state.IsIn("Stopped"), Is.False);
                Assert.That(state.ToString(), Is.EqualTo("Running"));
            });
        }

        [Test]
        public void FunctionSnapshotExposesValuesByName()
        {
            var snapshot = new LadsFunctionSnapshot(
                new NodeId(1u, 2),
                "Temperature",
                LadsFunctionKind.AnalogControl,
                [
                    new LadsFunctionValue(LadsFunctionMembers.IsEnabled, new NodeId(2u, 2),
                        new DataValue(Variant.From(true))),
                    new LadsFunctionValue(LadsFunctionMembers.CurrentValue, new NodeId(3u, 2),
                        new DataValue(Variant.From(36.5))),
                    new LadsFunctionValue(LadsFunctionMembers.TargetValue, new NodeId(4u, 2),
                        new DataValue(Variant.From(37.0))),
                    new LadsFunctionValue(LadsFunctionMembers.ControlFunctionState, new NodeId(5u, 2),
                        new DataValue(Variant.From(new LocalizedText("Running"))))
                ]);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsEnabled, Is.True);
                Assert.That(snapshot.CurrentValue.TryGetValue(out double current), Is.True);
                Assert.That(current, Is.EqualTo(36.5));
                Assert.That(snapshot.TargetValue.TryGetValue(out double target), Is.True);
                Assert.That(target, Is.EqualTo(37.0));
                Assert.That(snapshot.State, Is.EqualTo("Running"));
                Assert.That(snapshot.SensorValue.IsNull, Is.True);
                Assert.That(snapshot.TryGetValue(LadsFunctionMembers.RawValue, out _), Is.False);
            });
        }

        [Test]
        public void FunctionSnapshotWithoutValuesHasNoState()
        {
            var snapshot = new LadsFunctionSnapshot(new NodeId(1u, 2), "Empty", LadsFunctionKind.BaseSensor, []);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsEnabled, Is.Null);
                Assert.That(snapshot.State, Is.Null);
                Assert.That(snapshot.ValueOf(LadsFunctionMembers.SensorValue).IsNull, Is.True);
            });
        }

        /// <summary>
        /// Convenience values reject bad quality while raw and uncertain values remain available.
        /// </summary>
        [Test]
        public void FunctionSnapshotKeepsBadQualityValuesOutOfConvenienceProperties()
        {
            var snapshot = new LadsFunctionSnapshot(
                new NodeId(1u, 2),
                "Temperature",
                LadsFunctionKind.AnalogControl,
                [
                    new LadsFunctionValue(LadsFunctionMembers.IsEnabled, new NodeId(2u, 2),
                        new DataValue(Variant.From(true), StatusCodes.BadNoData)),
                    new LadsFunctionValue(LadsFunctionMembers.CurrentValue, new NodeId(3u, 2),
                        new DataValue(Variant.From(36.5), StatusCodes.BadOutOfRange)),
                    new LadsFunctionValue(LadsFunctionMembers.TargetValue, new NodeId(4u, 2),
                        new DataValue(Variant.From(37.0), StatusCodes.Uncertain))
                ]);
            var uncertain = new LadsFunctionSnapshot(
                new NodeId(5u, 2), "Uncertain", LadsFunctionKind.AnalogControl,
                [
                    new LadsFunctionValue(LadsFunctionMembers.IsEnabled, new NodeId(6u, 2),
                        new DataValue(Variant.From(true), StatusCodes.Uncertain))
                ]);

            Assert.That(snapshot.IsEnabled, Is.Null);
            Assert.That(uncertain.IsEnabled, Is.True);
            Assert.That(snapshot.CurrentValue.IsNull, Is.True);
            Assert.That(snapshot.ValueOf(LadsFunctionMembers.CurrentValue).IsNull, Is.True);
            Assert.That(snapshot.TargetValue.TryGetValue(out double target), Is.True);
            Assert.That(target, Is.EqualTo(37.0));
            Assert.That(snapshot.TryGetValue(LadsFunctionMembers.CurrentValue, out DataValue raw), Is.True);
            Assert.That(raw.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(raw.WrappedValue.TryGetValue(out double rawValue), Is.True);
            Assert.That(rawValue, Is.EqualTo(36.5));
        }

        [Test]
        public void FunctionMembersAreReadInADefinedOrder()
        {
            ArrayOf<string> members = LadsFunctionMembers.All;

            Assert.Multiple(() =>
            {
                Assert.That(members.Count, Is.EqualTo(11));
                Assert.That(members[0], Is.EqualTo(LadsFunctionMembers.IsEnabled));
                Assert.That(LadsFunctionMembers.ControlFunctionState, Is.EqualTo("ControlFunctionState/CurrentState"));
                Assert.That(LadsFunctionMembers.CoverState, Is.EqualTo("CoverState/CurrentState"));
            });
        }

        [Test]
        public void ActiveProgramProgressIsClamped()
        {
            var id = new NodeId(1u, 2);

            Assert.Multiple(() =>
            {
                Assert.That(new LadsActiveProgram(id).Progress, Is.Null);
                Assert.That(
                    new LadsActiveProgram(id)
                    {
                        CurrentRuntime = TimeSpan.FromSeconds(30),
                        EstimatedRuntime = TimeSpan.FromSeconds(60)
                    }.Progress,
                    Is.EqualTo(0.5));
                Assert.That(
                    new LadsActiveProgram(id)
                    {
                        CurrentRuntime = TimeSpan.FromSeconds(90),
                        EstimatedRuntime = TimeSpan.FromSeconds(60)
                    }.Progress,
                    Is.EqualTo(1.0));
                Assert.That(
                    new LadsActiveProgram(id)
                    {
                        CurrentRuntime = TimeSpan.FromSeconds(1),
                        EstimatedRuntime = TimeSpan.Zero
                    }.Progress,
                    Is.Null);
            });
        }

        [Test]
        public void StringsAreReadFromStringsAndLocalizedTexts()
        {
            Assert.Multiple(() =>
            {
                Assert.That(LadsClient.AsString(new DataValue(Variant.From("text"))), Is.EqualTo("text"));
                Assert.That(
                    LadsClient.AsString(new DataValue(Variant.From(new LocalizedText("localized")))),
                    Is.EqualTo("localized"));
                Assert.That(LadsClient.AsString(new DataValue(Variant.From(string.Empty))), Is.Null);
                Assert.That(
                    LadsClient.AsString(new DataValue(Variant.From("bad"), StatusCodes.BadNodeIdUnknown)),
                    Is.Null);
                Assert.That(LadsClient.AsString(new DataValue(Variant.From(1u))), Is.Null);
            });
        }

        [Test]
        public void DurationsAreReadInMilliseconds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    LadsClient.AsDuration(new DataValue(Variant.From(1500.0))),
                    Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
                Assert.That(LadsClient.AsDuration(new DataValue(Variant.From(double.NaN))), Is.Null);
                Assert.That(LadsClient.AsDuration(new DataValue(Variant.From(double.PositiveInfinity))), Is.Null);
                Assert.That(LadsClient.AsDuration(new DataValue(Variant.From(double.MaxValue))), Is.Null);
                Assert.That(
                    LadsClient.AsDuration(new DataValue(Variant.From(1.0), StatusCodes.BadOutOfRange)),
                    Is.Null);
                Assert.That(LadsClient.AsDuration(new DataValue(Variant.From("1"))), Is.Null);
            });
        }

        [Test]
        public void NumbersNodeIdsAndDatesAreConverted()
        {
            var nodeId = new NodeId(7u, 3);
            var date = new DateTimeUtc(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

            Assert.Multiple(() =>
            {
                Assert.That(LadsClient.AsUInt32(new DataValue(Variant.From(5u))), Is.EqualTo(5u));
                Assert.That(LadsClient.AsUInt32(new DataValue(Variant.From(5.0))), Is.Null);
                Assert.That(LadsClient.AsUInt64(new DataValue(Variant.From(9ul))), Is.EqualTo(9ul));
                Assert.That(LadsClient.AsUInt64(new DataValue(Variant.From(9u))), Is.EqualTo(9ul));
                Assert.That(
                    LadsClient.AsUInt64(new DataValue(Variant.From(9ul), StatusCodes.BadNoData)),
                    Is.Null);
                Assert.That(LadsClient.AsNodeId(new DataValue(Variant.From(nodeId))), Is.EqualTo(nodeId));
                Assert.That(LadsClient.AsNodeId(new DataValue(Variant.From("x"))).IsNull, Is.True);
                Assert.That(LadsClient.AsDate(new DataValue(Variant.From(date))), Is.EqualTo(date));
                Assert.That(LadsClient.AsDate(new DataValue(Variant.From(1u))), Is.Default);
                Assert.That(
                    LadsClient.AsVariant(new DataValue(Variant.From(1u), StatusCodes.BadNoData)).IsNull,
                    Is.True);
                Assert.That(LadsClient.AsVariant(new DataValue(Variant.From(1u))).TryGetValue(out uint one) && one == 1,
                    Is.True);
            });
        }

        [Test]
        public void DisplayNamesFallBackToBrowseNames()
        {
            var named = new ReferenceDescription
            {
                BrowseName = new QualifiedName("Browse", 2),
                DisplayName = new LocalizedText("Display")
            };
            var unnamed = new ReferenceDescription { BrowseName = new QualifiedName("Browse", 2) };

            Assert.Multiple(() =>
            {
                Assert.That(LadsClient.DisplayNameOf(named), Is.EqualTo("Display"));
                Assert.That(LadsClient.DisplayNameOf(unnamed), Is.EqualTo("Browse"));
            });
        }

        [Test]
        public void ResponsesAreValidated()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => LadsClient.ValidateResponse(new ResponseHeader(), 1, 1), Throws.Nothing);
                Assert.That(() => LadsClient.ValidateResponse(null, 2, 2), Throws.Nothing);
                ServiceResultException? bad = Assert.Throws<ServiceResultException>(() =>
                    LadsClient.ValidateResponse(
                        new ResponseHeader { ServiceResult = StatusCodes.BadTooManyOperations }, 1, 1));
                Assert.That(bad!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                ServiceResultException? count = Assert.Throws<ServiceResultException>(() =>
                    LadsClient.ValidateResponse(new ResponseHeader(), 0, 1));
                Assert.That(count!.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            });
        }

        [Test]
        public void NamesMatchBrowseOrDisplayName()
        {
            Assert.Multiple(() =>
            {
                Assert.That(LadsClient.Matches("Unit1", "Pump unit", "Unit1"), Is.True);
                Assert.That(LadsClient.Matches("Unit1", "Pump unit", "Pump unit"), Is.True);
                Assert.That(LadsClient.Matches("Unit1", "Pump unit", "unit1"), Is.False);
                Assert.Throws<ArgumentException>(() => LadsClient.ThrowIfNull(NodeId.Null, "id"));
            });
        }

        [Test]
        public void ASingleObservedMemberMatchesEveryChange()
        {
            var member = (Name: LadsFunctionMembers.SensorValue, NodeId: new NodeId(5u, 2));

            (string Name, NodeId NodeId)? mapped = LadsClient.MapChange(
                new DataValueChange(null, new DataValue(Variant.From(1.0)), null),
                [member]);

            Assert.That(mapped, Is.EqualTo(member));
        }

        [Test]
        public void ChangesAreMappedByTheLongestMatchingNodeIdSuffix()
        {
            var shortId = (Name: "Short", NodeId: new NodeId(1u, 2));
            var longId = (Name: "Long", NodeId: new NodeId(11u, 2));
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(i => i.Name).Returns("stream_data_7_" + longId.NodeId);
            var unnamed = new Mock<IMonitoredItem>();
            unnamed.SetupGet(i => i.Name).Returns("other");

            Assert.Multiple(() =>
            {
                Assert.That(
                    LadsClient.MapChange(new DataValueChange(item.Object, default, null), [shortId, longId]),
                    Is.EqualTo(longId));
                Assert.That(
                    LadsClient.MapChange(new DataValueChange(unnamed.Object, default, null), [shortId, longId]),
                    Is.Null);
                Assert.That(
                    LadsClient.MapChange(new DataValueChange(null, default, null), [shortId, longId]),
                    Is.Null);
            });
        }

        [Test]
        public void FileChunkSizeAcceptsTheMaximumSupportedValue()
        {
            var options = new LadsClientOptions { FileChunkSize = 1_048_576 };

            Assert.That(options.Validate, Throws.Nothing);
            Assert.That(options.FileChunkSize, Is.EqualTo(1_048_576));
        }

        [Test]
        public void FileChunkSizeRejectsTheValueAboveTheMaximum()
        {
            var options = new LadsClientOptions { FileChunkSize = 1_048_577 };

            Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(options.FileChunkSize, Is.EqualTo(1_048_577));
        }

        private static int IndexOf(ArrayOf<LadsFunctionKind> kinds, LadsFunctionKind kind)
        {
            for (int ii = 0; ii < kinds.Count; ii++)
            {
                if (kinds[ii] == kind)
                {
                    return ii;
                }
            }
            return -1;
        }
    }
}
