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

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Selects compiled, reflection-free factories from the single PA-DIM model producer.
    /// </summary>
    internal static class PadimFactories
    {
        /// <summary>
        /// Resolves the selected generated type identifier without constructing an instance.
        /// </summary>
        internal static ExpandedNodeId DeviceType(PadimDeviceKind kind)
        {
            return kind switch
            {
                PadimDeviceKind.Device => ObjectTypeIds.PADIMType,
                PadimDeviceKind.ProcessAnalyser => ObjectTypeIds.ProcessAnalyserType,
                PadimDeviceKind.AmperometricAnalyser => ObjectTypeIds.AmperometricAnalyserType,
                PadimDeviceKind.ConductivityMeter => ObjectTypeIds.ConductivityMeterType,
                PadimDeviceKind.FlameIonisationDetector => ObjectTypeIds.FlameIonisationDetectorType,
                PadimDeviceKind.NonDispersiveInfraredGasAnalyser => ObjectTypeIds.NonDispersiveInfraredGasAnalyserType,
                PadimDeviceKind.OpticalFluorescenseQuenchingSensor =>
                    ObjectTypeIds.OpticalFluorescenseQuenchingSensorType,
                PadimDeviceKind.ParamagneticGasAnalyser => ObjectTypeIds.ParamagneticGasAnalyserType,
                PadimDeviceKind.PhMeter => ObjectTypeIds.PhMeterType,
                PadimDeviceKind.ThermalConductivityGasAnalyser => ObjectTypeIds.ThermalConductivityGasAnalyserType,
                PadimDeviceKind.TocAnalyser => ObjectTypeIds.TocAnalyserType,
                PadimDeviceKind.TunableDiodeLaserSpectrometer => ObjectTypeIds.TunableDiodeLaserSpectrometerType,
                PadimDeviceKind.ZirconiumDioxideAnalyser => ObjectTypeIds.ZirconiumDioxideAnalyserType,
                PadimDeviceKind.GasChromatograph => ObjectTypeIds.GasChromatographType,
                PadimDeviceKind.InfraredSensor => ObjectTypeIds.InfraredSensorType,
                PadimDeviceKind.AmperometricGasDetector => ObjectTypeIds.AmperometricGasDetectorType,
                PadimDeviceKind.CatalyticBeadSensor => ObjectTypeIds.CatalyticBeadSensorType,
                PadimDeviceKind.DiodeArraySpectrometer => ObjectTypeIds.DiodeArraySpectrometerType,
                PadimDeviceKind.RamanSpectrometer => ObjectTypeIds.RamanSpectrometerType,
                PadimDeviceKind.FtnirOrFtirSpectrometer => ObjectTypeIds.FtnirOrFtirSpectrometerType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Creates a concrete generated device with its mandatory instance declarations.
        /// </summary>
        internal static PADIMState CreateDevice(
            ISystemContext context, NodeState parent, QualifiedName name, PadimDeviceKind kind)
        {
            return kind switch
            {
                PadimDeviceKind.Device => context.CreateInstanceOfPADIMType(parent, name),
                PadimDeviceKind.ProcessAnalyser => context.CreateInstanceOfProcessAnalyserType(parent, name),
                PadimDeviceKind.AmperometricAnalyser => context.CreateInstanceOfAmperometricAnalyserType(parent, name),
                PadimDeviceKind.ConductivityMeter => context.CreateInstanceOfConductivityMeterType(parent, name),
                PadimDeviceKind.FlameIonisationDetector =>
                    context.CreateInstanceOfFlameIonisationDetectorType(parent, name),
                PadimDeviceKind.NonDispersiveInfraredGasAnalyser =>
                    context.CreateInstanceOfNonDispersiveInfraredGasAnalyserType(parent, name),
                PadimDeviceKind.OpticalFluorescenseQuenchingSensor =>
                    context.CreateInstanceOfOpticalFluorescenseQuenchingSensorType(parent, name),
                PadimDeviceKind.ParamagneticGasAnalyser =>
                    context.CreateInstanceOfParamagneticGasAnalyserType(parent, name),
                PadimDeviceKind.PhMeter => context.CreateInstanceOfPhMeterType(parent, name),
                PadimDeviceKind.ThermalConductivityGasAnalyser =>
                    context.CreateInstanceOfThermalConductivityGasAnalyserType(parent, name),
                PadimDeviceKind.TocAnalyser => context.CreateInstanceOfTocAnalyserType(parent, name),
                PadimDeviceKind.TunableDiodeLaserSpectrometer =>
                    context.CreateInstanceOfTunableDiodeLaserSpectrometerType(parent, name),
                PadimDeviceKind.ZirconiumDioxideAnalyser =>
                    context.CreateInstanceOfZirconiumDioxideAnalyserType(parent, name),
                PadimDeviceKind.GasChromatograph => context.CreateInstanceOfGasChromatographType(parent, name),
                PadimDeviceKind.InfraredSensor => context.CreateInstanceOfInfraredSensorType(parent, name),
                PadimDeviceKind.AmperometricGasDetector =>
                    context.CreateInstanceOfAmperometricGasDetectorType(parent, name),
                PadimDeviceKind.CatalyticBeadSensor => context.CreateInstanceOfCatalyticBeadSensorType(parent, name),
                PadimDeviceKind.DiodeArraySpectrometer =>
                    context.CreateInstanceOfDiodeArraySpectrometerType(parent, name),
                PadimDeviceKind.RamanSpectrometer => context.CreateInstanceOfRamanSpectrometerType(parent, name),
                PadimDeviceKind.FtnirOrFtirSpectrometer =>
                    context.CreateInstanceOfFtnirOrFtirSpectrometerType(parent, name),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Creates a concrete generated signal, retaining its family-specific declared value type.
        /// </summary>
        internal static SignalState CreateSignal(
            ISystemContext context, NodeState parent, QualifiedName name, PadimSignalKind kind)
        {
            return kind switch
            {
                PadimSignalKind.Analog => context.CreateInstanceOfAnalogSignalType(parent, name),
                PadimSignalKind.Control => context.CreateInstanceOfControlSignalType(parent, name),
                PadimSignalKind.Discrete => context.CreateInstanceOfDiscreteSignalType(parent, name),
                PadimSignalKind.TwoStateDiscrete => context.CreateInstanceOfTwoStateDiscreteSignalType(parent, name),
                PadimSignalKind.MultiStateDiscrete =>
                    context.CreateInstanceOfMultiStateDiscreteSignalType(parent, name),
                PadimSignalKind.TwoStateControl =>
                    context.CreateInstanceOfTwoStateDiscreteControlSignalType(parent, name),
                PadimSignalKind.MultiStateControl =>
                    context.CreateInstanceOfMultiStateDiscreteControlSignalType(parent, name),
                PadimSignalKind.Analytical => context.CreateInstanceOfAnalyticalSignalType(parent, name),
                PadimSignalKind.Amperometric => context.CreateInstanceOfAmperometricSignalType(parent, name),
                PadimSignalKind.Conductivity => context.CreateInstanceOfConductivitySignalType(parent, name),
                PadimSignalKind.FlameIonisation => context.CreateInstanceOfFlameIonisationSignalType(parent, name),
                PadimSignalKind.NonDispersiveInfrared =>
                    context.CreateInstanceOfNonDispersiveInfraredSignalType(parent, name),
                PadimSignalKind.OpticalFluorescenseQuenching =>
                    context.CreateInstanceOfOpticalFluorescenseQuenchingSignalType(parent, name),
                PadimSignalKind.Paramagnetic => context.CreateInstanceOfParamagneticSignalType(parent, name),
                PadimSignalKind.Ph => context.CreateInstanceOfPhSignalType(parent, name),
                PadimSignalKind.ThermalConductivity =>
                    context.CreateInstanceOfThermalConductivitySignalType(parent, name),
                PadimSignalKind.Toc => context.CreateInstanceOfTocSignalType(parent, name),
                PadimSignalKind.TunableDiodeLaser => context.CreateInstanceOfTunableDiodeLaserSignalType(parent, name),
                PadimSignalKind.ZirconiumDioxide => context.CreateInstanceOfZirconiumDioxideSignalType(parent, name),
                PadimSignalKind.AmperometricGasDetector =>
                    context.CreateInstanceOfAmperometricGasDetectorSignalType(parent, name),
                PadimSignalKind.GasChromatograph => context.CreateInstanceOfGasChromatographSignalType(parent, name),
                PadimSignalKind.FtnirOrFtir => context.CreateInstanceOfFtnirOrFtirSignalType(parent, name),
                PadimSignalKind.DiodeArray => context.CreateInstanceOfDiodeArraySignalType(parent, name),
                PadimSignalKind.Raman => context.CreateInstanceOfRamanSignalType(parent, name),
                PadimSignalKind.Infrared => context.CreateInstanceOfInfraredSignalType(parent, name),
                PadimSignalKind.CatalyticBead => context.CreateInstanceOfCatalyticBeadSignalType(parent, name),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
    }
}
