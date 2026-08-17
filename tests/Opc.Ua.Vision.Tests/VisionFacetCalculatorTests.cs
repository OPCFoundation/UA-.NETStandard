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
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins facet derivation from the <see cref="VisionRegistry"/>. Robot
    /// Intent shipped 31 URIs no registered node backed — the drift is a
    /// build-time failure here because the test iterates every facet name
    /// constant via reflection and asserts publication tracks registry
    /// content exactly, so an extra name added to the URI list without a
    /// registry backing fails immediately.
    /// </summary>
    [TestFixture]
    public sealed class VisionFacetCalculatorTests
    {
        [Test]
        public void ComputeThrowsArgumentNullExceptionForNullRegistry()
        {
            Assert.That(() => VisionFacetCalculator.Compute(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ComputeReturnsEmptyWhenNoSensorOrPipelineIsRegistered()
        {
            var registry = new VisionRegistry();

            ArrayOf<string> facets = VisionFacetCalculator.Compute(registry);

            Assert.That(facets.Count, Is.EqualTo(0));
        }

        [Test]
        public void ComputeUnionsFacetsFromEverySensorAndPipeline()
        {
            var registry = new VisionRegistry();
            registry.AddSensor(NewSensor("cam-1", new NodeId(101), VisionConformanceUris.FacetNames.Base, VisionConformanceUris.FacetNames.MediaJpeg));
            registry.AddSensor(NewSensor("cam-2", new NodeId(102), VisionConformanceUris.FacetNames.SensorParams));
            registry.AddPipeline(NewPipeline("pipe-1", new NodeId(201), VisionConformanceUris.FacetNames.InferenceOnServer));

            List<string> facets = ToList(VisionFacetCalculator.Compute(registry));

            Assert.Multiple(() =>
            {
                Assert.That(facets, Contains.Item(VisionConformanceUris.FacetNames.Base));
                Assert.That(facets, Contains.Item(VisionConformanceUris.FacetNames.MediaJpeg));
                Assert.That(facets, Contains.Item(VisionConformanceUris.FacetNames.SensorParams));
                Assert.That(facets, Contains.Item(VisionConformanceUris.FacetNames.InferenceOnServer));
                Assert.That(facets.Count, Is.EqualTo(4));
            });
        }

        [Test]
        public void ComputeReturnsFacetsInOrdinalSortedOrder()
        {
            var registry = new VisionRegistry();
            registry.AddSensor(NewSensor("cam-1", new NodeId(101),
                VisionConformanceUris.FacetNames.MediaRtsp,
                VisionConformanceUris.FacetNames.Base,
                VisionConformanceUris.FacetNames.Feedback,
                VisionConformanceUris.FacetNames.MediaJpeg));

            ArrayOf<string> facets = VisionFacetCalculator.Compute(registry);

            var expected = new[]
            {
                VisionConformanceUris.FacetNames.MediaRtsp,
                VisionConformanceUris.FacetNames.Base,
                VisionConformanceUris.FacetNames.Feedback,
                VisionConformanceUris.FacetNames.MediaJpeg
            }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var actual = new List<string>();
            for (int i = 0; i < facets.Count; i++)
            {
                actual.Add(facets[i]);
            }
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ComputeDeduplicatesFacetsSharedAcrossSensors()
        {
            var registry = new VisionRegistry();
            registry.AddSensor(NewSensor("a", new NodeId(1), VisionConformanceUris.FacetNames.Base));
            registry.AddSensor(NewSensor("b", new NodeId(2), VisionConformanceUris.FacetNames.Base));

            ArrayOf<string> facets = VisionFacetCalculator.Compute(registry);

            Assert.That(facets.Count, Is.EqualTo(1));
        }

        [Test]
        public void PublishedFacetsAreExactlyThoseBackedByRegistryContent()
        {
            var registry = new VisionRegistry();
            var declaredFacetNames = new List<string>();
            foreach (FieldInfo field in typeof(VisionConformanceUris.FacetNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly))
            {
                object? raw = field.GetRawConstantValue();
                if (raw is string name)
                {
                    declaredFacetNames.Add(name);
                }
            }
            Assert.That(declaredFacetNames.Count, Is.GreaterThan(0),
                "The reflection sweep must find at least one facet-name constant.");

            for (int i = 0; i < declaredFacetNames.Count; i++)
            {
                registry.AddSensor(NewSensor("cam" + i, new NodeId((uint)(1000 + i)), declaredFacetNames[i]));
            }

            ArrayOf<string> facets = VisionFacetCalculator.Compute(registry);

            var published = new List<string>();
            for (int i = 0; i < facets.Count; i++)
            {
                published.Add(facets[i]);
            }
            var expected = declaredFacetNames.OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.That(published, Is.EqualTo(expected),
                "Every facet the specification declares must be published when a registry entry backs it; " +
                "any drift between VisionConformanceUris.FacetNames and what the calculator emits fails here.");
        }

        [Test]
        public void ComputeIgnoresFacetsThatAreNotBackedByAnyRegistryEntry()
        {
            var registry = new VisionRegistry();
            registry.AddSensor(NewSensor("cam", new NodeId(1), VisionConformanceUris.FacetNames.Base));

            List<string> facets = ToList(VisionFacetCalculator.Compute(registry));

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Not.Contain(VisionConformanceUris.FacetNames.MediaJpeg));
                Assert.That(facets, Does.Not.Contain(VisionConformanceUris.FacetNames.Feedback));
                Assert.That(facets, Does.Not.Contain(VisionConformanceUris.FacetNames.InferenceOnServer));
            });
        }

        [Test]
        public void ComputePassesThroughFacetsRegisteredThatAreNotInTheDeclaredFacetNameConstants()
        {
            const string custom = "http://example.com/vision/CustomFacet";
            var registry = new VisionRegistry();
            registry.AddSensor(NewSensor("cam", new NodeId(1), custom));

            List<string> facets = ToList(VisionFacetCalculator.Compute(registry));

            Assert.That(facets, Contains.Item(custom),
                "The calculator must be a pass-through of registered facets; a hard-coded whitelist here is the exact drift pattern Robot Intent shipped, so a silent filter must fail this test.");
        }

        [Test]
        public void ComputeProfilesPublishesBasicProfileWhenBaseJpegAndRtspAreAllPresent()
        {
            ArrayOf<string> facets = new string[]
            {
                VisionConformanceUris.FacetNames.Base,
                VisionConformanceUris.FacetNames.MediaJpeg,
                VisionConformanceUris.FacetNames.MediaRtsp
            }.ToArrayOf();

            List<string> profiles = ToList(VisionFacetCalculator.ComputeProfiles(facets));

            Assert.That(profiles, Contains.Item(VisionConformanceUris.Profiles.Basic));
        }

        [Test]
        public void ComputeProfilesDoesNotPublishBasicWhenAnyRequirementIsMissing()
        {
            ArrayOf<string> facets = new string[]
            {
                VisionConformanceUris.FacetNames.Base,
                VisionConformanceUris.FacetNames.MediaJpeg
            }.ToArrayOf();

            List<string> profiles = ToList(VisionFacetCalculator.ComputeProfiles(facets));

            Assert.That(profiles, Does.Not.Contain(VisionConformanceUris.Profiles.Basic));
        }

        [Test]
        public void ComputeProfilesPublishesInspectionProfileWhenInspectionAndFeedbackFacetsPresent()
        {
            ArrayOf<string> facets = new string[]
            {
                VisionConformanceUris.FacetNames.ResultInspection,
                VisionConformanceUris.FacetNames.Feedback
            }.ToArrayOf();

            List<string> profiles = ToList(VisionFacetCalculator.ComputeProfiles(facets));

            Assert.That(profiles, Contains.Item(VisionConformanceUris.Profiles.Inspection));
        }

        [Test]
        public void ComputeProfilesPublishesDetectionProfileWhenDetectionAndFeedbackFacetsPresent()
        {
            ArrayOf<string> facets = new string[]
            {
                VisionConformanceUris.FacetNames.ResultDetection,
                VisionConformanceUris.FacetNames.Feedback
            }.ToArrayOf();

            List<string> profiles = ToList(VisionFacetCalculator.ComputeProfiles(facets));

            Assert.That(profiles, Contains.Item(VisionConformanceUris.Profiles.Detection));
        }

        [Test]
        public void ComputeProfilesRequiresFeedbackForInspectionAndDetection()
        {
            ArrayOf<string> withoutFeedback = new string[]
            {
                VisionConformanceUris.FacetNames.ResultInspection,
                VisionConformanceUris.FacetNames.ResultDetection
            }.ToArrayOf();

            List<string> profiles = ToList(VisionFacetCalculator.ComputeProfiles(withoutFeedback));

            Assert.Multiple(() =>
            {
                Assert.That(profiles, Does.Not.Contain(VisionConformanceUris.Profiles.Inspection));
                Assert.That(profiles, Does.Not.Contain(VisionConformanceUris.Profiles.Detection));
            });
        }

        [Test]
        public void ComputeProfilesPublishesInferenceProfileWhenInferenceOnServerFacetIsPresent()
        {
            ArrayOf<string> facets = new string[]
            {
                VisionConformanceUris.FacetNames.InferenceOnServer
            }.ToArrayOf();

            List<string> profiles = ToList(VisionFacetCalculator.ComputeProfiles(facets));

            Assert.That(profiles, Contains.Item(VisionConformanceUris.Profiles.Inference));
        }

        [Test]
        public void ComputeProfilesReturnsEmptyForEmptyFacetInput()
        {
            ArrayOf<string> profiles = VisionFacetCalculator.ComputeProfiles(ArrayOf<string>.Empty);

            Assert.That(profiles.Count, Is.EqualTo(0));
        }

        [Test]
        public void ComputeProfilesIgnoresNullAndEmptyFacetStringsInInput()
        {
            ArrayOf<string> facets = new string[]
            {
                string.Empty,
                null!,
                VisionConformanceUris.FacetNames.InferenceOnServer
            }.ToArrayOf();

            List<string> profiles = ToList(VisionFacetCalculator.ComputeProfiles(facets));

            Assert.That(profiles, Contains.Item(VisionConformanceUris.Profiles.Inference));
        }

        private static List<string> ToList(ArrayOf<string> array)
        {
            var list = new List<string>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                list.Add(array[i]);
            }
            return list;
        }

        private static SensorRegistration NewSensor(string browseName, NodeId nodeId, params string[] facets)
        {
            var facetSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (string facet in facets)
            {
                facetSet.Add(facet);
            }
            return new SensorRegistration(
                browseName,
                nodeId,
                new VisionSensorState(null!),
                VisionSensorModalityEnum.Area2D,
                VisionRealityKindEnum.Physical,
                facetSet,
                mediaProvider: null);
        }

        private static PipelineRegistration NewPipeline(string browseName, NodeId nodeId, params string[] facets)
        {
            var facetSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (string facet in facets)
            {
                facetSet.Add(facet);
            }
            return new PipelineRegistration(
                browseName,
                nodeId,
                new InferencePipelineState(null!),
                facetSet);
        }
    }
}
