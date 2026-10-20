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
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies forecast bounds, explicit provider capability selection and pre-publication rejection.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolPrognosisValidationTests
    {
        /// <summary>
        /// Starts a real model owner for independent direct-controller instances.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new MachineToolRuntimeFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the real model owner and its certificate stores.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync() => await m_fixture.DisposeAsync().ConfigureAwait(false);

        /// <summary>
        /// Every rejected forecast leaves both its prior node and its original prediction untouched.
        /// </summary>
        [TestCase("MissingInventory")]
        [TestCase("NullEntry")]
        [TestCase("DuplicateKey")]
        [TestCase("TooMany")]
        [TestCase("EmptyKey")]
        [TestCase("LongKey")]
        [TestCase("UnknownNamespace")]
        [TestCase("UnknownKind")]
        [TestCase("NoPrediction")]
        [TestCase("WrongPredictionType")]
        [TestCase("UnspecifiedGoodPrediction")]
        [TestCase("MissingPredictionTime")]
        [TestCase("MissingSampleTime")]
        [TestCase("MissingActivity")]
        [TestCase("ExtraLocation")]
        [TestCase("LongActivity")]
        [TestCase("LongLocale")]
        [TestCase("ExtraPart")]
        [TestCase("ExtraTool")]
        [TestCase("ExtraJob")]
        [TestCase("ExtraUtility")]
        [TestCase("ExtraCompanionType")]
        [TestCase("UnsupportedTarget")]
        [TestCase("PartMissingName")]
        [TestCase("PartMissingLocation")]
        [TestCase("JobMissingIdentifier")]
        [TestCase("UtilityMissingName")]
        [TestCase("ToolMissingLocation")]
        [TestCase("TargetMissingKey")]
        [TestCase("TargetWrongKeyKind")]
        [TestCase("TargetIncompletePart")]
        [TestCase("TargetNodeAndKeys")]
        [TestCase("UnknownTarget")]
        [TestCase("ToolLoadHasNodeId")]
        [TestCase("StaticRemoval")]
        [TestCase("ChangedKind")]
        [TestCase("StaleRevision")]
        public async Task InvalidPrognosisPreservesAcceptedPredictionAsync(string violation)
        {
            MachineToolSnapshot initial = Example();
            await using var controller = new MachineToolPrognosisController(m_fixture.Context, CreateMachine(),
                new MachineToolPrognosisOptions
                {
                    Features = MachineToolFeatures.Prognoses |
                        (violation == "StaticRemoval" ?
                            MachineToolFeatures.None : MachineToolFeatures.DynamicPrognoses),
                    SupportedKinds = MachineToolPrognosisTests.StandardKinds,
                    MaximumPrognoses = 2,
                    MaximumTextLength = 64,
                    PublishFinalObservationsAsync = m_fixture.Context.Manager.WaitForValuePublicationAsync
                });
            await controller.InitializeAsync(initial).ConfigureAwait(false);
            var before = (MaintenancePrognosisState)controller.State!.FindChild(m_fixture.Context.Context,
                new QualifiedName("Maintenance", m_fixture.Context.InstanceNamespaceIndex))!;
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await controller.ApplyAsync(Invalid(initial, violation)).ConfigureAwait(false));
            Assert.That(controller.Observation, Is.SameAs(initial));
            Assert.That(before.PredictedTime!.Value,
                Is.EqualTo((DateTimeUtc)new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(before.Activity!.Value.Text, Is.EqualTo("Replace filter"));
        }

        /// <summary>
        /// Enabled capabilities require an explicit provider selection and dynamic lists require actual publication.
        /// </summary>
        [TestCase("NoKinds")]
        [TestCase("DuplicateKinds")]
        [TestCase("UnknownKind")]
        [TestCase("CompanionWithoutFactory")]
        [TestCase("DynamicWithoutPrognoses")]
        [TestCase("DynamicWithoutBarrier")]
        public void MissingPrognosisCapabilitiesFailSetup(string missing)
        {
            var options = new MachineToolPrognosisOptions
            {
                Features = MachineToolFeatures.Prognoses,
                SupportedKinds = [MachineToolPrognosisKind.Maintenance]
            };
            options = missing switch
            {
                "NoKinds" => options with { SupportedKinds = [] },
                "DuplicateKinds" => options with
                {
                    SupportedKinds = [MachineToolPrognosisKind.Maintenance, MachineToolPrognosisKind.Maintenance]
                },
                "UnknownKind" => options with { SupportedKinds = [(MachineToolPrognosisKind)99] },
                "CompanionWithoutFactory" => options with { SupportedKinds = [MachineToolPrognosisKind.Companion] },
                "DynamicWithoutPrognoses" => options with { Features = MachineToolFeatures.DynamicPrognoses },
                _ => options with { Features = MachineToolFeatures.Prognoses | MachineToolFeatures.DynamicPrognoses }
            };
            Assert.That(() => new MachineToolPrognosisController(m_fixture.Context, CreateMachine(), options),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Negative and excessive aggregate bounds are setup errors, not a truncated inventory.
        /// </summary>
        [TestCase(0, 1, 1)]
        [TestCase(65537, 1, 1)]
        [TestCase(1, 0, 1)]
        [TestCase(1, 131073, 1)]
        [TestCase(1, 1, 0)]
        [TestCase(1, 1, 65537)]
        public void InvalidPrognosisBoundsFailSetup(int forecasts, int references, int text)
        {
            Assert.That(() => new MachineToolPrognosisController(m_fixture.Context, CreateMachine(),
                new MachineToolPrognosisOptions
                {
                    MaximumPrognoses = forecasts,
                    MaximumReferencedEntities = references,
                    MaximumTextLength = text
                }), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Explicit bad-quality absence stays unavailable; the runtime never supplies a successful predicted date.
        /// </summary>
        [Test]
        public async Task UnknownPredictionRetainsBadQualityWithoutSynthesizingTimeAsync()
        {
            MachineToolSnapshot initial = Example();
            MachineToolPrognosis value = initial.Prognoses!.Entries[0] with
            {
                PredictedTime = new DataValue(Variant.Null, StatusCodes.BadWaitingForInitialData, default(DateTimeUtc))
            };
            await using var controller = new MachineToolPrognosisController(m_fixture.Context, CreateMachine(),
                new MachineToolPrognosisOptions
                {
                    Features = MachineToolFeatures.Prognoses,
                    SupportedKinds = [MachineToolPrognosisKind.Maintenance]
                });
            await controller.InitializeAsync(initial with { Prognoses = new() { Entries = [value] } })
                .ConfigureAwait(false);
            var node = (MaintenancePrognosisState)controller.State!.FindChild(m_fixture.Context.Context,
                new QualifiedName("Maintenance", m_fixture.Context.InstanceNamespaceIndex))!;
            Assert.That(node.PredictedTime!.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(node.PredictedTime.Timestamp, Is.Default);
            Assert.That(node.PredictedTime.WrappedValue.IsNull, Is.True);
        }

        /// <summary>
        /// Supplies a known independent prediction used by the rejection tests.
        /// </summary>
        internal static MachineToolSnapshot Example()
        {
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), StatusCodes.Good);
            return new MachineToolSnapshot
            {
                Prognoses = new()
                {
                    Entries =
                    [
                        MachineToolPrognosisTests.Example(MachineToolPrognosisKind.Maintenance, sample,
                            new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc))
                    ]
                }
            };
        }

        /// <summary>
        /// Creates one independently owned generated root.
        /// </summary>
        private MachineToolState CreateMachine() => m_fixture.Context.Context.CreateInstanceOfMachineToolType(
            m_fixture.Context.MachinesFolder,
            new QualifiedName("Forecast" + ++m_index, m_fixture.Context.InstanceNamespaceIndex));

        /// <summary>
        /// Changes one independently testable provider condition.
        /// </summary>
        private static MachineToolSnapshot Invalid(MachineToolSnapshot initial, string violation)
        {
            MachineToolPrognosis value = initial.Prognoses!.Entries[0];
            var next = initial with { Revision = 1 };
            switch (violation)
            {
                case "MissingInventory":
                    return next with { Prognoses = null };
                case "NullEntry":
                    return next with { Prognoses = new() { Entries = [null!] } };
                case "DuplicateKey":
                    return next with { Prognoses = new() { Entries = [value, value] } };
                case "TooMany":
                    return next with { Prognoses = new() { Entries = [value, value, value] } };
                case "EmptyKey":
                    value = value with { Key = string.Empty };
                    break;
                case "LongKey":
                    value = value with { Key = new string('x', 65) };
                    break;
                case "UnknownNamespace":
                    value = value with { NamespaceUri = "urn:unloaded" };
                    break;
                case "UnknownKind":
                    value = value with { Kind = (MachineToolPrognosisKind)99 };
                    break;
                case "NoPrediction":
                    value = value with { PredictedTime = DataValue.Null };
                    break;
                case "WrongPredictionType":
                    value = value with
                    {
                        PredictedTime = MachineToolEquipmentTests.Sample(Variant.From(1), value.Sample)
                    };
                    break;
                case "UnspecifiedGoodPrediction":
                    value = value with
                    {
                        PredictedTime = MachineToolEquipmentTests.Sample(
                            Variant.From(default(DateTimeUtc)), value.Sample)
                    };
                    break;
                case "MissingPredictionTime":
                    value = value with
                    {
                        PredictedTime = new DataValue(
                            value.PredictedTime.WrappedValue, StatusCodes.Good, default(DateTimeUtc))
                    };
                    break;
                case "MissingSampleTime":
                    value = value with { Sample = new(default, StatusCodes.Good) };
                    break;
                case "MissingActivity":
                    value = value with { Activity = LocalizedText.Null };
                    break;
                case "ExtraLocation":
                    value = value with { Location = new LocalizedText("extra") };
                    break;
                case "LongActivity":
                    value = value with { Activity = new LocalizedText(new string('x', 65)) };
                    break;
                case "LongLocale":
                    value = value with { Activity = new LocalizedText(new string('x', 65), "task") };
                    break;
                case "ExtraPart":
                    value = value with { PartName = "extra" };
                    break;
                case "ExtraTool":
                    value = value with { ToolName = "extra" };
                    break;
                case "ExtraJob":
                    value = value with { SourceIdentifier = "extra" };
                    break;
                case "ExtraUtility":
                    value = value with { UtilityName = "extra" };
                    break;
                case "ExtraCompanionType":
                    value = value with { CompanionTypeId = ObjectTypeIds.MaintenancePrognosisType };
                    break;
                case "UnsupportedTarget":
                    value = value with { Target = new() { ToolKey = "Tool" } };
                    break;
                case "PartMissingName":
                    value = Part(value) with { PartName = null };
                    break;
                case "PartMissingLocation":
                    value = Part(value) with { Location = LocalizedText.Null };
                    break;
                case "JobMissingIdentifier":
                    value = value with
                    {
                        Kind = MachineToolPrognosisKind.ProductionJobEnd, Activity = LocalizedText.Null
                    };
                    break;
                case "UtilityMissingName":
                    value = value with { Kind = MachineToolPrognosisKind.UtilityChange, Activity = LocalizedText.Null };
                    break;
                case "ToolMissingLocation":
                    value = value with { Kind = MachineToolPrognosisKind.ToolChange, Activity = LocalizedText.Null };
                    break;
                case "TargetMissingKey":
                    value = Part(value) with { Target = new() };
                    break;
                case "TargetWrongKeyKind":
                    value = Part(value) with { Target = new() { ToolKey = "Tool" } };
                    break;
                case "TargetIncompletePart":
                    value = Part(value) with { Target = new() { PartKey = "Part" } };
                    break;
                case "TargetNodeAndKeys":
                    value = Part(value) with { Target = new() { NodeId = new NodeId("Foreign", 1), PartKey = "Part" } };
                    break;
                case "UnknownTarget":
                    value = Part(value) with { Target = new() { NodeId = new NodeId("Foreign", 1) } };
                    break;
                case "ToolLoadHasNodeId":
                    value = value with
                    {
                        Kind = MachineToolPrognosisKind.ToolLoad,
                        Activity = LocalizedText.Null,
                        Location = new LocalizedText("Magazine"),
                        Target = new() { ToolKey = "Tool" }
                    };
                    break;
                case "StaticRemoval":
                    return next with { Prognoses = new() };
                case "ChangedKind":
                    value = value with { Kind = MachineToolPrognosisKind.ManualActivity };
                    break;
                case "StaleRevision":
                    return next with { Revision = 0 };
                default:
                    throw new ArgumentException("Unknown test case.", nameof(violation));
            }
            return next with { Prognoses = new() { Entries = [value] } };
        }

        /// <summary>
        /// Supplies a valid part forecast before the test's isolated modification.
        /// </summary>
        private static MachineToolPrognosis Part(MachineToolPrognosis value) => value with
        {
            Kind = MachineToolPrognosisKind.PartLoad,
            Activity = LocalizedText.Null,
            PartName = "Part",
            Location = new LocalizedText("Workspace")
        };

        /// <summary>The shared model owner.</summary>
        private MachineToolRuntimeFixture m_fixture = null!;

        /// <summary>Distinct root identifiers within this test fixture.</summary>
        private int m_index;
    }
}
