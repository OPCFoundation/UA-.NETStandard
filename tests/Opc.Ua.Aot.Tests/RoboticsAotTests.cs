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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Robotics.Server;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT roots for the Robotics and Robot Intent generated model, builders,
    /// binary encodings, server registration, and client registration.
    /// </summary>
    public sealed class RoboticsAotTests
    {
        [Test]
        public async Task RobotIntentModelEncodingsAndDiAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(global::Opc.Ua.RobotIntent.Namespaces.RobotIntent);
            context.Factory.Builder.AddOpcUaRobotIntent().Commit();

            var nodes = new NodeStateCollection();
            nodes.AddOpcUaRobotIntent(new SystemContext(telemetry)
            {
                NamespaceUris = context.NamespaceUris,
                EncodeableFactory = context.Factory
            });

            var controller = new IntentControllerState(null);
            controller.Create(
                new SystemContext(telemetry)
                {
                    NamespaceUris = context.NamespaceUris,
                    EncodeableFactory = context.Factory
                },
                new NodeId("RobotIntentAotController", 1),
                new QualifiedName("RobotIntentAotController", 1),
                new LocalizedText("RobotIntentAotController"),
                true);

            EncodeRoundTrip(context, CreateJointMove());
            EncodeRoundTrip(context, new LinearMoveIntentDataType { Target = Pose(0.2, 0.0, 0.1) });
            EncodeRoundTrip(context, new CircularMoveIntentDataType
            {
                ViaPoint = Pose(0.1, 0.1, 0.1),
                Target = Pose(0.2, 0.0, 0.1)
            });
            EncodeRoundTrip(context, new TrajectoryIntentDataType
            {
                Points =
                [
                    new TrajectoryPointDataType
                    {
                        TimeFromStart = 0.0,
                        Positions = ArrayOf.Create([0.0, 0.0, 0.0, 0.0, 0.0, 0.0])
                    },
                    new TrajectoryPointDataType
                    {
                        TimeFromStart = 100.0,
                        Positions = ArrayOf.Create([0.1, 0.0, 0.0, 0.0, 0.0, 0.0])
                    }
                ]
            });
            EncodeRoundTrip(context, new CartesianPathIntentDataType
            {
                Waypoints =
                [
                    new PathWaypointDataType { Pose = Pose(0.1, 0.0, 0.1) },
                    new PathWaypointDataType { Pose = Pose(0.2, 0.0, 0.1) }
                ]
            });
            EncodeRoundTrip(context, new ForceIntentDataType
            {
                Direction = ArrayOf.Create([0.0, 0.0, -1.0]),
                ContactForce = 10.0,
                MaxDistance = 0.05
            });
            EncodeRoundTrip(context, new ArcWeldIntentDataType());
            EncodeRoundTrip(context, new SpotWeldIntentDataType());
            EncodeRoundTrip(context, new DispenseIntentDataType());
            EncodeRoundTrip(context, new FastenIntentDataType());
            EncodeRoundTrip(context, new PalletiseIntentDataType());
            EncodeRoundTrip(context, new SurfaceFinishIntentDataType());
            EncodeRoundTrip(context, new GraspIntentDataType { Tool = new NodeId("tool", 1), Width = 0.02 });
            EncodeRoundTrip(context, new ReleaseIntentDataType { Tool = new NodeId("tool", 1) });
            EncodeRoundTrip(context, new PickIntentDataType
            {
                Source = new NodeId("source", 1),
                Tool = new NodeId("tool", 1)
            });
            EncodeRoundTrip(context, new PlaceIntentDataType
            {
                Destination = new NodeId("destination", 1),
                Tool = new NodeId("tool", 1)
            });
            EncodeRoundTrip(context, new ToolChangeIntentDataType
            {
                Tool = new NodeId("tool", 1),
                DockStation = new NodeId("dock", 1)
            });
            EncodeRoundTrip(context, new SetOutputIntentDataType
            {
                Output = new NodeId("output", 1),
                Value = new Variant(true)
            });
            EncodeRoundTrip(context, new CallProgramIntentDataType { Program = new NodeId("program", 1) });
            EncodeRoundTrip(context, new WaitIntentDataType { Duration = 10.0 });

            Pose3DDataType root = Pose("root", 0.0, 0.0, 0.0);
            Pose3DDataType child = PoseMath.FromThreeDFrame(
                new ThreeDFrame
                {
                    CartesianCoordinates = new ThreeDCartesianCoordinates { X = 0.1, Y = 0.2, Z = 0.3 }
                },
                "child");
            var frameTree = new FrameTree();
            bool rootAdded = frameTree.TryAdd("root", string.Empty, root, FrameRoleEnum.Base, out string rootError);
            bool childAdded = frameTree.TryAdd("child", "root", child, FrameRoleEnum.Tool, out string childError);
            bool transformed = frameTree.TryExpress(
                Pose("child", 0.0, 0.0, 0.0),
                "root",
                out Pose3DDataType resolved,
                out string transformError);

            JointMoveIntentDataType builtJoint = RobotIntentBuilder.JointMove(6)
                .ToJoints(ArrayOf.Create([0.1, 0.0, 0.0, 0.0, 0.0, 0.0]))
                .WithIntentId("built-joint")
                .Build();
            MissionDataType mission = RobotIntentBuilder.Mission("aot-mission")
                .ReleasedStep("step-1", builtJoint)
                .Build();

            var clientConfiguration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "RobotIntentAotClient",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration(),
                TransportQuotas = new TransportQuotas()
            };
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "RobotIntentAotServer";
                    options.AutoAcceptUntrustedCertificates = true;
                })
                .AddRobotIntent();
            services.AddOpcUa()
                .AddClient(options => options.Configuration = clientConfiguration)
                .AddRobotIntentClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            RobotIntentNodeManagerFactory nodeManagerFactory =
                provider.GetRequiredService<RobotIntentNodeManagerFactory>();
            Func<CancellationToken, Task<RobotIntentClient>> clientFactory =
                provider.GetRequiredService<Func<CancellationToken, Task<RobotIntentClient>>>();

            await Assert.That(nodes.Count).IsGreaterThan(0);
            await Assert.That(controller.SubmitIntent).IsNotNull();
            await Assert.That(rootAdded).IsTrue();
            await Assert.That(rootError).IsNull();
            await Assert.That(childAdded).IsTrue();
            await Assert.That(childError).IsNull();
            await Assert.That(transformed).IsTrue();
            await Assert.That(transformError).IsNull();
            await Assert.That(resolved.FrameId).IsEqualTo("root");
            await Assert.That(mission.Steps.Count).IsEqualTo(1);
            await Assert.That(nodeManagerFactory).IsNotNull();
            await Assert.That(clientFactory).IsNotNull();
        }

        private static void EncodeRoundTrip<T>(ServiceMessageContext context, T value)
            where T : class, IEncodeable, new()
        {
            byte[] encoded = BinaryEncoder.EncodeMessage(value, context);
            T decoded = BinaryDecoder.DecodeMessage<T>(encoded, context);
            if (!value.IsEqual(decoded))
            {
                throw new InvalidOperationException(typeof(T).Name + " did not round-trip through binary encoding.");
            }
        }

        private static JointMoveIntentDataType CreateJointMove()
        {
            return new JointMoveIntentDataType
            {
                HasJointTargets = true,
                JointTargets = ArrayOf.Create([0.1, 0.0, 0.0, 0.0, 0.0, 0.0])
            };
        }

        private static Pose3DDataType Pose(double x, double y, double z)
        {
            return Pose("base", x, y, z);
        }

        private static Pose3DDataType Pose(string frameId, double x, double y, double z)
        {
            return new Pose3DDataType
            {
                FrameId = frameId,
                Position = ArrayOf.Create([x, y, z]),
                Orientation = ArrayOf.Create([0.0, 0.0, 0.0, 1.0])
            };
        }
    }
}
