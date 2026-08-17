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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Focused reader for <c>DetectionResultType</c>, <c>InspectionResultType</c>
    /// and <c>SegmentationResultType</c> instances (§7). Reads the result-shared
    /// members (<c>ResultId</c>, <c>CreationTime</c>, <c>Sensor</c>,
    /// <c>Pipeline</c>, <c>ModelVersionUsed</c>, <c>Frame</c>) plus the subtype
    /// members, and subscribes to result changes over an
    /// <see cref="IStreamingSubscription"/>.
    /// </summary>
    public sealed class VisionResultReader
    {
        private readonly VisionClientOperations m_operations;

        internal VisionResultReader(VisionClientOperations operations, NodeId resultNodeId)
        {
            m_operations = operations
                ?? throw new ArgumentNullException(nameof(operations));
            if (resultNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Result NodeId must not be null.", nameof(resultNodeId));
            }
            ResultNodeId = resultNodeId;
        }

        /// <summary>
        /// Gets the result object NodeId.
        /// </summary>
        public NodeId ResultNodeId { get; }

        /// <summary>
        /// Reads the result as an <c>InspectionResultType</c> snapshot (§7.2).
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionInspectionResultSnapshot> ReadInspectionAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.ResultId,
                BrowseNames.CreationTime,
                BrowseNames.Sensor,
                BrowseNames.Pipeline,
                BrowseNames.ModelVersionUsed,
                BrowseNames.Frame,
                BrowseNames.Evaluation,
                BrowseNames.PartId,
                BrowseNames.RecipeId,
                BrowseNames.Characteristics
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                ResultNodeId, members, cancellationToken).ConfigureAwait(false);
            var toRead = ExtractPresent(nodes);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                toRead, cancellationToken).ConfigureAwait(false);
            int cursor = 0;
            string? resultId = TakeString(values, nodes, 0, ref cursor);
            DateTimeUtc creationTime = TakeDateTime(values, nodes, 1, ref cursor);
            NodeId sensor = TakeNodeId(values, nodes, 2, ref cursor);
            NodeId pipeline = TakeNodeId(values, nodes, 3, ref cursor);
            string? modelVersion = TakeString(values, nodes, 4, ref cursor);
            VisionImageReferenceDataType? frame = TakeImageReference(
                values, nodes, 5, ref cursor);
            VisionResultEvaluationEnum evaluation = TakeEnum<VisionResultEvaluationEnum>(
                values, nodes, 6, ref cursor);
            string? partId = TakeString(values, nodes, 7, ref cursor);
            string? recipeId = TakeString(values, nodes, 8, ref cursor);
            ArrayOf<VisionCharacteristicDataType> characteristics =
                TakeCharacteristics(values, nodes, 9, ref cursor);
            return new VisionInspectionResultSnapshot
            {
                NodeId = ResultNodeId,
                ResultId = resultId,
                CreationTime = creationTime,
                SensorId = sensor,
                PipelineId = pipeline,
                ModelVersionUsed = modelVersion,
                Frame = frame,
                Evaluation = evaluation,
                PartId = partId,
                RecipeId = recipeId,
                Characteristics = characteristics
            };
        }

        /// <summary>
        /// Reads the result as a <c>DetectionResultType</c> snapshot (§7.3).
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionDetectionResultSnapshot> ReadDetectionAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.ResultId,
                BrowseNames.CreationTime,
                BrowseNames.Sensor,
                BrowseNames.Pipeline,
                BrowseNames.ModelVersionUsed,
                BrowseNames.Frame,
                BrowseNames.FrameId,
                BrowseNames.Detections
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                ResultNodeId, members, cancellationToken).ConfigureAwait(false);
            var toRead = ExtractPresent(nodes);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                toRead, cancellationToken).ConfigureAwait(false);
            int cursor = 0;
            string? resultId = TakeString(values, nodes, 0, ref cursor);
            DateTimeUtc creationTime = TakeDateTime(values, nodes, 1, ref cursor);
            NodeId sensor = TakeNodeId(values, nodes, 2, ref cursor);
            NodeId pipeline = TakeNodeId(values, nodes, 3, ref cursor);
            string? modelVersion = TakeString(values, nodes, 4, ref cursor);
            VisionImageReferenceDataType? frame = TakeImageReference(
                values, nodes, 5, ref cursor);
            string? frameId = TakeString(values, nodes, 6, ref cursor);
            ArrayOf<VisionDetectionDataType> detections = TakeDetections(
                values, nodes, 7, ref cursor);
            return new VisionDetectionResultSnapshot
            {
                NodeId = ResultNodeId,
                ResultId = resultId,
                CreationTime = creationTime,
                SensorId = sensor,
                PipelineId = pipeline,
                ModelVersionUsed = modelVersion,
                Frame = frame,
                FrameId = frameId,
                Detections = detections
            };
        }

        /// <summary>
        /// Reads the result as a <c>SegmentationResultType</c> snapshot (§7.4).
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionSegmentationResultSnapshot> ReadSegmentationAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.ResultId,
                BrowseNames.CreationTime,
                BrowseNames.Sensor,
                BrowseNames.Pipeline,
                BrowseNames.Frame,
                BrowseNames.LabelClasses,
                BrowseNames.Mask
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                ResultNodeId, members, cancellationToken).ConfigureAwait(false);
            var toRead = ExtractPresent(nodes);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                toRead, cancellationToken).ConfigureAwait(false);
            int cursor = 0;
            string? resultId = TakeString(values, nodes, 0, ref cursor);
            DateTimeUtc creationTime = TakeDateTime(values, nodes, 1, ref cursor);
            NodeId sensor = TakeNodeId(values, nodes, 2, ref cursor);
            NodeId pipeline = TakeNodeId(values, nodes, 3, ref cursor);
            VisionImageReferenceDataType? frame = TakeImageReference(
                values, nodes, 4, ref cursor);
            ArrayOf<string> labels = TakeStringArray(values, nodes, 5, ref cursor);
            VisionImageReferenceDataType? mask = TakeImageReference(
                values, nodes, 6, ref cursor);
            return new VisionSegmentationResultSnapshot
            {
                NodeId = ResultNodeId,
                ResultId = resultId,
                CreationTime = creationTime,
                SensorId = sensor,
                PipelineId = pipeline,
                Frame = frame,
                LabelClasses = labels,
                Mask = mask
            };
        }

        /// <summary>
        /// Streams detection snapshots each time the <c>Detections</c> variable
        /// changes on the Server.
        /// </summary>
        /// <param name="streaming">
        /// The streaming subscription to monitor over.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the observation.
        /// </param>
        public IAsyncEnumerable<VisionDetectionResultSnapshot> ObserveDetectionsAsync(
            IStreamingSubscription streaming,
            CancellationToken cancellationToken = default)
        {
            if (streaming is null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            return ObserveDetectionsCoreAsync(streaming, cancellationToken);
        }

        private async IAsyncEnumerable<VisionDetectionResultSnapshot> ObserveDetectionsCoreAsync(
            IStreamingSubscription streaming,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId detectionsNode = await m_operations.ResolveChildAsync(
                ResultNodeId, BrowseNames.Detections, cancellationToken)
                .ConfigureAwait(false);
            if (detectionsNode.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "Result '{0}' does not expose a Detections variable.",
                    ResultNodeId);
            }
            var monitored = new List<NodeId> { detectionsNode };
            await foreach (DataValueChange _ in streaming.SubscribeDataChangesAsync(
                    monitored, null, cancellationToken).ConfigureAwait(false))
            {
                yield return await ReadDetectionAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Streams inspection snapshots each time the <c>Characteristics</c>
        /// variable changes on the Server.
        /// </summary>
        /// <param name="streaming">
        /// The streaming subscription to monitor over.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the observation.
        /// </param>
        public IAsyncEnumerable<VisionInspectionResultSnapshot> ObserveInspectionAsync(
            IStreamingSubscription streaming,
            CancellationToken cancellationToken = default)
        {
            if (streaming is null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            return ObserveInspectionCoreAsync(streaming, cancellationToken);
        }

        private async IAsyncEnumerable<VisionInspectionResultSnapshot> ObserveInspectionCoreAsync(
            IStreamingSubscription streaming,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId characteristicsNode = await m_operations.ResolveChildAsync(
                ResultNodeId, BrowseNames.Characteristics, cancellationToken)
                .ConfigureAwait(false);
            if (characteristicsNode.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "Result '{0}' does not expose a Characteristics variable.",
                    ResultNodeId);
            }
            var monitored = new List<NodeId> { characteristicsNode };
            await foreach (DataValueChange _ in streaming.SubscribeDataChangesAsync(
                    monitored, null, cancellationToken).ConfigureAwait(false))
            {
                yield return await ReadInspectionAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Streams segmentation snapshots each time the <c>Mask</c> variable changes
        /// on the Server.
        /// </summary>
        /// <param name="streaming">
        /// The streaming subscription to monitor over.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the observation.
        /// </param>
        public IAsyncEnumerable<VisionSegmentationResultSnapshot> ObserveSegmentationAsync(
            IStreamingSubscription streaming,
            CancellationToken cancellationToken = default)
        {
            if (streaming is null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            return ObserveSegmentationCoreAsync(streaming, cancellationToken);
        }

        private async IAsyncEnumerable<VisionSegmentationResultSnapshot> ObserveSegmentationCoreAsync(
            IStreamingSubscription streaming,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId maskNode = await m_operations.ResolveChildAsync(
                ResultNodeId, BrowseNames.Mask, cancellationToken).ConfigureAwait(false);
            if (maskNode.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "Result '{0}' does not expose a Mask variable.",
                    ResultNodeId);
            }
            var monitored = new List<NodeId> { maskNode };
            await foreach (DataValueChange _ in streaming.SubscribeDataChangesAsync(
                    monitored, null, cancellationToken).ConfigureAwait(false))
            {
                yield return await ReadSegmentationAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static List<NodeId> ExtractPresent(ArrayOf<NodeId> nodes)
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
            ArrayOf<DataValue> values,
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

        private static DateTimeUtc TakeDateTime(
            ArrayOf<DataValue> values,
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
            ArrayOf<DataValue> values,
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

        private static TEnum TakeEnum<TEnum>(
            ArrayOf<DataValue> values,
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

        private static ArrayOf<string> TakeStringArray(
            ArrayOf<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return ArrayOf<string>.Empty;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(out ArrayOf<string> array)
                ? array
                : ArrayOf<string>.Empty;
        }

        private VisionImageReferenceDataType? TakeImageReference(
            ArrayOf<DataValue> values,
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
                    out VisionImageReferenceDataType structure,
                    m_operations.Session.MessageContext)
                ? structure
                : null;
#pragma warning restore CS8600
        }

        private ArrayOf<VisionDetectionDataType> TakeDetections(
            ArrayOf<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return ArrayOf<VisionDetectionDataType>.Empty;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(
                    out ArrayOf<VisionDetectionDataType> array,
                    m_operations.Session.MessageContext)
                ? array
                : ArrayOf<VisionDetectionDataType>.Empty;
        }

        private ArrayOf<VisionCharacteristicDataType> TakeCharacteristics(
            ArrayOf<DataValue> values,
            ArrayOf<NodeId> nodes,
            int index,
            ref int cursor)
        {
            if (nodes[index].IsNull)
            {
                return ArrayOf<VisionCharacteristicDataType>.Empty;
            }
            DataValue value = values[cursor++];
            return value.WrappedValue.TryGetValue(
                    out ArrayOf<VisionCharacteristicDataType> array,
                    m_operations.Session.MessageContext)
                ? array
                : ArrayOf<VisionCharacteristicDataType>.Empty;
        }
    }
}
