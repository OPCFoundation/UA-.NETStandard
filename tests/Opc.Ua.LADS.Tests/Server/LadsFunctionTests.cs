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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the functional unit state machine and the function handles.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsFunctionTests
    {
        private static readonly string[] s_composedNames = ["A", "B"];
        private static readonly string[] s_modes = ["Heat", "Cool"];
        private static readonly double[] s_initialSpectrum = [1.0, 2.0];
        private static readonly double[] s_spectrum = [3.0, 4.0, 5.0];

        [Test]
        public async Task FunctionalUnitMovesThroughItsStatesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FunctionalUnitMovesThroughItsStatesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            var states = new List<LadsFunctionalStateValue>();
            unit.StateChanged += states.Add;

            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.Device, Is.SameAs(device));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.StopAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.AbortAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.ClearAsync())
                .ConfigureAwait(false);

            await unit.StartAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.StartAsync())
                .ConfigureAwait(false);
            await unit.StopAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));

            await unit.StartAsync().ConfigureAwait(false);
            await unit.AbortAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.StartAsync())
                .ConfigureAwait(false);
            await unit.ClearAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));

            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    LadsFunctionalStateValue.Running,
                    LadsFunctionalStateValue.Stopping,
                    LadsFunctionalStateValue.Stopped,
                    LadsFunctionalStateValue.Running,
                    LadsFunctionalStateValue.Aborting,
                    LadsFunctionalStateValue.Aborted,
                    LadsFunctionalStateValue.Clearing,
                    LadsFunctionalStateValue.Stopped
                }));
        }

        [Test]
        public async Task FunctionalUnitMethodsAreCallableAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FunctionalUnitMethodsAreCallableAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            NodeId state = await server.ChildAsync(unit.NodeId, "FunctionalUnitState").ConfigureAwait(false);

            Variant properties = Variant.From(default(ArrayOf<ExtensionObject>));
            CallMethodResult start = await server.CallByNameAsync(state, "Start", properties).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
            CallMethodResult again = await server.CallByNameAsync(state, "Start", properties).ConfigureAwait(false);
            Assert.That(again.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            CallMethodResult abort = await server.CallByNameAsync(state, "Abort").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(abort.StatusCode), Is.True, abort.StatusCode.ToString());
            CallMethodResult clear = await server.CallByNameAsync(state, "Clear").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(clear.StatusCode), Is.True, clear.StatusCode.ToString());
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));

            NodeId current = await server.ChildAsync(state, "CurrentState").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(current).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task AnalogControlFunctionValidatesItsCommandsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(AnalogControlFunctionValidatesItsCommandsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            IAnalogControlFunction temperature = unit.GetFunction<IAnalogControlFunction>("Temperature");

            Assert.That(temperature.Kind, Is.EqualTo(LadsAnalogControlKind.Plain));
            Assert.That(temperature.TargetValue, Is.EqualTo(37));
            Assert.That(temperature.CurrentValue, Is.EqualTo(22));
            Assert.That(temperature.FunctionalUnit, Is.SameAs(unit));
            LadsAssert.ThrowsStatus(StatusCodes.BadNotFound, () => unit.GetFunction<ICoverFunction>("Temperature"));
            Assert.That(unit.TryGetFunction("Missing", out IAnalogControlFunction? _), Is.False);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadOutOfRange,
                () => temperature.SetTargetValueAsync(70)).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported,
                () => temperature.ModifyTargetValueByAsync(1)).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => temperature.StopAsync()).ConfigureAwait(false);

            temperature.IsEnabled = false;
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => temperature.StartAsync()).ConfigureAwait(false);
            temperature.IsEnabled = true;

            await temperature.StartWithTargetValueAsync(40).ConfigureAwait(false);
            Assert.That(temperature.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(temperature.TargetValue, Is.EqualTo(40));
            await temperature.SetTargetValueAsync(42).ConfigureAwait(false);
            temperature.UpdateCurrentValue(41.5);
            Assert.That(temperature.CurrentValue, Is.EqualTo(41.5));
            await temperature.AbortAsync().ConfigureAwait(false);
            Assert.That(temperature.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            await temperature.ClearAsync().ConfigureAwait(false);
            await temperature.StartAsync().ConfigureAwait(false);
            await temperature.StopAsync().ConfigureAwait(false);
            Assert.That(temperature.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));

            NodeId target = await server.ChildAsync(temperature.NodeId, "TargetValue").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(target).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out double read), Is.True, value.WrappedValue.ToString());
            Assert.That(read, Is.EqualTo(42));

            NodeId state = await server.ChildAsync(temperature.NodeId, "ControlFunctionState")
                .ConfigureAwait(false);
            CallMethodResult start = await server.CallByNameAsync(
                state,
                "StartWithTargetValue",
                Variant.From(30.0)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
            Assert.That(temperature.TargetValue, Is.EqualTo(30));
            CallMethodResult stop = await server.CallByNameAsync(state, "Stop").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(stop.StatusCode), Is.True, stop.StatusCode.ToString());
        }

        [Test]
        public async Task CoverMovesThroughItsStatesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(CoverMovesThroughItsStatesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ICoverFunction door = device.GetFunctionalUnit(LadsIncubator.UnitName).GetFunction<ICoverFunction>("Door");
            var states = new List<LadsCoverStateValue>();
            door.StateChanged += states.Add;

            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Closed));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.CloseAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.UnlockAsync())
                .ConfigureAwait(false);
            await door.OpenAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Opened));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.LockAsync())
                .ConfigureAwait(false);
            await door.CloseAsync().ConfigureAwait(false);
            await door.LockAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Locked));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.OpenAsync())
                .ConfigureAwait(false);
            await door.UnlockAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Closed));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.ResetAsync())
                .ConfigureAwait(false);
            await door.ReportErrorAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Error));
            await door.ReportErrorAsync().ConfigureAwait(false);
            await door.ResetAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Opened));

            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    LadsCoverStateValue.Opening,
                    LadsCoverStateValue.Opened,
                    LadsCoverStateValue.Closing,
                    LadsCoverStateValue.Closed,
                    LadsCoverStateValue.Locking,
                    LadsCoverStateValue.Locked,
                    LadsCoverStateValue.Unlocking,
                    LadsCoverStateValue.Closed,
                    LadsCoverStateValue.Error,
                    LadsCoverStateValue.Opened
                }));

            NodeId coverState = await server.ChildAsync(door.NodeId, "CoverState").ConfigureAwait(false);
            CallMethodResult close = await server.CallByNameAsync(coverState, "Close").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(close.StatusCode), Is.True, close.StatusCode.ToString());
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Closed));
        }

        [Test]
        public async Task NonLockableCoverRejectsLockingAsync()
        {
            ICoverFunction? lid = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(NonLockableCoverRejectsLockingAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.Cover(
                        "Lid",
                        options => options.Lockable = false,
                        out lid))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(lid, Is.Not.Null);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadNotSupported, () => lid!.LockAsync())
                .ConfigureAwait(false);
            NodeId coverState = await server.ChildAsync(lid!.NodeId, "CoverState").ConfigureAwait(false);
            string[] children = await server.ChildNamesAsync(coverState).ConfigureAwait(false);
            Assert.That(children, Does.Contain("Open"));
            Assert.That(children, Does.Not.Contain("Lock"));
        }

        [Test]
        public async Task SensorsPublishTheirValuesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(SensorsPublishTheirValuesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            IAnalogScalarSensorFunction co2 = device.GetFunctionalUnit(LadsIncubator.UnitName)
                .GetFunction<IAnalogScalarSensorFunction>("CO2");

            Assert.That(co2.SensorValue, Is.EqualTo(5));
            Assert.That(co2.HasCompensation, Is.False);
            co2.UpdateValue(6.5, 6.4);
            Assert.That(co2.SensorValue, Is.EqualTo(6.5));
            Assert.That(co2.RawValue, Is.EqualTo(6.4));
            co2.UpdateValue(7);
            Assert.That(co2.RawValue, Is.EqualTo(7));
            LadsAssert.ThrowsStatus(StatusCodes.BadNotSupported, () => co2.UpdateCompensationValue(1));

            NodeId sensor = await server.ChildAsync(co2.NodeId, "SensorValue").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(sensor).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out double read), Is.True, value.WrappedValue.ToString());
            Assert.That(read, Is.EqualTo(7));
        }

        [Test]
        public async Task TimerTracksTheRemainingTimeAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(TimerTracksTheRemainingTimeAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ITimerFunction timer = device.GetFunctionalUnit(LadsIncubator.UnitName)
                .GetFunction<ITimerFunction>("Timer");

            await timer.SetTargetValueAsync(60).ConfigureAwait(false);
            timer.UpdateCurrentValue(10);
            Assert.That(timer.TargetValue, Is.EqualTo(60));
            Assert.That(timer.CurrentValue, Is.EqualTo(10));
            Assert.That(timer.DifferenceValue, Is.EqualTo(50));
            await timer.StartAsync().ConfigureAwait(false);
            Assert.That(timer.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            await timer.StopAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadOutOfRange, () => timer.SetTargetValueAsync(-1))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task EveryFunctionTypeIsCreatedAsync()
        {
            IAnalogControlFunction? relative = null;
            IAnalogControlFunction? totalizer = null;
            IAnalogControlFunction? composed = null;
            IMultiModeAnalogControlFunction? multiMode = null;
            ITwoStateDiscreteControlFunction? valve = null;
            IMultiStateDiscreteControlFunction? level = null;
            IAnalogArraySensorFunction? spectrum = null;
            ITwoStateDiscreteSensorFunction? presence = null;
            IMultiStateDiscreteSensorFunction? phase = null;
            IMultiSensorFunction? multi = null;
            IAnalogScalarSensorFunction? compensated = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(EveryFunctionTypeIsCreatedAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit
                        .AnalogControl(
                            "Relative",
                            o =>
                            {
                                o.Kind = LadsAnalogControlKind.Relative;
                                o.Range = new Range(100, 0);
                                o.InitialTargetValue = 10;
                                o.IncreaseRate = 1;
                                o.DecreaseRate = 1;
                            },
                            out relative)
                        .AnalogControl("Totalizer", o => o.Kind = LadsAnalogControlKind.Totalizer, out totalizer)
                        .AnalogControl(
                            "Composed",
                            o =>
                            {
                                o.Kind = LadsAnalogControlKind.Composed;
                                o.ComposedTargetValues = s_composedNames.ToArrayOf();
                            },
                            out composed)
                        .MultiModeAnalogControl(
                            "Mode",
                            o => o.Modes = s_modes.ToArrayOf(),
                            out multiMode)
                        .TwoStateDiscreteControl("Valve", null, out valve)
                        .MultiStateDiscreteControl(
                            "Level",
                            o => o.States = new[]
                            {
                                new LocalizedText("Low"),
                                new LocalizedText("High")
                            }.ToArrayOf(),
                            out level)
                        .AnalogArraySensor(
                            "Spectrum",
                            o => o.InitialValues = s_initialSpectrum.ToArrayOf(),
                            out spectrum)
                        .AnalogScalarSensor("Compensated", o => o.WithCompensation = true, out compensated)
                        .TwoStateDiscreteSensor("Presence", null, out presence)
                        .MultiStateDiscreteSensor(
                            "Phase",
                            o => o.States = new[]
                            {
                                new LocalizedText("A"),
                                new LocalizedText("B")
                            }.ToArrayOf(),
                            out phase)
                        .MultiSensor("Multi", m => m.AnalogScalarSensor("Inner"), out multi))))).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit aux = device.GetFunctionalUnit("Aux");

            Assert.That(aux.Functions.Count, Is.EqualTo(11));
            await relative!.ModifyTargetValueByAsync(5).ConfigureAwait(false);
            Assert.That(relative.TargetValue, Is.EqualTo(15));
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadOutOfRange,
                () => relative.ModifyTargetValueByAsync(1000)).ConfigureAwait(false);
            await totalizer!.ResetTotalizerAsync().ConfigureAwait(false);
            Assert.That(totalizer.TotalizedValue, Is.Zero);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported,
                () => relative.ResetTotalizerAsync()).ConfigureAwait(false);
            composed!.SetComposedTargetValue("A", 3);
            LadsAssert.ThrowsStatus(StatusCodes.BadNotFound, () => composed.SetComposedTargetValue("C", 3));
            Assert.That(multiMode!.Modes.Count, Is.EqualTo(2));
            await multiMode.SetModeAsync(1).ConfigureAwait(false);
            Assert.That(multiMode.CurrentMode, Is.EqualTo(1));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadOutOfRange, () => multiMode.SetModeAsync(5))
                .ConfigureAwait(false);
            multiMode.SetTargetValue("Cool", 4);
            Assert.That(multiMode.GetTargetValue("Cool"), Is.EqualTo(4));
            await valve!.StartWithTargetValueAsync(true).ConfigureAwait(false);
            Assert.That(valve.TargetValue, Is.True);
            valve.UpdateCurrentValue(true);
            Assert.That(valve.CurrentValue, Is.True);
            await level!.SetTargetValueAsync(1).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadOutOfRange, () => level.SetTargetValueAsync(7))
                .ConfigureAwait(false);
            level.UpdateCurrentValue(1);
            Assert.That(level.CurrentValue, Is.EqualTo(1));
            spectrum!.UpdateValue(s_spectrum.ToArrayOf());
            Assert.That(spectrum.SensorValue.Count, Is.EqualTo(3));
            compensated!.UpdateCompensationValue(2);
            Assert.That(compensated.HasCompensation, Is.True);
            presence!.UpdateValue(true);
            Assert.That(presence.SensorValue, Is.True);
            phase!.UpdateValue(1);
            Assert.That(phase.SensorValue, Is.EqualTo(1));
            Assert.That(multi!.Functions.Count, Is.EqualTo(1));
            Assert.That(aux.TryGetFunction("Multi/Inner", out IAnalogScalarSensorFunction? _), Is.True);
        }

        [Test]
        public async Task FunctionProviderCanVetoCommandsAsync()
        {
            var provider = new Mock<ILadsFunctionProvider>();
            var commands = new List<LadsFunctionCommandKind>();
            provider
                .Setup(p => p.OnCommandAsync(It.IsAny<LadsFunctionCommand>(), It.IsAny<CancellationToken>()))
                .Returns((LadsFunctionCommand command, CancellationToken _) =>
                {
                    commands.Add(command.Kind);
                    return new ValueTask<ServiceResult>(command.Kind == LadsFunctionCommandKind.Start
                        ? new ServiceResult(StatusCodes.BadDeviceFailure)
                        : ServiceResult.Good);
                });
            IAnalogControlFunction? heater = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(FunctionProviderCanVetoCommandsAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit
                        .FunctionProvider(provider.Object)
                        .AnalogControl("Heater", null, out heater))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadDeviceFailure, () => heater!.StartAsync())
                .ConfigureAwait(false);
            Assert.That(heater!.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            await heater.StartWithTargetValueAsync(5).ConfigureAwait(false);
            Assert.That(heater.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(
                commands,
                Is.EqualTo(new[] { LadsFunctionCommandKind.Start, LadsFunctionCommandKind.StartWithTargetValue }));
        }

        [Test]
        public async Task RelativeTargetDeltaUpdatesCurrentValueFromThePreviousTargetAsync()
        {
            IAnalogControlFunction? relative = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(RelativeTargetDeltaUpdatesCurrentValueFromThePreviousTargetAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.AnalogControl(
                        "Relative",
                        options =>
                        {
                            options.Kind = LadsAnalogControlKind.Relative;
                            options.Range = new Range(100, 0);
                            options.InitialTargetValue = 10;
                            options.InitialCurrentValue = 2;
                        },
                        out relative))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(relative!.TargetValue, Is.EqualTo(10));
            Assert.That(relative.CurrentValue, Is.EqualTo(2));
            await relative.ModifyTargetValueByAsync(5).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(relative.TargetValue, Is.EqualTo(15));
                Assert.That(relative.CurrentValue, Is.EqualTo(15));
            });
        }

        [Test]
        public async Task MultiModeStartWithTargetValueUpdatesSelectedModeCurrentValueAsync()
        {
            IMultiModeAnalogControlFunction? multiMode = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(MultiModeStartWithTargetValueUpdatesSelectedModeCurrentValueAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.MultiModeAnalogControl(
                        "Mode",
                        options => options.Modes = s_modes.ToArrayOf(),
                        out multiMode))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            multiMode!.SetTargetValue("Heat", 3.5);
            multiMode.UpdateCurrentValue("Heat", 2.5);
            multiMode.SetTargetValue("Cool", 12);
            multiMode.UpdateCurrentValue("Cool", 1.5);
            await multiMode.SetModeAsync(1).ConfigureAwait(false);
            NodeId controlState = await server.ChildAsync(multiMode.NodeId, "ControlFunctionState")
                .ConfigureAwait(false);
            CallMethodResult start = await server.CallByNameAsync(
                controlState, "StartWithTargetValue", Variant.From(8.5)).ConfigureAwait(false);

            DataValue coolCurrent = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Cool",
                "CurrentValue").ConfigureAwait(false)).ConfigureAwait(false);
            DataValue coolTarget = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Cool",
                "TargetValue").ConfigureAwait(false)).ConfigureAwait(false);
            DataValue heatCurrent = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Heat",
                "CurrentValue").ConfigureAwait(false)).ConfigureAwait(false);
            DataValue heatTarget = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Heat",
                "TargetValue").ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
                Assert.That(multiMode.CurrentMode, Is.EqualTo(1));
                Assert.That(multiMode.GetTargetValue("Cool"), Is.EqualTo(8.5));
                Assert.That(coolCurrent.WrappedValue.TryGetValue(out double coolCurrentValue), Is.True);
                Assert.That(coolCurrentValue, Is.EqualTo(8.5));
                Assert.That(coolTarget.WrappedValue.TryGetValue(out double coolTargetValue), Is.True);
                Assert.That(coolTargetValue, Is.EqualTo(8.5));
                Assert.That(multiMode.GetTargetValue("Heat"), Is.EqualTo(3.5));
                Assert.That(heatCurrent.WrappedValue.TryGetValue(out double heatCurrentValue), Is.True);
                Assert.That(heatCurrentValue, Is.EqualTo(2.5));
                Assert.That(heatTarget.WrappedValue.TryGetValue(out double heatTargetValue), Is.True);
                Assert.That(heatTargetValue, Is.EqualTo(3.5));
            });
        }
    }
}
