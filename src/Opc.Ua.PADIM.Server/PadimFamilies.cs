/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Standard analog variable specializations for transmitters and process measurements.
    /// </summary>
    public enum PadimMeasurementKind
    {
        /// <summary>An unrestricted numeric measurement.</summary>
        Analog,

        /// <summary>A temperature measurement.</summary>
        Temperature,

        /// <summary>A pressure measurement.</summary>
        Pressure,

        /// <summary>A flow measurement.</summary>
        Flow,

        /// <summary>A level measurement.</summary>
        Level,

        /// <summary>An actual-density measurement.</summary>
        Density,

        /// <summary>A totalizer measurement.</summary>
        Totalizer,

        /// <summary>A mass-flow-rate measurement.</summary>
        MassFlow,

        /// <summary>An actual-volume-flow-rate measurement.</summary>
        ActualVolumeFlow,

        /// <summary>A normalized-volume-flow-rate measurement.</summary>
        NormalizedVolumeFlow,

        /// <summary>An analytical measurement.</summary>
        Analytical,

        /// <summary>A process-analytical-technology measurement.</summary>
        Pat
    }

    /// <summary>
    /// Concrete device families defined by the PA-DIM 1.02 model.
    /// </summary>
    public enum PadimDeviceKind
    {
        /// <summary>A base process-automation device.</summary>
        Device,

        /// <summary>A generic process analyser.</summary>
        ProcessAnalyser,

        /// <summary>An amperometric analyser.</summary>
        AmperometricAnalyser,

        /// <summary>A conductivity meter.</summary>
        ConductivityMeter,

        /// <summary>A flame-ionisation detector.</summary>
        FlameIonisationDetector,

        /// <summary>A non-dispersive infrared gas analyser.</summary>
        NonDispersiveInfraredGasAnalyser,

        /// <summary>An optical fluorescence-quenching sensor.</summary>
        OpticalFluorescenseQuenchingSensor,

        /// <summary>A paramagnetic gas analyser.</summary>
        ParamagneticGasAnalyser,

        /// <summary>A pH meter.</summary>
        PhMeter,

        /// <summary>A thermal-conductivity gas analyser.</summary>
        ThermalConductivityGasAnalyser,

        /// <summary>A total-organic-carbon analyser.</summary>
        TocAnalyser,

        /// <summary>A tunable-diode-laser spectrometer.</summary>
        TunableDiodeLaserSpectrometer,

        /// <summary>A zirconium-dioxide analyser.</summary>
        ZirconiumDioxideAnalyser,

        /// <summary>A gas chromatograph.</summary>
        GasChromatograph,

        /// <summary>An infrared sensor.</summary>
        InfraredSensor,

        /// <summary>An amperometric gas detector.</summary>
        AmperometricGasDetector,

        /// <summary>A catalytic-bead sensor.</summary>
        CatalyticBeadSensor,

        /// <summary>A diode-array spectrometer.</summary>
        DiodeArraySpectrometer,

        /// <summary>A Raman spectrometer.</summary>
        RamanSpectrometer,

        /// <summary>A Fourier-transform infrared or near-infrared spectrometer.</summary>
        FtnirOrFtirSpectrometer
    }

    /// <summary>
    /// Concrete signal object families defined by the PA-DIM 1.02 model.
    /// </summary>
    public enum PadimSignalKind
    {
        /// <summary>An analog signal.</summary>
        Analog,

        /// <summary>A continuous control signal.</summary>
        Control,

        /// <summary>A generic discrete signal.</summary>
        Discrete,

        /// <summary>A Boolean discrete signal.</summary>
        TwoStateDiscrete,

        /// <summary>An enumerated discrete signal.</summary>
        MultiStateDiscrete,

        /// <summary>A Boolean control signal.</summary>
        TwoStateControl,

        /// <summary>An enumerated control signal.</summary>
        MultiStateControl,

        /// <summary>A generic analytical signal.</summary>
        Analytical,

        /// <summary>An amperometric signal.</summary>
        Amperometric,

        /// <summary>A conductivity signal.</summary>
        Conductivity,

        /// <summary>A flame-ionisation signal.</summary>
        FlameIonisation,

        /// <summary>A non-dispersive infrared signal.</summary>
        NonDispersiveInfrared,

        /// <summary>An optical fluorescence-quenching signal.</summary>
        OpticalFluorescenseQuenching,

        /// <summary>A paramagnetic signal.</summary>
        Paramagnetic,

        /// <summary>A pH signal.</summary>
        Ph,

        /// <summary>A thermal-conductivity signal.</summary>
        ThermalConductivity,

        /// <summary>A total-organic-carbon signal.</summary>
        Toc,

        /// <summary>A tunable-diode-laser signal.</summary>
        TunableDiodeLaser,

        /// <summary>A zirconium-dioxide signal.</summary>
        ZirconiumDioxide,

        /// <summary>An amperometric gas-detector signal.</summary>
        AmperometricGasDetector,

        /// <summary>A gas-chromatograph signal.</summary>
        GasChromatograph,

        /// <summary>A Fourier-transform infrared or near-infrared signal.</summary>
        FtnirOrFtir,

        /// <summary>A diode-array signal.</summary>
        DiodeArray,

        /// <summary>A Raman signal.</summary>
        Raman,

        /// <summary>An infrared signal.</summary>
        Infrared,

        /// <summary>A catalytic-bead signal.</summary>
        CatalyticBead
    }
}
