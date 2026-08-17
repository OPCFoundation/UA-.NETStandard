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

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Nameplate identity of a Vision sensor (§5.4).
    /// </summary>
    public sealed record VisionSensorIdentity
    {
        /// <summary>
        /// The sensor NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The Server-unique sensor identifier from <c>VisionSensorType.SensorId</c>.
        /// </summary>
        public string? SensorId { get; init; }

        /// <summary>
        /// Whether the sensor is <c>Physical</c>, <c>Simulated</c> or <c>Hybrid</c> (§4.3).
        /// </summary>
        public VisionRealityKindEnum RealityKind { get; init; }

        /// <summary>
        /// What the sensor senses — <c>Area2D</c>, <c>Depth3D</c>, <c>Thermal</c>, and so on.
        /// </summary>
        public VisionSensorModalityEnum Modality { get; init; }

        /// <summary>
        /// The sensor manufacturer, when reported.
        /// </summary>
        public LocalizedText Manufacturer { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The sensor model, when reported.
        /// </summary>
        public LocalizedText Model { get; init; } = LocalizedText.Null;

        /// <summary>
        /// The sensor serial number, when reported.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// The transport-level device URI (for example a GigE Vision device id), when reported.
        /// </summary>
        public string? DeviceUri { get; init; }

        /// <summary>
        /// The <c>FrameId</c> string of this sensor's own camera frame, when reported.
        /// </summary>
        public string? FrameId { get; init; }
    }

    /// <summary>
    /// Imaging members of an <c>ImageSensorType</c> (§5.5).
    /// </summary>
    public sealed record VisionImageSensorSnapshot
    {
        /// <summary>
        /// Image width in pixels.
        /// </summary>
        public uint Width { get; init; }

        /// <summary>
        /// Image height in pixels.
        /// </summary>
        public uint Height { get; init; }

        /// <summary>
        /// The GenICam PFNC pixel format (for example <c>Mono8</c>, <c>BayerRG12</c>,
        /// <c>RGB8</c>).
        /// </summary>
        public string? PixelFormat { get; init; }

        /// <summary>
        /// Exposure time in microseconds, or <c>null</c> when not reported.
        /// </summary>
        public double? ExposureTime { get; init; }

        /// <summary>
        /// Sensor gain, or <c>null</c> when not reported.
        /// </summary>
        public double? Gain { get; init; }

        /// <summary>
        /// Acquisition frame rate in Hz, or <c>null</c> when not reported.
        /// </summary>
        public double? AcquisitionFrameRate { get; init; }

        /// <summary>
        /// Camera intrinsics, when reported. <see cref="VisionIntrinsicsDataType"/> uses
        /// the corner-datum principal point convention of §5.12; a client bridging to
        /// ROS or OpenCV subtracts 0.5 from <c>Cx</c> and <c>Cy</c>.
        /// </summary>
        public VisionIntrinsicsDataType? Intrinsics { get; init; }
    }

    /// <summary>
    /// Depth members of a <c>Depth3DSensorType</c> (§5.6).
    /// </summary>
    public sealed record VisionDepth3DSensorSnapshot
    {
        /// <summary>
        /// The minimum valid depth in metres.
        /// </summary>
        public double MinDepth { get; init; }

        /// <summary>
        /// The maximum valid depth in metres.
        /// </summary>
        public double MaxDepth { get; init; }

        /// <summary>
        /// Scale factor applied to raw depth samples.
        /// </summary>
        public double DepthScale { get; init; }

        /// <summary>
        /// Stereo baseline in metres, or 0 for non-stereo sensors.
        /// </summary>
        public double Baseline { get; init; }

        /// <summary>
        /// Approximate points per frame of a point-cloud sensor.
        /// </summary>
        public uint PointsPerFrame { get; init; }
    }

    /// <summary>
    /// Optics description (§5.7).
    /// </summary>
    public sealed record VisionOpticsSnapshot
    {
        /// <summary>
        /// The optics NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The lens focal length in millimetres, when reported.
        /// </summary>
        public double? FocalLength { get; init; }

        /// <summary>
        /// The lens aperture (f-number), when reported.
        /// </summary>
        public double? Aperture { get; init; }

        /// <summary>
        /// The working distance in metres, when reported.
        /// </summary>
        public double? WorkingDistance { get; init; }
    }

    /// <summary>
    /// Illumination description (§5.7).
    /// </summary>
    public sealed record VisionIlluminationSnapshot
    {
        /// <summary>
        /// The illumination NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The dominant wavelength in nanometres, when reported.
        /// </summary>
        public double? Wavelength { get; init; }

        /// <summary>
        /// The relative intensity in percent (0..100), when reported.
        /// </summary>
        public double? RelativeIntensity { get; init; }
    }

    /// <summary>
    /// A snapshot of an <c>IntrinsicCalibrationType</c> instance (§5.8).
    /// </summary>
    public sealed record VisionIntrinsicCalibrationSnapshot
    {
        /// <summary>
        /// The calibration NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The stable calibration identifier.
        /// </summary>
        public string? CalibrationId { get; init; }

        /// <summary>
        /// The time the calibration was performed.
        /// </summary>
        public DateTimeUtc PerformedAt { get; init; }

        /// <summary>
        /// Whether the Server considers the calibration currently valid. A client should
        /// treat an invalid calibration as unusable rather than substituting a default.
        /// </summary>
        public bool Valid { get; init; }

        /// <summary>
        /// The residual re-projection error of the calibration.
        /// </summary>
        public double ResidualError { get; init; }

        /// <summary>
        /// A description of the calibration method.
        /// </summary>
        public string? Method { get; init; }

        /// <summary>
        /// The intrinsic parameters.
        /// </summary>
        public VisionIntrinsicsDataType? Intrinsics { get; init; }
    }

    /// <summary>
    /// A snapshot of an <c>ExtrinsicCalibrationType</c> instance (§5.8).
    /// </summary>
    public sealed record VisionExtrinsicCalibrationSnapshot
    {
        /// <summary>
        /// The calibration NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The stable calibration identifier.
        /// </summary>
        public string? CalibrationId { get; init; }

        /// <summary>
        /// The time the calibration was performed.
        /// </summary>
        public DateTimeUtc PerformedAt { get; init; }

        /// <summary>
        /// Whether the Server considers the calibration currently valid.
        /// </summary>
        public bool Valid { get; init; }

        /// <summary>
        /// The residual error of the calibration.
        /// </summary>
        public double ResidualError { get; init; }

        /// <summary>
        /// A description of the calibration method.
        /// </summary>
        public string? Method { get; init; }

        /// <summary>
        /// The camera-to-robot arrangement — <c>EyeInHand</c>, <c>EyeToHand</c>,
        /// <c>Fixed</c>, or <c>Unknown</c>.
        /// </summary>
        public VisionCalibrationMountEnum Mount { get; init; }

        /// <summary>
        /// The source frame NodeId of the transform (typically the camera frame).
        /// </summary>
        public NodeId SourceFrameId { get; init; } = NodeId.Null;

        /// <summary>
        /// The target frame NodeId of the transform (typically the flange or a station
        /// frame). §5.12 requires <c>Transform.FrameId</c> to equal the FrameId string
        /// of this frame.
        /// </summary>
        public NodeId TargetFrameId { get; init; } = NodeId.Null;

        /// <summary>
        /// The transform itself; <c>Position</c> is in metres, <c>Orientation</c> is a
        /// unit quaternion ordered (x, y, z, w).
        /// </summary>
        public VisionPose3DDataType? Transform { get; init; }
    }
}
