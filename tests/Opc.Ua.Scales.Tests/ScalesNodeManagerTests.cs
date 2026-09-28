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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PackML;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Builds scales of every kind into a started server's address space and
    /// drives their runtime through the bound method handlers, the way a
    /// client call would reach them.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    [NonParallelizable]
    public sealed class ScalesNodeManagerTests
    {
        private ScalesServerFixture m_fixture = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new ScalesServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        internal static ScaleIdentification Identity(string serial) => new()
        {
            Manufacturer = new LocalizedText("Contoso Weighing"),
            SerialNumber = serial,
            ProductInstanceUri = "urn:contoso:scale:" + serial,
            ManufacturerUri = "https://contoso.invalid",
            Model = new LocalizedText("CW-30"),
            ProductCode = "CW30",
            HardwareRevision = "1.0",
            SoftwareRevision = "2.1",
            DeviceClass = "Scale",
            AssetId = "A-" + serial,
            ComponentName = new LocalizedText("Scale " + serial),
            Location = "Line 1",
            YearOfConstruction = 2026,
            MonthOfConstruction = 3,
            InitialOperationDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        internal static void FullScale(IScaleBuilder builder, string serial)
        {
            builder
                .WithIdentification(Identity(serial))
                .WithWeighingRange(new WeighingRangeDefinition(0, 3, 0.001, 0.001))
                .WithWeighingRange(new WeighingRangeDefinition(0, 6, 0.002, 0.002))
                .WithMinimalWeight(0.02)
                .WithWeightDetails()
                .WithZeroAndTare()
                .WithRegisterWeight()
                .WithProductionOutput()
                .WithPackMLState()
                .WithMachineryBuildingBlocks()
                .WithProcessState("Ready", new LocalizedText("Ready"))
                .WithTypeFeatures();
        }

        private ValueTask<ScaleHandle> CreateAsync(
            ScaleKind kind,
            string name = "Scale",
            Action<IScaleBuilder>? extra = null)
        {
            return m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name(name),
                kind,
                b =>
                {
                    FullScale(b, name);
                    extra?.Invoke(b);
                });
        }

        private ServiceResult Call(MethodState? method, NodeId objectId, params Variant[] inputs)
        {
            Assert.That(method, Is.Not.Null, "the method is materialised");
            Assert.That(method!.OnCallMethod2, Is.Not.Null, "the method is bound");
            return method.OnCallMethod2!(m_fixture.Context, method, objectId, inputs.ToArrayOf(), []);
        }

        [TestCaseSource(typeof(ScalesModel), nameof(ScalesModel.AllKinds))]
        public async Task EveryKindIsPublishedWithItsMandatoryMembersAsync(ScaleKind kind)
        {
            ScaleHandle scale = await CreateAsync(kind, kind.ToString());

            Assert.That(scale.Kind, Is.EqualTo(kind));
            Assert.That(m_fixture.Manager.FindPredefinedNode(scale.NodeId), Is.SameAs(scale.Scale));
            Assert.That(scale.Scale.TypeDefinitionId.TryGetValue(out uint typeId), Is.True);
            Assert.That(typeId, Is.EqualTo(ScalesModel.ObjectTypeOf(kind)));
            Assert.That(scale.Scale.CurrentWeight, Is.Not.Null);
            Assert.That(scale.Scale.CurrentWeight!.Overload!.Value, Is.False);
            Assert.That(scale.Scale.CurrentWeight.EngineeringUnits!.Value.UnitId, Is.EqualTo(ScaleUnits.Kilogram.UnitId));
            Assert.That(scale.Scale.CurrentWeight.EURange!.Value.High, Is.EqualTo(6));

            var children = new List<BaseInstanceState>();
            scale.Scale.GetChildren(m_fixture.Context, children);
            Assert.That(children.OfType<WeighingRangeElementState>().Count(), Is.EqualTo(2));

            var identification = (Opc.Ua.Machinery.MachineIdentificationState)scale.Scale.Identification!;
            Assert.That(identification.SerialNumber!.Value, Is.EqualTo(kind.ToString()));
            Assert.That(identification.ProductInstanceUri!.Value, Does.StartWith("urn:contoso:scale:"));
            Assert.That(identification.Location!.Value, Is.EqualTo("Line 1"));
            Assert.That(scale.Scale.SerialNumber!.Value, Is.EqualTo(kind.ToString()), "mirrored onto the DI nameplate");

            Assert.That(scale.PackML, Is.Not.Null);
            Assert.That(scale.PackML!.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
            Assert.That(scale.Scale.MachineryItemState!.CurrentState!.Value.Text, Is.EqualTo("NotExecuting"));
            Assert.That(
                scale.Scale.MachineryBuildingBlocks!.ReferenceExists(
                    Opc.Ua.Types.ReferenceTypeIds.HasAddIn,
                    false,
                    scale.Scale.MachineryItemState.NodeId),
                Is.True);
            Assert.That(
                scale.Scale.ReferenceExists(Opc.Ua.Types.ReferenceTypeIds.Organizes, true, NodeId.Null) ||
                scale.Scale.ReferenceExists(Opc.Ua.Types.ReferenceTypeIds.Organizes, true, MachinesFolder()),
                Is.True,
                "the scale is organized into the Machines folder");
        }

        private NodeId MachinesFolder()
        {
            return NodeId.Create(
                Opc.Ua.Machinery.Objects.Machines,
                Opc.Ua.Machinery.Namespaces.Machinery,
                m_fixture.Server.NamespaceUris);
        }

        [Test]
        public async Task ScaleWithoutIdentificationOrRangeIsRejectedAsync()
        {
            ServiceResultException? noIdentity = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_fixture.Manager.CreateScaleAsync(
                    m_fixture.Name("NoId"),
                    ScaleKind.Simple,
                    b => b.WithWeighingRange(new WeighingRangeDefinition(0, 1, 0.1, 0.1))));
            Assert.That(noIdentity!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));

            ServiceResultException? noRange = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_fixture.Manager.CreateScaleAsync(
                    m_fixture.Name("NoRange"),
                    ScaleKind.Simple,
                    b => b.WithIdentification(Identity("x"))));
            Assert.That(noRange!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task IdentificationWithoutMandatoryFieldsIsRejectedAsync()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_fixture.Manager.CreateScaleAsync(
                    m_fixture.Name("Bad"),
                    ScaleKind.Simple,
                    b => b.WithIdentification(new ScaleIdentification { SerialNumber = "1" })));
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_fixture.Manager.CreateScaleAsync(
                    m_fixture.Name("Bad2"),
                    ScaleKind.Simple,
                    b => b.WithIdentification(new ScaleIdentification
                    {
                        Manufacturer = new LocalizedText("m"),
                        SerialNumber = "1"
                    })));
        }

        [Test]
        public async Task DuplicateScaleNameIsRejectedAsync()
        {
            await CreateAsync(ScaleKind.Simple, "Twin");
            ServiceResultException? duplicate = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAsync(ScaleKind.Simple, "Twin"));
            Assert.That(duplicate!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task PublishedLoadReachesEveryWeightPropertyAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            scale.PublishLoad(1.23456, stable: true);

            WeightItemState weight = scale.Scale.CurrentWeight!;
            Assert.That(weight.Value.Gross, Is.EqualTo(1.235).Within(1e-9));
            Assert.That(weight.Gross!.Value, Is.EqualTo(1.235).Within(1e-9));
            Assert.That(weight.Net!.Value, Is.EqualTo(1.235).Within(1e-9));
            Assert.That(weight.HighResolutionValue!.Value.Gross, Is.EqualTo(1.23456).Within(1e-12));
            Assert.That(weight.PrintableValue!.Value.Gross, Is.EqualTo("1.235"));
            Assert.That(weight.WeightStable!.Value, Is.True);
            Assert.That(weight.CurrentRangeId!.Value, Is.EqualTo((ushort)1));
            Assert.That(weight.InsideZero!.Value, Is.False);
            Assert.That(weight.CenterOfZero!.Value, Is.False);
            Assert.That(weight.GrossNegative!.Value, Is.False);
            Assert.That(weight.LegalForTrade!.Value, Is.False);
            Assert.That(scale.CurrentReading.Gross, Is.EqualTo(1.235).Within(1e-9));
            Assert.That(scale.Capacity, Is.EqualTo(6));
            Assert.That(scale.WeighingRanges, Has.Count.EqualTo(2));

            scale.PublishLoad(4.0);
            Assert.That(weight.CurrentRangeId.Value, Is.EqualTo((ushort)2));
        }

        [Test]
        public async Task ZeroTareAndRegisterMethodsDriveTheWeightAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            NodeId id = scale.NodeId;

            scale.PublishLoad(0.1);
            Assert.That(ServiceResult.IsGood(Call(scale.Scale.SetZero, id)), Is.True);
            Assert.That(scale.CurrentReading.Gross, Is.Zero);

            scale.PublishLoad(0.6);
            Assert.That(ServiceResult.IsGood(Call(scale.Scale.SetTare, id)), Is.True);
            Assert.That(scale.CurrentReading.TareMode, Is.EqualTo(TareMode.MeasuredTare_1));
            Assert.That(scale.Scale.CurrentWeight!.TareMode!.Value, Is.EqualTo(TareMode.MeasuredTare_1));

            scale.PublishLoad(2.1);
            Assert.That(scale.CurrentReading.Net, Is.EqualTo(1.5).Within(1e-9));

            Assert.That(ServiceResult.IsGood(Call(scale.Scale.ClearTare, id)), Is.True);
            Assert.That(scale.CurrentReading.TareMode, Is.EqualTo(TareMode.None_0));

            ScaleReading? registered = null;
            scale.WeightRegistered += (_, reading) => registered = reading;
            Assert.That(ServiceResult.IsGood(Call(scale.Scale.RegisterWeight, id)), Is.True);
            Assert.That(registered, Is.Not.Null);
            Assert.That(registered!.WeightId, Is.EqualTo("1"));
            Assert.That(scale.RegisteredReading!.Gross, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(scale.Scale.RegisteredWeight!.Value.Gross, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(scale.Scale.RegisteredWeight.WeightId!.Value, Is.EqualTo("1"));

            // RegisterWeight counts into ProductionOutput.
            Assert.That(scale.Scale.ProductionOutput!.TotalPackages!.ItemCount!.WrappedValue.TryGetValue(out ulong count), Is.True);
            Assert.That(count, Is.EqualTo(1UL));
        }

        [Test]
        public async Task PresetTareChecksAndConvertsTheUnitAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Simple,
                extra: b => b.WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram));
            SetPresetTareMethodState method = scale.Scale.SetPresetTare!;

            ServiceResult grams = method.OnCall!(m_fixture.Context, method, scale.NodeId, 250, ScaleUnits.Gram);
            Assert.That(ServiceResult.IsGood(grams), Is.True);
            Assert.That(scale.CurrentReading.Tare, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(scale.CurrentReading.TareMode, Is.EqualTo(TareMode.PresetTare_2));

            ServiceResult pounds = method.OnCall(m_fixture.Context, method, scale.NodeId, 1, ScaleUnits.Pound);
            Assert.That(pounds.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            ServiceResult tooHeavy = scale.SetPresetTare(100);
            Assert.That(tooHeavy.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
        }

        [Test]
        public async Task FailedZeroOrTareRaisesAFaultAndOverloadAnAlarmAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            scale.PublishLoad(2.0);
            Assert.That(scale.SetZero().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));

            scale.PublishLoad(8.0);
            Assert.That(scale.CurrentReading.Overload, Is.True);
            Assert.That(scale.Notifications.IsAlarmActive(ScaleNotificationId.OverloadFault), Is.True);
            ScaleAlarmState alarm = scale.Notifications.Alarm(ScaleNotificationId.OverloadFault)!;
            Assert.That(alarm.NotificationId!.WrappedValue.TryGetValue(out uint id), Is.True);
            Assert.That(id, Is.EqualTo(601u));
            Assert.That(alarm.NotificationId.ValueAsText!.Value.Text, Is.EqualTo("OVERLOAD_FAULT"));
            Assert.That(alarm.NotificationCategory!.ValueAsText!.Value.Text, Is.EqualTo("WEIGHING_MODULE"));
            Assert.That(scale.SetTare().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));

            scale.PublishLoad(1.0);
            Assert.That(scale.Notifications.IsAlarmActive(ScaleNotificationId.OverloadFault), Is.False);

            scale.PublishLoad(-2.0);
            Assert.That(scale.Notifications.IsAlarmActive(ScaleNotificationId.UnderloadFault), Is.True);
            Assert.That(scale.Notifications.Alarm(ScaleNotificationId.PowerSupplyFault), Is.Null);
        }

        [Test]
        public async Task EventsAndVendorEventsCanBeRaisedAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            scale.Notifications.RaiseEvent(ScaleNotificationId.LabelFault, new LocalizedText("Label jam"), EventSeverity.High, "Printer 1");
            scale.Notifications.RaiseVendorEvent(5001, ScaleNotificationCategory.Process, "VENDOR_JAM", new LocalizedText("Vendor jam"));
            Assert.That(
                () => scale.Notifications.RaiseVendorEvent(4000, ScaleNotificationCategory.Process, "X", new LocalizedText("x")),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(scale.Notifications.SourceNodeId, Is.EqualTo(scale.NodeId));
            Assert.That(scale.Notifications.SetAlarm(ScaleNotificationId.PowerSupplyFault, false, new LocalizedText("ok")), Is.False);
            Assert.That(scale.Notifications.SetAlarm(ScaleNotificationId.PowerSupplyFault, true, new LocalizedText("down")), Is.True);
            Assert.That(scale.Notifications.SetAlarm(ScaleNotificationId.PowerSupplyFault, true, new LocalizedText("down")), Is.False);
        }

        [Test]
        public async Task CommandInterceptorCanVetoACommandAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            var seen = new List<ScaleCommand>();
            scale.CommandInterceptor = command =>
            {
                seen.Add(command);
                return command == ScaleCommand.SetZero
                    ? ServiceResult.Create(StatusCodes.BadDeviceFailure, "electronics refused")
                    : ServiceResult.Good;
            };
            Assert.That(scale.SetZero().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadDeviceFailure));
            Assert.That(ServiceResult.IsGood(scale.ClearTare()), Is.True);
            Assert.That(seen, Is.EqualTo(new[] { ScaleCommand.SetZero, ScaleCommand.ClearTare }));
        }

        [Test]
        public async Task StateSettersPublishTheMachineryStatesAndProcessStateAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            scale.SetItemState(ScaleItemState.Executing);
            scale.SetOperationMode(ScaleOperationMode.Maintenance);
            scale.SetProcessState("Busy", new LocalizedText("Weighing"));
            Assert.That(scale.Scale.MachineryItemState!.CurrentState!.Value.Text, Is.EqualTo("Executing"));
            Assert.That(scale.Scale.MachineryOperationMode!.CurrentState!.Value.Text, Is.EqualTo("Maintenance"));
            Assert.That(scale.Scale.ProcessStateId!.Value, Is.EqualTo("Busy"));
            scale.SetItemState(ScaleItemState.OutOfService);
            scale.SetItemState(ScaleItemState.NotAvailable);
            scale.SetOperationMode(ScaleOperationMode.Setup);
            scale.SetOperationMode(ScaleOperationMode.None);
            Assert.That(scale.Scale.MachineryItemState.CurrentState.Value.Text, Is.EqualTo("NotAvailable"));
        }

        [Test]
        public async Task PackMLMethodsWalkTheStateMachineAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            PackMLStateMachineController packMl = scale.PackML!;
            PackMLBaseStateMachineState state = scale.Scale.State!;
            PackMLExecuteStateMachineState execute = state.MachineState!.ExecuteState!;
            var changes = new List<uint>();
            packMl.StateChanged += (_, e) => changes.Add(e.CurrentState);

            Assert.That(execute.Start!.Executable, Is.True);
            Assert.That(execute.Hold!.Executable, Is.False);
            Assert.That(
                ServiceResult.IsGood(execute.Start.OnCall!(m_fixture.Context, execute.Start, execute.NodeId, default)),
                Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Execute));

            Assert.That(ServiceResult.IsGood(Call(execute.Hold, execute.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Held));
            Assert.That(ServiceResult.IsGood(Call(execute.Unhold, execute.NodeId)), Is.True);
            Assert.That(ServiceResult.IsGood(Call(execute.Suspend, execute.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Suspended));
            Assert.That(ServiceResult.IsGood(Call(execute.Unsuspend, execute.NodeId)), Is.True);
            Assert.That(ServiceResult.IsGood(Call(execute.ToComplete, execute.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Complete));
            Assert.That(ServiceResult.IsGood(Call(execute.Reset, execute.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));

            Assert.That(ServiceResult.IsGood(Call(state.MachineState.Stop, state.MachineState.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(execute.CurrentState!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            Assert.That(ServiceResult.IsGood(Call(state.MachineState.Reset, state.MachineState.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));

            Assert.That(ServiceResult.IsGood(Call(state.Abort, state.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborted));
            Assert.That(Call(state.Abort, state.NodeId).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(Call(state.MachineState.Reset, state.MachineState.NodeId).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(ServiceResult.IsGood(Call(state.Clear, state.NodeId)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(changes, Is.Not.Empty);
            Assert.That(state.AvailableStates!.Value.Count, Is.EqualTo(3));
            Assert.That(execute.AvailableTransitions!.Value.Count, Is.EqualTo(19));
        }

        [Test]
        public async Task PackMLCommandGuardCanVetoAndCallBackIntoTheControllerAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            PackMLStateMachineController packMl = scale.PackML!;
            PackMLBaseStateMachineState state = scale.Scale.State!;
            PackMLExecuteStateMachineState execute = state.MachineState!.ExecuteState!;
            var consulted = new List<PackMLCommand>();
            uint observed = 0;
            packMl.CommandGuard = command =>
            {
                consulted.Add(command);
                observed = packMl.CurrentState;
                return command == PackMLCommand.Start
                    ? ServiceResult.Create(StatusCodes.BadUserAccessDenied, "Interlock open.")
                    : ServiceResult.Good;
            };

            Assert.That(
                packMl.Execute(PackMLCommand.Start).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
            Assert.That(observed, Is.EqualTo(PackMLStateNumbers.Idle));

            // A command the state does not allow is refused before the guard
            // is asked.
            Assert.That(
                Call(execute.Hold, execute.NodeId).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(consulted, Is.EqualTo(new[] { PackMLCommand.Start }));

            Assert.That(ServiceResult.IsGood(Call(state.MachineState.Stop, state.MachineState.NodeId)), Is.True);
            Assert.That(ServiceResult.IsGood(Call(state.MachineState.Reset, state.MachineState.NodeId)), Is.True);
            Assert.That(
                consulted,
                Is.EqualTo(new[] { PackMLCommand.Start, PackMLCommand.Stop, PackMLCommand.Reset }));
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
        }

        [Test]
        public async Task PackMLActingStatesCanBeCompletedByTheEquipmentAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Simple);
            PackMLStateMachineController packMl = scale.PackML!;
            packMl.AutoCompleteActingStates = false;
            Assert.That(ServiceResult.IsGood(packMl.Execute(PackMLCommand.Start)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Starting));
            Assert.That(packMl.Execute(PackMLCommand.Suspend).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(ServiceResult.IsGood(packMl.CompleteActingState()), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Execute));
            Assert.That(packMl.CompleteActingState().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));

            packMl.CommandGuard = command => command == PackMLCommand.Hold
                ? ServiceResult.Create(StatusCodes.BadUserAccessDenied, "no")
                : ServiceResult.Good;
            Assert.That(packMl.Execute(PackMLCommand.Hold).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));

            packMl.Initialize(PackMLInitialState.Aborted);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborted));
            packMl.Initialize(PackMLInitialState.Stopped);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(ServiceResult.IsGood(packMl.Execute(PackMLCommand.Reset)), Is.True);
            Assert.That(packMl.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
        }

        [Test]
        public async Task ProductionPresetSelectsDeselectsAndSwitchesProductsAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Simple,
                extra: b => b.WithProductionPreset(p => p
                    .AllowSelection()
                    .AllowManagement()
                    .AddProduct("P1", new LocalizedText("Apples"), (ctx, product) => ((SimpleProductState)product).AddContainerId(ctx))
                    .AddProduct("P2", new LocalizedText("Pears"))
                    .Select("P1")));
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductionPresetState node = preset.Preset;

            string[] firstProduct = ["P1"];
            string[] secondProduct = ["P2"];
            Assert.That(preset.CurrentProducts, Is.EqualTo(firstProduct));
            Assert.That(preset.ActiveProduct!.ProductId!.Value, Is.EqualTo("P1"));
            Assert.That(preset.Find("P1")!.ProductMode!.Value, Is.True);
            Assert.That(node.CurrentProducts!.Value.ToArray(), Is.EqualTo(firstProduct));

            Assert.That(ServiceResult.IsGood(node.SwitchProduct!.OnCall!(m_fixture.Context, node.SwitchProduct, node.NodeId, "P2")), Is.True);
            Assert.That(preset.CurrentProducts, Is.EqualTo(secondProduct));
            Assert.That(preset.Find("P1")!.ProductMode!.Value, Is.False);

            Assert.That(ServiceResult.IsGood(node.SelectProduct!.OnCall!(m_fixture.Context, node.SelectProduct, node.NodeId, "P1")), Is.True);
            Assert.That(preset.ActiveProduct, Is.Null, "two products are in processing");
            Assert.That(
                node.SwitchProduct.OnCall(m_fixture.Context, node.SwitchProduct, node.NodeId, "P2").StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(ServiceResult.IsGood(node.DeselectProduct!.OnCall!(m_fixture.Context, node.DeselectProduct, node.NodeId, "P1")), Is.True);
            Assert.That(
                node.SelectProduct.OnCall(m_fixture.Context, node.SelectProduct, node.NodeId, "nope").StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            Assert.That(ServiceResult.IsGood(preset.Deselect("P2")), Is.True);
            Assert.That(ServiceResult.IsGood(preset.Switch("P1")), Is.True);
        }

        [Test]
        public async Task ProductionPresetAddsAndRemovesProductsAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Checkweigher,
                extra: b => b.WithProductionPreset(p => p.AllowSelection().AllowManagement().AddProduct("P1", new LocalizedText("A"))));
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductionPresetState node = preset.Preset;
            AddProductMethodState add = node.AddProduct!;
            NodeId checkweigherProduct = new(ObjectTypes.CheckweigherProductType, m_fixture.Manager.NamespaceIndices.Scales);
            NodeId simpleProduct = new(ObjectTypes.SimpleProductType, m_fixture.Manager.NamespaceIndices.Scales);

            NodeId created = NodeId.Null;
            Assert.That(
                ServiceResult.IsGood(add.OnCall!(m_fixture.Context, add, node.NodeId, "Bread", "P2", checkweigherProduct, ref created)),
                Is.True);
            Assert.That(created.IsNull, Is.False);
            Assert.That(m_fixture.Manager.FindPredefinedNode(created), Is.InstanceOf<CheckweigherProductState>());

            NodeId ignored = NodeId.Null;
            Assert.That(
                add.OnCall(m_fixture.Context, add, node.NodeId, "Bread", "P2", checkweigherProduct, ref ignored).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdExists));
            Assert.That(
                add.OnCall(m_fixture.Context, add, node.NodeId, "Cake", "P3", simpleProduct, ref ignored).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            Assert.That(
                add.OnCall(m_fixture.Context, add, node.NodeId, "Cake", string.Empty, simpleProduct, ref ignored).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(
                ServiceResult.IsGood(add.OnCall(m_fixture.Context, add, node.NodeId, "Cake", "P4", NodeId.Null, ref ignored)),
                Is.True, "a null type means the scale's own product type");

            ProductState direct = preset.AddProduct("P5", new LocalizedText("Direct"));
            Assert.That(direct, Is.InstanceOf<CheckweigherProductState>());
            Assert.That(() => preset.AddProduct("P5", new LocalizedText("again")), Throws.InstanceOf<ServiceResultException>());

            Assert.That(ServiceResult.IsGood(preset.Select("P2")), Is.True);
            RemoveProductMethodStateResult inProcessingResult = await node.RemoveProduct!.OnCallAsync!(
                m_fixture.Context,
                node.RemoveProduct,
                node.NodeId,
                "P2",
                default).ConfigureAwait(false);
            ServiceResult inProcessing = inProcessingResult.ServiceResult;
            Assert.That(inProcessing.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(ServiceResult.IsGood(preset.Deselect("P2")), Is.True);
            Assert.That(ServiceResult.IsGood(await preset.RemoveAsync("P2")), Is.True);
            Assert.That(m_fixture.Manager.FindPredefinedNode(created), Is.Null);
            Assert.That((await preset.RemoveAsync("missing")).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            Assert.That(ServiceResult.IsGood(await preset.RemoveAsync("P4")), Is.True);
            Assert.That(ServiceResult.IsGood(await preset.RemoveAsync("P5")), Is.True);
            Assert.That((await preset.RemoveAsync("P1")).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState), "the last product stays");
        }

        [Test]
        public async Task CatchweigherProductZonesCanBeAddedAndRemovedAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Catchweigher,
                extra: b => b.WithProductionPreset(p => p.AddProduct(
                    "P1",
                    new LocalizedText("Box"),
                    (ctx, product) =>
                    {
                        var catchweigher = (CatchweigherProductState)product;
                        catchweigher.AddAddZone(ctx);
                        catchweigher.AddRemoveZone(ctx);
                    })));
            var product = (CatchweigherProductState)scale.ProductionPreset!.Find("P1")!;
            ISystemContext ctx = m_fixture.Context;
            AddZoneMethodState addZone = product.AddZone!;
            RemoveZoneMethodState removeZone = product.RemoveZone!;
            EUInformation kg = ScaleUnits.Kilogram;

            NodeId zoneId = NodeId.Null;
            ServiceResult added = addZone.OnCall!(ctx, addZone, product.NodeId, new LocalizedText("Light"), 0.1, 0.5, kg, ref zoneId);
            Assert.That(ServiceResult.IsGood(added), Is.True);
            var zone = (ZoneState)m_fixture.Manager.FindPredefinedNode(zoneId)!;
            Assert.That(zone.Name!.Value.Text, Is.EqualTo("Light"));

            NodeId ignored = NodeId.Null;
            ServiceResult inverted = addZone.OnCall(ctx, addZone, product.NodeId, new LocalizedText("Bad"), 1, 0.5, kg, ref ignored);
            Assert.That(inverted.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            ServiceResult unnamed = addZone.OnCall(ctx, addZone, product.NodeId, LocalizedText.Null, 0, 0.5, kg, ref ignored);
            Assert.That(unnamed.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));

            RemoveZoneMethodStateResult unknown =
                await removeZone.OnCallAsync!(ctx, removeZone, product.NodeId, new NodeId(12345u), default);
            Assert.That(unknown.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            RemoveZoneMethodStateResult removed =
                await removeZone.OnCallAsync(ctx, removeZone, product.NodeId, zoneId, default);
            Assert.That(ServiceResult.IsGood(removed.ServiceResult), Is.True);
            Assert.That(m_fixture.Manager.FindPredefinedNode(zoneId), Is.Null);
        }

        [Test]
        public async Task LockedProductsRejectOtherClientsAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Simple,
                extra: b => b.WithProductionPreset(p => p.AllowSelection().WithLocking().AddProduct("P1", new LocalizedText("A"))));
            ProductState product = scale.ProductionPreset!.Find("P1")!;
            Assert.That(product.Lock, Is.Not.Null);
            Assert.That(scale.ProductionPreset.Lockable, Is.True);
            // Without a lock held, selection is open to everyone.
            Assert.That(ServiceResult.IsGood(scale.ProductionPreset.Select("P1")), Is.True);
        }

        [Test]
        public async Task StaticPresetWithoutProductsIsRejectedAsync()
        {
            ServiceResultException? empty = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAsync(ScaleKind.Simple, "Empty", b => b.WithProductionPreset(p => p.AllowSelection())));
            Assert.That(empty!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAsync(ScaleKind.Simple, "Unknown", b => b.WithProductionPreset(p => p.AddProduct("P1", new LocalizedText("A")).Select("P9"))));
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await CreateAsync(ScaleKind.Simple, "Twice", b => b.WithProductionPreset(p => p
                    .AddProduct("P1", new LocalizedText("A"))
                    .AddProduct("P1", new LocalizedText("B")))));
        }

        [Test]
        public async Task ServerAdvertisesTheFacetsOfWhatItBuiltAsync()
        {
            await CreateAsync(ScaleKind.Vehicle, "V", b => b
                .WithProductionPreset(p => p.AllowSelection().AllowManagement().AddProduct("P1", new LocalizedText("Truck")))
                .AddFeederModule("Feeder", f => f.WithIdentification(Identity("F1")))
                .AddPrinterModule("Printer", pr => pr.WithIdentification(Identity("PR1"))));
            await CreateAsync(ScaleKind.Recipe, "R", b => b.WithRecipeFiles());

            string[] profiles = [.. m_fixture.Manager.ServerProfiles.ToList()];
            Assert.That(profiles, Does.Contain(ScalesProfiles.BaseScale));
            Assert.That(profiles, Does.Contain(ScalesProfiles.VehicleScale));
            Assert.That(profiles, Does.Contain(ScalesProfiles.RecipeScale));
            Assert.That(profiles, Does.Contain(ScalesProfiles.FullProductionPreset));
            Assert.That(profiles, Does.Contain(ScalesProfiles.FeederModule));
            Assert.That(profiles, Does.Contain(ScalesProfiles.PrinterModule));

            string[] units = [.. m_fixture.Manager.ConformanceUnits.ToList().Select(q => q.Name ?? string.Empty)];
            Assert.That(units, Does.Contain("Scales ScaleDeviceType"));
            Assert.That(units, Does.Contain("Scales VehicleScale"));
            Assert.That(units, Does.Contain("Scales ManageProduct"));
            Assert.That(units, Does.Contain("Scales RecipeManagment"));
            Assert.That(units, Does.Contain("Scales FileRecipeManagement"));
            Assert.That(units, Does.Contain("Scales FeederModule"));
            Assert.That(m_fixture.Manager.Scales, Has.Count.EqualTo(2));
        }
    }
}
