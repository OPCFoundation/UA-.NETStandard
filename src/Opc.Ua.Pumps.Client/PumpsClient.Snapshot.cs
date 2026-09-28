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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pumps.Client
{
    public sealed partial class PumpsClient
    {
        /// <summary>
        /// Reads everything one pump publishes: nameplate, configuration,
        /// operational data, supervision, maintenance, documentation and
        /// ports.
        /// </summary>
        /// <remarks>
        /// This is the expensive call - on a pump that publishes the full
        /// OPC 40223 surface it browses and reads every one of the roughly
        /// 350 variables the specification declares. Use it to take stock of
        /// an unfamiliar pump, and the individual group reads (
        /// <see cref="ReadMeasurementsAsync"/> above all) for anything on a
        /// cycle.
        /// </remarks>
        /// <param name="pump">The pump to read.</param>
        /// <param name="options">What to fetch for each group.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<PumpSnapshot> ReadPumpAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (pump.IsNull)
            {
                throw new System.ArgumentException(
                    "A pump NodeId is required.",
                    nameof(pump));
            }

            QualifiedName browseName = QualifiedName.Null;
            LocalizedText displayName = LocalizedText.Null;
            NodeId typeDefinition = NodeId.Null;

            ReadResponse attributes = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: new[]
                {
                    new ReadValueId { NodeId = pump, AttributeId = Attributes.BrowseName },
                    new ReadValueId { NodeId = pump, AttributeId = Attributes.DisplayName }
                }.ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);
            if (attributes.Results.Count > 0 &&
                attributes.Results[0].WrappedValue.TryGetValue(out QualifiedName name))
            {
                browseName = name;
            }
            if (attributes.Results.Count > 1 &&
                attributes.Results[1].WrappedValue.TryGetValue(out LocalizedText display))
            {
                displayName = display;
            }

            await foreach (ReferenceDescription reference in BrowseTypeDefinitionAsync(
                    pump,
                    cancellationToken).ConfigureAwait(false))
            {
                typeDefinition = ExpandedNodeId.ToNodeId(
                    reference.NodeId,
                    Session.NamespaceUris);
                break;
            }

            return new PumpSnapshot
            {
                NodeId = pump,
                BrowseName = browseName,
                DisplayName = displayName,
                TypeDefinitionId = typeDefinition,
                Nameplate = await ReadNameplateAsync(pump, cancellationToken)
                    .ConfigureAwait(false),
                Configuration = await ReadConfigurationAsync(pump, options, cancellationToken)
                    .ConfigureAwait(false),
                Operational = await ReadOperationalAsync(pump, options, cancellationToken)
                    .ConfigureAwait(false),
                Supervision = await ReadSupervisionAsync(pump, options, cancellationToken)
                    .ConfigureAwait(false),
                Maintenance = await ReadMaintenanceAsync(pump, options, cancellationToken)
                    .ConfigureAwait(false),
                Documentation = await ReadDocumentationAsync(pump, cancellationToken)
                    .ConfigureAwait(false),
                Ports = await ReadPortsAsync(pump, options, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Browses the <c>HasTypeDefinition</c> reference of a node.
        /// </summary>
        private async System.Collections.Generic.IAsyncEnumerable<ReferenceDescription>
            BrowseTypeDefinitionAsync(
                NodeId nodeId,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken)
        {
            var browser = new Opc.Ua.Client.Browser(Session, new Opc.Ua.Client.BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasTypeDefinition,
                IncludeSubtypes = false,
                NodeClassMask = (int)NodeClass.ObjectType,
                ResultMask = (uint)BrowseResultMask.All
            });

            ArrayOf<ReferenceDescription> references;
            try
            {
                references = await browser.BrowseAsync(nodeId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (StatusCode.IsBad(ex.StatusCode))
            {
                yield break;
            }
            for (int ii = 0; ii < references.Count; ii++)
            {
                yield return references[ii];
            }
        }
    }
}
