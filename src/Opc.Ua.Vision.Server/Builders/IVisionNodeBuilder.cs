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
using Opc.Ua.Vision;

namespace Opc.Ua.Vision.Server.Builders
{
    /// <summary>
    /// Top-level fluent Vision node builder.
    /// </summary>
    public interface IVisionNodeBuilder
    {
        /// <summary>
        /// Adds an <see cref="ImageSensorState"/> under
        /// <c>Vision/Sensors</c>.
        /// </summary>
        IVisionNodeBuilder AddImageSensor(string browseName, Action<IVisionImageSensorBuilder> configure);

        /// <summary>
        /// Adds a <see cref="Depth3DSensorState"/> under
        /// <c>Vision/Sensors</c>.
        /// </summary>
        IVisionNodeBuilder AddDepth3DSensor(string browseName, Action<IVisionDepth3DSensorBuilder> configure);

        /// <summary>
        /// Adds a generic <see cref="VisionSensorState"/> under
        /// <c>Vision/Sensors</c>. Used for modalities the specification
        /// does not model with a dedicated subtype (for example, a
        /// thermal or event camera).
        /// </summary>
        IVisionNodeBuilder AddSensor(string browseName, Action<IVisionSensorBuilder> configure);

        /// <summary>
        /// Adds a <see cref="CoordinateFrameState"/> under
        /// <c>Vision/Frames</c>.
        /// </summary>
        IVisionNodeBuilder AddFrame(string browseName, Action<IVisionFrameBuilder> configure);

        /// <summary>
        /// Adds an <see cref="InferencePipelineState"/> under
        /// <c>Vision/Pipelines</c>.
        /// </summary>
        IVisionNodeBuilder AddPipeline(string browseName, Action<IVisionPipelineBuilder> configure);
    }

    /// <summary>
    /// Configures a coordinate-frame instance.
    /// </summary>
    public interface IVisionFrameBuilder
    {
        /// <summary>
        /// Sets the required non-empty frame identifier (§5.12).
        /// </summary>
        IVisionFrameBuilder WithFrameId(string frameId);

        /// <summary>
        /// Sets the frame role.
        /// </summary>
        IVisionFrameBuilder WithRole(VisionFrameRoleEnum role);

        /// <summary>
        /// Sets the parent frame by its Vision frame identifier. When the
        /// parent has not yet been registered, the reference is deferred
        /// until build completion.
        /// </summary>
        /// <summary>
        /// Sets the parent frame's stable business identifier. The
        /// referenced frame must have been added earlier via
        /// <see cref="IVisionNodeBuilder.AddFrame"/>; unresolved names
        /// resolve to <see cref="NodeId.Null"/> at finalise time.
        /// </summary>
        IVisionFrameBuilder WithParent(string parentFrameId);

        /// <summary>
        /// Sets the parent frame's <see cref="NodeId"/> directly. Use
        /// this overload when the parent already exists in the address
        /// space and the caller has resolved its NodeId.
        /// </summary>
        IVisionFrameBuilder WithParent(NodeId parentNodeId);

        /// <summary>
        /// Sets the transform (frame in its parent). The transform's
        /// <c>FrameId</c> is set to the parent frame identifier per the
        /// §5.12 frame-precedence rule.
        /// </summary>
        IVisionFrameBuilder WithTransform(VisionPose3DDataType transform);
    }

    /// <summary>
    /// Configures a generic vision sensor instance.
    /// </summary>
    public interface IVisionSensorBuilder : IVisionSensorBuilder<IVisionSensorBuilder>;

    /// <summary>
    /// Configures an <see cref="ImageSensorState"/> instance.
    /// </summary>
    public interface IVisionImageSensorBuilder : IVisionSensorBuilder<IVisionImageSensorBuilder>
    {
        /// <summary>
        /// Sets the sensor resolution.
        /// </summary>
        IVisionImageSensorBuilder WithResolution(uint width, uint height);

        /// <summary>
        /// Sets the pixel format string per §5.5.2.
        /// </summary>
        IVisionImageSensorBuilder WithPixelFormat(string pixelFormat);

        /// <summary>
        /// Sets the sensor intrinsics.
        /// </summary>
        IVisionImageSensorBuilder WithIntrinsics(VisionIntrinsicsDataType intrinsics);
    }

    /// <summary>
    /// Configures a <see cref="Depth3DSensorState"/> instance.
    /// </summary>
    public interface IVisionDepth3DSensorBuilder : IVisionSensorBuilder<IVisionDepth3DSensorBuilder>
    {
        /// <summary>
        /// Sets the minimum and maximum depth in metres.
        /// </summary>
        IVisionDepth3DSensorBuilder WithDepthRange(double minMetres, double maxMetres);

        /// <summary>
        /// Sets the depth-value scale factor (metres per unit).
        /// </summary>
        IVisionDepth3DSensorBuilder WithDepthScale(double metresPerUnit);

        /// <summary>
        /// Sets the stereo baseline in metres.
        /// </summary>
        IVisionDepth3DSensorBuilder WithBaseline(double metres);
    }

    /// <summary>
    /// Shared surface for the strongly-typed sensor builders.
    /// </summary>
    /// <typeparam name="TSelf">
    /// The concrete builder type, so <c>With</c>-style methods
    /// return the concrete type instead of the shared interface.
    /// </typeparam>
    public interface IVisionSensorBuilder<TSelf>
        where TSelf : IVisionSensorBuilder<TSelf>
    {
        /// <summary>
        /// Sets the deployment-scoped sensor identifier.
        /// </summary>
        TSelf WithSensorId(string sensorId);

        /// <summary>
        /// Sets the sensor's reality kind (real, simulated, hybrid).
        /// </summary>
        TSelf WithRealityKind(VisionRealityKindEnum realityKind);

        /// <summary>
        /// Sets the sensor modality.
        /// </summary>
        TSelf WithModality(VisionSensorModalityEnum modality);

        /// <summary>
        /// Sets the manufacturer string.
        /// </summary>
        TSelf WithManufacturer(string manufacturer);

        /// <summary>
        /// Sets the model string.
        /// </summary>
        TSelf WithModel(string model);

        /// <summary>
        /// Sets the serial number.
        /// </summary>
        TSelf WithSerialNumber(string serialNumber);

        /// <summary>
        /// Sets the device URI.
        /// </summary>
        TSelf WithDeviceUri(string deviceUri);

        /// <summary>
        /// Sets the sensor's <c>FrameId</c>. If a frame with that id has
        /// been registered under <c>Vision/Frames</c>, the Server also
        /// adds a <c>MountedOn</c> reference to it.
        /// </summary>
        TSelf WithFrameId(string frameId);

        /// <summary>
        /// Adds a <c>HasScenePrim</c> reference to another node.
        /// </summary>
        TSelf HasScenePrim(NodeId scenePrimNodeId);

        /// <summary>
        /// Adds a <c>MountedOn</c> reference to another node — used when
        /// the mount is not a coordinate frame with a matching
        /// <c>FrameId</c>.
        /// </summary>
        TSelf MountedOn(NodeId mountNodeId);

        /// <summary>
        /// Configures the sensor's optional <c>Optics</c> child.
        /// </summary>
        TSelf WithOptics(Action<IVisionOpticsBuilder> configure);

        /// <summary>
        /// Configures the sensor's optional <c>Illumination</c> child.
        /// </summary>
        TSelf WithIllumination(Action<IVisionIlluminationBuilder> configure);

        /// <summary>
        /// Adds an intrinsic calibration under <c>Sensor/Calibrations</c>.
        /// </summary>
        TSelf AddIntrinsicCalibration(string browseName, Action<IVisionIntrinsicCalibrationBuilder> configure);

        /// <summary>
        /// Adds an extrinsic calibration under <c>Sensor/Calibrations</c>.
        /// </summary>
        TSelf AddExtrinsicCalibration(string browseName, Action<IVisionExtrinsicCalibrationBuilder> configure);

        /// <summary>
        /// Adds a stream endpoint under <c>Sensor/Media/StreamEndpoints</c>.
        /// </summary>
        TSelf AddStreamEndpoint(string browseName, Action<IVisionStreamEndpointBuilder> configure);

        /// <summary>
        /// Adds a clip endpoint under <c>Sensor/Media/ClipEndpoints</c>.
        /// </summary>
        TSelf AddClipEndpoint(string browseName, Action<IVisionClipEndpointBuilder> configure);

        /// <summary>
        /// Binds an <see cref="IVisionMediaProvider"/> to this sensor's
        /// media manager. All media methods delegate to the provider.
        /// </summary>
        TSelf UseMediaProvider(IVisionMediaProvider provider);
    }

    /// <summary>
    /// Configures the optics attached to a sensor.
    /// </summary>
    public interface IVisionOpticsBuilder
    {
        /// <summary>
        /// Sets the focal length in metres.
        /// </summary>
        IVisionOpticsBuilder WithFocalLength(double metres);

        /// <summary>
        /// Sets the aperture (f-number).
        /// </summary>
        IVisionOpticsBuilder WithAperture(double fNumber);

        /// <summary>
        /// Sets the working distance in metres.
        /// </summary>
        IVisionOpticsBuilder WithWorkingDistance(double metres);

        /// <summary>
        /// Sets the magnification.
        /// </summary>
        IVisionOpticsBuilder WithMagnification(double magnification);

        /// <summary>
        /// Sets the lens mount type.
        /// </summary>
        IVisionOpticsBuilder WithMountType(string mountType);

        /// <summary>
        /// Sets the lens type.
        /// </summary>
        IVisionOpticsBuilder WithLensType(string lensType);
    }

    /// <summary>
    /// Configures the illumination attached to a sensor.
    /// </summary>
    public interface IVisionIlluminationBuilder
    {
        /// <summary>
        /// Sets the lamp type.
        /// </summary>
        IVisionIlluminationBuilder WithLampType(VisionLampTypeEnum lampType);

        /// <summary>
        /// Sets the peak wavelength in nanometres.
        /// </summary>
        IVisionIlluminationBuilder WithWavelength(double nanometres);

        /// <summary>
        /// Sets the relative intensity (0…1).
        /// </summary>
        IVisionIlluminationBuilder WithRelativeIntensity(double relativeIntensity);

        /// <summary>
        /// Sets the lighting mode label.
        /// </summary>
        IVisionIlluminationBuilder WithLightingMode(VisionLightingModeEnum lightingMode);
    }

    /// <summary>
    /// Configures an intrinsic calibration.
    /// </summary>
    public interface IVisionIntrinsicCalibrationBuilder
    {
        /// <summary>
        /// Sets the calibration identifier.
        /// </summary>
        IVisionIntrinsicCalibrationBuilder WithCalibrationId(string calibrationId);

        /// <summary>
        /// Sets the <see cref="VisionIntrinsicsDataType"/> value.
        /// </summary>
        IVisionIntrinsicCalibrationBuilder WithIntrinsics(VisionIntrinsicsDataType intrinsics);

        /// <summary>
        /// Sets the residual reprojection error.
        /// </summary>
        IVisionIntrinsicCalibrationBuilder WithResidualError(double residualError);

        /// <summary>
        /// Sets the calibration method label.
        /// </summary>
        IVisionIntrinsicCalibrationBuilder WithMethod(string method);
    }

    /// <summary>
    /// Configures an extrinsic calibration.
    /// </summary>
    public interface IVisionExtrinsicCalibrationBuilder
    {
        /// <summary>
        /// Sets the calibration identifier.
        /// </summary>
        IVisionExtrinsicCalibrationBuilder WithCalibrationId(string calibrationId);

        /// <summary>
        /// Sets the mount kind.
        /// </summary>
        IVisionExtrinsicCalibrationBuilder WithMount(VisionCalibrationMountEnum mount);

        /// <summary>
        /// Sets the source and target frame identifiers.
        /// </summary>
        IVisionExtrinsicCalibrationBuilder WithFrames(string sourceFrame, string targetFrame);

        /// <summary>
        /// Sets the extrinsic transform. The Server sets the transform's
        /// <c>FrameId</c> equal to <paramref name="transform"/>.FrameId or
        /// the target frame if the pose's frame is empty per §5.12.
        /// </summary>
        IVisionExtrinsicCalibrationBuilder WithTransform(VisionPose3DDataType transform);

        /// <summary>
        /// Sets the residual error.
        /// </summary>
        IVisionExtrinsicCalibrationBuilder WithResidualError(double residualError);
    }

    /// <summary>
    /// Configures a stream endpoint.
    /// </summary>
    public interface IVisionStreamEndpointBuilder
    {
        /// <summary>
        /// Sets the deployment-scoped endpoint identifier.
        /// </summary>
        IVisionStreamEndpointBuilder WithEndpointId(string endpointId);

        /// <summary>
        /// Sets the endpoint URI.
        /// </summary>
        IVisionStreamEndpointBuilder WithEndpointUri(string endpointUri);

        /// <summary>
        /// Sets the stream protocol.
        /// </summary>
        IVisionStreamEndpointBuilder WithProtocol(VisionStreamProtocolEnum protocol);

        /// <summary>
        /// Sets the media codec.
        /// </summary>
        IVisionStreamEndpointBuilder WithCodec(VisionVideoCodecEnum codec);

        /// <summary>
        /// Sets the resolution.
        /// </summary>
        IVisionStreamEndpointBuilder WithResolution(uint width, uint height);

        /// <summary>
        /// Sets the frame rate.
        /// </summary>
        IVisionStreamEndpointBuilder WithFrameRate(double frameRate);

        /// <summary>
        /// Sets the bitrate.
        /// </summary>
        IVisionStreamEndpointBuilder WithBitrate(uint bitrate);

        /// <summary>
        /// Sets the profile name.
        /// </summary>
        IVisionStreamEndpointBuilder WithDefaultProfileName(string defaultProfileName);
    }

    /// <summary>
    /// Configures a clip endpoint.
    /// </summary>
    public interface IVisionClipEndpointBuilder
    {
        /// <summary>
        /// Sets the deployment-scoped endpoint identifier.
        /// </summary>
        IVisionClipEndpointBuilder WithEndpointId(string endpointId);

        /// <summary>
        /// Sets the endpoint URI.
        /// </summary>
        IVisionClipEndpointBuilder WithEndpointUri(string endpointUri);

        /// <summary>
        /// Sets the clip format.
        /// </summary>
        IVisionClipEndpointBuilder WithClipFormat(VisionClipFormatEnum format);

        /// <summary>
        /// Sets the clip quality (encoder-specific units).
        /// </summary>
        IVisionClipEndpointBuilder WithQuality(uint quality);

        /// <summary>
        /// Sets the clip resolution.
        /// </summary>
        IVisionClipEndpointBuilder WithResolution(uint width, uint height);

        /// <summary>
        /// Enables or disables inline delivery of clip bytes.
        /// </summary>
        /// <remarks>
        /// When set to <see langword="false"/>, the Server's
        /// <c>LatestClip</c> variable is served with
        /// <see cref="StatusCodes.BadNotSupported"/> and inline
        /// <c>GetClip</c> requests are refused, as required by §6.4.
        /// </remarks>
        IVisionClipEndpointBuilder WithInlineDelivery(bool enabled, uint maxInlineClipSize);

        /// <summary>
        /// Sets the profile name.
        /// </summary>
        IVisionClipEndpointBuilder WithDefaultProfileName(string defaultProfileName);
    }

    /// <summary>
    /// Configures an inference-pipeline instance.
    /// </summary>
    public interface IVisionPipelineBuilder
    {
        /// <summary>
        /// Sets the pipeline identifier.
        /// </summary>
        IVisionPipelineBuilder WithPipelineId(string pipelineId);

        /// <summary>
        /// Sets the target sensor node id.
        /// </summary>
        IVisionPipelineBuilder WithSensor(NodeId sensorNodeId);

        /// <summary>
        /// Sets the deployment node id. The specification deliberately
        /// keeps <c>Deployment</c> as a plain <see cref="NodeId"/>; the
        /// Server does not require any AI Model Management dependency.
        /// </summary>
        IVisionPipelineBuilder WithDeployment(NodeId deploymentNodeId);

        /// <summary>
        /// Sets the learning job node id. The specification deliberately
        /// keeps <c>LearningJob</c> as a plain <see cref="NodeId"/>; the
        /// Server does not require any AI Model Management dependency. Section
        /// 9.5.1 requires this to be non-null when ground-truth corrections are
        /// retained so a client can tell whether its label reached a learning
        /// loop.
        /// </summary>
        IVisionPipelineBuilder WithLearningJob(NodeId learningJobNodeId);

        /// <summary>
        /// Adds a <c>ProducedBy</c> reference from the pipeline to the
        /// referenced node (typically a controller or process instance).
        /// </summary>
        IVisionPipelineBuilder ProducedBy(NodeId producerNodeId);

        /// <summary>
        /// Binds the inference provider. All pipeline methods delegate
        /// to it. When <paramref name="onServer"/> is <see langword="true"/>
        /// the Server advertises the <c>VIS-Inference-OnServer</c>
        /// facet — otherwise <c>VIS-Inference-OffServer</c>.
        /// </summary>
        IVisionPipelineBuilder UseInferenceProvider(IVisionInferenceProvider provider, bool onServer = true);

        /// <summary>
        /// Binds the feedback sink for this pipeline's <c>Feedback</c>
        /// object.
        /// </summary>
        IVisionPipelineBuilder UseFeedbackSink(IVisionFeedbackSink sink);
    }
}
