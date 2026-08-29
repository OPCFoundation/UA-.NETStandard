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
using Opc.Ua.Vision;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Tracks the sensors, pipelines, coordinate frames, feedback objects
    /// and provider bindings materialised inside a single Vision node
    /// manager.
    /// </summary>
    /// <remarks>
    /// The registry is the single source of truth for facet computation
    /// (<see cref="VisionFacetCalculator"/>), coordinate-frame math and
    /// method-dispatch to the injected providers.
    /// </remarks>
    internal sealed class VisionRegistry
    {
        public IReadOnlyDictionary<string, SensorRegistration> Sensors => m_sensorsByBrowseName;

        public IReadOnlyDictionary<NodeId, SensorRegistration> SensorsByNodeId => m_sensorsByNodeId;

        public IReadOnlyDictionary<string, PipelineRegistration> Pipelines => m_pipelinesByBrowseName;

        public IReadOnlyDictionary<NodeId, PipelineRegistration> PipelinesByNodeId => m_pipelinesByNodeId;

        public IReadOnlyDictionary<string, FrameRegistration> Frames => m_framesByBrowseName;

        public IReadOnlyDictionary<string, FrameRegistration> FramesByFrameId => m_framesByFrameId;

        public bool AnySensorHasFacet(string facetName)
        {
            foreach (KeyValuePair<string, SensorRegistration> pair in m_sensorsByBrowseName)
            {
                if (pair.Value.Facets.Contains(facetName))
                {
                    return true;
                }
            }
            return false;
        }

        public bool AnyPipelineHasFacet(string facetName)
        {
            foreach (KeyValuePair<string, PipelineRegistration> pair in m_pipelinesByBrowseName)
            {
                if (pair.Value.Facets.Contains(facetName))
                {
                    return true;
                }
            }
            return false;
        }

        public void AddSensor(SensorRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            m_sensorsByBrowseName[registration.BrowseName] = registration;
            m_sensorsByNodeId[registration.NodeId] = registration;
        }

        public bool TryGetSensor(string browseName, out SensorRegistration? registration)
        {
            return m_sensorsByBrowseName.TryGetValue(browseName ?? string.Empty, out registration);
        }

        public bool TryGetSensor(NodeId nodeId, out SensorRegistration? registration)
        {
            if (nodeId.IsNull)
            {
                registration = null;
                return false;
            }
            return m_sensorsByNodeId.TryGetValue(nodeId, out registration);
        }

        public void AddPipeline(PipelineRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            m_pipelinesByBrowseName[registration.BrowseName] = registration;
            m_pipelinesByNodeId[registration.NodeId] = registration;
        }

        public bool TryGetPipeline(string browseName, out PipelineRegistration? registration)
        {
            return m_pipelinesByBrowseName.TryGetValue(browseName ?? string.Empty, out registration);
        }

        public bool TryGetPipeline(NodeId nodeId, out PipelineRegistration? registration)
        {
            if (nodeId.IsNull)
            {
                registration = null;
                return false;
            }
            return m_pipelinesByNodeId.TryGetValue(nodeId, out registration);
        }

        public void AddFrame(FrameRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            m_framesByBrowseName[registration.BrowseName] = registration;
            m_framesByFrameId[registration.FrameId] = registration;
        }

        public bool TryGetFrame(string browseName, out FrameRegistration? registration)
        {
            return m_framesByBrowseName.TryGetValue(browseName ?? string.Empty, out registration);
        }

        public bool TryGetFrameByFrameId(string frameId, out FrameRegistration? registration)
        {
            return m_framesByFrameId.TryGetValue(frameId ?? string.Empty, out registration);
        }

        public FrameRegistration? TryFindFrameByFrameId(string frameId)
        {
            return m_framesByFrameId.TryGetValue(frameId ?? string.Empty, out FrameRegistration? registration)
                ? registration
                : null;
        }

        public void AddDeferredExtrinsicResolution(
            ExtrinsicCalibrationState calibration,
            string sourceFrameId,
            string targetFrameId)
        {
            if (calibration == null)
            {
                return;
            }
            m_deferredExtrinsicResolutions.Add(new DeferredExtrinsic(calibration, sourceFrameId ?? string.Empty, targetFrameId ?? string.Empty));
        }

        public void ResolveDeferredExtrinsics()
        {
            foreach (DeferredExtrinsic deferred in m_deferredExtrinsicResolutions)
            {
                if (deferred.Calibration.SourceFrame != null &&
                    m_framesByFrameId.TryGetValue(deferred.SourceFrameId, out FrameRegistration? source))
                {
                    deferred.Calibration.SourceFrame.Value = source!.NodeId;
                }
                if (deferred.Calibration.TargetFrame != null &&
                    m_framesByFrameId.TryGetValue(deferred.TargetFrameId, out FrameRegistration? target))
                {
                    deferred.Calibration.TargetFrame.Value = target!.NodeId;
                }
            }
        }

        public IReadOnlyDictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot> ToFrameSnapshots()
        {
            var snapshots = new Dictionary<string, VisionCoordinateFrameMath.CoordinateFrameSnapshot>(
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, FrameRegistration> pair in m_framesByFrameId)
            {
                snapshots.Add(pair.Key, new VisionCoordinateFrameMath.CoordinateFrameSnapshot(
                    pair.Value.FrameId,
                    pair.Value.Role,
                    pair.Value.ParentFrameId ?? string.Empty,
                    pair.Value.Transform));
            }
            return snapshots;
        }

        private readonly Dictionary<string, SensorRegistration> m_sensorsByBrowseName = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeId, SensorRegistration> m_sensorsByNodeId = new();
        private readonly Dictionary<string, PipelineRegistration> m_pipelinesByBrowseName = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeId, PipelineRegistration> m_pipelinesByNodeId = new();
        private readonly Dictionary<string, FrameRegistration> m_framesByBrowseName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FrameRegistration> m_framesByFrameId = new(StringComparer.Ordinal);
        private readonly List<DeferredExtrinsic> m_deferredExtrinsicResolutions = [];

        private readonly struct DeferredExtrinsic
        {
            public DeferredExtrinsic(ExtrinsicCalibrationState calibration, string sourceFrameId, string targetFrameId)
            {
                Calibration = calibration;
                SourceFrameId = sourceFrameId;
                TargetFrameId = targetFrameId;
            }

            public ExtrinsicCalibrationState Calibration { get; }

            public string SourceFrameId { get; }

            public string TargetFrameId { get; }
        }
    }

    /// <summary>
    /// Metadata recorded per sensor.
    /// </summary>
    internal sealed class SensorRegistration
    {
        public SensorRegistration(
            string browseName,
            NodeId nodeId,
            VisionSensorState sensor,
            VisionSensorModalityEnum modality,
            VisionRealityKindEnum realityKind,
            HashSet<string> facets,
            IVisionMediaProvider? mediaProvider)
        {
            BrowseName = browseName;
            NodeId = nodeId;
            Sensor = sensor;
            Modality = modality;
            RealityKind = realityKind;
            Facets = facets;
            MediaProvider = mediaProvider;
        }

        public string BrowseName { get; }

        public NodeId NodeId { get; }

        public VisionSensorState Sensor { get; }

        public VisionSensorModalityEnum Modality { get; }

        public VisionRealityKindEnum RealityKind { get; set; }

        public HashSet<string> Facets { get; }

        public IVisionMediaProvider? MediaProvider { get; set; }

        public List<StreamEndpointState> StreamEndpoints { get; } = [];

        public List<ClipEndpointState> ClipEndpoints { get; } = [];

        public bool HasIntrinsicCalibration { get; set; }

        public bool HasExtrinsicCalibration { get; set; }

        public bool HasOptics { get; set; }

        public bool HasIllumination { get; set; }
    }

    /// <summary>
    /// Metadata recorded per pipeline.
    /// </summary>
    internal sealed class PipelineRegistration
    {
        public PipelineRegistration(
            string browseName,
            NodeId nodeId,
            InferencePipelineState pipeline,
            HashSet<string> facets)
        {
            BrowseName = browseName;
            NodeId = nodeId;
            Pipeline = pipeline;
            Facets = facets;
        }

        public string BrowseName { get; }

        public NodeId NodeId { get; }

        public InferencePipelineState Pipeline { get; }

        public HashSet<string> Facets { get; }

        public IVisionInferenceProvider? InferenceProvider { get; set; }

        public IVisionFeedbackSink? FeedbackSink { get; set; }
    }

    /// <summary>
    /// Metadata recorded per coordinate frame.
    /// </summary>
    internal sealed class FrameRegistration
    {
        public FrameRegistration(
            string browseName,
            NodeId nodeId,
            string frameId,
            VisionFrameRoleEnum role,
            string? parentFrameId,
            VisionPose3DDataType transform,
            CoordinateFrameState frame)
        {
            BrowseName = browseName;
            NodeId = nodeId;
            FrameId = frameId;
            Role = role;
            ParentFrameId = parentFrameId;
            Transform = transform;
            Frame = frame;
        }

        public string BrowseName { get; }

        public NodeId NodeId { get; }

        public string FrameId { get; }

        public VisionFrameRoleEnum Role { get; }

        public string? ParentFrameId { get; }

        public VisionPose3DDataType Transform { get; }

        public CoordinateFrameState Frame { get; }
    }
}
