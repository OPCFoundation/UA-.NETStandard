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
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.Perception;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// A simulated sensor's camera identity. Its advertised stage is evidence only and is never opened.
    /// </summary>
    internal sealed record RenderedVisionCamera(
        NodeId Sensor, NodeId Interface, string SensorId, string FrameId, string StageIdentifier, string PrimPath)
    {
        /// <summary>
        /// Gets the sensor and camera identity shown in the explicit selection list.
        /// </summary>
        public string DisplayName => $"{SensorId}: {PrimPath}";
    }

    /// <summary>
    /// Reads simulated camera metadata without acquiring a media lease, starting inference or fetching a URI.
    /// </summary>
    internal interface IRenderedVisionCameraSource
    {
        /// <summary>
        /// Discovers bounded simulated-camera metadata without starting acquisition.
        /// </summary>
        Task<ArrayOf<RenderedVisionCamera>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken);
        /// <summary>
        /// Rejects stale or reclassified sensor identities before accepting local frame evidence.
        /// </summary>
        Task RequireCurrentAsync(
            CompanionContext context, RenderedVisionCamera camera, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Reuses the focused Vision client and bounded companion reads for properties not exposed by its generated API.
    /// </summary>
    internal sealed class RenderedVisionCameraSource : IRenderedVisionCameraSource
    {
        /// <inheritdoc/>
        public async Task<ArrayOf<RenderedVisionCamera>> DiscoverAsync(
            CompanionContext context, CancellationToken cancellationToken)
        {
            using CancellationTokenSource bounded = CellCompanionSupport.BeginOperation(
                context, Opc.Ua.Vision.Perception.Namespaces.Vision, cancellationToken);
            var client = new VisionClient(context.Session, context.Telemetry);
            var result = new List<RenderedVisionCamera>();
            int count = 0;
            await foreach (VisionNodeEntry entry in client.EnumerateSensorsAsync(bounded.Token)
                .WithCancellation(bounded.Token).ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(
                    ++count, Math.Min(context.MaxTargets, 32), "Simulated camera candidates");
                VisionSensorIdentity identity = await client.Sensor(entry.NodeId).ReadIdentityAsync(bounded.Token)
                    .ConfigureAwait(false);
                if (identity.RealityKind != VisionRealityKindEnum.Simulated)
                {
                    continue;
                }
                RenderedVisionCamera? camera = await ReadAsync(context, identity, bounded.Token).ConfigureAwait(false);
                if (camera is not null)
                {
                    result.Add(camera);
                }
            }
            return [.. result];
        }

        /// <inheritdoc/>
        public async Task RequireCurrentAsync(
            CompanionContext context, RenderedVisionCamera camera, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(camera);
            using CancellationTokenSource bounded = CellCompanionSupport.BeginOperation(
                context, Opc.Ua.Vision.Perception.Namespaces.Vision, cancellationToken);
            var client = new VisionClient(context.Session, context.Telemetry);
            VisionSensorIdentity identity = await client.Sensor(camera.Sensor).ReadIdentityAsync(bounded.Token)
                .ConfigureAwait(false);
            if (identity.RealityKind != VisionRealityKindEnum.Simulated ||
                await ReadAsync(context, identity, bounded.Token).ConfigureAwait(false) != camera)
            {
                throw new InvalidOperationException(
                    "The simulated camera identity changed. Discover its metadata again.");
            }
        }

        private static async Task<RenderedVisionCamera?> ReadAsync(
            CompanionContext context, VisionSensorIdentity sensor, CancellationToken cancellationToken)
        {
            NodeId simulated = NodeId.Null;
            NodeId expected = ExpandedNodeId.ToNodeId(
                Opc.Ua.Vision.Perception.ObjectTypeIds.IVisionSimulatedType, context.Session.NamespaceUris);
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, sensor.NodeId, BrowseDirection.Forward, Opc.Ua.ReferenceTypeIds.HasInterface,
                NodeClass.Object, new IndustrialBrowseBudget(context), cancellationToken).ConfigureAwait(false))
            {
                if (IndustrialCompanionAccess.LocalId(context, reference.TypeDefinition) != expected)
                {
                    continue;
                }
                NodeId node = IndustrialCompanionAccess.LocalId(context, reference.NodeId);
                if (node.IsNull || !simulated.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyMatches,
                        "A simulated camera requires one local IVisionSimulated interface.");
                }
                simulated = node;
            }
            if (simulated.IsNull)
            {
                return null;
            }
            ArrayOf<CompanionValue> values = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, simulated, Opc.Ua.Vision.Perception.Namespaces.Vision,
                ["StageIdentifier", "PrimPath"], cancellationToken).ConfigureAwait(false);
            string stage = RequireText(values[0].Value, 1024);
            string prim = RequireText(values[1].Value, 512);
            OpenUsdCompanionExporter.ValidatePrimPath(prim);
            if (string.IsNullOrEmpty(sensor.SensorId) || string.IsNullOrEmpty(sensor.FrameId))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "A simulated camera requires sensor and coordinate-frame identities.");
            }
            return new RenderedVisionCamera(sensor.NodeId, simulated, sensor.SensorId, sensor.FrameId, stage, prim);
        }

        private static string RequireText(Variant value, int maximum)
        {
            if (!value.TryGetValue(out string? text) || string.IsNullOrWhiteSpace(text) || text.Length > maximum)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid simulated camera metadata.");
            }
            return text;
        }
    }
}
