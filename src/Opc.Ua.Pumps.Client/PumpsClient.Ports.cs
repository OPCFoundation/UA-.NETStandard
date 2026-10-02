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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pumps.Client
{
    public sealed partial class PumpsClient
    {
        /// <summary>
        /// Resolves a pump's <c>Ports</c> group, or <see cref="NodeId.Null"/>
        /// when it publishes none.
        /// </summary>
        /// <param name="pump">The pump to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolvePortsAsync(
            NodeId pump,
            CancellationToken cancellationToken = default)
        {
            return ResolvePumpsChildAsync(pump, BrowseNames.Ports, cancellationToken);
        }

        /// <summary>
        /// Enumerates the ports below a pump's <c>Ports</c> group.
        /// </summary>
        /// <remarks>
        /// <c>PortsGroupType</c> declares no children - which ports a pump has
        /// is decided per instance - so they are discovered by browsing and
        /// classified from their type definition. A vendor subtype of
        /// <c>PortType</c> that is none of the three standard ports comes back
        /// as <see cref="PumpPortKind.Unknown"/> with its own groups still
        /// read.
        /// </remarks>
        /// <param name="pump">The pump to inspect.</param>
        /// <param name="options">What to fetch for each port group.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<PumpPortDescriptor> EnumeratePortsAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId ports = await ResolvePortsAsync(pump, cancellationToken)
                .ConfigureAwait(false);
            if (ports.IsNull)
            {
                yield break;
            }

            var discovered = new List<(NodeId NodeId, string Name, NodeId TypeDefinition)>();
            await foreach (ReferenceDescription reference in BrowseChildrenAsync(
                    ports,
                    NodeClass.Object,
                    cancellationToken).ConfigureAwait(false))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (!nodeId.IsNull)
                {
                    discovered.Add((
                        nodeId,
                        NameOf(reference.BrowseName),
                        ExpandedNodeId.ToNodeId(
                            reference.TypeDefinition,
                            Session.NamespaceUris)));
                }
            }

            foreach ((NodeId nodeId, string name, NodeId typeDefinition) in discovered)
            {
                yield return await ReadPortAsync(
                    nodeId,
                    name,
                    typeDefinition,
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads every port of a pump.
        /// </summary>
        /// <param name="pump">The pump to inspect.</param>
        /// <param name="options">What to fetch for each port group.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<PumpPortDescriptor>> ReadPortsAsync(
            NodeId pump,
            PumpReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var ports = new List<PumpPortDescriptor>();
            await foreach (PumpPortDescriptor port in EnumeratePortsAsync(
                    pump,
                    options,
                    cancellationToken).ConfigureAwait(false))
            {
                ports.Add(port);
            }
            return ports.ToArrayOf();
        }

        /// <summary>
        /// Reads one port: its own attributes, then whichever of the four
        /// groups its kind carries.
        /// </summary>
        private async ValueTask<PumpPortDescriptor> ReadPortAsync(
            NodeId port,
            string name,
            NodeId typeDefinition,
            PumpReadOptions? options,
            CancellationToken cancellationToken)
        {
            PumpPortKind kind = PumpsModel.ClassifyPort(typeDefinition, Session.NamespaceUris);

            // Category, Direction and IdCarrier are variables directly on the
            // port; the four groups below it are objects and drop out of this
            // set on their own.
            PumpValueSet attributes = await ReadValueSetAsync(
                port,
                PumpReadOptions.ValuesOnly,
                cancellationToken).ConfigureAwait(false);

            PumpValueSet design = await ReadPumpsChildSetAsync(
                port, BrowseNames.Design, options, cancellationToken).ConfigureAwait(false);
            PumpValueSet measurements = await ReadPumpsChildSetAsync(
                port, BrowseNames.Measurements, options, cancellationToken)
                .ConfigureAwait(false);

            // A drive port has neither of these; resolving them costs one
            // round trip that returns nothing, so it is skipped.
            PumpValueSet implementation = PumpValueSet.Empty;
            PumpValueSet requirements = PumpValueSet.Empty;
            if (kind != PumpPortKind.Drive)
            {
                implementation = await ReadPumpsChildSetAsync(
                    port, BrowseNames.Implementation, options, cancellationToken)
                    .ConfigureAwait(false);
                requirements = await ReadPumpsChildSetAsync(
                    port, BrowseNames.SystemRequirements, options, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new PumpPortDescriptor
            {
                NodeId = port,
                Name = name,
                TypeDefinitionId = typeDefinition,
                Kind = kind,
                Direction = attributes.GetEnum<PortDirectionEnum>(BrowseNames.Direction),
                Category = attributes.GetString(BrowseNames.Category),
                IdCarrier = attributes.GetString(BrowseNames.IdCarrier),
                Design = design,
                Implementation = implementation,
                Measurements = measurements,
                SystemRequirements = requirements
            };
        }
    }
}
