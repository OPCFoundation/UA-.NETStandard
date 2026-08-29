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

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Tests for <see cref="VisionSensorClient"/> through the harness.
    /// Every test drives the client via the public front door
    /// (<c>Client.Sensor(id)</c>) so the underlying
    /// <c>VisionSensorTypeClient</c> proxy code is exercised as well.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionSensorClientTests
    {
        [Test]
        public async Task ReadIdentityReturnsAllPopulatedMembers()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.SensorId,
                new(2500u, 3), "cam-1");
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.RealityKind,
                new(2501u, 3), (int)VisionRealityKindEnum.Physical);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.Modality,
                new(2502u, 3), (int)VisionSensorModalityEnum.Area2D);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.Manufacturer,
                new(2503u, 3), new LocalizedText("ACME"));
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.Model,
                new(2504u, 3), new LocalizedText("Model X"));
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.SerialNumber,
                new(2505u, 3), "SN-1");
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.DeviceUri,
                new(2506u, 3), "urn:acme:camera:1");
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.FrameId,
                new(2507u, 3), "cam-1");

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionSensorIdentity identity = await sensor.ReadIdentityAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(identity.SensorId, Is.EqualTo("cam-1"));
                Assert.That(identity.RealityKind, Is.EqualTo(VisionRealityKindEnum.Physical));
                Assert.That(identity.Modality, Is.EqualTo(VisionSensorModalityEnum.Area2D));
                Assert.That(identity.Manufacturer.Text, Is.EqualTo("ACME"));
                Assert.That(identity.Model.Text, Is.EqualTo("Model X"));
                Assert.That(identity.SerialNumber, Is.EqualTo("SN-1"));
                Assert.That(identity.DeviceUri, Is.EqualTo("urn:acme:camera:1"));
                Assert.That(identity.FrameId, Is.EqualTo("cam-1"));
                Assert.That(identity.NodeId, Is.EqualTo(harness.SensorNodeId));
            });
        }

        [Test]
        public async Task ReadImageMembersReturnsNullWhenCoreMembersAbsent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionImageSensorSnapshot? snapshot = await sensor.ReadImageMembersAsync()
                .ConfigureAwait(false);

            Assert.That(snapshot, Is.Null,
                "Sensor with no Width, Height or PixelFormat is not an image sensor.");
        }

        [Test]
        public async Task ReadImageMembersReturnsPopulatedSnapshotWhenPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.Width,
                new(2510u, 3), 1920u);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.Height,
                new(2511u, 3), 1080u);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.PixelFormat,
                new(2512u, 3), "Mono8");
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.ExposureTime,
                new(2513u, 3), 0.001);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.Gain,
                new(2514u, 3), 1.5);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.AcquisitionFrameRate,
                new(2515u, 3), 30.0);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionImageSensorSnapshot? snapshot = await sensor.ReadImageMembersAsync()
                .ConfigureAwait(false);

            Assert.That(snapshot, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot!.Width, Is.EqualTo(1920u));
                Assert.That(snapshot.Height, Is.EqualTo(1080u));
                Assert.That(snapshot.PixelFormat, Is.EqualTo("Mono8"));
                Assert.That(snapshot.ExposureTime, Is.EqualTo(0.001));
                Assert.That(snapshot.Gain, Is.EqualTo(1.5));
                Assert.That(snapshot.AcquisitionFrameRate, Is.EqualTo(30.0));
            });
        }

        [Test]
        public async Task ReadDepthMembersReturnsNullWhenNoDepthMembersPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.Depth3DSensorType);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionDepth3DSensorSnapshot? snapshot = await sensor.ReadDepthMembersAsync()
                .ConfigureAwait(false);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public async Task ReadDepthMembersReturnsPopulatedSnapshotWhenPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.Depth3DSensorType);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.MinDepth,
                new(2520u, 3), 0.1);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.MaxDepth,
                new(2521u, 3), 5.0);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.DepthScale,
                new(2522u, 3), 0.001);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.Baseline,
                new(2523u, 3), 0.05);
            harness.AddValueChild(harness.SensorNodeId, BrowseNames.PointsPerFrame,
                new(2524u, 3), 640u * 480u);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionDepth3DSensorSnapshot? snapshot = await sensor.ReadDepthMembersAsync()
                .ConfigureAwait(false);

            Assert.That(snapshot, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot!.MinDepth, Is.EqualTo(0.1));
                Assert.That(snapshot.MaxDepth, Is.EqualTo(5.0));
                Assert.That(snapshot.DepthScale, Is.EqualTo(0.001));
                Assert.That(snapshot.Baseline, Is.EqualTo(0.05));
                Assert.That(snapshot.PointsPerFrame, Is.EqualTo(640u * 480u));
            });
        }

        [Test]
        public async Task ReadOpticsReturnsNullWhenNotPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionOpticsSnapshot? optics = await sensor.ReadOpticsAsync()
                .ConfigureAwait(false);

            Assert.That(optics, Is.Null);
        }

        [Test]
        public async Task ReadOpticsReadsMembersWhenPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddChild(harness.SensorNodeId, BrowseNames.Optics, harness.OpticsNodeId);
            harness.AddValueChild(harness.OpticsNodeId, BrowseNames.FocalLength,
                new(2530u, 3), 8.0);
            harness.AddValueChild(harness.OpticsNodeId, BrowseNames.Aperture,
                new(2531u, 3), 2.8);
            harness.AddValueChild(harness.OpticsNodeId, BrowseNames.MinimumWorkingDistance,
                new(2532u, 3), 0.3);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionOpticsSnapshot? optics = await sensor.ReadOpticsAsync()
                .ConfigureAwait(false);

            Assert.That(optics, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(optics!.NodeId, Is.EqualTo(harness.OpticsNodeId));
                Assert.That(optics.FocalLength, Is.EqualTo(8.0));
                Assert.That(optics.Aperture, Is.EqualTo(2.8));
                Assert.That(optics.WorkingDistance, Is.EqualTo(0.3));
            });
        }

        [Test]
        public async Task ReadIlluminationReturnsNullWhenNotPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionIlluminationSnapshot? illumination = await sensor.ReadIlluminationAsync()
                .ConfigureAwait(false);

            Assert.That(illumination, Is.Null);
        }

        [Test]
        public async Task ReadIlluminationReadsMembersWhenPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddChild(harness.SensorNodeId, BrowseNames.Illumination,
                harness.IlluminationNodeId);
            harness.AddValueChild(harness.IlluminationNodeId, BrowseNames.Wavelength,
                new(2540u, 3), 850.0);
            harness.AddValueChild(harness.IlluminationNodeId, BrowseNames.RelativeIntensity,
                new(2541u, 3), 0.9);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionIlluminationSnapshot? illumination = await sensor.ReadIlluminationAsync()
                .ConfigureAwait(false);

            Assert.That(illumination, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(illumination!.NodeId, Is.EqualTo(harness.IlluminationNodeId));
                Assert.That(illumination.Wavelength, Is.EqualTo(850.0));
                Assert.That(illumination.RelativeIntensity, Is.EqualTo(0.9));
            });
        }

        [Test]
        public async Task GetMountedFrameIdReturnsNullNodeIdWhenNotMounted()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            NodeId mount = await sensor.GetMountedFrameIdAsync().ConfigureAwait(false);

            Assert.That(mount.IsNull, Is.True);
        }

        [Test]
        public async Task GetMountedFrameIdReturnsFrameWhenMountedOnReferenceExists()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AppendBrowse(harness.SensorNodeId, new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(harness.FrameNodeId),
                BrowseName = new QualifiedName("MyMount", harness.VisionNamespaceIndex),
                DisplayName = new LocalizedText("MyMount"),
                NodeClass = NodeClass.Object,
                TypeDefinition = new ExpandedNodeId(
                    new NodeId(ObjectTypes.CoordinateFrameType, harness.VisionNamespaceIndex)),
                ReferenceTypeId = new NodeId(ReferenceTypes.MountedOn, harness.VisionNamespaceIndex),
                IsForward = true
            });

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            NodeId mount = await sensor.GetMountedFrameIdAsync().ConfigureAwait(false);

            Assert.That(mount, Is.EqualTo(harness.FrameNodeId));
        }

        [Test]
        public async Task EnumerateCalibrationsYieldsCalibrationsFromNestedFolder()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddChild(harness.SensorNodeId, BrowseNames.Calibrations,
                harness.CalibrationsFolderId);
            harness.AddBrowse(harness.CalibrationsFolderId,
                [harness.Ref(harness.IntrinsicCalibrationNodeId, "Intrinsic",
                    ObjectTypes.IntrinsicCalibrationType)]);

            var entries = new List<VisionNodeEntry>();
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            await foreach (VisionNodeEntry entry in sensor.EnumerateCalibrationsAsync())
            {
                entries.Add(entry);
            }

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].NodeId, Is.EqualTo(harness.IntrinsicCalibrationNodeId));
        }

        [Test]
        public async Task ReadIntrinsicCalibrationThrowsForNullNodeId()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);

            Assert.ThrowsAsync<System.ArgumentException>(async () =>
                await sensor.ReadIntrinsicCalibrationAsync(NodeId.Null)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task ReadIntrinsicCalibrationReturnsPopulatedSnapshot()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddValueChild(harness.IntrinsicCalibrationNodeId, BrowseNames.CalibrationId,
                new(2600u, 3), "cal-1");
            harness.AddValueChild(harness.IntrinsicCalibrationNodeId, BrowseNames.PerformedAt,
                new(2601u, 3), new DateTimeUtc(new System.DateTime(2024, 1, 1)));
            harness.AddValueChild(harness.IntrinsicCalibrationNodeId, BrowseNames.Valid,
                new(2602u, 3), true);
            harness.AddValueChild(harness.IntrinsicCalibrationNodeId, BrowseNames.ResidualError,
                new(2603u, 3), 0.15);
            harness.AddValueChild(harness.IntrinsicCalibrationNodeId, BrowseNames.Method,
                new(2604u, 3), "Zhang");

            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);
            VisionIntrinsicCalibrationSnapshot snapshot = await sensor
                .ReadIntrinsicCalibrationAsync(harness.IntrinsicCalibrationNodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.CalibrationId, Is.EqualTo("cal-1"));
                Assert.That(snapshot.Valid, Is.True);
                Assert.That(snapshot.ResidualError, Is.EqualTo(0.15));
                Assert.That(snapshot.Method, Is.EqualTo("Zhang"));
                Assert.That(snapshot.NodeId, Is.EqualTo(harness.IntrinsicCalibrationNodeId));
            });
        }

        [Test]
        public async Task ReadExtrinsicCalibrationThrowsForNullNodeId()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);

            Assert.ThrowsAsync<System.ArgumentException>(async () =>
                await sensor.ReadExtrinsicCalibrationAsync(NodeId.Null)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task OpenMediaReturnsNullWhenNoMediaObjectPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);

            VisionMediaClient? media = await sensor.OpenMediaAsync()
                .ConfigureAwait(false);

            Assert.That(media, Is.Null);
        }

        [Test]
        public async Task OpenMediaReturnsClientWhenMediaObjectPresent()
        {
            var harness = new VisionSessionHarness();
            harness.AddSensor(ObjectTypes.ImageSensorType);
            harness.AddChild(harness.SensorNodeId, BrowseNames.Media, harness.MediaNodeId);
            VisionSensorClient sensor = harness.Client.Sensor(harness.SensorNodeId);

            VisionMediaClient? media = await sensor.OpenMediaAsync()
                .ConfigureAwait(false);

            Assert.That(media, Is.Not.Null);
            Assert.That(media!.MediaNodeId, Is.EqualTo(harness.MediaNodeId));
        }

        [Test]
        public void ConstructorRejectsNullSensorNodeId()
        {
            var harness = new VisionSessionHarness();

            Assert.Throws<System.ArgumentException>(() => harness.Client.Sensor(NodeId.Null));
        }
    }
}
