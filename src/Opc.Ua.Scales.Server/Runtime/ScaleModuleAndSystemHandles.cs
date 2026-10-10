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
using Opc.Ua.Di;
using Opc.Ua.PackML;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// The runtime of a feeder or printer module of a scale (OPC 40200 §7.1,
    /// §7.2).
    /// </summary>
    public sealed class ScaleModuleHandle
    {
        internal ScaleModuleHandle(ScaleRuntimeServices services, ComponentState module, ScaleHandle? owner)
        {
            m_services = services;
            Module = module;
            m_owner = owner;
            Notifications = new ScaleNotifications(services.Context, module, services.Register);
            if (module is FeederModuleState feeder && feeder.SetFeederSpeed != null)
            {
                feeder.SetFeederSpeed.OnCall = (context, method, objectId, speed, units) =>
                {
                    if (m_owner?.CommandInterceptor is { } interceptor)
                    {
                        ServiceResult veto = interceptor(ScaleCommand.SetFeederSpeed);
                        if (ServiceResult.IsBad(veto))
                        {
                            return veto;
                        }
                    }
                    return SetFeederSpeed(speed, units);
                };
            }
            ScaleMachineryStates.SetItemState(services, MachineryItemState, ScaleItemState.NotExecuting);
            ScaleMachineryStates.SetOperationMode(services, MachineryOperationMode, ScaleOperationMode.Processing);
        }

        /// <summary>
        /// Gets the module node: a <c>FeederModuleState</c> or <c>PrinterModuleState</c>.
        /// </summary>
        public ComponentState Module { get; }

        /// <summary>
        /// Gets the module's NodeId.
        /// </summary>
        public NodeId NodeId => Module.NodeId;

        /// <summary>
        /// Gets the module as a feeder, or null.
        /// </summary>
        public FeederModuleState? Feeder => Module as FeederModuleState;

        /// <summary>
        /// Gets the module as a printer, or null.
        /// </summary>
        public PrinterModuleState? Printer => Module as PrinterModuleState;

        /// <summary>
        /// Gets the module's event and alarm publisher (for example
        /// FEEDER_FAULT, FEEDER_NOT_RUNNING, PRINTER_FAULT).
        /// </summary>
        public ScaleNotifications Notifications { get; }

        /// <summary>
        /// Sets the feeder speed, as <c>SetFeederSpeed</c> would: the speed
        /// has to be within the minimal and maximum feeder speed and the unit
        /// allowed (§7.1.4).
        /// </summary>
        /// <param name="speed">The target speed.</param>
        /// <param name="units">The unit of <paramref name="speed"/>.</param>
        public ServiceResult SetFeederSpeed(float speed, EUInformation units)
        {
            return Locked(() => SetFeederSpeedCore(speed, units));
        }

        private ServiceResult SetFeederSpeedCore(float speed, EUInformation units)
        {
            if (Feeder is not { FeederSpeed: { } target } feeder)
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported, "The module has no feeder speed.");
            }
            ArrayOf<EUInformation> allowed = target.AllowedEngineeringUnits?.Value ?? default;
            if (allowed.Count == 0 && ScaleValues.UnitsOf(m_services.Context, target) is { } own)
            {
                allowed = new[] { own }.ToArrayOf();
            }
            ServiceResult unit = ScaleValues.CheckUnit(units, allowed, requireSi: false);
            if (ServiceResult.IsBad(unit))
            {
                return unit;
            }
            if (float.IsNaN(speed) ||
                (ReadDouble(feeder.MinimalFeederSpeed) is double min && speed < min) ||
                (ReadDouble(feeder.MaximumFeederSpeed) is double max && speed > max))
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The feeder speed {0} is outside the minimal and maximum feeder speed.",
                    speed);
            }
            ScaleValues.Set(m_services.Context, target, Variant.From((double)speed));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Publishes whether the feeder runs, and raises or clears the
        /// FEEDER_NOT_RUNNING alarm when <paramref name="expectedRunning"/>
        /// says it should.
        /// </summary>
        /// <param name="running">Whether the feeder runs.</param>
        /// <param name="load">The feeder load, when measured.</param>
        /// <param name="expectedRunning">Whether the feeder is supposed to run.</param>
        public void PublishFeeder(bool running, double? load = null, bool expectedRunning = false)
        {
            if (Feeder is not { } feeder)
            {
                return;
            }
            Locked(() =>
            {
                ScaleValues.Set(m_services.Context, feeder.FeederRunning, running);
                if (load.HasValue)
                {
                    ScaleValues.Set(m_services.Context, feeder.FeederLoad, Variant.From(load.Value));
                }
                return true;
            });
            Notifications.SetAlarm(
                ScaleNotificationId.FeederNotRunning,
                expectedRunning && !running,
                new LocalizedText(expectedRunning && !running ? "The feeder is not running." : "The feeder is running."));
        }

        /// <summary>
        /// Publishes the label and print media stock of a printer module.
        /// </summary>
        /// <param name="labelStockPercent">The label stock in percent.</param>
        /// <param name="printMediaStockPercent">The print media stock in percent.</param>
        public void PublishPrinterStock(double? labelStockPercent, double? printMediaStockPercent)
        {
            if (Printer is not { } printer)
            {
                return;
            }
            Locked(() =>
            {
                if (labelStockPercent.HasValue)
                {
                    ScaleValues.Set(m_services.Context, printer.LabelStock, Variant.From(labelStockPercent.Value));
                }
                if (printMediaStockPercent.HasValue)
                {
                    ScaleValues.Set(m_services.Context, printer.PrintMediaStock, Variant.From(printMediaStockPercent.Value));
                }
                return true;
            });
        }

        /// <summary>
        /// Sets the module's <c>MachineryItemState</c>, when it has one.
        /// </summary>
        /// <param name="state">The state.</param>
        public void SetItemState(ScaleItemState state)
        {
            Locked(() =>
            {
                ScaleMachineryStates.SetItemState(m_services, MachineryItemState, state);
                return true;
            });
        }

        /// <summary>
        /// Runs a module update under the lock of the scale the module belongs
        /// to, so it cannot interleave with the scale's methods.
        /// </summary>
        /// <typeparam name="T">The result type of the update.</typeparam>
        private T Locked<T>(Func<T> action)
        {
            if (m_owner != null)
            {
                return m_owner.Locked(action);
            }
            lock (m_lock)
            {
                return action();
            }
        }

        private Opc.Ua.Machinery.MachineryItemState_StateMachineState? MachineryItemState =>
            (Module as FeederModuleState)?.MachineryItemState ?? (Module as PrinterModuleState)?.MachineryItemState;

        private Opc.Ua.Machinery.MachineryOperationModeStateMachineState? MachineryOperationMode =>
            (Module as FeederModuleState)?.MachineryOperationMode ?? (Module as PrinterModuleState)?.MachineryOperationMode;

        private static double? ReadDouble(BaseVariableState? variable)
        {
            if (variable == null)
            {
                return null;
            }
            Variant value = variable.WrappedValue;
            return value.TryGetValue(out double d) ? d
                : value.TryGetValue(out float f) ? f
                : null;
        }

        private readonly ScaleRuntimeServices m_services;
        private readonly ScaleHandle? m_owner;
        private readonly Lock m_lock = new();
    }

    /// <summary>
    /// The runtime of an OPC 40200 scale system (§7.3): its scales, its
    /// production preset and statistics, its PackML <c>SystemState</c> and
    /// <c>ResetGlobalStatistics</c>.
    /// </summary>
    public sealed class ScaleSystemHandle
    {
        internal ScaleSystemHandle(ScaleRuntimeServices services, ScaleSystemState system)
        {
            m_services = services;
            System = system;
            Notifications = new ScaleNotifications(services.Context, system, services.Register);
            if (system.SystemState != null)
            {
                PackML = new PackMLStateMachineController(services.Context, system.SystemState);
                PackML.Initialize(services.Options.PackMLInitialState);
            }
            if (system.ProductionPreset != null)
            {
                ProductionPreset = new ScaleProductionPreset(
                    services,
                    system.ProductionPreset,
                    services.ScalesId(ObjectTypes.ProductType),
                    () => default);
                ProductionPreset.OnRegistered();
            }
            if (system.ProductionOutput != null)
            {
                ProductionOutput = new ScaleStatistics(services.Context, system.ProductionOutput);
            }
            if (system.ResetGlobalStatistics != null)
            {
                system.ResetGlobalStatistics.OnCallMethod2 = (context, method, objectId, inputs, outputs) =>
                {
                    if (CommandInterceptor is { } interceptor)
                    {
                        ServiceResult veto = interceptor(ScaleCommand.ResetGlobalStatistics);
                        if (ServiceResult.IsBad(veto))
                        {
                            return veto;
                        }
                    }
                    ResetGlobalStatistics();
                    return ServiceResult.Good;
                };
            }
            ScaleMachineryStates.SetItemState(services, system.MachineryItemState, ScaleItemState.NotExecuting);
            ScaleMachineryStates.SetOperationMode(services, system.MachineryOperationMode, ScaleOperationMode.Processing);
        }

        /// <summary>
        /// Gets the scale system node.
        /// </summary>
        public ScaleSystemState System { get; }

        /// <summary>
        /// Gets the system's NodeId.
        /// </summary>
        public NodeId NodeId => System.NodeId;

        /// <summary>
        /// Gets the scales of the system.
        /// </summary>
        public ArrayOf<ScaleHandle> Scales => m_scales;

        /// <summary>
        /// Gets the system's event and alarm publisher.
        /// </summary>
        public ScaleNotifications Notifications { get; }

        /// <summary>
        /// Gets the PackML <c>SystemState</c> controller, when present.
        /// </summary>
        public PackMLStateMachineController? PackML { get; }

        /// <summary>
        /// Gets the system's production preset, when present.
        /// </summary>
        public ScaleProductionPreset? ProductionPreset { get; }

        /// <summary>
        /// Gets the system's <c>ProductionOutput</c> statistics, when present.
        /// </summary>
        public ScaleStatistics? ProductionOutput { get; }

        /// <summary>
        /// Gets or sets a hook <c>ResetGlobalStatistics</c> passes first; a
        /// bad result rejects the call.
        /// </summary>
        public Func<ScaleCommand, ServiceResult>? CommandInterceptor { get; set; }

        /// <summary>
        /// Resets every statistic of the system and its scales that is not
        /// related to a product (§7.3.4): the <c>ProductionOutput</c> of the
        /// system and of each scale.
        /// </summary>
        public void ResetGlobalStatistics()
        {
            ProductionOutput?.Reset("ResetGlobalStatistics");
            foreach (ScaleHandle scale in m_scales)
            {
                scale.ProductionOutput?.Reset("ResetGlobalStatistics");
            }
        }

        /// <summary>
        /// Publishes the process state; <c>ProcessStateMessage</c> is mandatory
        /// on a scale system.
        /// </summary>
        /// <param name="processStateId">The machine-readable state id.</param>
        /// <param name="processStateMessage">The human-readable state message.</param>
        public void SetProcessState(string? processStateId, LocalizedText processStateMessage)
        {
            lock (m_lock)
            {
                if (processStateId != null)
                {
                    ScaleValues.Set(m_services.Context, System.ProcessStateId, processStateId);
                }
                ScaleValues.Set(m_services.Context, System.ProcessStateMessage, processStateMessage);
            }
        }

        internal void AddScale(ScaleHandle scale)
        {
            m_scales = m_scales.AddItem(scale);
        }

        private readonly ScaleRuntimeServices m_services;
        private readonly Lock m_lock = new();
        private ArrayOf<ScaleHandle> m_scales = [];
    }
}
