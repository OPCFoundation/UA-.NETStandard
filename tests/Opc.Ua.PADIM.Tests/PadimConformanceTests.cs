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
                    {
                        point.Bind(ModelName("CalibrationSetpoint"), "setpoint", provider.Object);
                        point.Bind(ModelName("CalibrationActualValue"), "actual", provider.Object);
                    }, Name("Point")));
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
            var signal = (TwoStateDiscreteSignalObjectState)await device.AddSignalAsync(
                Name("Discrete"), PadimSignalKind.TwoStateDiscrete, "AT-CU2");
            device.BindValue(signal.TwoStateDiscreteSignal!, "discrete", new Mock<IPadimValueProvider>().Object);
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TwoStateDiscreteSignal"));

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
            var samples = new PadimPublishedSampleSet();
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (PadimDeviceKind kind in GetEnumValues<PadimDeviceKind>())
            {
                var builder = await m_manager.CreatePadimDeviceAsync(Name($"FacetDevice{kind}"), kind);
                PadimDevice device = builder.BindPadim();
                SetIdentity(device);
                device.BindValue(device.State.DeviceHealth!, "health", provider.Object);
                await device.ConfigureDeviceConditionsAsync(group =>
                    SelectPublishedMembers(device, group,
                        GroupDeclarations(device.State.TypeDefinitionId, "DeviceConditionSet"), samples));
                Capture(device);
            }
            PadimDevice root = await CreateDeviceAsync("FacetRoot");
            SetIdentity(root);
            root.WithHealthAlarms("health", provider.Object);
            root.WithAdministration(provider.Object, writer.Object, authorizer.Object);
            root.WithFactoryReset(operations.Object, authorizer.Object);
            foreach (PadimSignalKind kind in GetEnumValues<PadimSignalKind>())
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
                BindRequiredValueMembers(root, variable, provider.Object);
                if (GroupDeclarations(signal.TypeDefinitionId, "SignalConditionSet") is { Count: > 0 } conditions)
                {
                    await root.ConfigureSignalConditionsAsync(signal, group =>
                        SelectPublishedMembers(root, group, conditions, samples));
                }
                if (variable is PatMeasurementVariableState pat)
                {
                    pat.AddPatMeasurandDescription(root.Context);
                    root.BindValue(pat.PatMeasurandDescription!, $"measurand/{kind}", provider.Object);
                }
                if (signal is AnalogSignalObjectState analog)
                {
                    await root.AddCalibrationAsync(analog, Name("Calibration"), group =>
                        SelectPublishedMembers(root, group,
                            GroupDeclarations(signal.TypeDefinitionId, "<SignalCalibrationIdentifier>"), samples));
                }
                if (kind == PadimSignalKind.Analog)
                {
                    binding.WithSimulation(Variant.From(5f), writer.Object, authorizer.Object);
                }
            }
            foreach (PadimMeasurementKind kind in GetEnumValues<PadimMeasurementKind>())
            {
                AnalogSignalObjectState signal = await root.AddMeasurementAsync(
                    Name($"FacetMeasurement{kind}"), kind, $"PV-{kind}");
                root.BindValue(signal.AnalogSignal!, $"measurement/{kind}", provider.Object);
                BindRequiredValueMembers(root, signal.AnalogSignal!, provider.Object);
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
        /// Core support requires the complete mandatory PA-DIM base and a host profile that includes Nano Embedded.
        /// </summary>
        [Test]
        public async Task CoreFacetRequiresCompleteBaseAndNanoEmbeddedHostProfile()
        {
            PadimDevice device = await CreateDeviceAsync("CoreRequirements");
            SetIdentity(device);
            device.BindValue(
                device.State.DeviceHealth!,
                "health",
                new Mock<IPadimValueProvider>().Object);

            Assert.That(device.ServerProfiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/Core"));
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("PA-DIM Nano Embedded")));

            BaseVariableState profileArray =
                m_manager.Server.DiagnosticsNodeManager.FindPredefinedNode<BaseVariableState>(
                    Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray);
            Assert.That(profileArray.Value.TryGetValue(out ArrayOf<string> originalProfiles), Is.True);
            try
            {
                profileArray.Value = ArrayOf<string>.Empty;
                Assert.Multiple(() =>
                {
                    Assert.That(device.ServerProfiles.ToArray(),
                        Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/Core"));
                    Assert.That(device.ConformanceUnits.ToArray(),
                        Does.Not.Contain(new QualifiedName("PA-DIM Nano Embedded")));
                    Assert.That(device.ConformanceUnits.ToArray(),
                        Does.Contain(new QualifiedName("PA-DIM Base")));
                });
            }
            finally
            {
                profileArray.Value = originalProfiles;
            }

            device.State.ManufacturerUri!.Value = string.Empty;
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Base")));
        }

        /// <summary>
        /// Remapped DI names and dynamically supplied identity members retain the base contribution.
        /// </summary>
        [Test]
        public async Task IdentityUsesRemappedDiQualifiedNamesAndDynamicMembers()
        {
            PadimDevice device = await CreateDeviceAsync("IdentityNamespaceRemapping");
            SetIdentity(device);
            device.BindValue(device.State.DeviceHealth!, "health", new Mock<IPadimValueProvider>().Object);
            int diNamespace = device.Context.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi);
            Assert.That(diNamespace, Is.GreaterThan(1),
                "The runtime DI namespace must differ from the NodeSet's source index 1.");
            var children = new List<BaseInstanceState>();
            device.State.GetChildren(device.Context, children);
            foreach (string field in s_requiredIdentityFields)
            {
                Assert.That(children.Single(child => child.BrowseName.Name == field).BrowseName,
                    Is.EqualTo(new QualifiedName(field, (ushort)diNamespace)), field);
            }
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("PA-DIM Base")));

            device.State.Manufacturer!.BrowseName =
                new QualifiedName("Manufacturer", device.Manager.InstanceNamespaceIndex);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Base")));
            var dynamicManufacturer = new BaseDataVariableState(device.State)
            {
                NodeId = new NodeId("DynamicDiManufacturer", device.Manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Manufacturer", (ushort)diNamespace),
                WrappedValue = Variant.From(LocalizedText.From("Dynamic DI manufacturer"))
            };
            device.State.AddChild(dynamicManufacturer);
            Assert.That(device.Manager.FindPredefinedNode(dynamicManufacturer.NodeId), Is.Null);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Base")));
            await device.RegisterAsync();
            Assert.That(device.Manager.FindPredefinedNode(dynamicManufacturer.NodeId), Is.SameAs(dynamicManufacturer));
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("PA-DIM Base")),
                "A correct DI-qualified dynamic child must not be hidden by a generated same-name slot.");
        }

        /// <summary>
        /// Same-name identity values outside DI cannot satisfy the mandatory base requirements.
        /// </summary>
        [TestCaseSource(nameof(s_requiredIdentityFields))]
        public async Task IdentityRejectsSameNamedNonDiMembers(string field)
        {
            PadimDevice device = await CreateDeviceAsync($"WrongIdentityNamespace{field}");
            SetIdentity(device);
            device.BindValue(device.State.DeviceHealth!, "health", new Mock<IPadimValueProvider>().Object);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("PA-DIM Base")));
            var children = new List<BaseInstanceState>();
            device.State.GetChildren(device.Context, children);
            BaseInstanceState identity = children.Single(child => child.BrowseName.Name == field);
            foreach (ushort namespaceIndex in new[]
            {
                (ushort)device.Context.NamespaceUris.GetIndex(Namespaces.PADIM),
                device.Manager.InstanceNamespaceIndex
            })
            {
                identity.BrowseName = new QualifiedName(field, namespaceIndex);
                Assert.Multiple(() =>
                {
                    Assert.That(device.ConformanceUnits.ToArray(),
                        Does.Not.Contain(new QualifiedName("PA-DIM Base")), field);
                    Assert.That(device.ServerProfiles.ToArray(),
                        Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/Core"), field);
                });
            }
        }

        /// <summary>
        /// Health requires a bound DI-qualified child, including a dynamically supplied child.
        /// </summary>
        [Test]
        public async Task CoreFacetRequiresDiQualifiedHealthBinding()
        {
            PadimDevice device = await CreateDeviceAsync("HealthNamespaceRequirements");
            SetIdentity(device);
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            device.BindValue(device.State.DeviceHealth!, "health", provider.Object);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("PA-DIM Base")));
            device.State.DeviceHealth!.BrowseName = ModelName("DeviceHealth");
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Base")));
            device.State.DeviceHealth.BrowseName =
                new QualifiedName("DeviceHealth", device.Manager.InstanceNamespaceIndex);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Base")));
            var dynamicHealth = new BaseDataVariableState(device.State)
            {
                NodeId = new NodeId("DynamicDiHealth", device.Manager.InstanceNamespaceIndex),
                BrowseName = new QualifiedName(
                    "DeviceHealth", (ushort)device.Context.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi)),
                DataType = device.State.DeviceHealth.DataType,
                TypeDefinitionId = device.State.DeviceHealth.TypeDefinitionId
            };
            device.State.AddChild(dynamicHealth);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Base")),
                "A correctly named but unbound health child is insufficient.");
            device.BindValue(dynamicHealth, "dynamicHealth", provider.Object);
            Assert.That(device.Manager.FindPredefinedNode(dynamicHealth.NodeId), Is.Null);
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Base")),
                "A bound health child must be registered before it contributes.");
            await device.RegisterAsync();
            Assert.That(device.Manager.FindPredefinedNode(dynamicHealth.NodeId), Is.SameAs(dynamicHealth));
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("PA-DIM Base")));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Only declared 2017/2022 Nano profiles and their mandatory including profiles satisfy the PA-DIM unit.
        /// </summary>
        [TestCase("NanoEmbeddedDevice2017", true)]
        [TestCase("MicroEmbeddedDevice2017", true)]
        [TestCase("EmbeddedUA2017", true)]
        [TestCase("StandardUA2017", true)]
        [TestCase("NanoEmbeddedDevice2022", true)]
        [TestCase("MicroEmbeddedDevice2022", true)]
        [TestCase("EmbeddedUA2022", true)]
        [TestCase("StandardUA2022", true)]
        [TestCase("NanoEmbeddedDevice2025", false)]
        [TestCase("MicroEmbeddedDevice2025", false)]
        [TestCase("EmbeddedUA2025", false)]
        [TestCase("StandardUA2025", false)]
        public async Task CoreFacetFollowsPublishedNanoProfileVersionRequirement(string profile, bool supported)
        {
            PadimDevice device = await CreateDeviceAsync($"NanoProfile{profile}");
            SetIdentity(device);
            device.BindValue(device.State.DeviceHealth!, "health", new Mock<IPadimValueProvider>().Object);
            BaseVariableState profileArray =
                m_manager.Server.DiagnosticsNodeManager.FindPredefinedNode<BaseVariableState>(
                    Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray);
            Assert.That(profileArray.Value.TryGetValue(out ArrayOf<string> originalProfiles), Is.True);
            try
            {
                profileArray.Value = (ArrayOf<string>)[$"http://opcfoundation.org/UA-Profile/Server/{profile}"];
                Assert.Multiple(() =>
                {
                    Assert.That(device.ConformanceUnits.ToArray(),
                        Does.Contain(new QualifiedName("PA-DIM Base")));
                    Assert.That(device.ConformanceUnits.ToList().Contains(new QualifiedName("PA-DIM Nano Embedded")),
                        Is.EqualTo(supported), profile);
                    Assert.That(device.ServerProfiles.ToList().Contains(
                        "http://opcfoundation.org/UA-Profile/PADIM/Server/Core"),
                        Is.EqualTo(supported), profile);
                });
            }
            finally
            {
                profileArray.Value = originalProfiles;
            }
        }

        /// <summary>
        /// A primary pH reading cannot substitute for its required conditions, calibration and measurand.
        /// </summary>
        [Test]
        public async Task PhFacetRequiresCompleteCalibrationOnOneInstance([Values] bool bindMeasurandFirst)
        {
            PadimDevice device = await CreateDeviceAsync($"PhRequirements{bindMeasurandFirst}");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            var signal = (PhSignalState)await device.AddSignalAsync(Name("Ph"), PadimSignalKind.Ph, "AT-REQUIRED");
            device.BindValue(signal.AnalogSignal!, "ph", provider.Object);
            var pat = (PatMeasurementVariableState)signal.AnalogSignal!;
            pat.AddPatMeasurandDescription(device.Context);
            if (bindMeasurandFirst)
            {
                device.BindValue(pat.PatMeasurandDescription!, "measurand", provider.Object);
            }
            AssertAbsent();
            await device.ConfigureSignalConditionsAsync(signal, group =>
            {
                group.Bind(ModelName("PhMeasuringMethod"), "method", provider.Object);
                group.Bind(ModelName("SensingElementTemperature"), "temperature", provider.Object);
            });
            AssertAbsent();
            await device.AddCalibrationAsync(signal, Name("SlopeOnly"), group =>
                group.Bind(ModelName("SensorSlope"), "slope", provider.Object));
            await device.AddCalibrationAsync(signal, Name("PotentialOnly"), group =>
                group.Bind(ModelName("SensorAsymmetryPotential"), "potential", provider.Object));
            AssertAbsent();
            await device.AddCalibrationAsync(signal, Name("Complete"), group =>
            {
                group.Bind(ModelName("SensorSlope"), "completeSlope", provider.Object);
                group.Bind(ModelName("SensorAsymmetryPotential"), "completePotential", provider.Object);
            });
            if (!bindMeasurandFirst)
            {
                AssertAbsent();
                device.BindValue(pat.PatMeasurandDescription!, "measurand", provider.Object);
            }
            Assert.Multiple(() =>
            {
                Assert.That(device.ServerProfiles.ToArray(),
                    Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/pHSignal"));
                Assert.That(device.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("PA-DIM PhSignalType")));
                Assert.That(device.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("PA-DIM PatMeasurementVariableType")));
            });
            provider.VerifyNoOtherCalls();

            void AssertAbsent()
            {
                Assert.That(device.ServerProfiles.ToArray(),
                    Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/pHSignal"));
                Assert.That(device.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("PA-DIM PhSignalType")));
            }
        }

        /// <summary>
        /// The TOC device facet needs all four provider-backed mandatory condition values.
        /// </summary>
        [Test]
        public async Task TocAnalyserFacetRequiresAllPublishedConditions([Values] bool complete)
        {
            var builder = await m_manager.CreatePadimDeviceAsync(
                Name($"TocRequirements{complete}"), PadimDeviceKind.TocAnalyser);
            PadimDevice device = builder.BindPadim();
            SetIdentity(device);
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            device.BindValue(device.State.DeviceHealth!, "health", provider.Object);
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TOCAnalyser"));
            await device.ConfigureDeviceConditionsAsync(group =>
            {
                group.Bind(ModelName("ReactorTemperature"), "reactor", provider.Object);
                group.Bind(ModelName("CoolerTemperature"), "cooler", provider.Object);
                group.Bind(ModelName("CarrierGasVolumeFlow"), "carrier", provider.Object);
                if (complete)
                {
                    group.Bind(ModelName("ReferenceInjectionVolume"), "reference", provider.Object);
                }
            });
            Assert.That(device.ServerProfiles.ToArray().Contains(
                "http://opcfoundation.org/UA-Profile/PADIM/Server/TOCAnalyser"), Is.EqualTo(complete));
            Assert.That(device.ConformanceUnits.ToArray().Contains(
                new QualifiedName("PA-DIM TocAnalyserType")), Is.EqualTo(complete));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Neither of the two mandatory pH condition fields can be replaced by the other.
        /// </summary>
        [TestCase("PhMeasuringMethod")]
        [TestCase("SensingElementTemperature")]
        [TestCase("")]
        public async Task PhFacetRequiresEachPublishedConditionField(string omitted)
        {
            PadimDevice device = await CreateDeviceAsync($"PhConditionRequirements{omitted}");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            var signal = (PhSignalState)await device.AddSignalAsync(Name("Ph"), PadimSignalKind.Ph, "AT-CONDITIONS");
            device.BindValue(signal.AnalogSignal!, "ph", provider.Object);
            var pat = (PatMeasurementVariableState)signal.AnalogSignal!;
            pat.AddPatMeasurandDescription(device.Context);
            device.BindValue(pat.PatMeasurandDescription!, "measurand", provider.Object);
            await device.AddCalibrationAsync(signal, Name("Calibration"), group =>
            {
                group.Bind(ModelName("SensorSlope"), "slope", provider.Object);
                group.Bind(ModelName("SensorAsymmetryPotential"), "potential", provider.Object);
            });
            await device.ConfigureSignalConditionsAsync(signal, group =>
            {
                foreach (string field in s_requiredPhConditionFields)
                {
                    if (field != omitted)
                    {
                        group.Bind(ModelName(field), field, provider.Object);
                    }
                }
            });
            Assert.That(device.ServerProfiles.ToArray().Contains(
                "http://opcfoundation.org/UA-Profile/PADIM/Server/pHSignal"), Is.EqualTo(omitted.Length == 0));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A calibration point unit needs both values on one point, not just a populated point-set folder.
        /// </summary>
        [Test]
        public async Task CalibrationPointUnitDoesNotCombineDifferentPoints()
        {
            PadimDevice device = await CreateDeviceAsync("PointRequirements");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            var signal = (AnalogSignalObjectState)await device.AddSignalAsync(
                Name("Analog"), PadimSignalKind.Analog, "AT-POINTS");
            await device.AddCalibrationAsync(signal, Name("SplitPoints"), group =>
                group.Group(ModelName("CalibrationPointSet"), points =>
                {
                    points.Group(ModelName("<CalibrationPointIdentifier>"), point =>
                        point.Bind(ModelName("CalibrationSetpoint"), "setpoint", provider.Object), Name("Setpoint"));
                    points.Group(ModelName("<CalibrationPointIdentifier>"), point =>
                        point.Bind(ModelName("CalibrationActualValue"), "actual", provider.Object), Name("Actual"));
                }));
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM ICalibration CalibrationPointSet")));
            await device.AddCalibrationAsync(signal, Name("CompletePoint"), group =>
                group.Group(ModelName("CalibrationPointSet"), points =>
                    points.Group(ModelName("<CalibrationPointIdentifier>"), point =>
                    {
                        point.Bind(ModelName("CalibrationSetpoint"), "both/setpoint", provider.Object);
                        point.Bind(ModelName("CalibrationActualValue"), "both/actual", provider.Object);
                    }, Name("Complete"))));
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("PA-DIM ICalibration CalibrationPointSet")));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Multi-field optional units cannot combine values belonging to different signal instances.
        /// </summary>
        [Test]
        public async Task RetentionTimeUnitRequiresBothFieldsOnTheSameSignal()
        {
            PadimDevice device = await CreateDeviceAsync("RetentionRequirements");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            foreach (string field in new[] { "ExpectedRetentionTime", "ActualRetentionTime", "Both" })
            {
                var signal = (GasChromatographSignalState)await device.AddSignalAsync(
                    Name(field), PadimSignalKind.GasChromatograph, field);
                device.BindValue(signal.AnalogSignal!, field, provider.Object);
                await device.ConfigureSignalConditionsAsync(signal, group =>
                {
                    if (field is "ExpectedRetentionTime" or "Both")
                    {
                        group.Bind(ModelName("ExpectedRetentionTime"), $"{field}/expected", provider.Object);
                    }
                    if (field is "ActualRetentionTime" or "Both")
                    {
                        group.Bind(ModelName("ActualRetentionTime"), $"{field}/actual", provider.Object);
                    }
                });
                Assert.That(device.ConformanceUnits.ToArray().Contains(
                    new QualifiedName("PA-DIM GasChromatographSignalType RetentionTime")),
                    Is.EqualTo(field == "Both"));
            }
            provider.VerifyNoOtherCalls();
        }
        /// <summary>
        /// A primary reading does not supply the required configuration members of its published VariableType.
        /// </summary>
        [TestCase(PadimMeasurementKind.Temperature, "TemperatureSignal", "SensorType")]
        [TestCase(PadimMeasurementKind.Flow, "VolumeFlowSignal", "LowFlowCutOff")]
        [TestCase(PadimMeasurementKind.MassFlow, "MassFlowSignal", "LowFlowCutOff")]
        [TestCase(PadimMeasurementKind.ActualVolumeFlow, "VolumeFlowSignal", "LowFlowCutOff")]
        [TestCase(PadimMeasurementKind.NormalizedVolumeFlow, "VolumeFlowSignal", "LowFlowCutOff")]
        [TestCase(PadimMeasurementKind.Totalizer, "TotalizerSignal", "PulseValue")]
        [TestCase(PadimMeasurementKind.Totalizer, "TotalizerSignal", "PulseWidth")]
        public async Task MeasurementFacetRequiresEachPublishedParameter(
            PadimMeasurementKind kind, string facet, string omitted)
        {
            PadimDevice device = await CreateDeviceAsync($"MeasurementRequirements{kind}{omitted}");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            AnalogSignalObjectState signal = await device.AddMeasurementAsync(Name("Value"), kind, "PV-PARAMETER");
            device.BindValue(signal.AnalogSignal!, "value", provider.Object);
            BindRequiredValueMembers(device, signal.AnalogSignal!, provider.Object, omitted);
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Not.Contain($"http://opcfoundation.org/UA-Profile/PADIM/Server/{facet}"));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A same-type sibling cannot replace the bound, PA-DIM-qualified AnalogSignal child.
        /// </summary>
        [Test]
        public async Task MeasurementFacetRequiresQualifiedPrimaryBindingIncludingDynamicMembers()
        {
            PadimDevice device = await CreateDeviceAsync("PrimaryMeasurementRequirements");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            AnalogSignalObjectState signal = await device.AddMeasurementAsync(
                Name("Value"), PadimMeasurementKind.Temperature, "PV-PRIMARY");
            TemperatureMeasurementVariableState sibling =
                device.Context.CreateInstanceOfTemperatureMeasurementVariableType(signal, ModelName("Unrelated"));
            signal.AddChild(sibling);
            device.BindValue(sibling, "sibling", provider.Object);
            BindRequiredValueMembers(device, sibling, provider.Object);
            await device.RegisterAsync();
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"),
                "A bound same-type sibling must not substitute for the unbound primary measurement.");
            sibling.BrowseName = new QualifiedName("AnalogSignal", device.Manager.InstanceNamespaceIndex);
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"),
                "The primary name in a vendor namespace must not satisfy the PA-DIM path.");
            Assert.That(device.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName("PA-DIM Analog Signal")));
            signal.AnalogSignal!.BrowseName = ModelName("UnboundOriginal");
            sibling.BrowseName = ModelName("AnalogSignal");
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"),
                "A bound dynamic child at the exact primary path is valid despite the old generated slot.");
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Indexing a primary and its parameters cannot substitute for registering its signal ownership path.
        /// </summary>
        [Test]
        public async Task RegisteredPrimaryCannotBypassUnregisteredSignalAncestor()
        {
            PadimDevice device = await CreateDeviceAsync("UnregisteredSignalRequirements");
            device.State.AddSignalSet_no_(device.Context);
            AnalogSignalObjectState signal = device.Context.CreateInstanceOfAnalogSignalType(
                device.State.SignalSet_no_!, Name("LateSignal"));
            signal.NodeId = new NodeId("LateSignalAncestor", device.Manager.InstanceNamespaceIndex);
            device.State.SignalSet_no_!.AddChild(signal);
            TemperatureMeasurementVariableState primary =
                device.Context.CreateInstanceOfTemperatureMeasurementVariableType(signal, ModelName("AnalogSignal"));
            signal.CreateOrReplaceAnalogSignal(device.Context, primary);
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            device.BindValue(primary, "primary", provider.Object);
            BindRequiredValueMembers(device, primary, provider.Object);
            await device.Manager.AddPredefinedNodeAsync(primary);
            Assert.Multiple(() =>
            {
                Assert.That(device.Manager.FindPredefinedNode(primary.NodeId), Is.SameAs(primary));
                Assert.That(device.Manager.FindPredefinedNode(signal.NodeId), Is.Null);
                Assert.That(device.ServerProfiles.ToArray(),
                    Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"));
                Assert.That(device.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("PA-DIM Analog Signal")));
            });
            await device.RegisterAsync();
            Assert.That(device.Manager.FindPredefinedNode(signal.NodeId), Is.SameAs(signal));
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A registered primary requires its mandatory provider-bound parameter to be registered as well.
        /// </summary>
        [Test]
        public async Task MeasurementFacetWaitsForRegisteredRequiredParameter()
        {
            PadimDevice device = await CreateDeviceAsync("UnregisteredParameterRequirements");
            AnalogSignalObjectState signal = await device.AddMeasurementAsync(
                Name("Value"), PadimMeasurementKind.Temperature, "TT-PARAMETER");
            var primary = (TemperatureMeasurementVariableState)signal.AnalogSignal!;
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            device.BindValue(primary, "primary", provider.Object);
            primary.SensorType!.BrowseName = Name("OriginalSensorType");
            var parameter = new BaseDataVariableState(primary)
            {
                NodeId = new NodeId("LateSensorType", device.Manager.InstanceNamespaceIndex),
                BrowseName = ModelName("SensorType"),
                DataType = primary.SensorType.DataType,
                TypeDefinitionId = primary.SensorType.TypeDefinitionId
            };
            primary.AddChild(parameter);
            device.BindValue(parameter, "sensorType", provider.Object);
            Assert.That(device.Manager.FindPredefinedNode(parameter.NodeId), Is.Null);
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Not.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"));
            await device.RegisterAsync();
            Assert.That(device.Manager.FindPredefinedNode(parameter.NodeId), Is.SameAs(parameter));
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/PADIM/Server/TemperatureSignal"));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Control facets require their setpoint and any mandatory actuator or operating-direction parameters.
        /// </summary>
        [TestCase(PadimSignalKind.Control, "ControlSignal", "Setpoint")]
        [TestCase(PadimSignalKind.Control, "ControlSignal", "ActuatorType")]
        [TestCase(PadimSignalKind.Control, "ControlSignal", "OperatingDirection")]
        [TestCase(PadimSignalKind.TwoStateControl, "TwoStateControlSignal", "Setpoint")]
        [TestCase(PadimSignalKind.TwoStateControl, "TwoStateControlSignal", "OperatingDirection")]
        [TestCase(PadimSignalKind.MultiStateControl, "MultiStateControlSignal", "Setpoint")]
        public async Task ControlFacetRequiresEachPublishedParameter(
            PadimSignalKind kind, string facet, string omitted)
        {
            PadimDevice device = await CreateDeviceAsync($"ControlRequirements{kind}{omitted}");
            var provider = new Mock<IPadimValueProvider>(MockBehavior.Strict);
            SignalState signal = await device.AddSignalAsync(Name("Value"), kind, "CV-PARAMETER");
            var variable = (BaseVariableState)signal.FindChild(device.Context, ModelName("ControlSignal"))!;
            device.BindValue(variable, "value", provider.Object);
            BindRequiredValueMembers(device, variable, provider.Object, omitted);
            Assert.That(device.ServerProfiles.ToArray(),
                Does.Not.Contain($"http://opcfoundation.org/UA-Profile/PADIM/Server/{facet}"));
            provider.VerifyNoOtherCalls();
        }
        /// <summary>
        /// Binds the measurement and control parameters that their published VariableTypes require.
        /// </summary>
        private void BindRequiredValueMembers(
            PadimDevice device, BaseVariableState variable, IPadimValueProvider provider, string? omitted = null)
        {
            string[] fields = variable switch
            {
                ControlVariableState => ["Setpoint", "OperatingDirection", "ActuatorType"],
                TwoStateDiscreteControlVariableState => ["Setpoint", "OperatingDirection"],
                MultiStateDiscreteControlVariableState => ["Setpoint"],
                TemperatureMeasurementVariableState => ["SensorType"],
                FlowMeasurementVariableState => ["LowFlowCutOff"],
                TotalizerVariableState => ["PulseValue", "PulseWidth"],
                _ => []
            };
            foreach (string field in fields)
            {
                if (field == omitted)
                {
                    continue;
                }
                var member = (BaseVariableState)variable.FindChild(device.Context, ModelName(field))!;
                device.BindValue(member, $"{variable.NodeId}/{field}", provider);
            }
        }

        /// <summary>
        /// Enumerates every declared family on modern and legacy test targets.
        /// </summary>
        /// <typeparam name="TEnum">The family enumeration.</typeparam>
        internal static TEnum[] GetEnumValues<TEnum>()
            where TEnum : struct, Enum
        {
#if NET6_0_OR_GREATER
            return Enum.GetValues<TEnum>();
#else
            return (TEnum[])Enum.GetValues(typeof(TEnum));
#endif
        }

        /// <summary>
        /// Supplies real identity values before the fixture asks the runtime to advertise its core.
        /// </summary>
        private static void SetIdentity(PadimDevice device)
        {
            device.State.Manufacturer!.Value = LocalizedText.From("PA-DIM fixture manufacturer");
            device.State.ManufacturerUri!.Value = "urn:fixture:manufacturer";
            device.State.Model!.Value = LocalizedText.From("PA-DIM fixture");
            device.State.SerialNumber!.Value = device.State.NodeId.ToString();
            device.State.SoftwareRevision!.Value = "1.0";
            device.State.HardwareRevision!.Value = "1.0";
            device.State.ProductCode!.Value = "fixture";
            device.State.ProductInstanceUri!.Value = $"urn:fixture:{device.State.NodeId}";
            device.State.AddAssetId(device.Context).Value = $"asset:{device.State.NodeId}";
            device.State.RevisionCounter!.Value = 0;
        }

        /// <summary>The mandatory condition fields required by the pH signal facet.</summary>
        private static readonly string[] s_requiredPhConditionFields =
            ["PhMeasuringMethod", "SensingElementTemperature"];

        /// <summary>The mandatory identity fields declared in the DI namespace by PA-DIM.</summary>
        private static readonly string[] s_requiredIdentityFields =
        [
            "Manufacturer", "ManufacturerUri", "Model", "SerialNumber", "SoftwareRevision",
            "HardwareRevision", "ProductCode", "ProductInstanceUri", "AssetId", "RevisionCounter"
        ];
    }
}
