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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PADIM.Server;

namespace Opc.Ua.PADIM.Tests
{
    public sealed partial class PadimRuntimeTests
    {
        /// <summary>
        /// Optional calibration and condition units require their bound members, not just generated declarations.
        /// </summary>
        [Test]
        public async Task OptionalCalibrationAndConditionUnitsFollowTheirBoundFields()
        {
            PadimDevice device = await CreateDeviceAsync("OptionalCapabilityUnits");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            var signal = (PhSignalState)await device.AddSignalAsync(Name("Ph"), PadimSignalKind.Ph, "AT-OPTIONAL");
            device.BindValue(signal.AnalogSignal!, "ph", provider.Object);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM ICalibration CalibrationTimestamp")));
            await device.AddCalibrationAsync(signal, Name("Calibration"), group =>
            {
                group.Bind(ModelName("CalibrationTimestamp"), "calibratedAt", provider.Object);
                group.Group(ModelName("CalibrationPointSet"), points =>
                    points.Group(ModelName("<CalibrationPointIdentifier>"), point =>
                        point.Bind(ModelName("CalibrationActualValue"), "actual", provider.Object), Name("Point")));
            });
            await device.ConfigureDeviceConditionsAsync(group =>
                group.Group(ModelName("GeneralDeviceConditions"), conditions =>
                    conditions.Bind(ModelName("InternalTemperature"), "internalTemperature", provider.Object)));

            QualifiedName[] units = device.ConformanceUnits.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(units, Does.Contain(new QualifiedName("PA-DIM ICalibration")));
                Assert.That(units, Does.Contain(new QualifiedName("PA-DIM ICalibration CalibrationTimestamp")));
                Assert.That(units, Does.Contain(new QualifiedName("PA-DIM ICalibration CalibrationPointSet")));
                Assert.That(units,
                    Does.Contain(new QualifiedName("PA-DIM GeneralDeviceConditionSetType InternalTemperature")));
                Assert.That(units, Does.Not.Contain(new QualifiedName("PA-DIM ICalibration TypeOfCalibration")));
                Assert.That(units,
                    Does.Not.Contain(new QualifiedName("PA-DIM GeneralDeviceConditionSetType ResidualLife")));
                Assert.That(units,
                    Does.Not.Contain(new QualifiedName("PA-DIM IGeneralDeviceConditionSet DeviceComponentConditions")));
            });
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Loading every type and materializing unbound members does not advertise an implemented family.
        /// </summary>
        [Test]
        public async Task ModelsAndUnboundDeclarationsDoNotAdvertiseRuntimeFacets()
        {
            PadimDevice device = await CreateDeviceAsync("UnboundCapabilities");
            SignalState signal = await device.AddSignalAsync(Name("Ph"), PadimSignalKind.Ph, "AT-CU");
            Assert.Multiple(() =>
            {
                Assert.That(device.ServerProfiles.Count, Is.Zero);
                Assert.That(device.ConformanceUnits.Count, Is.Zero);
                Assert.That(device.State.DeviceHealth!.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            });
            device.BindValue(signal.SignalTag!, "tag", new Mock<IPadimValueProvider>().Object);
            Assert.That(device.ServerProfiles.Count, Is.Zero,
                "A signal identifier binding cannot substitute for acquiring its process variable.");
        }

        /// <summary>
        /// A live contribution is removed when its provider owner is disposed.
        /// </summary>
        [Test]
        public async Task DisposedRuntimeWithdrawsItsConformanceContribution()
        {
            PadimDevice device = await CreateDeviceAsync("DisposedCapabilities");
            var signal = (PhSignalState)await device.AddSignalAsync(Name("Ph"), PadimSignalKind.Ph, "AT-CU2");
            device.BindValue(signal.AnalogSignal!, "ph", new Mock<IPadimValueProvider>().Object);
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/pHSignal"));

            await device.DisposeAsync();

            Assert.Multiple(() =>
            {
                Assert.That(device.ServerProfiles.Count, Is.Zero);
                Assert.That(device.ConformanceUnits.Count, Is.Zero);
            });
        }

        /// <summary>
        /// Modular subdevices keep independent provider serialization and cannot be rebound through their parent.
        /// </summary>
        [Test]
        public async Task ParentCannotBindAnotherDevicesModularSignal()
        {
            PadimDevice parent = await CreateDeviceAsync("ModularParent");
            PadimDevice child = await parent.AddSubDeviceAsync(Name("Module"));
            var signal = (AnalogSignalObjectState)await child.AddSignalAsync(
                Name("Value"), PadimSignalKind.Analog, "TT-MODULE");
            var provider = new Mock<IPadimValueProvider>();
            Assert.Throws<ArgumentException>(() => parent.BindValue(signal.AnalogSignal!, "value", provider.Object));
            PadimValueBinding binding = child.BindValue(signal.AnalogSignal!, "value", provider.Object);
            Assert.That(binding.Variable, Is.SameAs(signal.AnalogSignal));
        }

        /// <summary>
        /// Exercises all published facet selections using the catalogue's exact URI spellings.
        /// </summary>
        [Test]
        public async Task ConfiguredFamiliesCoverAllFiftySixReleasedFacetUris()
        {
            var provider = new Mock<IPadimValueProvider>();
            var writer = new Mock<IPadimWriteProvider>();
            var authorizer = new Mock<IPadimAuthorizer>();
            var operations = new Mock<IPadimOperationProvider>();
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (PadimDeviceKind kind in Enum.GetValues(typeof(PadimDeviceKind)))
            {
                var builder = await m_manager.CreatePadimDeviceAsync(Name($"FacetDevice{kind}"), kind);
                PadimDevice device = builder.BindPadim();
                SetIdentity(device);
                device.BindValue(device.State.DeviceHealth!, "health", provider.Object);
                Capture(device);
            }
            PadimDevice root = await CreateDeviceAsync("FacetRoot");
            SetIdentity(root);
            root.WithHealthAlarms("health", provider.Object);
            root.WithAdministration(provider.Object, writer.Object, authorizer.Object);
            root.WithFactoryReset(operations.Object, authorizer.Object);
            foreach (PadimSignalKind kind in Enum.GetValues(typeof(PadimSignalKind)))
            {
                SignalState signal = await root.AddSignalAsync(Name($"FacetSignal{kind}"), kind, $"TAG-{kind}");
                string member = kind switch
                {
                    PadimSignalKind.Control or PadimSignalKind.TwoStateControl or PadimSignalKind.MultiStateControl =>
                        "ControlSignal",
                    PadimSignalKind.Discrete => "DiscreteSignal",
                    PadimSignalKind.TwoStateDiscrete => "TwoStateDiscreteSignal",
                    PadimSignalKind.MultiStateDiscrete => "MultiStateDiscreteSignal",
                    _ => "AnalogSignal"
                };
                var variable = (BaseVariableState)signal.FindChild(root.Context, ModelName(member))!;
                PadimValueBinding binding = root.BindValue(variable, $"signal/{kind}", provider.Object);
                if (kind == PadimSignalKind.Analog)
                {
                    binding.WithSimulation(Variant.From(5f), writer.Object, authorizer.Object);
                    await root.AddCalibrationAsync((AnalogSignalObjectState)signal, Name("Calibration"), group =>
                        group.Bind(ModelName("CalibrationTimestamp"), "calibratedAt", provider.Object));
                }
            }
            foreach (PadimMeasurementKind kind in Enum.GetValues(typeof(PadimMeasurementKind)))
            {
                AnalogSignalObjectState signal = await root.AddMeasurementAsync(
                    Name($"FacetMeasurement{kind}"), kind, $"PV-{kind}");
                root.BindValue(signal.AnalogSignal!, $"measurement/{kind}", provider.Object);
            }
            await root.RegisterAsync();
            Capture(root);
            string[] expected =
            [
                "Core", "BaseDeviceCondition", "BaseSignalCalibration", "Admin", "Alarming", "Simulation",
                "AmperometricAnalyser", "ConductivityMeter", "FlameIonisationDetector", "NDIRAnalyser",
                "FluorescenseSensor", "ParamagneticAnalyser", "pHMeter", "ConductivityAnalyser", "TOCAnalyser",
                "TDLSpectrometer", "ZrO2Analyser", "GasChromatograph", "InfraredSensor", "AmperometricGasDetector",
                "CatalyticBeadSensor", "DiodeArraySpectrometer", "RamanSpectrometer", "FtnirOrFtirSpectrometer",
                "ControlSignal", "DiscreteSignal", "TwoStateDiscreteSignal", "MultiStateDiscreteSignal",
                "TwoStateControlSignal", "MultiStateControlSignal", "AmperometricSignal", "ConductivitySignal",
                "FlameIonisationSIgnal", "NDIRSignal", "FluorescenseSignal", "ParamagneticSignal", "pHSignal",
                "ThermalConductivitySignal", "TOCSignal", "TDLSignal", "ZrO2Signal", "AmperometricGasDetectorSignal",
                "GasChromatographSignal", "FtnirOrFtirSignal", "DiodeArraySignal", "RamanSignal", "InfraredSignal",
                "InfraredSensorSignal", "CatalyticBeadSignal", "TemperatureSignal", "PressureSignal", "LevelSignal",
                "DensitySignal", "TotalizerSignal", "VolumeFlowSignal", "MassFlowSignal"
            ];
            for (int ii = 0; ii < expected.Length; ii++)
            {
                expected[ii] = $"http://opcfoundation.org/UA-Profile/PADIM/Server/{expected[ii]}";
            }
            Assert.Multiple(() =>
            {
                Assert.That(expected, Has.Length.EqualTo(56));
                Assert.That(profiles, Is.EquivalentTo(expected));
                Assert.That(m_manager.ServerProfiles.ToArray(), Is.SupersetOf(expected));
                Assert.That(root.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("PA-DIM PhSignalType")));
                Assert.That(root.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("PA-DIM MultiStateDiscreteVariable")));
                Assert.That(root.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("PA-DIM AnalogSignalVariable Simulation")));
            });

            void Capture(PadimDevice device)
            {
                ArrayOf<string> enabled = device.ServerProfiles;
                for (int ii = 0; ii < enabled.Count; ii++)
                {
                    profiles.Add(enabled[ii]);
                }
            }
        }

        /// <summary>
        /// Supplies real identity values before the fixture asks the runtime to advertise its core.
        /// </summary>
        private static void SetIdentity(PadimDevice device)
        {
            device.State.Manufacturer!.Value = LocalizedText.From("PA-DIM fixture manufacturer");
            device.State.Model!.Value = LocalizedText.From("PA-DIM fixture");
            device.State.SerialNumber!.Value = device.State.NodeId.ToString();
        }
    }
}
