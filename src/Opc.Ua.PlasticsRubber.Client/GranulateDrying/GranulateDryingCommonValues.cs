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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PlasticsRubber;
using static Opc.Ua.GranulateDrying.Client.GranulateDryingClientContext;

namespace Opc.Ua.GranulateDrying.Client
{
    /// <summary>
    /// Typed variable access beside a generated monitored/controlled parameter proxy.
    /// A plain monitored setpoint is informational; a controlled setpoint belongs to its physical controller.
    /// </summary>
    public sealed class GranulateDryingParameterClient
    {
        /// <summary>
        /// Opens common values without replacing the generated method implementation.
        /// </summary>
        internal GranulateDryingParameterClient(
            GranulateDryingClientContext context, MonitoredParameterTypeClient proxy)
        {
            m_context = context;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets the original generated proxy, including ResetMonitoring.
        /// </summary>
        public MonitoredParameterTypeClient Proxy { get; }

        /// <summary>
        /// Gets the actual measurement.
        /// </summary>
        public GranulateDryingVariable<double> ActualValue => Number("ActualValue", false);

        /// <summary>
        /// Gets the optional monitored reference or controlled setpoint.
        /// </summary>
        public GranulateDryingVariable<double> SetValue => Number("SetValue");

        /// <summary>
        /// Gets the optional minimum monitoring boundary.
        /// </summary>
        public GranulateDryingVariable<double> MinValue => Number("MinValue");

        /// <summary>
        /// Gets the optional maximum monitoring boundary.
        /// </summary>
        public GranulateDryingVariable<double> MaxValue => Number("MaxValue");

        /// <summary>
        /// Gets the signed nonpositive first lower offset.
        /// </summary>
        public GranulateDryingVariable<double> LowerTolerance => Number("LowerTolerance");

        /// <summary>
        /// Gets the nonnegative first upper offset.
        /// </summary>
        public GranulateDryingVariable<double> UpperTolerance => Number("UpperTolerance");

        /// <summary>
        /// Gets the outer signed lower offset, at or below LowerTolerance.
        /// </summary>
        public GranulateDryingVariable<double> LowerTolerance2 => Number("LowerTolerance2");

        /// <summary>
        /// Gets the outer upper offset, at or above UpperTolerance.
        /// </summary>
        public GranulateDryingVariable<double> UpperTolerance2 => Number("UpperTolerance2");

        /// <summary>
        /// Gets the increasing ramp; only ControlledParameter and its subtypes permit writing it.
        /// </summary>
        public GranulateDryingVariable<double> SetRampUp => Number("SetRampUp", Proxy is ControlledParameterTypeClient);

        /// <summary>
        /// Gets the decreasing ramp; only ControlledParameter and its subtypes permit writing it.
        /// </summary>
        public GranulateDryingVariable<double> SetRampDown => Number("SetRampDown",
            Proxy is ControlledParameterTypeClient);

        /// <summary>
        /// Gets automatic tolerance monitoring.
        /// </summary>
        public GranulateDryingVariable<bool> AutomaticMonitoring =>
            Value("AutomaticMonitoring", GranulateDryingCodecs.Boolean, true);

        /// <summary>
        /// Gets monitoring sensitivity.
        /// </summary>
        public GranulateDryingVariable<ushort> MonitoringSensitivity =>
            Value("MonitoringSensitivity", GranulateDryingCodecs.UInt16, true);

        /// <summary>
        /// Gets alarm suppression without hiding the measured parameter status.
        /// </summary>
        public GranulateDryingVariable<ushort> AlarmSuppression =>
            Value("AlarmSuppression", GranulateDryingCodecs.UInt16, true);

        /// <summary>
        /// Gets the common monitoring-status code.
        /// </summary>
        public GranulateDryingVariable<ushort> Status => Value("Status", GranulateDryingCodecs.UInt16);

        /// <summary>
        /// Opens the optional generated closed-loop child of a controlled parameter.
        /// </summary>
        public async ValueTask<GranulateDryingClosedLoopClient?> ClosedLoopAsync(
            CancellationToken cancellationToken = default)
        {
            if (Proxy is not ControlledParameterTypeClient controlled)
            {
                return null;
            }
            ClosedLoopControlTypeClient? child = await controlled
                .GetClosedLoopControlAsync(m_context.Telemetry, cancellationToken).ConfigureAwait(false);
            return child == null ? null : new GranulateDryingClosedLoopClient(m_context, child);
        }

        /// <summary>
        /// Creates a statically typed numeric member.
        /// </summary>
        private GranulateDryingVariable<double> Number(string name, bool writable = true) =>
            Value(name, GranulateDryingCodecs.Double, writable);

        /// <summary>
        /// Creates a common-namespace variable.
        /// </summary>
        /// <typeparam name="T">The declared variable value type.</typeparam>
        private GranulateDryingVariable<T> Value<T>(string name, GranulateDryingCodec<T> codec,
            bool writable = false) =>
            m_context.Variable(Proxy.ObjectId, codec, writable, Common(name));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }

    /// <summary>
    /// Typed PID and tuning values alongside the generated common tuning methods.
    /// </summary>
    public sealed class GranulateDryingClosedLoopClient
    {
        /// <summary>
        /// Opens the selected generated closed-loop instance.
        /// </summary>
        internal GranulateDryingClosedLoopClient(
            GranulateDryingClientContext context, ClosedLoopControlTypeClient proxy)
        {
            m_context = context;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets the generated AutoTuningOn/AutoTuningOff methods.
        /// </summary>
        public ClosedLoopControlTypeClient Proxy { get; }

        /// <summary>
        /// Gets the generated structured PID coefficient array.
        /// </summary>
        public GranulateDryingVariable<ArrayOf<PIDParametersDataType>> PIDParameters =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Structures<PIDParametersDataType>(),
                true, Common("PIDParameters"));

        /// <summary>
        /// Gets automatic-controller-mode selection.
        /// </summary>
        public GranulateDryingVariable<bool> AutomaticControllerMode =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Boolean, true, Common("AutomaticControllerMode"));

        /// <summary>
        /// Gets actual tuning activity; invoking AutoTuningOn does not make this true optimistically.
        /// </summary>
        public GranulateDryingVariable<bool> AutoTuningActive =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Boolean, false, Common("AutoTuningActive"));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }

    /// <summary>
    /// All common energy variables, each retaining its own unit, quality, and source time.
    /// </summary>
    public sealed class GranulateDryingEnergyClient
    {
        /// <summary>
        /// Opens a generated energy instance.
        /// </summary>
        internal GranulateDryingEnergyClient(GranulateDryingClientContext context, EnergyTypeClient proxy)
        {
            m_context = context;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets the generated energy proxy.
        /// </summary>
        public EnergyTypeClient Proxy { get; }

        /// <summary>
        /// Gets actual power.
        /// </summary>
        public GranulateDryingVariable<double> ActualPower => Number("ActualPower");

        /// <summary>
        /// Gets lifetime energy consumption.
        /// </summary>
        public GranulateDryingVariable<double> PowerConsumption => Number("PowerConsumption");

        /// <summary>
        /// Gets specific energy in the device's declared unit.
        /// </summary>
        public GranulateDryingVariable<double> ActualSpecificEnergy => Number("ActualSpecificEnergy");

        /// <summary>
        /// Gets the optional power factor.
        /// </summary>
        public GranulateDryingVariable<double> PowerFactor => Number("PowerFactor");

        /// <summary>
        /// Creates an optional common energy reading.
        /// </summary>
        private GranulateDryingVariable<double> Number(string member) =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false, Common(member));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }

    /// <summary>
    /// Typed machine naming, location, timezone, and page catalogue beside generated clock/page methods.
    /// </summary>
    public sealed class GranulateDryingConfigurationClient
    {
        /// <summary>
        /// Opens generated common configuration.
        /// </summary>
        internal GranulateDryingConfigurationClient(
            GranulateDryingClientContext context, MachineConfigurationTypeClient proxy)
        {
            m_context = context;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets the generated SetMachineTime/GetPage/GetCurrentPage methods.
        /// </summary>
        public MachineConfigurationTypeClient Proxy { get; }

        /// <summary>
        /// Gets the user-assigned device name.
        /// </summary>
        public GranulateDryingVariable<string> UserMachineName =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.String, true, Common("UserMachineName"));

        /// <summary>
        /// Gets the device location label.
        /// </summary>
        public GranulateDryingVariable<string> LocationName =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.String, true, Common("LocationName"));

        /// <summary>
        /// Gets the device's timezone, not the client or server host's timezone.
        /// </summary>
        public GranulateDryingVariable<TimeZoneDataType> TimeZoneOffset =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Structure<TimeZoneDataType>(),
                true, Common("TimeZoneOffset"));

        /// <summary>
        /// Gets the optional generated page directory.
        /// </summary>
        public GranulateDryingVariable<ArrayOf<PageEntryDataType>> PageDirectory =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Structures<PageEntryDataType>(),
                false, Common("PageDirectory"));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }
}
