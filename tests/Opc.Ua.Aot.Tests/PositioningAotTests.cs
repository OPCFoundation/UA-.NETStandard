// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Client;
using Opc.Ua.Positioning.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// NativeAOT reachability tests for the Positioning model, transforms,
    /// client registration, and server registration.
    /// </summary>
    public sealed class PositioningAotTests
    {
        [Test]
        public async Task PositioningModelsTransformsAndHostingAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            var clientConfiguration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "PositioningAotClient",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration(),
                TransportQuotas = new TransportQuotas()
            };
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(options =>
                {
                    options.ApplicationName = "PositioningAotServer";
                    options.AutoAcceptUntrustedCertificates = true;
                })
                .AddPositioningServer();
            services.AddOpcUa()
                .AddClient(options =>
                    options.Configuration = clientConfiguration)
                .AddPositioningClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            PositioningNodeManagerFactory factory =
                provider.GetRequiredService<PositioningNodeManagerFactory>();
            Func<CancellationToken, Task<RelativeSpatialLocationClient>>
                relativeFactory = provider.GetRequiredService<
                    Func<CancellationToken, Task<RelativeSpatialLocationClient>>>();
            Func<CancellationToken, Task<GlobalPositioningClient>>
                globalFactory = provider.GetRequiredService<
                    Func<CancellationToken, Task<GlobalPositioningClient>>>();

            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates
                {
                    X = 1.0,
                    Y = 2.0,
                    Z = 3.0
                },
                Orientation = new ThreeDOrientation { C = 45.0 }
            };
            var transform = RslFrameTransform.FromFrame(
                frame,
                AngleUnit.Degrees);
            ThreeDCartesianCoordinates transformed = transform.TransformPoint(
                new ThreeDCartesianCoordinates());

            ArrayOf<GroundControlPointDataType> controlPoints =
            [
                CreateControlPoint(0.0, 0.0, 8.0, 47.0),
                CreateControlPoint(1.0, 0.0, 8.00001, 47.0),
                CreateControlPoint(0.0, 1.0, 8.0, 47.00001)
            ];
            GroundControlPointFitResult fit =
                new GroundControlPointFitter().Fit(controlPoints);
            ThreeDCartesianCoordinates local = fit.GlobalToLocal(
                controlPoints[0].GlobalPosition,
                AngleUnit.Degrees);

            var context =
                ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Rsl.Namespaces.RSL);
            context.NamespaceUris.GetIndexOrAppend(Gpos.Namespaces.GPOS);
            context.Factory.Builder.AddOpcUaGpos().Commit();
            var location = new GlobalLocationDataType
            {
                Position = new GlobalPositionDataType
                {
                    Longitude = 8.0,
                    Latitude = 47.0
                }
            };
            byte[] encoded = BinaryEncoder.EncodeMessage(location, context);
            GlobalLocationDataType decoded = BinaryDecoder.DecodeMessage<GlobalLocationDataType>(
                encoded,
                context);

            await Assert.That(factory.NamespacesUris.Count).IsEqualTo(2);
            await Assert.That(relativeFactory).IsNotNull();
            await Assert.That(globalFactory).IsNotNull();
            await Assert.That(transformed.X).IsEqualTo(1.0);
            await Assert.That(double.IsFinite(local.X)).IsTrue();
            await Assert.That(location.IsEqual(decoded)).IsTrue();
        }

        private static GroundControlPointDataType CreateControlPoint(
            double x,
            double y,
            double longitude,
            double latitude)
        {
            return new GroundControlPointDataType
            {
                LocalPosition = new ThreeDCartesianCoordinates
                {
                    X = x,
                    Y = y
                },
                GlobalPosition = new S3DGeographicCoordinateDataType
                {
                    Longitude = longitude,
                    Latitude = latitude
                }
            };
        }
    }
}
