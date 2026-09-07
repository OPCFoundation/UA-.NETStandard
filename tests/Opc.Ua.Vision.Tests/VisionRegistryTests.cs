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
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the shape of the internal Vision registry that backs facet
    /// computation, frame math, and method dispatch. The registry is the
    /// single source of truth the node manager consults after materialising
    /// nodes from the address space; every "does anyone have this facet?",
    /// "resolve this frame id", and "route this method call" query
    /// eventually hits one of these lookups.
    /// </summary>
    [TestFixture]
    public sealed class VisionRegistryTests
    {
        [Test]
        public void AddSensorMakesItLookupableByBrowseNameAndNodeId()
        {
            var registry = new VisionRegistry();
            SensorRegistration reg = NewSensor("cam1", 101);

            registry.AddSensor(reg);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetSensor("cam1", out SensorRegistration? byName), Is.True);
                Assert.That(byName, Is.SameAs(reg));
                Assert.That(registry.TryGetSensor(new NodeId(101, 4), out SensorRegistration? byId), Is.True);
                Assert.That(byId, Is.SameAs(reg));
            });
        }

        [Test]
        public void TryGetSensorReturnsFalseForUnknownBrowseName()
        {
            var registry = new VisionRegistry();

            Assert.That(registry.TryGetSensor("nope", out _), Is.False);
        }

        [Test]
        public void TryGetSensorWithNullNodeIdReturnsFalse()
        {
            var registry = new VisionRegistry();
            registry.AddSensor(NewSensor("cam1", 101));
            NodeId nullId = default;

            Assert.That(registry.TryGetSensor(nullId, out _), Is.False,
                "A null NodeId must not throw and must not return a sensor.");
        }

        [Test]
        public void AddPipelineMakesItLookupableByBrowseNameAndNodeId()
        {
            var registry = new VisionRegistry();
            PipelineRegistration reg = NewPipeline("pipe1", 201);

            registry.AddPipeline(reg);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetPipeline("pipe1", out PipelineRegistration? byName), Is.True);
                Assert.That(byName, Is.SameAs(reg));
                Assert.That(registry.TryGetPipeline(new NodeId(201, 4), out PipelineRegistration? byId), Is.True);
                Assert.That(byId, Is.SameAs(reg));
            });
        }

        [Test]
        public void TryGetPipelineWithNullNodeIdReturnsFalse()
        {
            var registry = new VisionRegistry();
            registry.AddPipeline(NewPipeline("pipe1", 201));
            NodeId nullId = default;

            Assert.That(registry.TryGetPipeline(nullId, out _), Is.False);
        }

        [Test]
        public void AnySensorHasFacetIsTrueOnlyWhenAnySensorContainsThatFacet()
        {
            var registry = new VisionRegistry();
            var facets = new HashSet<string> { VisionConformanceUris.FacetNames.Optics };
            registry.AddSensor(NewSensor("cam1", 101, facets));

            Assert.Multiple(() =>
            {
                Assert.That(registry.AnySensorHasFacet(VisionConformanceUris.FacetNames.Optics), Is.True);
                Assert.That(registry.AnySensorHasFacet(VisionConformanceUris.FacetNames.MediaInline), Is.False);
                Assert.That(registry.AnySensorHasFacet("nonexistent-facet"), Is.False);
            });
        }

        [Test]
        public void AnyPipelineHasFacetIsTrueOnlyWhenAnyPipelineContainsThatFacet()
        {
            var registry = new VisionRegistry();
            var facets = new HashSet<string> { VisionConformanceUris.FacetNames.InferenceOnServer };
            registry.AddPipeline(NewPipeline("pipe1", 201, facets));

            Assert.Multiple(() =>
            {
                Assert.That(registry.AnyPipelineHasFacet(VisionConformanceUris.FacetNames.InferenceOnServer), Is.True);
                Assert.That(registry.AnyPipelineHasFacet(VisionConformanceUris.FacetNames.Feedback), Is.False);
            });
        }

        [Test]
        public void AddFrameMakesItLookupableByBrowseNameAndFrameId()
        {
            var registry = new VisionRegistry();
            FrameRegistration reg = NewFrame("baseFrame", 301, frameId: "base", parentFrameId: null);

            registry.AddFrame(reg);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetFrame("baseFrame", out FrameRegistration? byName), Is.True);
                Assert.That(byName, Is.SameAs(reg));
                Assert.That(registry.TryGetFrameByFrameId("base", out FrameRegistration? byFrameId), Is.True);
                Assert.That(byFrameId, Is.SameAs(reg));
                Assert.That(registry.TryFindFrameByFrameId("base"), Is.SameAs(reg));
            });
        }

        [Test]
        public void TryGetFrameForUnknownFrameIdReturnsFalseWithoutThrowing()
        {
            var registry = new VisionRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetFrameByFrameId("nope", out FrameRegistration? reg), Is.False);
                Assert.That(reg, Is.Null);
                Assert.That(registry.TryFindFrameByFrameId("nope"), Is.Null);
            });
        }

        [Test]
        public void TryGetFrameHandlesNullBrowseNameAsEmptyStringLookup()
        {
            var registry = new VisionRegistry();

            Assert.That(registry.TryGetFrame(null!, out FrameRegistration? reg), Is.False);
            Assert.That(reg, Is.Null);
        }

        [Test]
        public void ToFrameSnapshotsKeysByFrameIdAndCarriesTransform()
        {
            var registry = new VisionRegistry();
            registry.AddFrame(NewFrame("baseFrame", 301, frameId: "base", parentFrameId: null));
            registry.AddFrame(NewFrame("tcpFrame", 302, frameId: "tcp", parentFrameId: "base"));

            IReadOnlyDictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot> snapshots
                = registry.ToFrameSnapshots();

            Assert.Multiple(() =>
            {
                Assert.That(snapshots, Has.Count.EqualTo(2));
                Assert.That(snapshots.ContainsKey("base"), Is.True);
                Assert.That(snapshots.ContainsKey("tcp"), Is.True);
                Assert.That(snapshots["tcp"].ParentFrameId, Is.EqualTo("base"));
                Assert.That(snapshots["base"].ParentFrameId, Is.Empty,
                    "Frame with a null parent must surface as an empty ParentFrameId, not null.");
            });
        }

        [Test]
        public void SensorAndPipelineRegistrationsAreEnumerableViaTheReadOnlyDictionaries()
        {
            var registry = new VisionRegistry();
            registry.AddSensor(NewSensor("cam1", 101));
            registry.AddSensor(NewSensor("cam2", 102));
            registry.AddPipeline(NewPipeline("pipe1", 201));

            Assert.Multiple(() =>
            {
                Assert.That(registry.Sensors, Has.Count.EqualTo(2));
                Assert.That(registry.SensorsByNodeId, Has.Count.EqualTo(2));
                Assert.That(registry.Pipelines, Has.Count.EqualTo(1));
                Assert.That(registry.PipelinesByNodeId, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void ResolveDeferredExtrinsicsIsSafeToCallWithNothingDeferred()
        {
            var registry = new VisionRegistry();

            Assert.DoesNotThrow(() => registry.ResolveDeferredExtrinsics());
        }

        [Test]
        public void AddDeferredExtrinsicResolutionAcceptsNullCalibrationWithoutThrowing()
        {
            var registry = new VisionRegistry();

            Assert.DoesNotThrow(
                () => registry.AddDeferredExtrinsicResolution(null!, "sourceFrame", "targetFrame"));
        }

        private static SensorRegistration NewSensor(
            string browseName, uint id, HashSet<string>? facets = null)
        {
            var sensor = new VisionSensorState(null);
            return new SensorRegistration(
                browseName,
                new NodeId(id, 4),
                sensor,
                VisionSensorModalityEnum.Area2D,
                VisionRealityKindEnum.Physical,
                facets ?? new HashSet<string>(System.StringComparer.Ordinal),
                mediaProvider: null);
        }

        private static PipelineRegistration NewPipeline(
            string browseName, uint id, HashSet<string>? facets = null)
        {
            var pipeline = new InferencePipelineState(null);
            return new PipelineRegistration(
                browseName,
                new NodeId(id, 4),
                pipeline,
                facets ?? new HashSet<string>(System.StringComparer.Ordinal));
        }

        private static FrameRegistration NewFrame(
            string browseName, uint id, string frameId, string? parentFrameId)
        {
            var frame = new CoordinateFrameState(null);
            var transform = new VisionPose3DDataType
            {
                FrameId = frameId,
                Position = new double[] { 0, 0, 0 },
                Orientation = new double[] { 0, 0, 0, 1 },
                Covariance = System.Array.Empty<double>()
            };
            return new FrameRegistration(
                browseName,
                new NodeId(id, 4),
                frameId,
                VisionFrameRoleEnum.Base,
                parentFrameId,
                transform,
                frame);
        }
    }
}
