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

namespace Opc.Ua.Vision.Server.Builders
{
    internal sealed class VisionNodeBuilder : IVisionNodeBuilder
    {
        public VisionNodeBuilder(
            VisionBuildContext context,
            VisionRegistry registry,
            VisionMethodDispatcher dispatcher)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public IVisionNodeBuilder AddImageSensor(
            string browseName,
            Action<IVisionImageSensorBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FolderState sensorsFolder = EnsureSensorsFolder();
            var qualifiedName = new QualifiedName(browseName, m_context.InstanceNamespaceIndex);
            ImageSensorState sensor = m_context.Context.CreateInstanceOfImageSensorType(
                sensorsFolder,
                qualifiedName);
            sensor.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionImageSensorBuilder(m_context, m_registry, m_dispatcher, sensor, browseName);
            configure(builder);
            builder.Finalize(sensorsFolder);
            m_context.EnqueueForRegistration(sensor);
            return this;
        }

        public IVisionNodeBuilder AddDepth3DSensor(
            string browseName,
            Action<IVisionDepth3DSensorBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FolderState sensorsFolder = EnsureSensorsFolder();
            var qualifiedName = new QualifiedName(browseName, m_context.InstanceNamespaceIndex);
            Depth3DSensorState sensor = m_context.Context.CreateInstanceOfDepth3DSensorType(
                sensorsFolder,
                qualifiedName);
            sensor.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionDepth3DSensorBuilder(m_context, m_registry, m_dispatcher, sensor, browseName);
            configure(builder);
            builder.Finalize(sensorsFolder);
            m_context.EnqueueForRegistration(sensor);
            return this;
        }

        public IVisionNodeBuilder AddSensor(
            string browseName,
            Action<IVisionSensorBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FolderState sensorsFolder = EnsureSensorsFolder();
            var qualifiedName = new QualifiedName(browseName, m_context.InstanceNamespaceIndex);
            VisionSensorState sensor = m_context.Context.CreateInstanceOfVisionSensorType(
                sensorsFolder,
                qualifiedName);
            sensor.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionGenericSensorBuilder(m_context, m_registry, m_dispatcher, sensor, browseName);
            configure(builder);
            builder.Finalize(sensorsFolder);
            m_context.EnqueueForRegistration(sensor);
            return this;
        }

        public IVisionNodeBuilder AddFrame(
            string browseName,
            Action<IVisionFrameBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FolderState framesFolder = EnsureFramesFolder();
            var qualifiedName = new QualifiedName(browseName, m_context.InstanceNamespaceIndex);
            CoordinateFrameState frame = m_context.Context.CreateInstanceOfCoordinateFrameType(
                framesFolder,
                qualifiedName);
            frame.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionFrameBuilder(m_context, m_registry, frame, browseName);
            configure(builder);
            builder.Finalize(framesFolder);
            m_context.EnqueueForRegistration(frame);
            return this;
        }

        public IVisionNodeBuilder AddPipeline(
            string browseName,
            Action<IVisionPipelineBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FolderState pipelinesFolder = EnsurePipelinesFolder();
            var qualifiedName = new QualifiedName(browseName, m_context.InstanceNamespaceIndex);
            InferencePipelineState pipeline = m_context.Context.CreateInstanceOfInferencePipelineType(
                pipelinesFolder,
                qualifiedName);
            pipeline.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionPipelineBuilder(m_context, m_registry, m_dispatcher, pipeline, browseName);
            configure(builder);
            builder.Finalize(pipelinesFolder);
            m_context.EnqueueForRegistration(pipeline);
            return this;
        }

        private FolderState EnsureSensorsFolder()
        {
            m_context.Root.CreateOrReplaceSensors(m_context.Context, null);
            return m_context.Root.Sensors!;
        }

        private FolderState EnsurePipelinesFolder()
        {
            m_context.Root.CreateOrReplacePipelines(m_context.Context, null);
            FolderState pipelines = m_context.Root.Pipelines!;
            m_context.EnqueueForRegistration(pipelines);
            return pipelines;
        }

        private FolderState EnsureFramesFolder()
        {
            m_context.Root.CreateOrReplaceFrames(m_context.Context, null);
            FolderState frames = m_context.Root.Frames!;
            m_context.EnqueueForRegistration(frames);
            return frames;
        }

        private readonly VisionBuildContext m_context;
        private readonly VisionRegistry m_registry;
        private readonly VisionMethodDispatcher m_dispatcher;
    }

    internal sealed class VisionFrameBuilder : IVisionFrameBuilder
    {
        public VisionFrameBuilder(
            VisionBuildContext context,
            VisionRegistry registry,
            CoordinateFrameState frame,
            string browseName)
        {
            m_context = context;
            m_registry = registry;
            m_frame = frame;
            m_browseName = browseName;
        }

        public IVisionFrameBuilder WithFrameId(string frameId)
        {
            m_frameId = frameId ?? string.Empty;
            m_frame.CreateOrReplaceFrameId(m_context.Context, null);
            m_frame.FrameId!.Value = m_frameId;
            return this;
        }

        public IVisionFrameBuilder WithRole(VisionFrameRoleEnum role)
        {
            m_role = role;
            m_frame.CreateOrReplaceRole(m_context.Context, null);
            m_frame.Role!.Value = role;
            return this;
        }

        public IVisionFrameBuilder WithParent(string parentFrameId)
        {
            m_parentFrameId = parentFrameId ?? string.Empty;
            m_parentNodeId = NodeId.Null;
            return this;
        }

        public IVisionFrameBuilder WithParent(NodeId parentNodeId)
        {
            m_parentNodeId = parentNodeId;
            m_parentFrameId = string.Empty;
            if (!parentNodeId.IsNull)
            {
                m_frame.CreateOrReplaceParentFrame(m_context.Context, null);
                m_frame.ParentFrame!.Value = parentNodeId;
            }
            return this;
        }

        public IVisionFrameBuilder WithTransform(VisionPose3DDataType transform)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }
            m_frame.CreateOrReplaceTransform(m_context.Context, null);
            var pose = new VisionPose3DDataType
            {
                FrameId = string.IsNullOrEmpty(transform.FrameId) ? m_parentFrameId : transform.FrameId,
                Position = transform.Position,
                Orientation = transform.Orientation,
                Covariance = transform.Covariance
            };
            m_frame.Transform!.Value = pose;
            m_transform = pose;
            return this;
        }

        internal void Finalize(FolderState parent)
        {
            if (string.IsNullOrEmpty(m_frameId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Coordinate frame '{0}' must declare a non-empty FrameId (§5.12).",
                    m_browseName);
            }
            NodeId parentNodeId = m_parentNodeId;
            if (parentNodeId.IsNull && !string.IsNullOrEmpty(m_parentFrameId))
            {
                FrameRegistration? existing = m_registry.TryFindFrameByFrameId(m_parentFrameId);
                if (existing != null)
                {
                    parentNodeId = existing.NodeId;
                }
            }
            if (!parentNodeId.IsNull)
            {
                m_frame.CreateOrReplaceParentFrame(m_context.Context, null);
                m_frame.ParentFrame!.Value = parentNodeId;
            }
            parent.AddChild(m_frame);
            var registration = new FrameRegistration(
                m_browseName,
                m_frame.NodeId,
                m_frameId,
                m_role,
                string.IsNullOrEmpty(m_parentFrameId) ? null : m_parentFrameId,
                m_transform ?? VisionCoordinateFrameMath.Identity(m_parentFrameId ?? string.Empty),
                m_frame);
            m_registry.AddFrame(registration);
        }

        private readonly VisionBuildContext m_context;
        private readonly VisionRegistry m_registry;
        private readonly CoordinateFrameState m_frame;
        private readonly string m_browseName;
        private string m_frameId = string.Empty;
        private VisionFrameRoleEnum m_role = VisionFrameRoleEnum.Other;
        private string m_parentFrameId = string.Empty;
        private NodeId m_parentNodeId = NodeId.Null;
        private VisionPose3DDataType? m_transform;
    }

    internal abstract class VisionSensorBuilderBase<TSelf, TSensor> : IVisionSensorBuilder<TSelf>
        where TSelf : IVisionSensorBuilder<TSelf>
        where TSensor : VisionSensorState
    {
        protected VisionSensorBuilderBase(
            VisionBuildContext context,
            VisionRegistry registry,
            VisionMethodDispatcher dispatcher,
            TSensor sensor,
            string browseName)
        {
            BuildContext = context;
            Registry = registry;
            m_dispatcher = dispatcher;
            Sensor = sensor;
            m_browseName = browseName;
        }

        protected TSensor Sensor { get; }

        protected VisionBuildContext BuildContext { get; }

        protected VisionRegistry Registry { get; }

        protected abstract TSelf Self { get; }

        public TSelf WithSensorId(string sensorId)
        {
            Sensor.CreateOrReplaceSensorId(BuildContext.Context, null);
            Sensor.SensorId!.Value = sensorId ?? string.Empty;
            return Self;
        }

        public TSelf WithRealityKind(VisionRealityKindEnum realityKind)
        {
            m_realityKind = realityKind;
            Sensor.CreateOrReplaceRealityKind(BuildContext.Context, null);
            Sensor.RealityKind!.Value = realityKind;
            return Self;
        }

        public TSelf WithModality(VisionSensorModalityEnum modality)
        {
            m_modality = modality;
            Sensor.CreateOrReplaceModality(BuildContext.Context, null);
            Sensor.Modality!.Value = modality;
            return Self;
        }

        public TSelf WithManufacturer(string manufacturer)
        {
            Sensor.CreateOrReplaceManufacturer(BuildContext.Context, null);
            Sensor.Manufacturer!.Value = new LocalizedText(manufacturer ?? string.Empty);
            m_hasSensorParams = true;
            return Self;
        }

        public TSelf WithModel(string model)
        {
            Sensor.CreateOrReplaceModel(BuildContext.Context, null);
            Sensor.Model!.Value = new LocalizedText(model ?? string.Empty);
            m_hasSensorParams = true;
            return Self;
        }

        public TSelf WithSerialNumber(string serialNumber)
        {
            Sensor.CreateOrReplaceSerialNumber(BuildContext.Context, null);
            Sensor.SerialNumber!.Value = serialNumber ?? string.Empty;
            m_hasSensorParams = true;
            return Self;
        }

        public TSelf WithDeviceUri(string deviceUri)
        {
            Sensor.CreateOrReplaceDeviceUri(BuildContext.Context, null);
            Sensor.DeviceUri!.Value = deviceUri ?? string.Empty;
            return Self;
        }

        public TSelf WithFrameId(string frameId)
        {
            m_frameId = frameId ?? string.Empty;
            Sensor.CreateOrReplaceFrameId(BuildContext.Context, null);
            Sensor.FrameId!.Value = m_frameId;
            return Self;
        }

        public TSelf HasScenePrim(NodeId scenePrimNodeId)
        {
            if (!scenePrimNodeId.IsNull)
            {
                Sensor.AddReference(VisionReferenceTypeIds(BuildContext, "HasScenePrim"), false, scenePrimNodeId);
                m_hasScenePrim = true;
            }
            return Self;
        }

        public TSelf MountedOn(NodeId mountNodeId)
        {
            if (!mountNodeId.IsNull)
            {
                Sensor.AddReference(VisionReferenceTypeIds(BuildContext, "MountedOn"), false, mountNodeId);
            }
            return Self;
        }

        public TSelf WithOptics(Action<IVisionOpticsBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Sensor.CreateOrReplaceOptics(BuildContext.Context, null);
            var opticsBuilder = new VisionOpticsBuilder(BuildContext, Sensor.Optics!);
            configure(opticsBuilder);
            m_hasOptics = true;
            return Self;
        }

        public TSelf WithIllumination(Action<IVisionIlluminationBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Sensor.CreateOrReplaceIllumination(BuildContext.Context, null);
            var illuminationBuilder = new VisionIlluminationBuilder(BuildContext, Sensor.Illumination!);
            configure(illuminationBuilder);
            m_hasIllumination = true;
            return Self;
        }

        public TSelf AddIntrinsicCalibration(
            string browseName,
            Action<IVisionIntrinsicCalibrationBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FolderState calibrations = EnsureCalibrationsFolder();
            var qualifiedName = new QualifiedName(browseName, BuildContext.InstanceNamespaceIndex);
            IntrinsicCalibrationState calibration = BuildContext.Context
                .CreateInstanceOfIntrinsicCalibrationType(calibrations, qualifiedName);
            calibration.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionIntrinsicCalibrationBuilder(BuildContext, calibration);
            configure(builder);
            calibrations.AddChild(calibration);
            Sensor.AddReference(VisionReferenceTypeIds(BuildContext, "HasCalibration"), false, calibration.NodeId);
            m_hasCalibration = true;
            return Self;
        }

        public TSelf AddExtrinsicCalibration(
            string browseName,
            Action<IVisionExtrinsicCalibrationBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FolderState calibrations = EnsureCalibrationsFolder();
            var qualifiedName = new QualifiedName(browseName, BuildContext.InstanceNamespaceIndex);
            ExtrinsicCalibrationState calibration = BuildContext.Context
                .CreateInstanceOfExtrinsicCalibrationType(calibrations, qualifiedName);
            calibration.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionExtrinsicCalibrationBuilder(BuildContext, calibration);
            configure(builder);
            calibrations.AddChild(calibration);
            Sensor.AddReference(VisionReferenceTypeIds(BuildContext, "HasCalibration"), false, calibration.NodeId);
            m_hasCalibration = true;
            m_hasExtrinsicCalibration = true;
            return Self;
        }

        public TSelf AddStreamEndpoint(
            string browseName,
            Action<IVisionStreamEndpointBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            VisionMediaManagementState media = EnsureMedia();
            media.CreateOrReplaceStreamEndpoints(BuildContext.Context, null);
            FolderState endpoints = media.StreamEndpoints!;
            var qualifiedName = new QualifiedName(browseName, BuildContext.InstanceNamespaceIndex);
            StreamEndpointState endpoint = BuildContext.Context.CreateInstanceOfStreamEndpointType(
                endpoints,
                qualifiedName);
            endpoint.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionStreamEndpointBuilder(BuildContext, endpoint);
            configure(builder);
            endpoints.AddChild(endpoint);
            m_streamEndpoints.Add(endpoint);
            return Self;
        }

        public TSelf AddClipEndpoint(
            string browseName,
            Action<IVisionClipEndpointBuilder> configure)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(browseName));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            VisionMediaManagementState media = EnsureMedia();
            media.CreateOrReplaceClipEndpoints(BuildContext.Context, null);
            FolderState endpoints = media.ClipEndpoints!;
            var qualifiedName = new QualifiedName(browseName, BuildContext.InstanceNamespaceIndex);
            ClipEndpointState endpoint = BuildContext.Context.CreateInstanceOfClipEndpointType(
                endpoints,
                qualifiedName);
            endpoint.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            var builder = new VisionClipEndpointBuilder(BuildContext, endpoint);
            configure(builder);
            endpoints.AddChild(endpoint);
            m_clipEndpoints.Add(endpoint);
            return Self;
        }

        public TSelf UseMediaProvider(IVisionMediaProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_mediaProvider = provider;
            EnsureMedia();
            return Self;
        }

        internal void Finalize(FolderState parent)
        {
            parent.AddChild(Sensor);
            HashSet<string> facets = ComputeFacets();
            var registration = new SensorRegistration(
                m_browseName,
                Sensor.NodeId,
                Sensor,
                m_modality,
                m_realityKind,
                facets,
                m_mediaProvider)
            {
                HasIntrinsicCalibration = m_hasCalibration,
                HasExtrinsicCalibration = m_hasExtrinsicCalibration,
                HasOptics = m_hasOptics,
                HasIllumination = m_hasIllumination
            };
            for (int ii = 0; ii < m_streamEndpoints.Count; ii++)
            {
                registration.StreamEndpoints.Add(m_streamEndpoints[ii]);
            }
            for (int ii = 0; ii < m_clipEndpoints.Count; ii++)
            {
                registration.ClipEndpoints.Add(m_clipEndpoints[ii]);
            }
            OnFinalize(registration);
            Registry.AddSensor(registration);
            if (!string.IsNullOrEmpty(m_frameId) &&
                Registry.TryGetFrameByFrameId(m_frameId, out FrameRegistration? mountFrame) &&
                mountFrame != null)
            {
                Sensor.AddReference(VisionReferenceTypeIds(BuildContext, "MountedOn"), false, mountFrame.NodeId);
            }
            if (Sensor.Media is VisionMediaManagementState media)
            {
                EnsureMediaMethods(media);
                m_dispatcher.AttachMediaMethods(Sensor.NodeId, media);
            }
        }

        private void EnsureMediaMethods(VisionMediaManagementState media)
        {
            if (m_mediaProvider == null)
            {
                return;
            }
            ISystemContext context = BuildContext.Context;
            DeclareMedia(
                media.CreateOrReplaceGetStreamEndpoint(context, null),
                MethodIds.VisionMediaManagementType_GetStreamEndpoint,
                VisionMethodArguments.Declare);
            DeclareMedia(
                media.CreateOrReplaceReleaseStreamEndpoint(context, null),
                MethodIds.VisionMediaManagementType_ReleaseStreamEndpoint,
                VisionMethodArguments.Declare);
            DeclareMedia(
                media.CreateOrReplaceConfigureStreamEndpoint(context, null),
                MethodIds.VisionMediaManagementType_ConfigureStreamEndpoint,
                VisionMethodArguments.Declare);
            DeclareMedia(
                media.CreateOrReplaceSelectEndpoint(context, null),
                MethodIds.VisionMediaManagementType_SelectEndpoint,
                VisionMethodArguments.Declare);
            DeclareMedia(
                media.CreateOrReplaceGetClip(context, null),
                MethodIds.VisionMediaManagementType_GetClip,
                VisionMethodArguments.Declare);
        }

        private void DeclareMedia<TMethod>(
            TMethod method,
            ExpandedNodeId declarationId,
            Action<ISystemContext, TMethod> declare)
            where TMethod : MethodState
        {
            declare(BuildContext.Context, method);
            method.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            method.MethodDeclarationId = ExpandedNodeId.ToNodeId(
                declarationId, BuildContext.Context.NamespaceUris);
        }

        protected virtual void OnFinalize(SensorRegistration registration)
        {
        }

        protected VisionMediaManagementState EnsureMedia()
        {
            Sensor.CreateOrReplaceMedia(BuildContext.Context, null);
            Sensor.Media!.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            return Sensor.Media!;
        }

        private FolderState EnsureCalibrationsFolder()
        {
            Sensor.CreateOrReplaceCalibrations(BuildContext.Context, null);
            return Sensor.Calibrations!;
        }

        private HashSet<string> ComputeFacets()
        {
            var facets = new HashSet<string>(StringComparer.Ordinal)
            {
                VisionConformanceUris.FacetNames.Base
            };
            if (m_hasSensorParams)
            {
                facets.Add(VisionConformanceUris.FacetNames.SensorParams);
            }
            if (m_hasOptics)
            {
                facets.Add(VisionConformanceUris.FacetNames.Optics);
            }
            if (m_hasCalibration || m_hasExtrinsicCalibration)
            {
                facets.Add(VisionConformanceUris.FacetNames.Calibration);
            }
            for (int ii = 0; ii < m_streamEndpoints.Count; ii++)
            {
                if (m_streamEndpoints[ii].StreamProtocol?.Value == VisionStreamProtocolEnum.Rtsp)
                {
                    facets.Add(VisionConformanceUris.FacetNames.MediaRtsp);
                }
                facets.Add(VisionConformanceUris.FacetNames.EndpointConfig);
            }
            for (int ii = 0; ii < m_clipEndpoints.Count; ii++)
            {
                if (m_clipEndpoints[ii].ClipFormat?.Value == VisionClipFormatEnum.Jpeg)
                {
                    facets.Add(VisionConformanceUris.FacetNames.MediaJpeg);
                }
                if (m_clipEndpoints[ii].InlineDeliveryEnabled?.Value == true)
                {
                    facets.Add(VisionConformanceUris.FacetNames.MediaInline);
                }
            }
            if (m_realityKind == VisionRealityKindEnum.Simulated ||
                m_realityKind == VisionRealityKindEnum.Hybrid)
            {
                facets.Add(VisionConformanceUris.FacetNames.Simulation);
            }
            if (m_hasScenePrim)
            {
                facets.Add(VisionConformanceUris.FacetNames.InteropScene);
            }
            return facets;
        }

        private NodeId VisionReferenceTypeIds(VisionBuildContext context, string referenceName)
        {
            ExpandedNodeId expanded = referenceName switch
            {
                "HasCalibration" => Opc.Ua.Vision.ReferenceTypeIds.HasCalibration,
                "MountedOn" => Opc.Ua.Vision.ReferenceTypeIds.MountedOn,
                "HasScenePrim" => Opc.Ua.Vision.ReferenceTypeIds.HasScenePrim,
                "ProducedBy" => Opc.Ua.Vision.ReferenceTypeIds.ProducedBy,
                _ => throw new ArgumentOutOfRangeException(nameof(referenceName))
            };
            return ExpandedNodeId.ToNodeId(expanded, context.Context.NamespaceUris);
        }

        private readonly VisionMethodDispatcher m_dispatcher;
        private readonly string m_browseName;
        private readonly List<StreamEndpointState> m_streamEndpoints = [];
        private readonly List<ClipEndpointState> m_clipEndpoints = [];
        private VisionSensorModalityEnum m_modality;
        private VisionRealityKindEnum m_realityKind = VisionRealityKindEnum.Physical;
        private string m_frameId = string.Empty;
        private IVisionMediaProvider? m_mediaProvider;
        private bool m_hasSensorParams;
        private bool m_hasOptics;
        private bool m_hasIllumination;
        private bool m_hasCalibration;
        private bool m_hasExtrinsicCalibration;
        private bool m_hasScenePrim;
    }

    internal sealed class VisionGenericSensorBuilder :
        VisionSensorBuilderBase<IVisionSensorBuilder, VisionSensorState>,
        IVisionSensorBuilder
    {
        public VisionGenericSensorBuilder(
            VisionBuildContext context,
            VisionRegistry registry,
            VisionMethodDispatcher dispatcher,
            VisionSensorState sensor,
            string browseName)
            : base(context, registry, dispatcher, sensor, browseName)
        {
        }

        protected override IVisionSensorBuilder Self => this;
    }

    internal sealed class VisionImageSensorBuilder :
        VisionSensorBuilderBase<IVisionImageSensorBuilder, ImageSensorState>,
        IVisionImageSensorBuilder
    {
        public VisionImageSensorBuilder(
            VisionBuildContext context,
            VisionRegistry registry,
            VisionMethodDispatcher dispatcher,
            ImageSensorState sensor,
            string browseName)
            : base(context, registry, dispatcher, sensor, browseName)
        {
        }

        protected override IVisionImageSensorBuilder Self => this;

        public IVisionImageSensorBuilder WithResolution(uint width, uint height)
        {
            Sensor.CreateOrReplaceWidth(BuildContext.Context, null);
            Sensor.CreateOrReplaceHeight(BuildContext.Context, null);
            Sensor.Width!.Value = width;
            Sensor.Height!.Value = height;
            return this;
        }

        public IVisionImageSensorBuilder WithPixelFormat(string pixelFormat)
        {
            Sensor.CreateOrReplacePixelFormat(BuildContext.Context, null);
            Sensor.PixelFormat!.Value = pixelFormat ?? string.Empty;
            return this;
        }

        public IVisionImageSensorBuilder WithIntrinsics(VisionIntrinsicsDataType intrinsics)
        {
            if (intrinsics == null)
            {
                throw new ArgumentNullException(nameof(intrinsics));
            }
            Sensor.CreateOrReplaceIntrinsics(BuildContext.Context, null);
            Sensor.Intrinsics!.Value = intrinsics;
            m_hasIntrinsics = true;
            return this;
        }

        protected override void OnFinalize(SensorRegistration registration)
        {
            registration.HasIntrinsicCalibration = registration.HasIntrinsicCalibration || m_hasIntrinsics;
        }

        private new ImageSensorState Sensor => (ImageSensorState)base.Sensor!;

        private bool m_hasIntrinsics;
    }

    internal sealed class VisionDepth3DSensorBuilder :
        VisionSensorBuilderBase<IVisionDepth3DSensorBuilder, Depth3DSensorState>,
        IVisionDepth3DSensorBuilder
    {
        public VisionDepth3DSensorBuilder(
            VisionBuildContext context,
            VisionRegistry registry,
            VisionMethodDispatcher dispatcher,
            Depth3DSensorState sensor,
            string browseName)
            : base(context, registry, dispatcher, sensor, browseName)
        {
        }

        protected override IVisionDepth3DSensorBuilder Self => this;

        public IVisionDepth3DSensorBuilder WithDepthRange(double minMetres, double maxMetres)
        {
            Sensor.CreateOrReplaceMinDepth(BuildContext.Context, null);
            Sensor.CreateOrReplaceMaxDepth(BuildContext.Context, null);
            Sensor.MinDepth!.Value = minMetres;
            Sensor.MaxDepth!.Value = maxMetres;
            return this;
        }

        public IVisionDepth3DSensorBuilder WithDepthScale(double metresPerUnit)
        {
            Sensor.CreateOrReplaceDepthScale(BuildContext.Context, null);
            Sensor.DepthScale!.Value = metresPerUnit;
            return this;
        }

        public IVisionDepth3DSensorBuilder WithBaseline(double metres)
        {
            Sensor.CreateOrReplaceBaseline(BuildContext.Context, null);
            Sensor.Baseline!.Value = metres;
            return this;
        }

        private new Depth3DSensorState Sensor => (Depth3DSensorState)base.Sensor!;
    }

    internal sealed class VisionOpticsBuilder : IVisionOpticsBuilder
    {
        public VisionOpticsBuilder(VisionBuildContext context, OpticsState optics)
        {
            m_context = context;
            m_optics = optics;
        }

        public IVisionOpticsBuilder WithFocalLength(double metres)
        {
            m_optics.CreateOrReplaceFocalLength(m_context.Context, null);
            m_optics.FocalLength!.Value = metres;
            return this;
        }

        public IVisionOpticsBuilder WithAperture(double fNumber)
        {
            m_optics.CreateOrReplaceAperture(m_context.Context, null);
            m_optics.Aperture!.Value = fNumber;
            return this;
        }

        public IVisionOpticsBuilder WithWorkingDistance(double metres)
        {
            m_optics.CreateOrReplaceWorkingDistance(m_context.Context, null);
            m_optics.WorkingDistance!.Value = metres;
            return this;
        }

        public IVisionOpticsBuilder WithMagnification(double magnification)
        {
            m_optics.CreateOrReplaceMagnification(m_context.Context, null);
            m_optics.Magnification!.Value = magnification;
            return this;
        }

        public IVisionOpticsBuilder WithMountType(string mountType)
        {
            m_optics.CreateOrReplaceMountType(m_context.Context, null);
            m_optics.MountType!.Value = mountType ?? string.Empty;
            return this;
        }

        public IVisionOpticsBuilder WithLensType(string lensType)
        {
            m_optics.CreateOrReplaceLensType(m_context.Context, null);
            m_optics.LensType!.Value = lensType ?? string.Empty;
            return this;
        }

        private readonly VisionBuildContext m_context;
        private readonly OpticsState m_optics;
    }

    internal sealed class VisionIlluminationBuilder : IVisionIlluminationBuilder
    {
        public VisionIlluminationBuilder(VisionBuildContext context, IlluminationState illumination)
        {
            m_context = context;
            m_illumination = illumination;
        }

        public IVisionIlluminationBuilder WithLampType(VisionLampTypeEnum lampType)
        {
            m_illumination.CreateOrReplaceLampType(m_context.Context, null);
            m_illumination.LampType!.Value = lampType;
            return this;
        }

        public IVisionIlluminationBuilder WithWavelength(double nanometres)
        {
            m_illumination.CreateOrReplaceWavelength(m_context.Context, null);
            m_illumination.Wavelength!.Value = nanometres;
            return this;
        }

        public IVisionIlluminationBuilder WithRelativeIntensity(double relativeIntensity)
        {
            m_illumination.CreateOrReplaceRelativeIntensity(m_context.Context, null);
            m_illumination.RelativeIntensity!.Value = relativeIntensity;
            return this;
        }

        public IVisionIlluminationBuilder WithLightingMode(VisionLightingModeEnum lightingMode)
        {
            m_illumination.CreateOrReplaceLightingMode(m_context.Context, null);
            m_illumination.LightingMode!.Value = lightingMode;
            return this;
        }

        private readonly VisionBuildContext m_context;
        private readonly IlluminationState m_illumination;
    }

    internal sealed class VisionIntrinsicCalibrationBuilder : IVisionIntrinsicCalibrationBuilder
    {
        public VisionIntrinsicCalibrationBuilder(
            VisionBuildContext context,
            IntrinsicCalibrationState calibration)
        {
            m_context = context;
            m_calibration = calibration;
        }

        public IVisionIntrinsicCalibrationBuilder WithCalibrationId(string calibrationId)
        {
            m_calibration.CreateOrReplaceCalibrationId(m_context.Context, null);
            m_calibration.CalibrationId!.Value = calibrationId ?? string.Empty;
            return this;
        }

        public IVisionIntrinsicCalibrationBuilder WithIntrinsics(VisionIntrinsicsDataType intrinsics)
        {
            if (intrinsics == null)
            {
                throw new ArgumentNullException(nameof(intrinsics));
            }
            m_calibration.CreateOrReplaceIntrinsics(m_context.Context, null);
            m_calibration.Intrinsics!.Value = intrinsics;
            return this;
        }

        public IVisionIntrinsicCalibrationBuilder WithResidualError(double residualError)
        {
            m_calibration.CreateOrReplaceResidualError(m_context.Context, null);
            m_calibration.ResidualError!.Value = residualError;
            return this;
        }

        public IVisionIntrinsicCalibrationBuilder WithMethod(string method)
        {
            m_calibration.CreateOrReplaceMethod(m_context.Context, null);
            m_calibration.Method!.Value = method ?? string.Empty;
            return this;
        }

        private readonly VisionBuildContext m_context;
        private readonly IntrinsicCalibrationState m_calibration;
    }

    internal sealed class VisionExtrinsicCalibrationBuilder : IVisionExtrinsicCalibrationBuilder
    {
        public VisionExtrinsicCalibrationBuilder(
            VisionBuildContext context,
            ExtrinsicCalibrationState calibration)
        {
            m_context = context;
            m_calibration = calibration;
        }

        public IVisionExtrinsicCalibrationBuilder WithCalibrationId(string calibrationId)
        {
            m_calibration.CreateOrReplaceCalibrationId(m_context.Context, null);
            m_calibration.CalibrationId!.Value = calibrationId ?? string.Empty;
            return this;
        }

        public IVisionExtrinsicCalibrationBuilder WithMount(VisionCalibrationMountEnum mount)
        {
            m_calibration.CreateOrReplaceMount(m_context.Context, null);
            m_calibration.Mount!.Value = mount;
            return this;
        }

        public IVisionExtrinsicCalibrationBuilder WithFrames(string sourceFrame, string targetFrame)
        {
            m_calibration.CreateOrReplaceSourceFrame(m_context.Context, null);
            m_calibration.CreateOrReplaceTargetFrame(m_context.Context, null);
            NodeId sourceNodeId = m_context.Registry.TryFindFrameByFrameId(sourceFrame)?.NodeId ?? NodeId.Null;
            NodeId targetNodeId = m_context.Registry.TryFindFrameByFrameId(targetFrame)?.NodeId ?? NodeId.Null;
            m_calibration.SourceFrame!.Value = sourceNodeId;
            m_calibration.TargetFrame!.Value = targetNodeId;
            m_sourceFrameId = sourceFrame ?? string.Empty;
            m_targetFrame = targetFrame ?? string.Empty;
            m_context.Registry.AddDeferredExtrinsicResolution(m_calibration, m_sourceFrameId, m_targetFrame);
            return this;
        }

        public IVisionExtrinsicCalibrationBuilder WithTransform(VisionPose3DDataType transform)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }
            m_calibration.CreateOrReplaceTransform(m_context.Context, null);
            m_calibration.Transform!.Value = new VisionPose3DDataType
            {
                FrameId = string.IsNullOrEmpty(transform.FrameId) ? m_targetFrame : transform.FrameId,
                Position = transform.Position,
                Orientation = transform.Orientation,
                Covariance = transform.Covariance
            };
            return this;
        }

        public IVisionExtrinsicCalibrationBuilder WithResidualError(double residualError)
        {
            m_calibration.CreateOrReplaceResidualError(m_context.Context, null);
            m_calibration.ResidualError!.Value = residualError;
            return this;
        }

        private readonly VisionBuildContext m_context;
        private readonly ExtrinsicCalibrationState m_calibration;
        private string m_sourceFrameId = string.Empty;
        private string m_targetFrame = string.Empty;
    }

    internal sealed class VisionStreamEndpointBuilder : IVisionStreamEndpointBuilder
    {
        public VisionStreamEndpointBuilder(VisionBuildContext context, StreamEndpointState endpoint)
        {
            m_context = context;
            m_endpoint = endpoint;
        }

        public IVisionStreamEndpointBuilder WithEndpointId(string endpointId)
        {
            m_endpoint.CreateOrReplaceEndpointId(m_context.Context, null);
            m_endpoint.EndpointId!.Value = endpointId ?? string.Empty;
            return this;
        }

        public IVisionStreamEndpointBuilder WithEndpointUri(string endpointUri)
        {
            m_endpoint.CreateOrReplaceEndpointUri(m_context.Context, null);
            m_endpoint.EndpointUri!.Value = endpointUri ?? string.Empty;
            return this;
        }

        public IVisionStreamEndpointBuilder WithProtocol(VisionStreamProtocolEnum protocol)
        {
            m_endpoint.CreateOrReplaceStreamProtocol(m_context.Context, null);
            m_endpoint.StreamProtocol!.Value = protocol;
            return this;
        }

        public IVisionStreamEndpointBuilder WithCodec(VisionVideoCodecEnum codec)
        {
            m_endpoint.CreateOrReplaceCodec(m_context.Context, null);
            m_endpoint.Codec!.Value = codec;
            return this;
        }

        public IVisionStreamEndpointBuilder WithResolution(uint width, uint height)
        {
            m_endpoint.CreateOrReplaceWidth(m_context.Context, null);
            m_endpoint.CreateOrReplaceHeight(m_context.Context, null);
            m_endpoint.Width!.Value = width;
            m_endpoint.Height!.Value = height;
            return this;
        }

        public IVisionStreamEndpointBuilder WithFrameRate(double frameRate)
        {
            m_endpoint.CreateOrReplaceFrameRate(m_context.Context, null);
            m_endpoint.FrameRate!.Value = frameRate;
            return this;
        }

        public IVisionStreamEndpointBuilder WithBitrate(uint bitrate)
        {
            m_endpoint.CreateOrReplaceBitrate(m_context.Context, null);
            m_endpoint.Bitrate!.Value = bitrate;
            return this;
        }

        public IVisionStreamEndpointBuilder WithDefaultProfileName(string defaultProfileName)
        {
            m_endpoint.CreateOrReplaceDefaultProfileName(m_context.Context, null);
            m_endpoint.DefaultProfileName!.Value = defaultProfileName ?? string.Empty;
            return this;
        }

        private readonly VisionBuildContext m_context;
        private readonly StreamEndpointState m_endpoint;
    }

    internal sealed class VisionClipEndpointBuilder : IVisionClipEndpointBuilder
    {
        public VisionClipEndpointBuilder(VisionBuildContext context, ClipEndpointState endpoint)
        {
            m_context = context;
            m_endpoint = endpoint;
        }

        public IVisionClipEndpointBuilder WithEndpointId(string endpointId)
        {
            m_endpoint.CreateOrReplaceEndpointId(m_context.Context, null);
            m_endpoint.EndpointId!.Value = endpointId ?? string.Empty;
            return this;
        }

        public IVisionClipEndpointBuilder WithEndpointUri(string endpointUri)
        {
            m_endpoint.CreateOrReplaceEndpointUri(m_context.Context, null);
            m_endpoint.EndpointUri!.Value = endpointUri ?? string.Empty;
            return this;
        }

        public IVisionClipEndpointBuilder WithClipFormat(VisionClipFormatEnum format)
        {
            m_endpoint.CreateOrReplaceClipFormat(m_context.Context, null);
            m_endpoint.ClipFormat!.Value = format;
            return this;
        }

        public IVisionClipEndpointBuilder WithQuality(uint quality)
        {
            m_endpoint.CreateOrReplaceQuality(m_context.Context, null);
            m_endpoint.Quality!.Value = quality;
            return this;
        }

        public IVisionClipEndpointBuilder WithResolution(uint width, uint height)
        {
            m_endpoint.CreateOrReplaceWidth(m_context.Context, null);
            m_endpoint.CreateOrReplaceHeight(m_context.Context, null);
            m_endpoint.Width!.Value = width;
            m_endpoint.Height!.Value = height;
            return this;
        }

        public IVisionClipEndpointBuilder WithInlineDelivery(bool enabled, uint maxInlineClipSize)
        {
            m_endpoint.CreateOrReplaceInlineDeliveryEnabled(m_context.Context, null);
            m_endpoint.CreateOrReplaceMaxInlineClipSize(m_context.Context, null);
            m_endpoint.CreateOrReplaceLatestClip(m_context.Context, null);
            m_endpoint.CreateOrReplaceLatestClipMetadata(m_context.Context, null);
            m_endpoint.InlineDeliveryEnabled!.Value = enabled;
            m_endpoint.MaxInlineClipSize!.Value = maxInlineClipSize;
            if (!enabled)
            {
                m_endpoint.LatestClip!.StatusCode = StatusCodes.BadNotSupported;
            }
            else
            {
                m_endpoint.LatestClip!.StatusCode = StatusCodes.BadNoDataAvailable;
            }
            m_endpoint.LatestClipMetadata!.StatusCode = StatusCodes.BadNoDataAvailable;
            return this;
        }

        public IVisionClipEndpointBuilder WithDefaultProfileName(string defaultProfileName)
        {
            m_endpoint.CreateOrReplaceDefaultProfileName(m_context.Context, null);
            m_endpoint.DefaultProfileName!.Value = defaultProfileName ?? string.Empty;
            return this;
        }

        private readonly VisionBuildContext m_context;
        private readonly ClipEndpointState m_endpoint;
    }

    internal sealed class VisionPipelineBuilder : IVisionPipelineBuilder
    {
        public VisionPipelineBuilder(
            VisionBuildContext context,
            VisionRegistry registry,
            VisionMethodDispatcher dispatcher,
            InferencePipelineState pipeline,
            string browseName)
        {
            m_context = context;
            m_registry = registry;
            m_dispatcher = dispatcher;
            m_pipeline = pipeline;
            m_browseName = browseName;
        }

        public IVisionPipelineBuilder WithPipelineId(string pipelineId)
        {
            m_pipeline.CreateOrReplacePipelineId(m_context.Context, null);
            m_pipeline.PipelineId!.Value = pipelineId ?? string.Empty;
            return this;
        }

        public IVisionPipelineBuilder WithSensor(NodeId sensorNodeId)
        {
            m_pipeline.CreateOrReplaceSensor(m_context.Context, null);
            m_pipeline.Sensor!.Value = sensorNodeId;
            return this;
        }

        public IVisionPipelineBuilder WithDeployment(NodeId deploymentNodeId)
        {
            m_pipeline.CreateOrReplaceDeployment(m_context.Context, null);
            m_pipeline.Deployment!.Value = deploymentNodeId;
            return this;
        }

        public IVisionPipelineBuilder WithLearningJob(NodeId learningJobNodeId)
        {
            m_pipeline.CreateOrReplaceLearningJob(m_context.Context, null);
            m_pipeline.LearningJob!.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasProperty;
            m_pipeline.LearningJob.TypeDefinitionId = global::Opc.Ua.VariableTypeIds.PropertyType;
            m_pipeline.LearningJob.Value = learningJobNodeId;
            return this;
        }

        public IVisionPipelineBuilder ProducedBy(NodeId producerNodeId)
        {
            if (!producerNodeId.IsNull)
            {
                m_pipeline.AddReference(
                    ExpandedNodeId.ToNodeId(
                        Opc.Ua.Vision.ReferenceTypeIds.ProducedBy,
                        m_context.Context.NamespaceUris),
                    false,
                    producerNodeId);
            }
            return this;
        }

        public IVisionPipelineBuilder UseInferenceProvider(IVisionInferenceProvider provider, bool onServer = true)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_inferenceProvider = provider;
            m_inferenceOnServer = onServer;
            return this;
        }

        public IVisionPipelineBuilder UseFeedbackSink(IVisionFeedbackSink sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }
            m_feedbackSink = sink;
            return this;
        }

        internal void Finalize(FolderState parent)
        {
            parent.AddChild(m_pipeline);
            EnsureMethods();
            var facets = new HashSet<string>(StringComparer.Ordinal);
            if (m_inferenceProvider != null)
            {
                facets.Add(m_inferenceOnServer
                    ? VisionConformanceUris.FacetNames.InferenceOnServer
                    : VisionConformanceUris.FacetNames.InferenceOffServer);
            }
            if (m_feedbackSink != null)
            {
                facets.Add(VisionConformanceUris.FacetNames.Feedback);
            }
            var registration = new PipelineRegistration(
                m_browseName,
                m_pipeline.NodeId,
                m_pipeline,
                facets)
            {
                InferenceProvider = m_inferenceProvider,
                FeedbackSink = m_feedbackSink
            };
            m_registry.AddPipeline(registration);
            m_dispatcher.AttachPipelineMethods(m_pipeline.NodeId, m_pipeline);
            if (m_pipeline.Feedback is VisionFeedbackState feedback)
            {
                m_dispatcher.AttachFeedbackMethods(m_pipeline.NodeId, feedback);
            }
        }

        private void EnsureMethods()
        {
            ISystemContext context = m_context.Context;
            if (m_inferenceProvider != null)
            {
                FolderState results = m_pipeline.CreateOrReplaceResults(context, null);
                results.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                results.TypeDefinitionId = global::Opc.Ua.ObjectTypeIds.FolderType;
                Declare(
                    m_pipeline.CreateOrReplaceRunInference(context, null),
                    MethodIds.InferencePipelineType_RunInference,
                    VisionMethodArguments.Declare);
                Declare(
                    m_pipeline.CreateOrReplaceStartContinuous(context, null),
                    MethodIds.InferencePipelineType_StartContinuous,
                    VisionMethodArguments.DeclareStartContinuous);
                Declare(
                    m_pipeline.CreateOrReplaceStop(context, null),
                    MethodIds.InferencePipelineType_Stop,
                    VisionMethodArguments.DeclareStop);
            }
            if (m_feedbackSink != null)
            {
                m_pipeline.CreateOrReplaceFeedback(context, null);
                VisionFeedbackState feedback = m_pipeline.Feedback!;
                feedback.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                feedback.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.VisionFeedbackType, context.NamespaceUris);
                Declare(
                    feedback.CreateOrReplaceSubmitDetections(context, null),
                    MethodIds.VisionFeedbackType_SubmitDetections,
                    VisionMethodArguments.Declare);
                Declare(
                    feedback.CreateOrReplaceSubmitInspectionResult(context, null),
                    MethodIds.VisionFeedbackType_SubmitInspectionResult,
                    VisionMethodArguments.Declare);
                Declare(
                    feedback.CreateOrReplaceSubmitCorrection(context, null),
                    MethodIds.VisionFeedbackType_SubmitCorrection,
                    VisionMethodArguments.Declare);
                Declare(
                    feedback.CreateOrReplaceSubmitImageReference(context, null),
                    MethodIds.VisionFeedbackType_SubmitImageReference,
                    VisionMethodArguments.Declare);
            }
        }

        private void Declare<TMethod>(
            TMethod method,
            ExpandedNodeId declarationId,
            Action<ISystemContext, TMethod> declare)
            where TMethod : MethodState
        {
            declare(m_context.Context, method);
            method.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
            method.MethodDeclarationId = ExpandedNodeId.ToNodeId(
                declarationId, m_context.Context.NamespaceUris);
        }

        private readonly VisionBuildContext m_context;
        private readonly VisionRegistry m_registry;
        private readonly VisionMethodDispatcher m_dispatcher;
        private readonly InferencePipelineState m_pipeline;
        private readonly string m_browseName;
        private IVisionInferenceProvider? m_inferenceProvider;
        private IVisionFeedbackSink? m_feedbackSink;
        private bool m_inferenceOnServer = true;
    }
}
