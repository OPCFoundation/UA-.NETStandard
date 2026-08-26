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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Focused client over a single <c>VisionSensorType</c> (or subtype) instance.
    /// Reads identity, imaging or depth members, optics, illumination, the sensor's
    /// mounted frame, and its intrinsic and hand-eye extrinsic calibrations, so a
    /// caller can act on detections in world coordinates without knowing NodeIds
    /// or BrowseNames.
    /// </summary>
    public sealed class VisionSensorClient
    {
        private readonly VisionClientOperations m_operations;
        private readonly VisionSensorTypeClient m_proxy;

        internal VisionSensorClient(VisionClientOperations operations, NodeId sensorNodeId)
        {
            m_operations = operations
                ?? throw new ArgumentNullException(nameof(operations));
            if (sensorNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Sensor NodeId must not be null.", nameof(sensorNodeId));
            }
            SensorNodeId = sensorNodeId;
            m_proxy = new VisionSensorTypeClient(
                m_operations.Session, sensorNodeId, m_operations.Telemetry);
        }

        /// <summary>
        /// Gets the sensor object NodeId.
        /// </summary>
        public NodeId SensorNodeId { get; }

        /// <summary>
        /// Reads the sensor's identity nameplate (§5.4).
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionSensorIdentity> ReadIdentityAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.SensorId,
                BrowseNames.RealityKind,
                BrowseNames.Modality,
                BrowseNames.Manufacturer,
                BrowseNames.Model,
                BrowseNames.SerialNumber,
                BrowseNames.DeviceUri,
                BrowseNames.FrameId
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                SensorNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                ToList(nodes), cancellationToken).ConfigureAwait(false);
            var buffer = new List<DataValue>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                buffer.Add(values[ii]);
            }
            int cursor = 0;
            string? sensorId = TakeString(buffer, nodes, 0, ref cursor);
            VisionRealityKindEnum reality = TakeEnum<VisionRealityKindEnum>(
                buffer, nodes, 1, ref cursor);
            VisionSensorModalityEnum modality = TakeEnum<VisionSensorModalityEnum>(
                buffer, nodes, 2, ref cursor);
            LocalizedText manufacturer = TakeLocalizedText(buffer, nodes, 3, ref cursor);
            LocalizedText model = TakeLocalizedText(buffer, nodes, 4, ref cursor);
            string? serialNumber = TakeString(buffer, nodes, 5, ref cursor);
            string? deviceUri = TakeString(buffer, nodes, 6, ref cursor);
            string? frameId = TakeString(buffer, nodes, 7, ref cursor);
            return new VisionSensorIdentity
            {
                NodeId = SensorNodeId,
                SensorId = sensorId,
                RealityKind = reality,
                Modality = modality,
                Manufacturer = manufacturer,
                Model = model,
                SerialNumber = serialNumber,
                DeviceUri = deviceUri,
                FrameId = frameId
            };
        }

        /// <summary>
        /// Reads the imaging members from the sensor, when it is an
        /// <c>ImageSensorType</c> (§5.5). Members that are Optional on the type are
        /// returned as <c>null</c> when the Server did not materialise them.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionImageSensorSnapshot?> ReadImageMembersAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.Width,
                BrowseNames.Height,
                BrowseNames.PixelFormat,
                BrowseNames.ExposureTime,
                BrowseNames.Gain,
                BrowseNames.AcquisitionFrameRate,
                BrowseNames.Intrinsics
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                SensorNodeId, members, cancellationToken).ConfigureAwait(false);
            if (nodes[0].IsNull && nodes[1].IsNull && nodes[2].IsNull)
            {
                return null;
            }
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                ToList(nodes), cancellationToken).ConfigureAwait(false);
            var buffer = new List<DataValue>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                buffer.Add(values[ii]);
            }
            int cursor = 0;
            uint width = TakeUInt32(buffer, nodes, 0, ref cursor);
            uint height = TakeUInt32(buffer, nodes, 1, ref cursor);
            string? pixelFormat = TakeString(buffer, nodes, 2, ref cursor);
            double? exposureTime = TakeDoubleOrNull(buffer, nodes, 3, ref cursor);
            double? gain = TakeDoubleOrNull(buffer, nodes, 4, ref cursor);
            double? frameRate = TakeDoubleOrNull(buffer, nodes, 5, ref cursor);
            VisionIntrinsicsDataType? intrinsics = TakeIntrinsics(
                buffer, nodes, 6, ref cursor);
            return new VisionImageSensorSnapshot
            {
                Width = width,
                Height = height,
                PixelFormat = pixelFormat,
                ExposureTime = exposureTime,
                Gain = gain,
                AcquisitionFrameRate = frameRate,
                Intrinsics = intrinsics
            };
        }

        /// <summary>
        /// Reads the depth members from the sensor, when it is a
        /// <c>Depth3DSensorType</c> (§5.6). Returns <c>null</c> when the sensor does
        /// not carry any depth-specific members.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionDepth3DSensorSnapshot?> ReadDepthMembersAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.MinDepth,
                BrowseNames.MaxDepth,
                BrowseNames.DepthScale,
                BrowseNames.Baseline,
                BrowseNames.PointsPerFrame
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                SensorNodeId, members, cancellationToken).ConfigureAwait(false);
            bool anyPresent = false;
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    anyPresent = true;
                    break;
                }
            }
            if (!anyPresent)
            {
                return null;
            }
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                ToList(nodes), cancellationToken).ConfigureAwait(false);
            var buffer = new List<DataValue>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                buffer.Add(values[ii]);
            }
            int cursor = 0;
            double minDepth = TakeDouble(buffer, nodes, 0, ref cursor);
            double maxDepth = TakeDouble(buffer, nodes, 1, ref cursor);
            double depthScale = TakeDouble(buffer, nodes, 2, ref cursor);
            double baseline = TakeDouble(buffer, nodes, 3, ref cursor);
            uint points = TakeUInt32(buffer, nodes, 4, ref cursor);
            return new VisionDepth3DSensorSnapshot
            {
                MinDepth = minDepth,
                MaxDepth = maxDepth,
                DepthScale = depthScale,
                Baseline = baseline,
                PointsPerFrame = points
            };
        }

        /// <summary>
        /// Reads the optics description of the sensor, when present.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionOpticsSnapshot?> ReadOpticsAsync(
            CancellationToken cancellationToken = default)
        {
            OpticsTypeClient? optics = await m_proxy.GetOpticsAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (optics is null || optics.ObjectId.IsNull)
            {
                return null;
            }
            string[] members =
            [
                BrowseNames.FocalLength,
                BrowseNames.Aperture,
                BrowseNames.MinimumWorkingDistance
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                optics.ObjectId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                ToList(nodes), cancellationToken).ConfigureAwait(false);
            var buffer = new List<DataValue>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                buffer.Add(values[ii]);
            }
            int cursor = 0;
            double? focalLength = TakeDoubleOrNull(buffer, nodes, 0, ref cursor);
            double? aperture = TakeDoubleOrNull(buffer, nodes, 1, ref cursor);
            double? workingDistance = TakeDoubleOrNull(buffer, nodes, 2, ref cursor);
            return new VisionOpticsSnapshot
            {
                NodeId = optics.ObjectId,
                FocalLength = focalLength,
                Aperture = aperture,
                WorkingDistance = workingDistance
            };
        }

        /// <summary>
        /// Reads the illumination description of the sensor, when present.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionIlluminationSnapshot?> ReadIlluminationAsync(
            CancellationToken cancellationToken = default)
        {
            IlluminationTypeClient? illumination = await m_proxy.GetIlluminationAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (illumination is null || illumination.ObjectId.IsNull)
            {
                return null;
            }
            string[] members =
            [
                BrowseNames.Wavelength,
                BrowseNames.RelativeIntensity
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                illumination.ObjectId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                ToList(nodes), cancellationToken).ConfigureAwait(false);
            var buffer = new List<DataValue>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                buffer.Add(values[ii]);
            }
            int cursor = 0;
            double? wavelength = TakeDoubleOrNull(buffer, nodes, 0, ref cursor);
            double? intensity = TakeDoubleOrNull(buffer, nodes, 1, ref cursor);
            return new VisionIlluminationSnapshot
            {
                NodeId = illumination.ObjectId,
                Wavelength = wavelength,
                RelativeIntensity = intensity
            };
        }

        /// <summary>
        /// Reads the frame the sensor is mounted on (§5.11 <c>MountedOn</c>). Returns
        /// a null NodeId when the sensor does not declare a mount frame.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<NodeId> GetMountedFrameIdAsync(
            CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> refs = await m_operations.BrowseAsync(
                SensorNodeId,
                m_operations.VisionReference(ReferenceTypes.MountedOn),
                BrowseDirection.Forward,
                (uint)NodeClass.Object,
                cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < refs.Count; ii++)
            {
                NodeId target = ExpandedNodeId.ToNodeId(
                    refs[ii].NodeId, m_operations.Session.NamespaceUris);
                if (!target.IsNull)
                {
                    return target;
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Opens the media-management client rooted at this sensor's
        /// <c>Media</c> object.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionMediaClient?> OpenMediaAsync(
            CancellationToken cancellationToken = default)
        {
            VisionMediaManagementTypeClient? media = await m_proxy.GetMediaAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (media is null || media.ObjectId.IsNull)
            {
                return null;
            }
            return new VisionMediaClient(m_operations, media.ObjectId);
        }

        /// <summary>
        /// Enumerates the calibrations attached to the sensor via <c>HasCalibration</c>
        /// or nested in the sensor's <c>Calibrations</c> folder.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async IAsyncEnumerable<VisionNodeEntry> EnumerateCalibrationsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId calibrationType = m_operations.VisionNamespaceType(
                ObjectTypes.VisionCalibrationType);
            if (calibrationType.IsNull)
            {
                yield break;
            }
            var refs = new List<ReferenceDescription>();
            ArrayOf<ReferenceDescription> direct = await m_operations.BrowseAsync(
                SensorNodeId,
                m_operations.VisionReference(ReferenceTypes.HasCalibration),
                BrowseDirection.Forward,
                (uint)NodeClass.Object,
                cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < direct.Count; ii++)
            {
                refs.Add(direct[ii]);
            }
            FolderTypeClient? folder = await m_proxy.GetCalibrationsAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (folder is not null && !folder.ObjectId.IsNull)
            {
                ArrayOf<ReferenceDescription> nested = await m_operations
                    .BrowseHierarchicalObjectsAsync(folder.ObjectId, cancellationToken)
                    .ConfigureAwait(false);
                for (int ii = 0; ii < nested.Count; ii++)
                {
                    refs.Add(nested[ii]);
                }
            }
            var seen = new HashSet<NodeId>();
            for (int ii = 0; ii < refs.Count; ii++)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    refs[ii].NodeId, m_operations.Session.NamespaceUris);
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    refs[ii].TypeDefinition, m_operations.Session.NamespaceUris);
                if (nodeId.IsNull || typeDef.IsNull || !seen.Add(nodeId))
                {
                    continue;
                }
                if (!await m_operations.Session.NodeCache.IsTypeOfAsync(
                        typeDef, calibrationType, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }
                yield return new VisionNodeEntry(
                    nodeId, refs[ii].BrowseName, refs[ii].DisplayName, typeDef);
            }
        }

        /// <summary>
        /// Reads an intrinsic-calibration snapshot from the given calibration NodeId.
        /// </summary>
        /// <param name="calibrationNodeId">
        /// The <c>IntrinsicCalibrationType</c> instance NodeId; typically obtained
        /// from <see cref="EnumerateCalibrationsAsync"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        /// <exception cref="ArgumentException"></exception>
        public async Task<VisionIntrinsicCalibrationSnapshot> ReadIntrinsicCalibrationAsync(
            NodeId calibrationNodeId,
            CancellationToken cancellationToken = default)
        {
            if (calibrationNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Calibration NodeId must not be null.", nameof(calibrationNodeId));
            }
            string[] members =
            [
                BrowseNames.CalibrationId,
                BrowseNames.PerformedAt,
                BrowseNames.Valid,
                BrowseNames.ResidualError,
                BrowseNames.Method,
                BrowseNames.Intrinsics
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                calibrationNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                ToList(nodes), cancellationToken).ConfigureAwait(false);
            var buffer = new List<DataValue>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                buffer.Add(values[ii]);
            }
            int cursor = 0;
            string? calibrationId = TakeString(buffer, nodes, 0, ref cursor);
            DateTimeUtc performedAt = TakeDateTime(buffer, nodes, 1, ref cursor);
            bool valid = TakeBool(buffer, nodes, 2, ref cursor);
            double residual = TakeDouble(buffer, nodes, 3, ref cursor);
            string? method = TakeString(buffer, nodes, 4, ref cursor);
            VisionIntrinsicsDataType? intrinsics = TakeIntrinsics(
                buffer, nodes, 5, ref cursor);
            return new VisionIntrinsicCalibrationSnapshot
            {
                NodeId = calibrationNodeId,
                CalibrationId = calibrationId,
                PerformedAt = performedAt,
                Valid = valid,
                ResidualError = residual,
                Method = method,
                Intrinsics = intrinsics
            };
        }

        /// <summary>
        /// Reads an extrinsic-calibration snapshot from the given calibration NodeId.
        /// </summary>
        /// <param name="calibrationNodeId">
        /// The <c>ExtrinsicCalibrationType</c> instance NodeId; typically obtained
        /// from <see cref="EnumerateCalibrationsAsync"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        /// <exception cref="ArgumentException"></exception>
        public async Task<VisionExtrinsicCalibrationSnapshot> ReadExtrinsicCalibrationAsync(
            NodeId calibrationNodeId,
            CancellationToken cancellationToken = default)
        {
            if (calibrationNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Calibration NodeId must not be null.", nameof(calibrationNodeId));
            }
            string[] members =
            [
                BrowseNames.CalibrationId,
                BrowseNames.PerformedAt,
                BrowseNames.Valid,
                BrowseNames.ResidualError,
                BrowseNames.Method,
                BrowseNames.Mount,
                BrowseNames.SourceFrame,
                BrowseNames.TargetFrame,
                BrowseNames.Transform
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                calibrationNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                ToList(nodes), cancellationToken).ConfigureAwait(false);
            var buffer = new List<DataValue>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                buffer.Add(values[ii]);
            }
            int cursor = 0;
            string? calibrationId = TakeString(buffer, nodes, 0, ref cursor);
            DateTimeUtc performedAt = TakeDateTime(buffer, nodes, 1, ref cursor);
            bool valid = TakeBool(buffer, nodes, 2, ref cursor);
            double residual = TakeDouble(buffer, nodes, 3, ref cursor);
            string? method = TakeString(buffer, nodes, 4, ref cursor);
            VisionCalibrationMountEnum mount = TakeEnum<VisionCalibrationMountEnum>(
                buffer, nodes, 5, ref cursor);
            NodeId source = TakeNodeId(buffer, nodes, 6, ref cursor);
            NodeId target = TakeNodeId(buffer, nodes, 7, ref cursor);
            VisionPose3DDataType? transform = TakePose(
                buffer, nodes, 8, ref cursor);
            return new VisionExtrinsicCalibrationSnapshot
            {
                NodeId = calibrationNodeId,
                CalibrationId = calibrationId,
                PerformedAt = performedAt,
                Valid = valid,
                ResidualError = residual,
                Method = method,
                Mount = mount,
                SourceFrameId = source,
                TargetFrameId = target,
                Transform = transform
            };
        }

        private static List<NodeId> ToList(ArrayOf<NodeId> nodes)
        {
            var list = new List<NodeId>(nodes.Count);
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    list.Add(nodes[ii]);
                }
            }
            return list;
        }

        private static string? TakeString(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return null;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out string text) ? text : null;
        }

        private static LocalizedText TakeLocalizedText(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return LocalizedText.Null;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out LocalizedText text)
                ? text
                : LocalizedText.Null;
        }

        private static TEnum TakeEnum<TEnum>(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
            where TEnum : struct, Enum
        {
            if (nodes[index].IsNull)
            {
                return default;
            }
            DataValue value = values[cursor++];
            return VisionClientOperations.TryReadEnum(value, out TEnum result)
                ? result
                : default;
        }

        private static double TakeDouble(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return 0.0;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out double d) ? d : 0.0;
        }

        private static double? TakeDoubleOrNull(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return null;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out double d) ? d : null;
        }

        private static uint TakeUInt32(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return 0;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out uint u) ? u : 0;
        }

        private static bool TakeBool(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return false;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out bool b) && b;
        }

        private static DateTimeUtc TakeDateTime(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return default;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out DateTimeUtc dt) ? dt : default;
        }

        private static NodeId TakeNodeId(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return NodeId.Null;
            }
            DataValue value = values[cursor++];
            return VisionClientOperations.TryReadNodeId(value, out NodeId nodeId)
                ? nodeId
                : NodeId.Null;
        }

        private VisionIntrinsicsDataType? TakeIntrinsics(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return null;
            }
            DataValue value = values[cursor++];
#pragma warning disable CS8600 // TryGetValue uses [MaybeNullWhen(false)] on encodeable overloads.
            return value.WrappedValue.TryGetValue(
                    out VisionIntrinsicsDataType structure,
                    m_operations.Session.MessageContext)
                ? structure
                : null;
#pragma warning restore CS8600
        }

        private VisionPose3DDataType? TakePose(
            List<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return null;
            }
            DataValue value = values[cursor++];
#pragma warning disable CS8600 // TryGetValue uses [MaybeNullWhen(false)] on encodeable overloads.
            return value.WrappedValue.TryGetValue(
                    out VisionPose3DDataType structure,
                    m_operations.Session.MessageContext)
                ? structure
                : null;
#pragma warning restore CS8600
        }
    }
}
