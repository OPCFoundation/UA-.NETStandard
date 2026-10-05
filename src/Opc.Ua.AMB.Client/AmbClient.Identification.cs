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

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        private static readonly string[] s_identification =
        [
            Opc.Ua.Di.BrowseNames.ProductInstanceUri,
            Opc.Ua.Di.BrowseNames.AssetId,
            Opc.Ua.Di.BrowseNames.Manufacturer,
            Opc.Ua.Di.BrowseNames.ManufacturerUri,
            Opc.Ua.Di.BrowseNames.Model,
            Opc.Ua.Di.BrowseNames.ProductCode,
            Opc.Ua.Di.BrowseNames.SerialNumber,
            Opc.Ua.Di.BrowseNames.HardwareRevision,
            Opc.Ua.Di.BrowseNames.SoftwareRevision,
            Opc.Ua.Di.BrowseNames.RevisionCounter
        ];

        /// <summary>
        /// Reads the identification of an asset (OPC 10000-110 §7, §10.2):
        /// each property on the asset first, then in its
        /// <c>2:Identification</c> group.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The identification; what the asset does not publish is null.</returns>
        public async ValueTask<AssetIdentificationRecord> ReadIdentificationAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            NodeId[] nodes = await ResolveIdentificationAsync(asset, cancellationToken).ConfigureAwait(false);
            DataValue[] values = await ReadAsync(nodes, Attributes.Value, cancellationToken).ConfigureAwait(false);
            Variant Value(int index) => StatusCode.IsGood(values[index].StatusCode)
                ? values[index].WrappedValue
                : Variant.Null;

            return new AssetIdentificationRecord
            {
                Asset = asset,
                ProductInstanceUri = StringOf(Value(0)),
                AssetId = StringOf(Value(1)),
                Manufacturer = TextOf(Value(2)),
                ManufacturerUri = StringOf(Value(3)),
                Model = TextOf(Value(4)),
                ProductCode = StringOf(Value(5)),
                SerialNumber = StringOf(Value(6)),
                HardwareRevision = StringOf(Value(7)),
                SoftwareRevision = StringOf(Value(8)),
                // OPC 10000-100: -1 says the asset does not support it.
                RevisionCounter = Value(9).TryGetValue(out int counter) && counter >= 0 ? counter : null
            };
        }

        /// <summary>
        /// Writes the <c>AssetId</c> of an asset ("AMB Configurable Asset
        /// Identification"); the server moves the asset in
        /// <c>AssetsByAssetId</c>.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="assetId">The new AssetId; null or empty clears it.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNodeIdUnknown"/> when the asset has no
        /// <c>AssetId</c>, or the status the server answered the write with.
        /// </exception>
        public async ValueTask WriteAssetIdAsync(
            NodeId asset,
            string? assetId,
            CancellationToken cancellationToken = default)
        {
            NodeId[] nodes = await ResolveIdentificationAsync(asset, cancellationToken).ConfigureAwait(false);
            await WriteValueAsync(
                nodes[1],
                assetId == null ? Variant.Null : Variant.From(assetId),
                "The asset publishes no AssetId.",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the value of a node and turns a bad status into an exception.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNodeIdUnknown"/> when the node is null, or
        /// the status the server answered the write with.
        /// </exception>
        internal async ValueTask WriteValueAsync(
            NodeId node,
            Variant value,
            string missing,
            CancellationToken cancellationToken)
        {
            if (node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, missing);
            }
            WriteResponse response = await Session.WriteAsync(
                null,
                [new WriteValue { NodeId = node, AttributeId = Attributes.Value, Value = new DataValue(value) }],
                cancellationToken).ConfigureAwait(false);
            StatusCode status = response.Results[0];
            if (StatusCode.IsBad(status))
            {
                throw new ServiceResultException(status);
            }
        }

        private async ValueTask<NodeId[]> ResolveIdentificationAsync(NodeId asset, CancellationToken cancellationToken)
        {
            QualifiedName group = Di(Opc.Ua.Di.BrowseNames.Identification);
            var paths = new QualifiedName[s_identification.Length * 2][];
            for (int ii = 0; ii < s_identification.Length; ii++)
            {
                QualifiedName name = Di(s_identification[ii]);
                paths[ii] = [name];
                paths[s_identification.Length + ii] = [group, name];
            }
            NodeId[] resolved = await ResolveAsync(asset, paths, cancellationToken).ConfigureAwait(false);
            var nodes = new NodeId[s_identification.Length];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                nodes[ii] = resolved[ii].IsNull ? resolved[s_identification.Length + ii] : resolved[ii];
            }
            return nodes;
        }
    }
}
