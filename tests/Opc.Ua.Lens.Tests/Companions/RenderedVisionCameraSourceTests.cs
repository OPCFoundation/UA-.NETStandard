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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Perception;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class RenderedVisionCameraSourceTests
    {
        [Test]
        public async Task DiscoversOnlyTypedSimulatedCameraMetadataWithoutAcquisitionAsync()
        {
            var fixture = new CameraFixture();
            var source = new RenderedVisionCameraSource();
            ArrayOf<RenderedVisionCamera> cameras = await source.DiscoverAsync(fixture.Server.Context, default)
                .ConfigureAwait(false);
            Assert.That(cameras, Has.Count.EqualTo(1));
            Assert.That(cameras[0].Sensor, Is.EqualTo(fixture.Sensor));
            Assert.That(cameras[0].Interface, Is.EqualTo(fixture.Simulated));
            Assert.That(cameras[0].SensorId, Is.EqualTo("eye-1"));
            Assert.That(cameras[0].FrameId, Is.EqualTo("camera-frame"));
            Assert.That(cameras[0].PrimPath, Is.EqualTo("/World/Camera"));
            Assert.That(cameras[0].StageIdentifier, Is.EqualTo("https://never-fetched.invalid/stage.usda"));
            await source.RequireCurrentAsync(fixture.Server.Context, cameras[0], default).ConfigureAwait(false);
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(VisionRealityKindEnum.Physical)]
        [TestCase(VisionRealityKindEnum.Hybrid)]
        public async Task PhysicalAndHybridSensorsCannotBeUsedAsRenderedSimulatedVisionAsync(
            VisionRealityKindEnum reality)
        {
            var fixture = new CameraFixture();
            fixture.Set("RealityKind", Variant.From((int)reality));
            var source = new RenderedVisionCameraSource();
            ArrayOf<RenderedVisionCamera> cameras = await source.DiscoverAsync(fixture.Server.Context, default)
                .ConfigureAwait(false);
            Assert.That(cameras, Is.Empty);
            Assert.That(() => source.RequireCurrentAsync(fixture.Server.Context,
                new RenderedVisionCamera(fixture.Sensor, fixture.Simulated, "eye-1", "camera-frame",
                    "https://never-fetched.invalid/stage.usda", "/World/Camera"), default),
                Throws.InvalidOperationException);
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task ChangedCameraIdentityIsRevalidatedBeforeItsPixelsCanBePublishedAsync()
        {
            var fixture = new CameraFixture();
            var source = new RenderedVisionCameraSource();
            ArrayOf<RenderedVisionCamera> cameras = await source.DiscoverAsync(fixture.Server.Context, default)
                .ConfigureAwait(false);
            NodeId prim = fixture.Server.Children[(fixture.Simulated, "PrimPath")];
            fixture.Server.Values[(prim, Attributes.Value)] = new DataValue(Variant.From("/World/OtherCamera"));
            Assert.That(() => source.RequireCurrentAsync(fixture.Server.Context, cameras[0], default),
                Throws.InvalidOperationException);
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public void AmbiguousSimulatedInterfacesFailInsteadOfChoosingAnArbitraryCamera()
        {
            var fixture = new CameraFixture();
            ArrayOf<ReferenceDescription> references = fixture.Server.Browses[fixture.Sensor];
            fixture.Server.Browses[fixture.Sensor] = [.. references, references[^1]];
            var source = new RenderedVisionCameraSource();
            Assert.That(() => source.DiscoverAsync(fixture.Server.Context, default),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyMatches));
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        private sealed class CameraFixture
        {
            public CameraFixture()
            {
                var client = new VisionClient(Server.Session.Object, Server.Context.Telemetry);
                Sensor = new NodeId("sensor", Server.NamespaceIndex);
                Simulated = new NodeId("simulated-interface", Server.NamespaceIndex);
                Server.Browses[client.SensorsFolderId] =
                    [Server.Reference(Sensor, "Camera", ExpandedNodeId.ToNodeId(
                        Opc.Ua.Vision.Perception.ObjectTypeIds.VisionSensorType, Server.NamespaceUris))];
                Server.AddValue(Sensor, "SensorId", Variant.From("eye-1"));
                Server.AddValue(Sensor, "RealityKind", Variant.From((int)VisionRealityKindEnum.Simulated));
                Server.AddValue(Sensor, "Modality", Variant.From(0));
                Server.AddValue(Sensor, "Manufacturer", Variant.From(new LocalizedText("Fixture")));
                Server.AddValue(Sensor, "Model", Variant.From(new LocalizedText("Camera")));
                Server.AddValue(Sensor, "SerialNumber", Variant.From("123"));
                Server.AddValue(Sensor, "DeviceUri", Variant.From("urn:fixture:camera"));
                Server.AddValue(Sensor, "FrameId", Variant.From("camera-frame"));
                ReferenceDescription simulated = Server.Reference(Simulated, "Simulated",
                    ExpandedNodeId.ToNodeId(
                        Opc.Ua.Vision.Perception.ObjectTypeIds.IVisionSimulatedType, Server.NamespaceUris));
                simulated.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasInterface;
                Server.Browses[Sensor] = [.. Server.Browses[Sensor], simulated];
                Server.AddValue(Simulated, "StageIdentifier", Variant.From("https://never-fetched.invalid/stage.usda"));
                Server.AddValue(Simulated, "PrimPath", Variant.From("/World/Camera"));
            }

            public CellProviderTestSession Server { get; } = new(Opc.Ua.Vision.Perception.Namespaces.Vision);
            public NodeId Sensor { get; }
            public NodeId Simulated { get; }

            public void Set(string name, Variant value)
            {
                Server.Values[(Server.Children[(Sensor, name)], Attributes.Value)] = new DataValue(value);
            }
        }
    }
}
