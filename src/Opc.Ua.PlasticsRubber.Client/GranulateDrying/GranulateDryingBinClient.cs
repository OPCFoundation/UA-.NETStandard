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
    /// Every mandatory and optional BinType variable and generated feeder/protection/controller method.
    /// Missing optional variables remain distinguishable from zero, false, or OFF.
    /// </summary>
    public sealed class GranulateDryingBinClient
    {
        /// <summary>
        /// Opens one actual BinType instance.
        /// </summary>
        internal GranulateDryingBinClient(GranulateDryingClientContext context, NodeId node)
        {
            m_context = context;
            Proxy = new BinTypeClient(context.Session, node, context.Telemetry);
        }

        /// <summary>
        /// Gets the generated exact-signature control methods and parameter child proxies.
        /// </summary>
        public BinTypeClient Proxy { get; }

        /// <summary>
        /// Gets material identification.
        /// </summary>
        public GranulateDryingVariable<string> MaterialIdentification =>
            Value("MaterialIdentification", GranulateDryingCodecs.String, true);

        /// <summary>
        /// Gets batch identification.
        /// </summary>
        public GranulateDryingVariable<string> BatchNumber => Value("BatchNumber", GranulateDryingCodecs.String, true);

        /// <summary>
        /// Gets the material drying preset in UA Duration milliseconds.
        /// </summary>
        public GranulateDryingVariable<double> MaterialDryingTime =>
            Value("MaterialDryingTime", GranulateDryingCodecs.Double, true);

        /// <summary>
        /// Gets the remaining drying duration in milliseconds.
        /// </summary>
        public GranulateDryingVariable<double> RemainingMaterialDryingTime =>
            Value("RemainingMaterialDryingTime", GranulateDryingCodecs.Double);

        /// <summary>
        /// Gets elapsed drying duration in milliseconds.
        /// </summary>
        public GranulateDryingVariable<double> ExpiredDryingTime =>
            Value("ExpiredDryingTime", GranulateDryingCodecs.Double);

        /// <summary>
        /// Gets the relative protection/storage temperature reduction in kelvin.
        /// </summary>
        public GranulateDryingVariable<double> TemperatureReduction =>
            Value("TemperatureReduction", GranulateDryingCodecs.Double, true);

        /// <summary>
        /// Gets actual or requested drying-protection status.
        /// </summary>
        public GranulateDryingVariable<FunctionStatusEnumeration> DryingProtectionStatus =>
            FunctionStatus("DryingProtectionStatus");

        /// <summary>
        /// Gets dryer-feed material availability.
        /// </summary>
        public GranulateDryingVariable<bool> MaterialAvailableDryer =>
            Value("MaterialAvailableDryer", GranulateDryingCodecs.Boolean);

        /// <summary>
        /// Gets machine-feed material availability; this does not imply the material is dry.
        /// </summary>
        public GranulateDryingVariable<bool> MaterialAvailableMachine =>
            Value("MaterialAvailableMachine", GranulateDryingCodecs.Boolean);

        /// <summary>
        /// Gets actual or requested dryer-feeder status.
        /// </summary>
        public GranulateDryingVariable<FunctionStatusEnumeration> MaterialFeederDryerStatus =>
            FunctionStatus("MaterialFeederDryerStatus");

        /// <summary>
        /// Gets actual or requested machine-feeder status.
        /// </summary>
        public GranulateDryingVariable<FunctionStatusEnumeration> MaterialFeederMachineStatus =>
            FunctionStatus("MaterialFeederMachineStatus");

        /// <summary>
        /// Gets combined feeder status.
        /// </summary>
        public GranulateDryingVariable<FunctionStatusEnumeration> AllFeedersStatus
            => FunctionStatus("AllFeedersStatus");

        /// <summary>
        /// Gets actual or requested temperature-controller status.
        /// </summary>
        public GranulateDryingVariable<FunctionStatusEnumeration> TemperatureControllerStatus =>
            FunctionStatus("TemperatureControllerStatus");

        /// <summary>
        /// Gets the post-fill dust-removal pulse count.
        /// </summary>
        public GranulateDryingVariable<ushort> DustRemovalPulses =>
            Value("DustRemovalPulses", GranulateDryingCodecs.UInt16, true);

        /// <summary>
        /// Gets the mandatory drying mode.
        /// </summary>
        public GranulateDryingVariable<DryingModeEnumeration> DryingMode =>
            Value("DryingMode", GranulateDryingCodecs.Enumeration<DryingModeEnumeration>(), true);

        /// <summary>
        /// Gets the requested drying level.
        /// </summary>
        public GranulateDryingVariable<DryingLevelEnumeration> DryingLevel =>
            Value("DryingLevel", GranulateDryingCodecs.Enumeration<DryingLevelEnumeration>(), true);

        /// <summary>
        /// Gets airflow mode.
        /// </summary>
        public GranulateDryingVariable<AirFlowModeEnumeration> AirFlowMode =>
            Value("AirFlowMode", GranulateDryingCodecs.Enumeration<AirFlowModeEnumeration>(), true);

        /// <summary>
        /// Gets normal-flow interval duration in milliseconds.
        /// </summary>
        public GranulateDryingVariable<double> IntervalTimeOn =>
            Value("IntervalTimeOn", GranulateDryingCodecs.Double, true);

        /// <summary>
        /// Gets reduced-flow interval duration in milliseconds.
        /// </summary>
        public GranulateDryingVariable<double> IntervalTimeOff =>
            Value("IntervalTimeOff", GranulateDryingCodecs.Double, true);

        /// <summary>
        /// Gets whether initial filling is in progress.
        /// </summary>
        public GranulateDryingVariable<bool> InitialFillingActive =>
            Value("InitialFillingActive", GranulateDryingCodecs.Boolean);

        /// <summary>
        /// Gets the authority's receipt timestamp for the last drying-timer reset.
        /// </summary>
        public GranulateDryingVariable<DateTimeUtc> ResetDryingTimeResponseStatus =>
            Value("ResetDryingTimeResponseStatus", GranulateDryingCodecs.Time);

        /// <summary>
        /// Gets fresh-air or return-air selection.
        /// </summary>
        public GranulateDryingVariable<AirSourceEnumeration> AirSource =>
            Value("AirSource", GranulateDryingCodecs.Enumeration<AirSourceEnumeration>(), true);

        /// <summary>
        /// Gets total air consumption; read EngineeringUnits rather than assuming a server unit system.
        /// </summary>
        public GranulateDryingVariable<double> TotalAirConsumption =>
            Value("TotalAirConsumption", GranulateDryingCodecs.Double);

        /// <summary>
        /// Gets the bin dew point.
        /// </summary>
        public GranulateDryingVariable<double> DewPoint => Value("DewPoint", GranulateDryingCodecs.Double);

        /// <summary>
        /// Gets actual or requested protective airflow-reduction status.
        /// </summary>
        public GranulateDryingVariable<FunctionStatusEnumeration> DryingProtectionAirVolumeFlowMode =>
            FunctionStatus("DryingProtectionAirVolumeFlowMode");

        /// <summary>
        /// Gets the loosening-aid pulse count.
        /// </summary>
        public GranulateDryingVariable<ushort> LooseningAid =>
            Value("LooseningAid", GranulateDryingCodecs.UInt16, true);

        /// <summary>
        /// Gets resettable energy in kWh.
        /// </summary>
        public GranulateDryingVariable<double> EnergyConsumptionResettable =>
            Value("EnergyConsumptionResettable", GranulateDryingCodecs.Double);

        /// <summary>
        /// Gets the mandatory material-temperature measurement with quality and timestamps.
        /// </summary>
        public GranulateDryingVariable<double> Temperature => Measurement("Temperature");

        /// <summary>
        /// Gets monitored bin airflow.
        /// </summary>
        public GranulateDryingVariable<double> AirFlow => Measurement("AirFlow");

        /// <summary>
        /// Gets dry-air temperature.
        /// </summary>
        public GranulateDryingVariable<double> DryAirTemperature => Measurement("DryAirTemperature");

        /// <summary>
        /// Gets ambient-air temperature.
        /// </summary>
        public GranulateDryingVariable<double> AmbientAirTemperature => Measurement("AmbientAirTemperature");

        /// <summary>
        /// Gets the top-of-bin temperature.
        /// </summary>
        public GranulateDryingVariable<double> TopTemperature => Measurement("TopTemperature");

        /// <summary>
        /// Gets the cone temperature.
        /// </summary>
        public GranulateDryingVariable<double> ConeTemperature => Measurement("ConeTemperature");

        /// <summary>
        /// Gets the cylinder temperature.
        /// </summary>
        public GranulateDryingVariable<double> CylinderTemperature => Measurement("CylinderTemperature");

        /// <summary>
        /// Gets lifetime energy consumption from the optional common energy group.
        /// </summary>
        public GranulateDryingVariable<double> LifetimeEnergy =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false,
                Gdd("TotalEnergy"), Common("PowerConsumption"));

        /// <summary>
        /// Gets actual power from the optional common energy group.
        /// </summary>
        public GranulateDryingVariable<double> ActualPower =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false,
                Gdd("TotalEnergy"), Common("ActualPower"));

        /// <summary>
        /// Gets the generated mandatory temperature-parameter proxy for ResetMonitoring and common child access.
        /// </summary>
        public ValueTask<MonitoredParameterTypeClient?> TemperatureParametersAsync(
            CancellationToken cancellationToken = default) =>
            Proxy.GetTemperatureAsync(m_context.Telemetry, cancellationToken);

        /// <summary>
        /// Creates a typed GDD variable.
        /// </summary>
        /// <typeparam name="T">The declared variable value type.</typeparam>
        private GranulateDryingVariable<T> Value<T>(string name, GranulateDryingCodec<T> codec,
            bool writable = false) =>
            m_context.Variable(Proxy.ObjectId, codec, writable, Gdd(name));

        /// <summary>
        /// Creates a typed FunctionStatus response.
        /// </summary>
        private GranulateDryingVariable<FunctionStatusEnumeration> FunctionStatus(string name) =>
            Value(name, GranulateDryingCodecs.Enumeration<FunctionStatusEnumeration>());

        /// <summary>
        /// Resolves a common ActualValue under a GDD-named monitored parameter.
        /// </summary>
        private GranulateDryingVariable<double> Measurement(string parameter) =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false,
                Gdd(parameter), Common("ActualValue"));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }

    /// <summary>
    /// All optional desiccant-bed measurements and its generated controller methods.
    /// </summary>
    public sealed class GranulateDryingBedClient
    {
        /// <summary>
        /// Opens one actual bed instance.
        /// </summary>
        internal GranulateDryingBedClient(GranulateDryingClientContext context, NodeId node)
        {
            m_context = context;
            Proxy = new DesiccantBedTypeClient(context.Session, node, context.Telemetry);
        }

        /// <summary>
        /// Gets the generated controller methods and monitored/controlled parameter proxies.
        /// </summary>
        public DesiccantBedTypeClient Proxy { get; }

        /// <summary>
        /// Gets monitored bed dew point.
        /// </summary>
        public GranulateDryingVariable<double> DewPoint => Measurement("DewPoint");

        /// <summary>
        /// Gets actual controlled airflow.
        /// </summary>
        public GranulateDryingVariable<double> AirFlow => Measurement("AirFlow");

        /// <summary>
        /// Gets actual controlled bed temperature.
        /// </summary>
        public GranulateDryingVariable<double> TemperatureOfDesiccantBed => Measurement("TemperatureOfDesiccantBed");

        /// <summary>
        /// Gets monitored outlet temperature.
        /// </summary>
        public GranulateDryingVariable<double> TemperatureDesiccantBedOut => Measurement("TemperatureDesiccantBedOut");

        /// <summary>
        /// Gets monitored return-air temperature.
        /// </summary>
        public GranulateDryingVariable<double> TemperatureBackToDesiccantBed
            => Measurement("TemperatureBackToDesiccantBed");

        /// <summary>
        /// Gets resettable bed energy in kWh.
        /// </summary>
        public GranulateDryingVariable<double> EnergyConsumptionResettable =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false, Gdd("EnergyConsumptionResettable"));

        /// <summary>
        /// Gets actual or requested bed-controller status.
        /// </summary>
        public GranulateDryingVariable<FunctionStatusEnumeration> DesiccantBedControllerStatus =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Enumeration<FunctionStatusEnumeration>(),
                false, Gdd("DesiccantBedControllerStatus"));

        /// <summary>
        /// Gets the optional controlled airflow reference.
        /// </summary>
        public GranulateDryingVariable<double> AirFlowSetValue =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, true, Gdd("AirFlow"), Common("SetValue"));

        /// <summary>
        /// Gets the optional controlled bed-temperature reference.
        /// </summary>
        public GranulateDryingVariable<double> TemperatureSetValue =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, true,
                Gdd("TemperatureOfDesiccantBed"), Common("SetValue"));

        /// <summary>
        /// Gets lifetime bed energy consumption.
        /// </summary>
        public GranulateDryingVariable<double> LifetimeEnergy =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false,
                Gdd("TotalEnergy"), Common("PowerConsumption"));

        /// <summary>
        /// Gets actual bed power.
        /// </summary>
        public GranulateDryingVariable<double> ActualPower =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false,
                Gdd("TotalEnergy"), Common("ActualPower"));

        /// <summary>
        /// Resolves a common ActualValue beneath its GDD declaration.
        /// </summary>
        private GranulateDryingVariable<double> Measurement(string parameter) =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.Double, false,
                Gdd(parameter), Common("ActualValue"));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }
}
