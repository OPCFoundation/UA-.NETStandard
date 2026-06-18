/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Client-side wrapper for the OPC 10000-100 §10.3 software-update
    /// facet of a DI device. Reads the device's
    /// <c>SoftwareVersion</c> child and (when present) exposes a NodeId
    /// for the <c>SoftwareUpdate</c> object so callers can drive the
    /// underlying state machines through the standard <c>Call</c>
    /// service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The v1 surface is intentionally minimal — it focuses on the
    /// read-only discovery operations that every DI client needs.
    /// Method invocation against the loading / installation /
    /// power-cycle / confirmation state machines remains an
    /// application-specific concern that the typed
    /// <c>SoftwareUpdateState</c> proxies (generated from the model)
    /// handle directly.
    /// </para>
    /// </remarks>
    public partial class SoftwareUpdateClient
    {
        /// <summary>
        /// Creates a new software-update client rooted at the supplied
        /// <c>SoftwareUpdateType</c> instance.
        /// </summary>
        public SoftwareUpdateClient(
            ISession session,
            NodeId softwareUpdateNodeId,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (softwareUpdateNodeId.IsNull)
            {
                throw new ArgumentException(
                    "SoftwareUpdate NodeId is required.",
                    nameof(softwareUpdateNodeId));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            Session = session;
            SoftwareUpdateNodeId = softwareUpdateNodeId;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The owning session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>The <c>SoftwareUpdateType</c> instance NodeId.</summary>
        public NodeId SoftwareUpdateNodeId { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Reads the software version property
        /// (<c>SoftwareVersion</c>) of the underlying device. Returns
        /// an empty string when the property is absent.
        /// </summary>
        public async ValueTask<string> ReadSoftwareVersionAsync(
            CancellationToken cancellationToken = default)
        {
            ushort diNs = Session.NamespaceUris
                .GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);

            BrowsePath browsePath = new BrowsePath
            {
                StartingNode = SoftwareUpdateNodeId,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasProperty,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("SoftwareVersion", diNs)
                        }
                    ]
                }
            };

            TranslateBrowsePathsToNodeIdsResponse translateResponse = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    new[] { browsePath }.ToArrayOf(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (translateResponse.Results.Count == 0)

            {
                return string.Empty;
            }
            BrowsePathResult result = translateResponse.Results[0];
            if (!StatusCode.IsGood(result.StatusCode) || result.Targets.Count == 0)
            {
                return string.Empty;
            }

            NodeId targetId = ExpandedNodeId.ToNodeId(
                result.Targets[0].TargetId, Session.NamespaceUris);

            ReadValueId readItem = new ReadValueId
            {
                NodeId = targetId,
                AttributeId = Attributes.Value
            };

            ReadResponse readResponse = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[] { readItem }.ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);

            if (readResponse.Results.Count == 0)

            {
                return string.Empty;
            }
            DataValue dv = readResponse.Results[0];
            if (!StatusCode.IsGood(dv.StatusCode))
            {
                return string.Empty;
            }
            return dv.WrappedValue.TryGetValue(out string s) ? s : string.Empty;
        }
    }
}
