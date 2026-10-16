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
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Checks equipment rejection atomicity and every specified life indication at the public controller seam.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolEquipmentValidationTests
    {
        /// <summary>
        /// Starts one real model owner while preserving a separate machine per test.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new MachineToolRuntimeFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the server and its stores.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync() => await m_fixture.DisposeAsync().ConfigureAwait(false);

        /// <summary>
        /// Invalid snapshots must not change any accepted tool identity, lifetime number, or revision.
        /// </summary>
        [TestCase("MissingInventory")]
        [TestCase("NullTool")]
        [TestCase("DuplicateKey")]
        [TestCase("DuplicateIdentifier")]
        [TestCase("DuplicateControl")]
        [TestCase("EmptyKey")]
        [TestCase("LongKey")]
        [TestCase("LongIdentifier")]
        [TestCase("LongName")]
        [TestCase("EmptyLocation")]
        [TestCase("UnknownKind")]
        [TestCase("CompanionWithoutFactory")]
        [TestCase("UnexpectedCompanionType")]
        [TestCase("MissingControl")]
        [TestCase("UnknownManagement")]
        [TestCase("UnknownLockReason")]
        [TestCase("GroupWithoutSecondIdentifier")]
        [TestCase("NumberWithSecondIdentifier")]
        [TestCase("MissingSourceTime")]
        [TestCase("BadLastUsageType")]
        [TestCase("UnsetGoodLastUsage")]
        [TestCase("MissingLife")]
        [TestCase("NullLife")]
        [TestCase("DuplicateLife")]
        [TestCase("TooManyLifeEntries")]
        [TestCase("UnknownLifeNamespace")]
        [TestCase("UnknownIndication")]
        [TestCase("MissingUnits")]
        [TestCase("WrongUnits")]
        [TestCase("WrongUnitNamespace")]
        [TestCase("NoValue")]
        [TestCase("NullGoodValue")]
        [TestCase("StringValue")]
        [TestCase("ArrayValue")]
        [TestCase("NanValue")]
        [TestCase("InfiniteValue")]
        [TestCase("FractionalCount")]
        [TestCase("ValueMissingTime")]
        [TestCase("LifeMetadataMissingTime")]
        [TestCase("InvalidStart")]
        [TestCase("InvalidWarning")]
        [TestCase("InvalidLimit")]
        [TestCase("DescendingUpThreshold")]
        [TestCase("AscendingDownThreshold")]
        [TestCase("UInt64AdjacentThresholdInversion")]
        [TestCase("ToolContainsTool")]
        [TestCase("NestedMultiTool")]
        [TestCase("MultiToolHasController")]
        [TestCase("StaticRemoval")]
        [TestCase("SchemaChange")]
        [TestCase("StaleRevision")]
        public async Task InvalidEquipmentPreservesCommittedObservationAsync(string violation)
        {
            MachineToolSnapshot initial = MachineToolEquipmentTests.Example();
            MachineToolState state = CreateMachine();
            await using var controller = new MachineToolEquipmentController(m_fixture.Context, state,
                new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife |
                        (violation == "StaticRemoval" ? MachineToolFeatures.None : MachineToolFeatures.DynamicTools),
                    PublishFinalObservationsAsync = m_fixture.Context.Manager.WaitForValuePublicationAsync,
                    MaximumTextLength = 64,
                    MaximumLifeEntriesPerTool = 2
                });
            await controller.InitializeAsync(initial).ConfigureAwait(false);
            MachineToolSnapshot bad = Invalid(initial, violation);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await controller.ApplyAsync(bad).ConfigureAwait(false));
            Assert.That(controller.Observation, Is.SameAs(initial));
            var tool = (ToolState)state.Equipment!.Tools!.FindChild(m_fixture.Context.Context,
                new QualifiedName("T1", m_fixture.Context.InstanceNamespaceIndex))!;
            Assert.That(tool.Identifier!.Value, Is.EqualTo("GLOBAL-123"));
            var life = (ToolLifeState)tool.ToolLife!.FindChild(m_fixture.Context.Context,
                new QualifiedName("Parts", m_fixture.Context.InstanceNamespaceIndex))!;
            Assert.That(life.WrappedValue.TryGetValue(out ulong count), Is.True);
            Assert.That(count, Is.EqualTo(18_446_744_073_709_551_610ul));
        }

        /// <summary>
        /// Every numeric representation remains unchanged; limits can use a different exact Number representation.
        /// </summary>
        [TestCase("SByte")]
        [TestCase("Byte")]
        [TestCase("Int16")]
        [TestCase("UInt16")]
        [TestCase("Int32")]
        [TestCase("UInt32")]
        [TestCase("Int64")]
        [TestCase("UInt64")]
        [TestCase("Float")]
        [TestCase("Double")]
        [TestCase("Subnormal")]
        [TestCase("LargeDouble")]
        [TestCase("NegativeRemaining")]
        public async Task LifeNumbersRetainTheirOriginalRepresentationAsync(string representation)
        {
            Variant value = representation switch
            {
                "SByte" => Variant.From((sbyte)3),
                "Byte" => Variant.From((byte)3),
                "Int16" => Variant.From((short)3),
                "UInt16" => Variant.From((ushort)3),
                "Int32" => Variant.From(3),
                "UInt32" => Variant.From(3u),
                "Int64" => Variant.From(3L),
                "UInt64" => Variant.From(3ul),
                "Float" => Variant.From(3.25f),
                "Double" => Variant.From(3.25),
                "Subnormal" => Variant.From(double.Epsilon),
                "LargeDouble" => Variant.From(double.MaxValue),
                _ => Variant.From(-0.25)
            };
            MachineToolSnapshot example = MachineToolEquipmentTests.Example();
            MachineToolTool tool = example.Equipment!.Tools[0];
            MachineToolToolLife observation = tool.Life[0] with
            {
                Indication = ToolLifeIndication.Other,
                Value = MachineToolEquipmentTests.Sample(value, tool.Sample),
                StartValue = MachineToolEquipmentTests.Sample(Variant.From(-1), tool.Sample),
                WarningValue = MachineToolEquipmentTests.Sample(Variant.From(10.5), tool.Sample),
                LimitValue = MachineToolEquipmentTests.Sample(Variant.From(12ul), tool.Sample)
            };
            var snapshot = example with
            {
                Equipment = example.Equipment with { Tools = [tool with { Life = [observation] }] }
            };
            MachineToolState machine = CreateMachine();
            await using var controller = new MachineToolEquipmentController(m_fixture.Context, machine,
                new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife
                });
            await controller.InitializeAsync(snapshot).ConfigureAwait(false);
            var state = (ToolState)controller.State.Tools!.FindChild(m_fixture.Context.Context,
                new QualifiedName("T1", m_fixture.Context.InstanceNamespaceIndex))!;
            var life = (ToolLifeState)state.ToolLife!.FindChild(m_fixture.Context.Context,
                new QualifiedName("Parts", m_fixture.Context.InstanceNamespaceIndex))!;
            Assert.That(life.WrappedValue, Is.EqualTo(value));
            Assert.That(life.WrappedValue.TypeInfo, Is.EqualTo(value.TypeInfo));
            Assert.That(life.Timestamp, Is.EqualTo(observation.Value.SourceTimestamp));
        }

        /// <summary>
        /// The released indication table restricts time to hours, counts to one, and distances to the stated units.
        /// </summary>
        [TestCase(ToolLifeIndication.Time, 0x485552)]
        [TestCase(ToolLifeIndication.NumberOfParts, 0x433632)]
        [TestCase(ToolLifeIndication.NumberOfUsages, 0x433632)]
        [TestCase(ToolLifeIndication.Feed_Distance, 0x4D4D54)]
        [TestCase(ToolLifeIndication.Feed_Distance, 0x4D5452)]
        [TestCase(ToolLifeIndication.Cutting_Distance, 0x4B4D54)]
        [TestCase(ToolLifeIndication.Length, 0x3448)]
        [TestCase(ToolLifeIndication.Diameter, 0x4D4D54)]
        [TestCase(ToolLifeIndication.Other, 0x433632)]
        public async Task LifeIndicationsUseExactUnitsAndCountdownThresholdsAsync(
            ToolLifeIndication indication, int unit)
        {
            MachineToolSnapshot example = MachineToolEquipmentTests.Example();
            MachineToolTool tool = example.Equipment!.Tools[0];
            MachineToolToolLife value = tool.Life[0] with
            {
                Indication = indication,
                EngineeringUnits = MachineToolEquipmentTests.Unit(unit, "unit", "reported unit"),
                IsCountingUp = false,
                Value = MachineToolEquipmentTests.Sample(Variant.From(2d), tool.Sample),
                StartValue = MachineToolEquipmentTests.Sample(Variant.From(8u), tool.Sample),
                WarningValue = MachineToolEquipmentTests.Sample(Variant.From(2f), tool.Sample),
                LimitValue = MachineToolEquipmentTests.Sample(Variant.From(0L), tool.Sample)
            };
            await using var controller = new MachineToolEquipmentController(m_fixture.Context, CreateMachine(),
                new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife
                });
            await controller.InitializeAsync(example with
            {
                Equipment = example.Equipment with { Tools = [tool with { Life = [value] }] }
            }).ConfigureAwait(false);
            var single = (ToolState)controller.State.Tools!.FindChild(m_fixture.Context.Context,
                new QualifiedName("T1", m_fixture.Context.InstanceNamespaceIndex))!;
            var life = (ToolLifeState)single.ToolLife!.FindChild(m_fixture.Context.Context,
                new QualifiedName("Parts", m_fixture.Context.InstanceNamespaceIndex))!;
            Assert.That(life.IsCountingUp!.Value, Is.False);
            Assert.That(life.EngineeringUnits!.Value!.UnitId, Is.EqualTo(unit));
            Assert.That(life.Indication!.Value, Is.EqualTo(indication));
            Assert.That(life.WarningValue!.WrappedValue.TryGetValue(out float warning), Is.True);
            Assert.That(warning, Is.EqualTo(2f));
            Assert.That(life.LimitValue!.WrappedValue.TryGetValue(out long limit), Is.True);
            Assert.That(limit, Is.Zero);
        }

        /// <summary>
        /// An enabled feature must have its explicit observations, and dependent features need their real capabilities.
        /// </summary>
        [TestCase(MachineToolFeatures.ToolLife)]
        [TestCase(MachineToolFeatures.DynamicTools)]
        [TestCase(MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools)]
        public void IncompleteFeatureConfigurationsFailSetup(MachineToolFeatures features)
        {
            Assert.That(() => new MachineToolEquipmentController(m_fixture.Context, CreateMachine(),
                new MachineToolEquipmentOptions { Features = features }), Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Every independent bound is enforced before a generated inventory is allocated.
        /// </summary>
        [TestCase(0, 1, 1, 1)]
        [TestCase(65537, 1, 1, 1)]
        [TestCase(1, 0, 1, 1)]
        [TestCase(1, 257, 1, 1)]
        [TestCase(1, 1, 0, 1)]
        [TestCase(1, 1, 65537, 1)]
        [TestCase(1, 1, 1, 0)]
        [TestCase(1, 1, 1, 65537)]
        public void EquipmentBoundsRejectInvalidConfiguration(int tools, int lives, int total, int text)
        {
            Assert.That(() => new MachineToolEquipmentController(m_fixture.Context, CreateMachine(),
                new MachineToolEquipmentOptions
                {
                    MaximumTools = tools,
                    MaximumLifeEntriesPerTool = lives,
                    MaximumElements = total,
                    MaximumTextLength = text
                }), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Creates a fresh generated root for each test, without registering unrelated observations.
        /// </summary>
        private MachineToolState CreateMachine() => m_fixture.Context.Context.CreateInstanceOfMachineToolType(
            m_fixture.Context.MachinesFolder,
            new QualifiedName("Validation" + ++m_index, m_fixture.Context.InstanceNamespaceIndex));

        /// <summary>
        /// Changes exactly the independent condition named by the test case.
        /// </summary>
        private static MachineToolSnapshot Invalid(MachineToolSnapshot original, string violation)
        {
            MachineToolTool tool = original.Equipment!.Tools[0];
            MachineToolToolLife life = tool.Life[0];
            MachineToolToolControl control = tool.Control!;
            MachineToolSampleInfo sample = tool.Sample;
            var next = original with { Revision = 1 };
            switch (violation)
            {
                case "MissingInventory":
                    return next with { Equipment = null };
                case "NullTool":
                    return next with { Equipment = new() { Tools = [null!] } };
                case "DuplicateKey":
                    return next with { Equipment = new() { Tools = [tool, tool] } };
                case "DuplicateIdentifier":
                    return next with { Equipment = new() { Tools = [tool, tool with { Key = "Other" }] } };
                case "DuplicateControl":
                    return next with
                    {
                        Equipment = new() { Tools = [tool, tool with { Key = "Other", Identifier = "Other" }] }
                    };
                case "EmptyKey":
                    tool = tool with { Key = string.Empty };
                    break;
                case "LongKey":
                    tool = tool with { Key = new string('x', 65) };
                    break;
                case "LongIdentifier":
                    tool = tool with { Identifier = new string('x', 65) };
                    break;
                case "LongName":
                    tool = tool with { Name = new string('x', 65) };
                    break;
                case "EmptyLocation":
                    tool = tool with { Location = new(string.Empty, 1) };
                    break;
                case "UnknownKind":
                    tool = tool with { Kind = (MachineToolToolKind)100 };
                    break;
                case "CompanionWithoutFactory":
                    tool = tool with { Kind = MachineToolToolKind.Companion };
                    break;
                case "UnexpectedCompanionType":
                    tool = tool with { CompanionTypeId = ObjectTypeIds.ToolType };
                    break;
                case "MissingControl":
                    tool = tool with { Control = null };
                    break;
                case "UnknownManagement":
                    tool = tool with { Control = control with { Interpretation = (ToolManagement)99 } };
                    break;
                case "UnknownLockReason":
                    tool = tool with { Control = control with { ReasonForLocking = (ToolLocked)99 } };
                    break;
                case "GroupWithoutSecondIdentifier":
                    tool = tool with { Control = control with { Interpretation = ToolManagement.GroupBased } };
                    break;
                case "NumberWithSecondIdentifier":
                    tool = tool with { Control = control with { ControlIdentifier2 = 1 } };
                    break;
                case "MissingSourceTime":
                    tool = tool with { Sample = new(default, StatusCodes.Good) };
                    break;
                case "BadLastUsageType":
                    tool = tool with
                    {
                        Control = control with
                        {
                            LastUsage = MachineToolEquipmentTests.Sample(Variant.From("not a time"), sample)
                        }
                    };
                    break;
                case "UnsetGoodLastUsage":
                    tool = tool with
                    {
                        Control = control with
                        {
                            LastUsage = MachineToolEquipmentTests.Sample(Variant.From(default(DateTimeUtc)), sample)
                        }
                    };
                    break;
                case "MissingLife":
                    tool = tool with { Life = [] };
                    break;
                case "NullLife":
                    tool = tool with { Life = [null!] };
                    break;
                case "DuplicateLife":
                    tool = tool with { Life = [life, life] };
                    break;
                case "TooManyLifeEntries":
                    tool = tool with { Life = [life, life, life] };
                    break;
                case "UnknownLifeNamespace":
                    life = life with { NamespaceUri = "urn:absent" };
                    break;
                case "UnknownIndication":
                    life = life with { Indication = (ToolLifeIndication)99 };
                    break;
                case "MissingUnits":
                    life = life with { EngineeringUnits = null };
                    break;
                case "WrongUnits":
                    life = life with { EngineeringUnits = MachineToolEquipmentTests.Unit(0x534543, "s", "second") };
                    break;
                case "WrongUnitNamespace":
                    life = life with
                    {
                        EngineeringUnits = new EUInformation { NamespaceUri = "urn:other", UnitId = 0x433632 }
                    };
                    break;
                case "NoValue":
                    life = life with { Value = DataValue.Null };
                    break;
                case "NullGoodValue":
                    life = life with { Value = MachineToolEquipmentTests.Sample(Variant.Null, sample) };
                    break;
                case "StringValue":
                    life = life with { Value = MachineToolEquipmentTests.Sample(Variant.From("NaN"), sample) };
                    break;
                case "ArrayValue":
                    life = life with
                    {
                        Value = MachineToolEquipmentTests.Sample(Variant.From((ArrayOf<int>)[1, 2]), sample)
                    };
                    break;
                case "NanValue":
                    life = life with { Value = MachineToolEquipmentTests.Sample(Variant.From(double.NaN), sample) };
                    break;
                case "InfiniteValue":
                    life = life with
                    {
                        Value = MachineToolEquipmentTests.Sample(Variant.From(float.PositiveInfinity), sample)
                    };
                    break;
                case "FractionalCount":
                    life = life with { Value = MachineToolEquipmentTests.Sample(Variant.From(1.5), sample) };
                    break;
                case "ValueMissingTime":
                    life = life with { Value = new DataValue(1ul) };
                    break;
                case "LifeMetadataMissingTime":
                    life = life with { Sample = new(default, StatusCodes.Good) };
                    break;
                case "InvalidStart":
                    life = life with { StartValue = MachineToolEquipmentTests.Sample(Variant.From(true), sample) };
                    break;
                case "InvalidWarning":
                    life = life with { WarningValue = MachineToolEquipmentTests.Sample(Variant.From(false), sample) };
                    break;
                case "InvalidLimit":
                    life = life with { LimitValue = MachineToolEquipmentTests.Sample(Variant.From("100"), sample) };
                    break;
                case "DescendingUpThreshold":
                    life = life with
                    {
                        StartValue = MachineToolEquipmentTests.Sample(Variant.From(ulong.MaxValue), sample)
                    };
                    break;
                case "AscendingDownThreshold":
                    life = life with { IsCountingUp = false };
                    break;
                case "UInt64AdjacentThresholdInversion":
                    life = life with
                    {
                        WarningValue = MachineToolEquipmentTests.Sample(Variant.From(ulong.MaxValue), sample),
                        LimitValue = MachineToolEquipmentTests.Sample(Variant.From(ulong.MaxValue - 1), sample)
                    };
                    break;
                case "ToolContainsTool":
                    tool = tool with { Tools = [tool] };
                    break;
                case "NestedMultiTool":
                    tool = tool with
                    {
                        Kind = MachineToolToolKind.MultiTool,
                        Control = null,
                        Life = [],
                        Tools = [new MachineToolTool { Key = "Nested", Kind = MachineToolToolKind.MultiTool }]
                    };
                    break;
                case "MultiToolHasController":
                    tool = tool with { Kind = MachineToolToolKind.MultiTool, Life = [] };
                    break;
                case "StaticRemoval":
                    return next with { Equipment = new() };
                case "SchemaChange":
                    tool = tool with { Name = null };
                    break;
                case "StaleRevision":
                    return next with { Revision = 0 };
                default:
                    throw new ArgumentException("Unknown test case.", nameof(violation));
            }
            if (!ReferenceEquals(life, original.Equipment.Tools[0].Life[0]))
            {
                tool = tool with { Life = [life] };
            }
            return next with { Equipment = original.Equipment with { Tools = [tool] } };
        }

        /// <summary>The shared real model owner.</summary>
        private MachineToolRuntimeFixture m_fixture = null!;

        /// <summary>Allocates independent test roots without key collisions.</summary>
        private int m_index;
    }
}
