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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// A connected client. Wraps the real session together with the
    /// generated Vision client and (when Robot Intent is wired) a
    /// lookup helper for the intent controller — so tests can express
    /// what a client does end-to-end without repeating the plumbing.
    /// </summary>
    internal sealed class VisionIntentClientContext : IAsyncDisposable
    {
        public VisionIntentClientContext(
            ISession session, ITelemetryContext telemetry, IStreamingSubscription streaming)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_streaming = streaming ?? throw new ArgumentNullException(nameof(streaming));
            Vision = new VisionClient(session, telemetry);
        }

        public ISession Session { get; }

        public VisionClient Vision { get; }

        public async ValueTask<RobotIntentControllerClient> GetControllerAsync(string name)
        {
            var discovery = new RobotIntentClient(Session, m_telemetry, m_streaming);
            ArrayOf<RobotIntentNodeLookupEntry> controllers = await discovery.DiscoverControllersAsync()
                .ConfigureAwait(false);
            RobotIntentNodeLookupEntry[] snapshot = controllers.ToArray()!;
            RobotIntentNodeLookupEntry? entry = snapshot.SingleOrDefault(
                controller => controller.Name == name);
            string names = string.Join(", ", snapshot.Select(c => c.Name));
            Assert.That(
                entry,
                Is.Not.Null,
                FormattableString.Invariant(
                    $"Controller '{name}' was not discovered. Controllers: {names}."));
            return discovery.Controller(entry!.NodeId);
        }

        /// <summary>
        /// Resolves the Vision/Sensors folder NodeId by browse from the
        /// Vision root. The generated <see cref="VisionClient.SensorsFolderId"/>
        /// returns the well-known type-level NodeId
        /// <c>Objects.Vision_Sensors</c>, but the Server's NodeIdFactory
        /// rebases the Mandatory Sensors child of the Vision root onto a
        /// per-instance NodeId — so the well-known ID resolves to an orphan
        /// node with no children in every hosted deployment. Resolving by
        /// browse is what a compliant client must do anyway. Filed against
        /// the Vision.Server as a defect.
        /// </summary>
        public ValueTask<NodeId> ResolveSensorsFolderIdAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId root = Vision.VisionRootId;
            return BrowseChildByBrowseNameAsync(root, "Sensors", cancellationToken);
        }

        /// <summary>
        /// Enumerates every sensor under the Vision/Sensors folder by
        /// direct browse, tolerating the same well-known NodeId defect as
        /// <see cref="ResolveSensorsFolderIdAsync"/>.
        /// </summary>
        public async ValueTask<ArrayOf<NodeId>> DiscoverSensorNodeIdsAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId sensorsFolder = await ResolveSensorsFolderIdAsync(cancellationToken)
                .ConfigureAwait(false);
            if (sensorsFolder.IsNull)
            {
                return ArrayOf<NodeId>.Empty;
            }
            List<NodeId> sensors = new();
            ArrayOf<ReferenceDescription> refs = await BrowseHierarchicalObjectsAsync(
                sensorsFolder, cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < refs.Count; ii++)
            {
                NodeId child = ExpandedNodeId.ToNodeId(refs[ii].NodeId, Session.NamespaceUris);
                if (!child.IsNull)
                {
                    sensors.Add(child);
                }
            }
            return sensors.ToArrayOf();
        }

        private async ValueTask<NodeId> BrowseChildByBrowseNameAsync(
            NodeId parent, string browseName, CancellationToken cancellationToken)
        {
            if (parent.IsNull)
            {
                return NodeId.Null;
            }
            ArrayOf<ReferenceDescription> refs = await BrowseHierarchicalObjectsAsync(
                parent, cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < refs.Count; ii++)
            {
                if (string.Equals(refs[ii].BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return ExpandedNodeId.ToNodeId(refs[ii].NodeId, Session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseHierarchicalObjectsAsync(
            NodeId parent, CancellationToken cancellationToken)
        {
            var description = new BrowseDescription
            {
                NodeId = parent,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0u, new[] { description }.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                return ArrayOf<ReferenceDescription>.Empty;
            }
            return response.Results[0].References;
        }

        public async ValueTask DisposeAsync()
        {
            if (Session.Connected)
            {
                try
                {
                    await Session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                catch
                {
                }
            }
            try
            {
                await m_streaming.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            Session.Dispose();
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly IStreamingSubscription m_streaming;
    }
}
