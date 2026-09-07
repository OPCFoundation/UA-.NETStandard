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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Vision;

namespace Vision.VisualInspectionCell
{
    internal sealed class VisualInspectionResultPublisher
    {
        public async ValueTask<PublishedInspectionResult> PublishAsync(
            VisualInspectionTarget target,
            string resultId,
            DateTimeUtc timestamp,
            VisionResultEvaluationEnum evaluation,
            ArrayOf<VisionCharacteristicDataType> characteristics,
            string modelVersion,
            VisionImageReferenceDataType frameReference,
            string fixtureName,
            CancellationToken cancellationToken)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ISystemContext context = target.SystemContext;
            if (TryGetPublished(resultId, out PublishedInspectionResult? existing) &&
                existing != null)
            {
                return existing;
            }

            var qualifiedName = new QualifiedName(resultId, target.InstanceNamespaceIndex);
            InspectionResultState state = context.CreateInstanceOfInspectionResultType(
                target.ResultsFolder, qualifiedName);
            state.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes;
            if (state.ResultId != null)
            {
                state.ResultId.Value = resultId;
            }
            if (state.CreationTime != null)
            {
                state.CreationTime.Value = timestamp;
            }
            state.AddSensor(context);
            state.CreateOrReplaceSensor(context, null).Value = target.SensorNodeId;
            state.AddPipeline(context);
            state.CreateOrReplacePipeline(context, null).Value = target.PipelineNodeId;
            state.AddModelVersionUsed(context);
            state.CreateOrReplaceModelVersionUsed(context, null).Value = modelVersion;
            state.CreateOrReplaceEvaluation(context, null).Value = evaluation;
            state.AddPartId(context);
            state.CreateOrReplacePartId(context, null).Value = InspectionRecipe.PartId;
            state.AddRecipeId(context);
            state.CreateOrReplaceRecipeId(context, null).Value = InspectionRecipe.RecipeId;
            state.CreateOrReplaceCharacteristics(context, null).Value = characteristics;
            state.AddFrame(context);
            BaseDataVariableState<VisionImageReferenceDataType> frame =
                state.CreateOrReplaceFrame(context, null);
            frame.Value = frameReference;
            state.NodeId = context.RequireNodeIdFactory().New(context, state);
            context.AssignInstanceChildNodeIds(state, state.NodeId);
            var published = new PublishedInspectionResult(
                resultId,
                state.NodeId,
                evaluation,
                characteristics,
                modelVersion,
                frameReference,
                fixtureName);
            var evicted = new List<NodeId>();
            lock (m_lock)
            {
                if (m_results.TryGetValue(resultId, out PublishedInspectionResult? retained))
                {
                    return retained;
                }
                target.ResultsFolder.AddChild(state);
                m_results.Add(resultId, published);
                m_retained.Add(resultId);
                while (m_retained.Count > ResultRetention)
                {
                    string evictedResultId = m_retained[0];
                    m_retained.RemoveAt(0);
                    if (m_results.Remove(evictedResultId, out PublishedInspectionResult? removed))
                    {
                        evicted.Add(removed.NodeId);
                    }
                }
            }
            await target.NodeManager.AddPredefinedNodeAsync(state, cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < evicted.Count; ii++)
            {
                if (target.SystemContext is ServerSystemContext serverContext)
                {
                    _ = await target.NodeManager.DeleteNodeAsync(serverContext, evicted[ii], cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            return published;
        }

        public bool TryGetPublished(string resultId, out PublishedInspectionResult? published)
        {
            lock (m_lock)
            {
                return m_results.TryGetValue(resultId, out published);
            }
        }

        private const int ResultRetention = 16;
        private readonly Lock m_lock = new();
        private readonly Dictionary<string, PublishedInspectionResult> m_results = [];
        private readonly List<string> m_retained = [];
    }

    internal sealed record PublishedInspectionResult(
        string ResultId,
        NodeId NodeId,
        VisionResultEvaluationEnum Evaluation,
        ArrayOf<VisionCharacteristicDataType> Characteristics,
        string ModelVersion,
        VisionImageReferenceDataType FrameReference,
        string FixtureName);
}
