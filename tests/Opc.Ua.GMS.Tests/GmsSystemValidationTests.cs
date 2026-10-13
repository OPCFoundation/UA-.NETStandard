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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Rejects malformed coherent observations through the public provider/handle boundary, not private validators.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsSystemValidationTests
    {
        /// <summary>
        /// Starts one real owner for independent, sequentially disposed validation instances.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new GmsResultServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the real listener and all test-owned PKI resources.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Neither native nor inherited values, identities or calibration history change when any field is rejected.
        /// </summary>
        [TestCase("MissingQualification")]
        [TestCase("MissingClass")]
        [TestCase("SignedClass")]
        [TestCase("ClassRange")]
        [TestCase("ClassNoTimestamp")]
        [TestCase("WrongQualifiedType")]
        [TestCase("ExpiredButQualified")]
        [TestCase("InfiniteQualification")]
        [TestCase("InvalidAlignment")]
        [TestCase("WrongAxes")]
        [TestCase("WrongCapability")]
        [TestCase("CapabilityRange")]
        [TestCase("NegativeRange")]
        [TestCase("NegativeResolution")]
        [TestCase("WrongUnitStructure")]
        [TestCase("NestedSingleSensor")]
        [TestCase("DuplicateSensor")]
        [TestCase("ForeignToolKind")]
        [TestCase("UnsignedLoadingRange")]
        [TestCase("SignedLoading")]
        [TestCase("WrongLoadingPosition")]
        [TestCase("MissingActiveSensor")]
        [TestCase("DuplicateActiveSensor")]
        [TestCase("ForeignActiveNode")]
        [TestCase("AmbiguousActiveTools")]
        [TestCase("NegativeWorkspace")]
        [TestCase("InfiniteWorkspace")]
        [TestCase("AbstractWorkspace")]
        [TestCase("MissingSubclass")]
        [TestCase("OverlongSubclass")]
        [TestCase("MissingCalibrationDate")]
        [TestCase("FutureCalibration")]
        [TestCase("BackdatedCalibration")]
        [TestCase("OldPrediction")]
        [TestCase("ZeroInterval")]
        [TestCase("NegativePreparation")]
        [TestCase("ExcessivePreparation")]
        [TestCase("RelativeCertificate")]
        [TestCase("WrongCertificates")]
        [TestCase("InheritedBadMode")]
        public async Task InvalidNativeObservationPreservesAcceptedRevisionAsync(string mistake)
        {
            IMachineryBuildContext context = GmsSystemTests.Context(m_fixture!);
            GmsSystemSnapshot initial = GmsCalibrationTests.Example();
            var provider = new InMemoryGmsProvider(initial);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "Validation" + mistake, provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Sensors | GmsFeatures.Identification | GmsFeatures.Loading |
                        GmsFeatures.ToolMonitoring | GmsFeatures.Calibration,
                    MaximumTextLength = 128
                }).WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            GmsSystemSnapshot invalid = Change(initial, mistake);
            await provider.SetSnapshotAsync(invalid).ConfigureAwait(false);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await handle.RefreshAsync().ConfigureAwait(false))!;
            Assert.That(StatusCode.IsBad(error.StatusCode), Is.True);
            Assert.That(handle.Observation, Is.SameAs(initial));
            Assert.That(handle.CalibrationHistory.Count, Is.EqualTo(1));
            Assert.That(handle.TryGetSensor("Probe", out BaseToolState? sensor), Is.True);
            NodeId original = sensor!.NodeId;
            await provider.SetSnapshotAsync(initial with
            {
                Calibration = initial.Calibration! with
                {
                    Calibrated = GmsEquipmentTests.Sample(Variant.From(false))
                }
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensor("Probe", out sensor), Is.True);
            Assert.That(sensor!.NodeId, Is.EqualTo(original));
            Assert.That(handle.CalibrationHistory.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Explicit optional selection cannot silently create an empty successful provider surface.
        /// </summary>
        [TestCase(GmsFeatures.Identification)]
        [TestCase(GmsFeatures.Sensors)]
        [TestCase(GmsFeatures.Loading)]
        [TestCase(GmsFeatures.Accessories)]
        [TestCase(GmsFeatures.AdditionalSensors)]
        [TestCase(GmsFeatures.Calibration)]
        public void MissingSelectedObservationFailsBeforePublication(GmsFeatures feature)
        {
            IMachineryBuildContext context = GmsSystemTests.Context(m_fixture!);
            GmsBuilder builder = GmsSystemTests.Builder(context, "Missing" + feature,
                new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithOptions(new GmsSystemOptions { Features = feature })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()));
            Assert.ThrowsAsync<ServiceResultException>(async () => await builder.BuildAsync().ConfigureAwait(false));
            Assert.That(context.MachinesFolder.FindChild(context.Context,
                new QualifiedName("Missing" + feature, context.InstanceNamespaceIndex)), Is.Null);
        }

        /// <summary>
        /// Native configuration dependencies are checked before any provider acquisition or root allocation.
        /// </summary>
        [TestCase(GmsFeatures.DynamicSensors)]
        [TestCase(GmsFeatures.DynamicProduction)]
        [TestCase(GmsFeatures.ToolMonitoring)]
        [TestCase(GmsFeatures.SensorWarnings)]
        [TestCase(GmsFeatures.Sensors | GmsFeatures.SensorWarnings)]
        [TestCase((GmsFeatures)4096)]
        public void InvalidFeatureDependenciesFailConfiguration(GmsFeatures features)
        {
            GmsBuilder builder = GmsSystemTests.Builder(GmsSystemTests.Context(m_fixture!), "BadOptions",
                new InMemoryGmsProvider(new GmsSystemSnapshot()));
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                builder.WithOptions(new GmsSystemOptions { Features = features }))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Constructs a single deliberately invalid field while preserving all unrelated valid observations.
        /// </summary>
        private static GmsSystemSnapshot Change(GmsSystemSnapshot snapshot, string mistake)
        {
            GmsSensor sensor = snapshot.Sensors[0];
            GmsCalibrationObservation calibration = snapshot.Calibration!;
            DataValue Sample(Variant value) => GmsEquipmentTests.Sample(value);
            GmsSystemSnapshot Sensor(GmsSensor value) => snapshot with { Sensors = [value] };
            GmsSystemSnapshot Calibration(GmsCalibrationObservation value) => snapshot with { Calibration = value };
            return mistake switch
            {
                "MissingQualification" => Sensor(sensor with { Qualified = null }),
                "MissingClass" => Sensor(sensor with { Class = DataValue.Null }),
                "SignedClass" => Sensor(sensor with { Class = Sample(Variant.From(3)) }),
                "ClassRange" => Sensor(sensor with { Class = Sample(Variant.From(ulong.MaxValue)) }),
                "ClassNoTimestamp" => Sensor(sensor with { Class = new DataValue(Variant.From(3u)) }),
                "WrongQualifiedType" => Sensor(sensor with
                {
                    Qualified = sensor.Qualified! with { RemainingHours = Sample(Variant.From(1)) }
                }),
                "ExpiredButQualified" => Sensor(sensor with
                {
                    Qualified = sensor.Qualified! with { RemainingHours = Sample(Variant.From(0.0)) }
                }),
                "InfiniteQualification" => Sensor(sensor with
                {
                    Qualified = sensor.Qualified! with
                    {
                        RemainingHours = Sample(Variant.From(double.PositiveInfinity))
                    }
                }),
                "InvalidAlignment" => Sensor(sensor with { Alignment = Sample(Variant.From((ToolAlignmentState)3)) }),
                "WrongAxes" => Sensor(sensor with { Axes = Sample(Variant.From("X")) }),
                "WrongCapability" => Sensor(sensor with { Capabilities = Sample(Variant.From((ArrayOf<int>)[1])) }),
                "CapabilityRange" => Sensor(sensor with { Capabilities = Sample(Variant.From((ArrayOf<uint>)[6])) }),
                "NegativeRange" => Sensor(sensor with { MeasuringRange = Sample(Variant.From(-1.0)) }),
                "NegativeResolution" => Sensor(sensor with { Resolution = Sample(Variant.From(-0.1)) }),
                "WrongUnitStructure" => Sensor(sensor with
                {
                    EngineeringUnit = Sample(Variant.FromStructure(new CartesianWorkspaceType()))
                }),
                "NestedSingleSensor" => Sensor(sensor with { Sensors = [GmsEquipmentTests.Sensor("Child")] }),
                "DuplicateSensor" => snapshot with { Sensors = [sensor, sensor] },
                "ForeignToolKind" => Sensor(sensor with
                {
                    Tool = sensor.Tool with { Kind = MachineToolToolKind.Companion }
                }),
                "UnsignedLoadingRange" => snapshot with
                {
                    Loading = snapshot.Loading! with { LoadStatus = Sample(Variant.From(4u)) }
                },
                "SignedLoading" => snapshot with
                {
                    Loading = snapshot.Loading! with { LoadStatus = Sample(Variant.From(2)) }
                },
                "WrongLoadingPosition" => snapshot with
                {
                    Loading = snapshot.Loading! with { IsInLoadingPosition = Sample(Variant.From("false")) }
                },
                "MissingActiveSensor" => snapshot with
                {
                    ToolMonitoring = snapshot.ToolMonitoring! with { ActiveSensorKeys = ["OtherMachine"] }
                },
                "DuplicateActiveSensor" => snapshot with
                {
                    ToolMonitoring = snapshot.ToolMonitoring! with { ActiveSensorKeys = ["Probe", "Probe"] }
                },
                "ForeignActiveNode" => snapshot with
                {
                    ToolMonitoring = snapshot.ToolMonitoring! with
                    {
                        ActiveSensorKeys = default,
                        ActiveToolIds = Sample(Variant.From((ArrayOf<NodeId>)[Opc.Ua.ObjectIds.ObjectsFolder]))
                    }
                },
                "AmbiguousActiveTools" => snapshot with
                {
                    ToolMonitoring = snapshot.ToolMonitoring! with
                    {
                        ActiveToolIds = Sample(Variant.From((ArrayOf<NodeId>)[]))
                    }
                },
                "NegativeWorkspace" => snapshot with
                {
                    Identification = snapshot.Identification! with
                    {
                        Workspace = Sample(Variant.FromStructure(new CartesianWorkspaceType { Length = -1 }))
                    }
                },
                "InfiniteWorkspace" => snapshot with
                {
                    Identification = snapshot.Identification! with
                    {
                        Workspace = Sample(Variant.FromStructure(new CylindricalWorkspaceType { Radius = double.NaN }))
                    }
                },
                "AbstractWorkspace" => snapshot with
                {
                    Identification = snapshot.Identification! with
                    {
                        Workspace = Sample(Variant.FromStructure(new WorkspaceType()))
                    }
                },
                "MissingSubclass" => snapshot with
                {
                    Identification = snapshot.Identification! with { SubDeviceClass = DataValue.Null }
                },
                "OverlongSubclass" => snapshot with
                {
                    Identification = snapshot.Identification! with
                    {
                        SubDeviceClass = Sample(Variant.From(new string('x', 129)))
                    }
                },
                "MissingCalibrationDate" => Calibration(calibration with
                {
                    DateOfCalibration = new DataValue(Variant.Null, StatusCodes.BadNoDataAvailable)
                }),
                "FutureCalibration" => Calibration(calibration with
                {
                    DateOfCalibration = Sample(Variant.From(
                        new DateTimeUtc(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc))))
                }),
                "BackdatedCalibration" => Calibration(calibration with
                {
                    DateOfCalibration = Sample(Variant.From(
                        new DateTimeUtc(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))))
                }),
                "OldPrediction" => Calibration(calibration with
                {
                    PredictedTime = Sample(Variant.From(
                        new DateTimeUtc(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))))
                }),
                "ZeroInterval" => Calibration(calibration with { CalibrationInterval = Sample(Variant.From(0.0)) }),
                "NegativePreparation" => Calibration(calibration with
                {
                    CalibrationPreptime = Sample(Variant.From(-1.0))
                }),
                "ExcessivePreparation" => Calibration(calibration with
                {
                    CalibrationPreptime = Sample(Variant.From(1e15))
                }),
                "RelativeCertificate" => Calibration(calibration with
                {
                    CalibrationCertificate = Sample(Variant.From((ArrayOf<string>)["relative.pdf"]))
                }),
                "WrongCertificates" => Calibration(calibration with
                {
                    CalibrationCertificate = Sample(Variant.From("uri"))
                }),
                "InheritedBadMode" => snapshot with
                {
                    MachineTool = snapshot.MachineTool with
                    {
                        Operation = new MachineToolOperation { Mode = (MachineOperationMode)99 }
                    }
                },
                _ => throw new ArgumentOutOfRangeException(nameof(mistake))
            };
        }

        /// <summary>The real shared-owner fixture, reused only across sequential validation cases.</summary>
        private GmsResultServerFixture? m_fixture;
    }
}
