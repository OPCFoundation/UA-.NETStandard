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

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// Independently selectable device capabilities from OPC 40082-6 clauses 7 and 8.
    /// Maintenance and desiccant beds are selected by supplying their instance descriptions.
    /// </summary>
    [Flags]
    public enum GranulateDryingDeviceFeatures
    {
        /// <summary>
        /// Only mandatory device members.
        /// </summary>
        None = 0,

        /// <summary>
        /// The HMI display language.
        /// </summary>
        DisplayLanguage = 1 << 0,

        /// <summary>
        /// Machine-user login, logout, and actual-user information.
        /// </summary>
        Users = 1 << 1,

        /// <summary>
        /// Physical device identification.
        /// </summary>
        IdentifyDevice = 1 << 2,

        /// <summary>
        /// Reset all active errors.
        /// </summary>
        ResetAllErrors = 1 << 3,

        /// <summary>
        /// Reset a selected active error.
        /// </summary>
        ResetErrorById = 1 << 4,

        /// <summary>
        /// Reset device and component energy measurements.
        /// </summary>
        ResetEnergyMeasurement = 1 << 5,

        /// <summary>
        /// Persisted power-failure behavior.
        /// </summary>
        MainFailureBehaviour = 1 << 6,

        /// <summary>
        /// Device lifetime energy and power.
        /// </summary>
        TotalEnergy = 1 << 7,

        /// <summary>
        /// Resettable device energy in kWh.
        /// </summary>
        EnergyConsumptionResettable = 1 << 8,

        /// <summary>
        /// Relative energy-consumption limit.
        /// </summary>
        EnergyConsumptionLimit = 1 << 9,

        /// <summary>
        /// The optional Machinery operation-mode add-in.
        /// </summary>
        OperationMode = 1 << 10,

        /// <summary>
        /// Optional common machine-configuration page operations.
        /// </summary>
        ConfigurationPages = 1 << 11,

        /// <summary>
        /// Every selectable device member.
        /// </summary>
        All = (1 << 12) - 1
    }

    /// <summary>
    /// Independently selectable bin capabilities; temperature and drying mode are always present.
    /// Selecting a control group creates its methods and its response-status variable together.
    /// </summary>
    [Flags]
    public enum GranulateDryingBinFeatures : ulong
    {
        /// <summary>
        /// Only mandatory bin members.
        /// </summary>
        None = 0,

        /// <summary>
        /// Material identification.
        /// </summary>
        MaterialIdentification = 1UL << 0,

        /// <summary>
        /// Batch identification.
        /// </summary>
        BatchNumber = 1UL << 1,

        /// <summary>
        /// The specified material drying duration.
        /// </summary>
        MaterialDryingTime = 1UL << 2,

        /// <summary>
        /// The remaining drying duration.
        /// </summary>
        RemainingMaterialDryingTime = 1UL << 3,

        /// <summary>
        /// The elapsed drying duration.
        /// </summary>
        ExpiredDryingTime = 1UL << 4,

        /// <summary>
        /// Relative temperature reduction in kelvin.
        /// </summary>
        TemperatureReduction = 1UL << 5,

        /// <summary>
        /// Drying-protection activation, deactivation, and status.
        /// </summary>
        DryingProtection = 1UL << 6,

        /// <summary>
        /// Material availability at the dryer feeder.
        /// </summary>
        MaterialAvailableDryer = 1UL << 7,

        /// <summary>
        /// Material availability at the machine feeder.
        /// </summary>
        MaterialAvailableMachine = 1UL << 8,

        /// <summary>
        /// Dryer-feeder control and status.
        /// </summary>
        MaterialFeeder = 1UL << 9,

        /// <summary>
        /// Machine-feeder control and status.
        /// </summary>
        MachineFeeder = 1UL << 10,

        /// <summary>
        /// Combined feeder control and status.
        /// </summary>
        AllFeeders = 1UL << 11,

        /// <summary>
        /// Temperature-controller activation, deactivation, and status.
        /// </summary>
        TemperatureController = 1UL << 12,

        /// <summary>
        /// Dust-removal pulse count.
        /// </summary>
        DustRemovalPulses = 1UL << 13,

        /// <summary>
        /// Requested drying level.
        /// </summary>
        DryingLevel = 1UL << 14,

        /// <summary>
        /// Airflow mode.
        /// </summary>
        AirFlowMode = 1UL << 15,

        /// <summary>
        /// Normal-flow interval duration.
        /// </summary>
        IntervalTimeOn = 1UL << 16,

        /// <summary>
        /// Low-flow interval duration.
        /// </summary>
        IntervalTimeOff = 1UL << 17,

        /// <summary>
        /// The complete interval-airflow group.
        /// </summary>
        AirFlowInterval = AirFlowMode | IntervalTimeOn | IntervalTimeOff,

        /// <summary>
        /// Initial filling indication.
        /// </summary>
        InitialFillingActive = 1UL << 18,

        /// <summary>
        /// Drying-timer reset and response timestamp.
        /// </summary>
        ResetDryingTime = 1UL << 19,

        /// <summary>
        /// Fresh-air or return-air selection.
        /// </summary>
        AirSource = 1UL << 20,

        /// <summary>
        /// Monitored bin airflow.
        /// </summary>
        AirFlow = 1UL << 21,

        /// <summary>
        /// Absolute air-volume consumption.
        /// </summary>
        TotalAirConsumption = 1UL << 22,

        /// <summary>
        /// Bin dew point.
        /// </summary>
        DewPoint = 1UL << 23,

        /// <summary>
        /// Airflow-reduction control during protection or storage.
        /// </summary>
        DryingProtectionAirVolumeFlowReduction = 1UL << 24,

        /// <summary>
        /// Dry-air temperature.
        /// </summary>
        DryAirTemperature = 1UL << 25,

        /// <summary>
        /// Ambient-air temperature.
        /// </summary>
        AmbientAirTemperature = 1UL << 26,

        /// <summary>
        /// Top-of-bin temperature.
        /// </summary>
        TopTemperature = 1UL << 27,

        /// <summary>
        /// Cone temperature.
        /// </summary>
        ConeTemperature = 1UL << 28,

        /// <summary>
        /// Cylinder temperature.
        /// </summary>
        CylinderTemperature = 1UL << 29,

        /// <summary>
        /// Loosening-aid pulse count.
        /// </summary>
        LooseningAid = 1UL << 30,

        /// <summary>
        /// Bin lifetime energy and power.
        /// </summary>
        TotalEnergy = 1UL << 31,

        /// <summary>
        /// Resettable bin energy in kWh.
        /// </summary>
        EnergyConsumptionResettable = 1UL << 32,

        /// <summary>
        /// Every selectable bin member.
        /// </summary>
        All = (1UL << 33) - 1
    }

    /// <summary>
    /// Independently selectable desiccant-bed capabilities from OPC 40082-6 clause 11.
    /// </summary>
    [Flags]
    public enum GranulateDryingBedFeatures
    {
        /// <summary>
        /// No optional members.
        /// </summary>
        None = 0,

        /// <summary>
        /// Monitored dew point.
        /// </summary>
        DewPoint = 1 << 0,

        /// <summary>
        /// Controlled airflow.
        /// </summary>
        AirFlow = 1 << 1,

        /// <summary>
        /// Controlled bed temperature.
        /// </summary>
        TemperatureOfDesiccantBed = 1 << 2,

        /// <summary>
        /// Monitored outlet temperature.
        /// </summary>
        TemperatureDesiccantBedOut = 1 << 3,

        /// <summary>
        /// Monitored return temperature.
        /// </summary>
        TemperatureBackToDesiccantBed = 1 << 4,

        /// <summary>
        /// Resettable bed energy in kWh.
        /// </summary>
        EnergyConsumptionResettable = 1 << 5,

        /// <summary>
        /// Bed lifetime energy and power.
        /// </summary>
        TotalEnergy = 1 << 6,

        /// <summary>
        /// Controller activation, deactivation, and response status.
        /// </summary>
        Controller = 1 << 7,

        /// <summary>
        /// Every selectable bed member.
        /// </summary>
        All = (1 << 8) - 1
    }
}
