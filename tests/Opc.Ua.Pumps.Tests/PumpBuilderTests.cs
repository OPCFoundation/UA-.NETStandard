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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Covers the builder that materialises the OPC 40223 surface. The point
    /// of interest is that children come from the model rather than being
    /// hand-built, so they carry the type, data type and structure a client
    /// browses for.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpBuilderTests
    {
        private PumpsServerFixture m_fixture = null!;
        private IPumpBuilder m_pump = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new PumpsServerFixture();
            await m_fixture.StartAsync();
            m_pump = await m_fixture.Manager.CreatePumpAsync(m_fixture.PumpName("Pump_1"));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync();
        }

        [Test]
        public void NothingIsMaterialisedUntilItIsAskedFor()
        {
            // The full PumpType surface is some 3000 nodes. A pump that
            // publishes all of them, nearly all empty, costs every client a
            // browse and a read per node to discover there is nothing there.
            Assert.Multiple(() =>
            {
                Assert.That(m_pump.Pump.Operational, Is.Null);
                Assert.That(m_pump.Pump.Events, Is.Null);
                Assert.That(m_pump.Pump.Configuration, Is.Null);
                Assert.That(m_pump.Pump.Maintenance, Is.Null);
                Assert.That(m_pump.Pump.Ports, Is.Null);
            });
        }

        /// <summary>
        /// All seven published classes preserve the model's enum DataType.
        /// </summary>
        [TestCase(DeviceTypeEnum.RotodynamicPump)]
        [TestCase(DeviceTypeEnum.PositiveDisplacementPump)]
        [TestCase(DeviceTypeEnum.ProcessVacuumPump)]
        [TestCase(DeviceTypeEnum.TurboVacuumPump)]
        [TestCase(DeviceTypeEnum.VacuumPump)]
        [TestCase(DeviceTypeEnum.LiquidPump)]
        [TestCase(DeviceTypeEnum.Pump)]
        public void PumpClassAcceptsOnlyPublishedValues(DeviceTypeEnum pumpClass)
        {
            m_pump.Design.Set(BrowseNames.PumpClass, Variant.From(pumpClass));
            var value = (BaseVariableState)m_pump.Design.Child(BrowseNames.PumpClass)!;
            Assert.That(value.WrappedValue.TryGetValue(out DeviceTypeEnum actual), Is.True);
            Assert.That(actual, Is.EqualTo(pumpClass));
            Assert.That(value.DataType, Is.EqualTo(ExpandedNodeId.ToNodeId(
                DataTypeIds.DeviceTypeEnum, m_fixture.Manager.SystemContext.NamespaceUris)));
        }

        /// <summary>
        /// A rejected class leaves the previous value unchanged.
        /// </summary>
        [TestCase(-1)]
        [TestCase(7)]
        public void PumpClassRejectsValuesOutsideThePublishedEnumeration(int invalid)
        {
            m_pump.Design.Set(BrowseNames.PumpClass, Variant.From(DeviceTypeEnum.RotodynamicPump));
            Assert.That(
                () => m_pump.Design.Set(BrowseNames.PumpClass, Variant.From((DeviceTypeEnum)invalid)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            var value = (BaseVariableState)m_pump.Design.Child(BrowseNames.PumpClass)!;
            Assert.That(value.WrappedValue.TryGetValue(out DeviceTypeEnum actual), Is.True);
            Assert.That(actual, Is.EqualTo(DeviceTypeEnum.RotodynamicPump));
        }

        /// <summary>
        /// Optional files use the common bounded FileType binding and session-owned handles.
        /// </summary>
        [Test]
        public async Task DocumentationFileUsesSharedReadOnlySessionHandlesAsync()
        {
            IFileTypeBinding binding = m_fixture.Manager.Runtime.BindDocumentationFile(
                m_pump, BrowseNames.OperationManual, ByteString.From("manual"u8), new FileTypeBinder());
            Assert.That(binding.File.Size!.Value, Is.EqualTo(6ul));
            Assert.That(binding.File.Writable!.Value, Is.False);
            Assert.That(binding.File.Read!.OnCallMethod2Async, Is.Not.Null);
            var session = new NodeId(42u);
            uint handle = await binding.OpenAsync(session, 1).ConfigureAwait(false);
            Assert.That(binding.File.OpenCount!.Value, Is.EqualTo(1));
            await Assert.ThatAsync(
                async () => await binding.CloseAsync(new NodeId(43u), handle).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            await binding.CloseAsync(session, handle).ConfigureAwait(false);
            Assert.That(binding.File.OpenCount!.Value, Is.Zero);
            await Assert.ThatAsync(
                async () => await binding.OpenAsync(session, 2).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(
                () => m_fixture.Manager.Runtime.BindDocumentationFile(
                    m_pump, BrowseNames.OperationManual, ByteString.From("replacement"u8), new FileTypeBinder()),
                Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// Markings are named FileType instances, not documentation links or placeholder nodes.
        /// </summary>
        [Test]
        public void MarkingFileUsesApplicationNamespaceAndRejectsDuplicateNames()
        {
            IFileTypeBinding binding = m_fixture.Manager.Runtime.BindMarkingFile(
                m_pump, "CE", ByteString.From("marking"u8), new FileTypeBinder());
            Assert.That(binding.File.BrowseName,
                Is.EqualTo(new QualifiedName("CE", m_fixture.Manager.InstanceNamespaceIndex)));
            Assert.That(binding.File.Parent!.BrowseName.Name, Is.EqualTo(BrowseNames.Markings));
            Assert.That(binding.File.TypeDefinitionId, Is.EqualTo(Opc.Ua.ObjectTypeIds.FileType));
            Assert.That(
                () => m_fixture.Manager.Runtime.BindMarkingFile(
                    m_pump, "CE", ByteString.From("duplicate"u8), new FileTypeBinder()),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(
                () => m_fixture.Manager.Runtime.BindDocumentationFile(
                    m_pump, BrowseNames.OperationManualLink, ByteString.From("not a file"u8), new FileTypeBinder()),
                Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// A provider sample keeps its acquisition timestamp and unavailable status.
        /// </summary>
        [Test]
        public void PublishingProviderValuePreservesSourceQualityAndTime()
        {
            IPumpGroupBuilder measurements = m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, 0);
            var sample = new DataValue(
                Variant.From(12.5), StatusCodes.BadOutOfService, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            m_fixture.Manager.Runtime.PublishValue(measurements, BrowseNames.MassFlow, sample);
            var variable = (BaseVariableState)measurements.Child(BrowseNames.MassFlow)!;
            Assert.That(variable.WrappedValue, Is.EqualTo(sample.WrappedValue));
            Assert.That(variable.StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(variable.Timestamp, Is.EqualTo(sample.SourceTimestamp));
        }

        /// <summary>
        /// Publication requires an existing variable rather than using the builder's lazy creation path.
        /// </summary>
        [Test]
        public void PublishingDoesNotMaterializeAnAbsentOptionalVariable()
        {
            IPumpGroupBuilder measurements = m_pump.Measurements;
            var name = new QualifiedName(BrowseNames.MassFlow, m_fixture.Manager.NamespaceIndices.Pumps);
            Assert.That(measurements.Node.FindChild(m_fixture.Manager.SystemContext, name), Is.Null);
            var sample = new DataValue(Variant.From(12.5));
            Assert.That(
                () => m_fixture.Manager.Runtime.PublishValue(measurements, BrowseNames.MassFlow, sample),
                Throws.TypeOf<ArgumentException>());
            Assert.That(measurements.Node.FindChild(m_fixture.Manager.SystemContext, name), Is.Null);
        }

        /// <summary>
        /// A stopped runtime cannot publish through a previously obtained group.
        /// </summary>
        [Test]
        public async Task PublishingRejectsStoppedRuntimeWithoutChangingSourceValueAsync()
        {
            IPumpGroupBuilder measurements = m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, 2);
            var variable = (BaseVariableState)measurements.Child(BrowseNames.MassFlow)!;
            var sample = new DataValue(Variant.From(12.5));
            await m_fixture.Manager.Runtime.DisposeAsync();
            Assert.That(
                () => m_fixture.Manager.Runtime.PublishValue(measurements, BrowseNames.MassFlow, sample),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From(2d)));
        }

        /// <summary>
        /// A removed variable is not updated merely because its parent still holds its construction handle.
        /// </summary>
        [Test]
        public async Task PublishingRejectsRemovedVariableWithoutChangingSourceValueAsync()
        {
            IPumpGroupBuilder measurements = m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, 2);
            var variable = (BaseVariableState)measurements.Child(BrowseNames.MassFlow)!;
            await m_fixture.Manager.DeleteNodeAsync(
                m_fixture.Manager.SystemContext, variable.NodeId, CancellationToken.None);
            var sample = new DataValue(Variant.From(12.5));
            Assert.That(
                () => m_fixture.Manager.Runtime.PublishValue(measurements, BrowseNames.MassFlow, sample),
                Throws.TypeOf<ArgumentException>());
            Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From(2d)));
        }

        /// <summary>
        /// Control and both valve actuations retain their declared types without invoking equipment.
        /// </summary>
        [Test]
        public void ControlAndValveActuationPublishDeclaredModelMembers()
        {
            IPumpGroupBuilder control = m_pump.Control.SetAnalog(BrowseNames.CommandVariable, 50);
            Assert.That(((BaseObjectState)control.Node).TypeDefinitionId,
                Is.EqualTo(ExpandedNodeId.ToNodeId(ObjectTypeIds.ControlType,
                    m_fixture.Manager.SystemContext.NamespaceUris)));
            Assert.That(((BaseVariableState)control.Child(BrowseNames.CommandVariable)!).WrappedValue,
                Is.EqualTo(Variant.From(50.0)));
            foreach (string name in new[] { BrowseNames.BypassActuation, BrowseNames.ThrottleValveActuation })
            {
                IPumpGroupBuilder actuation = m_pump.Operational.Nested(name)
                    .SetDiscrete(BrowseNames.FaultAction, false);
                var variable = (BaseVariableState)actuation.Child(BrowseNames.FaultAction)!;
                Assert.That(variable.WrappedValue, Is.EqualTo(Variant.From(false)));
                Assert.That(variable.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
                Assert.That(variable.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Boolean));
            }
        }

        [Test]
        public void AskingForAGroupMaterialisesIt()
        {
            IPumpGroupBuilder measurements = m_pump.Measurements;

            Assert.Multiple(() =>
            {
                Assert.That(measurements.Node, Is.Not.Null);
                Assert.That(m_pump.Pump.Operational, Is.Not.Null, "Operational");
                Assert.That(
                    m_pump.Pump.Operational!.Measurements,
                    Is.Not.Null,
                    "Operational/Measurements");
            });
        }

        [Test]
        public void AnAnalogMeasurementCarriesItsValueUnitAndRange()
        {
            m_pump.Measurements.SetAnalog(
                BrowseNames.DifferentialPressure,
                350_000.0,
                new EUInformation { DisplayName = new LocalizedText("Pa") },
                new Range { Low = 0, High = 1_000_000 });

            BaseAnalogState<double>? pressure =
                m_pump.Pump.Operational!.Measurements!.DifferentialPressure;

            Assert.That(pressure, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(pressure!.Value, Is.EqualTo(350_000.0));
                Assert.That(StatusCode.IsGood(pressure.StatusCode), Is.True);
                Assert.That(
                    pressure.FindChild(
                        m_fixture.Manager.SystemContext,
                        new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits, 0)),
                    Is.Not.Null,
                    "EngineeringUnits");
                Assert.That(
                    pressure.FindChild(
                        m_fixture.Manager.SystemContext,
                        new QualifiedName(Opc.Ua.BrowseNames.EURange, 0)),
                    Is.Not.Null,
                    "EURange");
            });
        }

        [Test]
        public void ASupervisionSignalIsATwoStateDiscreteVariable()
        {
            m_pump.Supervision(BrowseNames.SupervisionProcessFluid)
                .SetDiscrete(BrowseNames.Cavitation, true);

            TwoStateDiscreteState? cavitation =
                m_pump.Pump.Events!.SupervisionProcessFluid!.Cavitation;

            Assert.That(cavitation, Is.Not.Null);
            Assert.That(cavitation!.Value, Is.True);
        }

        [Test]
        public void ASignalIsADiscreteObjectWhoseValueSitsOneLevelDown()
        {
            // SignalsType holds DiscreteInputObjectType objects, not
            // variables. A builder that wrote to the object itself would
            // publish nothing a client could read.
            m_pump.Signals.SetDiscrete(BrowseNames.PumpOperation, true);

            DiscreteInputObjectState? signal =
                m_pump.Pump.Operational!.Signals!.PumpOperation;

            Assert.That(signal, Is.Not.Null);
            Assert.That(signal!.DiscreteInputValue, Is.Not.Null);
            Assert.That(signal.DiscreteInputValue!.Value, Is.True);
        }

        [Test]
        public void AnActuationRequestIsADiscreteOutputObject()
        {
            m_pump.PumpActuation.SetDiscrete(BrowseNames.FlushValveRequest, true);

            DiscreteOutputObjectState? request =
                m_pump.Pump.Operational!.PumpActuation!.FlushValveRequest;

            Assert.That(request, Is.Not.Null);
            Assert.That(request!.DiscreteOutputValue, Is.Not.Null);
            Assert.That(request.DiscreteOutputValue!.Value, Is.True);
        }

        [Test]
        public void TheDesignGroupReachesTheWholeSpecificationSurface()
        {
            // DesignType declares 78 optional parameters. A sample across the
            // range proves the builder is not limited to a hand-written list.
            m_pump.Design
                .SetAnalog(BrowseNames.MaximumAllowableWorkingPressure, 1_600_000)
                .SetAnalog(BrowseNames.Shut_OffHead, 82.0)
                .SetAnalog(BrowseNames.SpecificSpeed, 24.0)
                .SetAnalog(BrowseNames.MeanTimebetweenFailures, 43_800.0)
                .SetAnalog(BrowseNames.SoundPressureLevel, 71.5);

            DesignState design = m_pump.Pump.Configuration!.Design!;

            Assert.Multiple(() =>
            {
                Assert.That(
                    design.MaximumAllowableWorkingPressure!.Value,
                    Is.EqualTo(1_600_000.0));
                Assert.That(design.Shut_OffHead!.Value, Is.EqualTo(82.0));
                Assert.That(design.SpecificSpeed!.Value, Is.EqualTo(24.0));
                Assert.That(design.MeanTimebetweenFailures!.Value, Is.EqualTo(43_800.0));
                Assert.That(design.SoundPressureLevel!.Value, Is.EqualTo(71.5));
            });
        }

        [Test]
        public void AMaterialisedChildCarriesTheModellingMetadataAClientBrowsesFor()
        {
            // The generic NodeState.CreateChild path creates a child of the
            // right CLR type and browse name but leaves ReferenceTypeId,
            // TypeDefinitionId and DataType unset. Such a node can be read by
            // NodeId and looks correct from inside the server, yet it is
            // invisible to every client: a Browse filtered on
            // HierarchicalReferences - which is all of them - has no reference
            // type to match. The builder completes each child from its
            // instance declaration to close that, and this is the guard.
            m_pump.WithNameplate(new PumpNameplate
            {
                NodeId = NodeId.Null,
                Model = new LocalizedText("PumpX-2000"),
                CountryOfOrigin = "DE"
            });

            PumpIdentificationState identification = m_pump.Pump.Identification!;
            BaseInstanceState model = Child(identification, Opc.Ua.Di.BrowseNames.Model)!;
            BaseInstanceState country = identification.CountryOfOrigin!;

            Assert.Multiple(() =>
            {
                Assert.That(
                    model.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasProperty),
                    "Model reference type");
                Assert.That(
                    model.TypeDefinitionId,
                    Is.EqualTo(Opc.Ua.Types.VariableTypeIds.PropertyType),
                    "Model type definition");
                Assert.That(
                    ((BaseVariableState)model).DataType,
                    Is.EqualTo(Opc.Ua.DataTypeIds.LocalizedText),
                    "Model data type");
                Assert.That(
                    ((BaseVariableState)model).ValueRank,
                    Is.EqualTo(ValueRanks.Scalar),
                    "Model value rank");

                Assert.That(
                    country.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasProperty),
                    "CountryOfOrigin reference type");
                Assert.That(
                    ((BaseVariableState)country).DataType,
                    Is.EqualTo(Opc.Ua.DataTypeIds.String),
                    "CountryOfOrigin data type");
            });
        }

        [Test]
        public void AnUndeclaredChildIsRejected()
        {
            // Silently creating the node would leave a client unable to tell a
            // vendor extension from a typo.
            ArgumentException? error = Assert.Throws<ArgumentException>(
                () => m_pump.Measurements.Set("NotAPumpsVariable", Variant.From(1.0)));
            Assert.That(error!.Message, Does.Contain("NotAPumpsVariable"));
        }

        [Test]
        public void AWrittenValueIsPublishedToMonitoredItems()
        {
            // The node manager reports data changes by exception: a value a
            // client subscribed to only reaches it once the change is
            // published, which the builder does on every write.
            m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, 12.5);
            BaseInstanceState flow = m_pump.Measurements.Child(BrowseNames.MassFlow)!;
            int published = 0;
            flow.OnStateChanged = (_, _, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Value) != 0)
                {
                    published++;
                }
            };

            m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, 13.5);
            m_pump.Signals.SetDiscrete(BrowseNames.PumpOperation, true);
            m_pump.Supervision(BrowseNames.SupervisionProcessFluid)
                .SetDiscrete(BrowseNames.Cavitation, true);

            Assert.That(published, Is.EqualTo(1), "the MassFlow write must be published");
        }

        [Test]
        public void AVibrationMeasurementIsCreatedFromThePlaceholder()
        {
            IPumpGroupBuilder vibration = m_pump.Measurements.AddVibration("DriveEndBearing")
                .SetAnalog(BrowseNames.OverallVibrationVelocityRMS, 2.8);

            NodeId vibrationType = PumpsModel.TypeNodeId(
                ObjectTypes.VibrationMeasurementType,
                m_fixture.Manager.SystemContext.NamespaceUris);
            var node = (BaseInstanceState)vibration.Node;
            Assert.Multiple(() =>
            {
                Assert.That(node.BrowseName.Name, Is.EqualTo("DriveEndBearing"));
                Assert.That(node.TypeDefinitionId, Is.EqualTo(vibrationType));
                Assert.That(node.ReferenceTypeId, Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(node.Parent, Is.SameAs(m_pump.Measurements.Node));
                Assert.That(vibration.Child(BrowseNames.OverallVibrationVelocityRMS), Is.Not.Null);
                Assert.That(
                    m_pump.Measurements.AddVibration("DriveEndBearing").Node,
                    Is.SameAs(node),
                    "asking again for an existing name returns the same instance");
            });
        }

        [Test]
        public void APlaceholderIsRejectedWithAHintToItsFactory()
        {
            ArgumentException? error = Assert.Throws<ArgumentException>(
                () => m_pump.Measurements.Nested("Vibration"));
            Assert.That(error!.Message, Does.Contain("<Vibration>").And.Contain("AddVibration"));
        }

        [Test]
        public void AVibrationMeasurementIsRejectedOutsideAMeasurementsGroup()
        {
            Assert.Throws<ArgumentException>(() => m_pump.Design.AddVibration("DriveEndBearing"));
        }

        [Test]
        public void TheNameplateIsFilledFromTheSharedContract()
        {
            m_pump.WithNameplate(new PumpNameplate
            {
                NodeId = NodeId.Null,
                Manufacturer = new LocalizedText("Acme Pumps"),
                SerialNumber = "SN-001",
                Model = new LocalizedText("PumpX-2000"),
                Location = "Plant 1 / Utility Skid",
                YearOfConstruction = 2025,
                CountryOfOrigin = "DE",
                ArticleNumber = "ART-4711"
            });

            PumpIdentificationState identification = m_pump.Pump.Identification!;

            Assert.Multiple(() =>
            {
                // OPC 10000-100 fields, in the DI namespace.
                Assert.That(
                    ReadText(identification, Opc.Ua.Di.BrowseNames.SerialNumber),
                    Is.EqualTo("SN-001"));
                // OPC 40001-1 fields, in the Machinery namespace.
                Assert.That(
                    ReadText(identification, Opc.Ua.Machinery.BrowseNames.Location),
                    Is.EqualTo("Plant 1 / Utility Skid"));
                // OPC 40223 fields, in the Pumps namespace.
                Assert.That(identification.CountryOfOrigin, Is.Not.Null);
                Assert.That(identification.CountryOfOrigin!.Value, Is.EqualTo("DE"));
                Assert.That(identification.ArticleNumber!.Value, Is.EqualTo("ART-4711"));

                // A field the record does not carry stays unmaterialised
                // rather than being published as an empty string.
                Assert.That(identification.Supplier, Is.Null);
            });
        }

        [Test]
        public void PortsAreAddedByKindAndCarryTheirOwnGroups()
        {
            IPumpGroupBuilder inlet = m_pump.AddPort(PumpPortKind.InletConnection, "Suction");
            IPumpGroupBuilder drive = m_pump.AddPort(PumpPortKind.Drive, "Motor");

            inlet.Set(BrowseNames.Direction, Variant.From(PortDirectionEnum.In));

            Assert.Multiple(() =>
            {
                Assert.That(m_pump.Pump.Ports, Is.Not.Null);
                Assert.That(inlet.Node, Is.InstanceOf<InletConnectionPortState>());
                Assert.That(drive.Node, Is.InstanceOf<DrivePortState>());
                Assert.That(
                    inlet.Node.BrowseName,
                    Is.EqualTo(new QualifiedName(
                        "Suction",
                        m_fixture.Manager.NamespaceIndices.Pumps)));

                // An inlet connection port has all four groups; a drive port
                // has design and measurements only.
                var inletPort = (InletConnectionPortState)inlet.Node;
                inlet.Nested(BrowseNames.Design);
                inlet.Nested(BrowseNames.SystemRequirements);
                Assert.That(inletPort.Design, Is.Not.Null);
                Assert.That(inletPort.SystemRequirements, Is.Not.Null);

                var drivePort = (DrivePortState)drive.Node;
                drive.Nested(BrowseNames.Measurements);
                Assert.That(drivePort.Measurements, Is.Not.Null);
            });
        }

        [Test]
        public void MaintenanceCategoriesAreReachable()
        {
            m_pump.MaintenanceCategory(BrowseNames.GeneralMaintenance)
                .Set(BrowseNames.StateOfTheItem, Variant.From(StateOfTheItemEnum.OperatingState))
                .SetAnalog(BrowseNames.OperatingTime, 1234.0);
            m_pump.MaintenanceCategory(BrowseNames.BreakdownMaintenance)
                .SetDiscrete(BrowseNames.Failure, false);

            MaintenanceGroupState maintenance = m_pump.Pump.Maintenance!;

            Assert.Multiple(() =>
            {
                Assert.That(
                    maintenance.GeneralMaintenance!.OperatingTime!.Value,
                    Is.EqualTo(1234.0));
                Assert.That(maintenance.BreakdownMaintenance!.Failure!.Value, Is.False);
            });
        }

        private BaseInstanceState? Child(NodeState parent, string browseName)
        {
            foreach (ushort index in m_fixture.Manager.NamespaceIndices.SearchOrder)
            {
                BaseInstanceState? child = parent.FindChild(
                    m_fixture.Manager.SystemContext,
                    new QualifiedName(browseName, index));
                if (child != null)
                {
                    return child;
                }
            }
            return null;
        }

        private string? ReadText(NodeState parent, string browseName)
        {
            foreach (ushort index in m_fixture.Manager.NamespaceIndices.SearchOrder)
            {
                if (parent.FindChild(
                        m_fixture.Manager.SystemContext,
                        new QualifiedName(browseName, index)) is BaseVariableState variable)
                {
                    return variable.WrappedValue.TryGetValue(out string text) ? text : null;
                }
            }
            return null;
        }
    }
}
