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
using Moq;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Robotics;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Defines explicit simulated equipment, separate from its optional runtime capabilities.
    /// </summary>
    internal static class CraneRuntimeTestData
    {
        /// <summary>
        /// Gets a deterministic acquisition time unrelated to receive time.
        /// </summary>
        internal static DateTimeUtc SourceTime { get; } =
            new(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        /// <summary>
        /// Builds a minimally valid native topology or explicitly selects every released crane facet.
        /// </summary>
        internal static CraneSystemBuilder Define(
            CranesHoistsBuildContext context,
            string name,
            ICraneDataProvider provider,
            bool full = false,
            ICraneLimitProvider? limits = null,
            ICraneWriteAuthorizer? authorizer = null,
            Action<LoadState, ISystemContext>? configureLoad = null)
        {
            CraneSystemBuilder builder = context.AddCraneSystem(name)
                .WithProvider(provider)
                .WithIdentification(data => Identify(data, name))
                .AddController("Controller", controller =>
                {
                    controller.WithIdentification(data =>
                    {
                        data.Manufacturer = new LocalizedText("Simulated manufacturer");
                        data.Model = new LocalizedText("Controller");
                        data.ProductCode = "Controller-model";
                        data.SerialNumber = "Controller-1";
                    });
                    controller.WithCurrentUser(user => user.WithLevel("Observer").WithName("Test operator"));
                    controller.AddSoftware("Runtime", software => software.WithIdentification(data =>
                    {
                        data.Manufacturer = new LocalizedText("Simulated manufacturer");
                        data.Model = new LocalizedText("Runtime");
                        data.SoftwareRevision = "1.0";
                    }));
                    controller.AddTaskControl("Main");
                })
                .AddSafetyState("Safety")
                .AddDevice("Hoist", CraneMotionDeviceCategoryEnum.HOIST, device =>
                {
                    device.WithIdentification(data => Identify(data, name + "-hoist"));
                    if (full)
                    {
                        device.WithWorkingPeriod().WithComponents();
                    }
                    device.AddAxis("Lift", axis =>
                    {
                        if (full)
                        {
                            axis.WithPositionCapability(true, AxisMotionProfileEnumeration.LINEAR)
                                .WithAxisFunctions().WithMovingState().WithSpeedLimits()
                                .WithRatedSpeed(2.5, new EUInformation
                                {
                                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                                    UnitId = 5067859,
                                    DisplayName = new LocalizedText("m/s")
                                })
                                .WithActualLoad(configureLoad)
                                .AddBackupBrake("BackupBrake", data => Identify(data, name + "-brake"));
                        }
                        axis.AddMotor("LiftMotor", motor =>
                        {
                            if (full)
                            {
                                motor.WithOverheated().WithServiceBrake()
                                    .WithIdentification(data => Identify(data, name + "-motor"));
                            }
                        });
                    });
                });
            if (full)
            {
                builder.WithExternalControl().WithGlobalSpeedLimit()
                    .WithFunctionalGroups().WithComponents().WithEvents()
                    .WithLimitRequests(limits ?? (ICraneLimitProvider)provider,
                        authorizer ?? AllowWrites().Object)
                    .AddProtectiveFunction("Overload", ["Lift"], protection => protection.WithStateMonitoring()
                        .WithDescription(ProtectiveFunctionEnumeration.FORCE_LIMITER, "Overload protection", true)
                        .WithDirections(true, true, false, true)
                        .WithIdentification(data => Identify(data, name + "-protection")));
            }
            return builder;
        }

        /// <summary>
        /// Returns explicit provider samples, never defaults inferred by the server.
        /// </summary>
        internal static ArrayOf<CraneSample> Samples(string name, bool full = false)
        {
            var samples = new List<CraneSample>
            {
                Sample(name, CraneSignal.OperationalMode, Variant.From((int)CraneOperationalModeEnumeration.MANUAL)),
                Sample(name, CraneSignal.SystemOn, Variant.From(true)),
                Sample("Hoist", CraneSignal.SpeedOverride, Variant.From(83.25)),
                Sample("LiftMotor", CraneSignal.MotorTemperature, Variant.From(35.5)),
                Sample("Safety", CraneSignal.EmergencyStop, Variant.From(false)),
                Sample("Safety", CraneSignal.ProtectiveStop, Variant.From(false)),
                Sample("Safety", CraneSignal.SafetyOperationalMode,
                    Variant.From((int)OperationalModeEnumeration.AUTOMATIC))
            };
            if (full)
            {
                samples.AddRange(
                [
                    Sample(name, CraneSignal.ExternalControlRequest,
                        Variant.From((int)ExternalControlRequestEnumeration.REQUESTED_AND_CONTROL_ACTIVE)),
                    Sample("Hoist", CraneSignal.DesignedWorkingPeriod, Variant.From(92.5)),
                    Sample("Lift", CraneSignal.ActualPosition, Variant.From(12.75)),
                    Sample("Lift", CraneSignal.AxisFunction, Variant.From(ArrayOf.Wrapped<ushort>([0, 2, 4]))),
                    Sample("Lift", CraneSignal.IsMoving, Variant.From(true)),
                    Sample("Lift", CraneSignal.SpeedLimitDirMinus, Variant.From(27.5)),
                    Sample("Lift", CraneSignal.SpeedLimitDirPlus, Variant.From(31.5)),
                    Sample("Lift", CraneSignal.LoadMass, Variant.From(153.0)),
                    Sample("LiftMotor", CraneSignal.Overheated, Variant.From(false)),
                    Sample("LiftMotor", CraneSignal.BrakeReleased, Variant.From(true)),
                    Sample("BackupBrake", CraneSignal.BrakeReleased, Variant.From(false)),
                    Sample("Overload", CraneSignal.Enabled, Variant.From(true)),
                    Sample("Overload", CraneSignal.Active, Variant.From(false))
                ]);
            }
            return samples.ToArrayOf();
        }

        /// <summary>
        /// Creates a timestamped sample with an explicit status.
        /// </summary>
        internal static CraneSample Sample(
            string id,
            CraneSignal signal,
            Variant value,
            StatusCode status = default)
        {
            return new CraneSample(new CraneSignalAddress(id, signal), new DataValue(value, status, SourceTime));
        }

        /// <summary>
        /// Replaces one sample without mutating the original acquisition.
        /// </summary>
        internal static ArrayOf<CraneSample> Replace(ArrayOf<CraneSample> samples, CraneSample replacement)
        {
            var result = new List<CraneSample>();
            foreach (CraneSample sample in samples)
            {
                result.Add(sample.Address == replacement.Address ? replacement : sample);
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Supplies coherent Machinery and inherited DI identity values.
        /// </summary>
        internal static void Identify(MachineryIdentificationData data, string name)
        {
            data.Manufacturer = new LocalizedText("Simulated manufacturer");
            data.SerialNumber = name;
            data.ProductInstanceUri = "urn:crane:simulation:" + name;
            data.Model = new LocalizedText("Simulated crane");
            data.ProductCode = "Simulation-only";
        }

        /// <summary>
        /// Creates an explicit test authorization policy, never a production default.
        /// </summary>
        internal static Mock<ICraneWriteAuthorizer> AllowWrites()
        {
            var authorizer = new Mock<ICraneWriteAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(value => value.AuthorizeAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CraneLimitRequest>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(true);
            return authorizer;
        }
    }
}
