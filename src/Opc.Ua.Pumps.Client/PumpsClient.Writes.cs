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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Pumps.Client
{
    public sealed partial class PumpsClient
    {
        /// <summary>
        /// Writes the user-assigned asset identifier. The server controls authorization and retention.
        /// </summary>
        public ValueTask WriteAssetIdAsync(
            NodeId pump,
            string assetId,
            CancellationToken cancellationToken = default)
        {
            if (assetId is null)
            {
                throw new ArgumentNullException(nameof(assetId));
            }
            return WriteIdentificationAsync(
                pump,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Opc.Ua.Di.BrowseNames.AssetId,
                assetId,
                static proxy => proxy.AssetIdVariable(),
                cancellationToken);
        }

        /// <summary>
        /// Writes the component name without discarding its locale.
        /// </summary>
        public ValueTask WriteComponentNameAsync(
            NodeId pump,
            LocalizedText componentName,
            CancellationToken cancellationToken = default)
        {
            if (componentName.IsNull)
            {
                throw new ArgumentException("A component name is required.", nameof(componentName));
            }
            return WriteIdentificationAsync(
                pump,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Opc.Ua.Di.BrowseNames.ComponentName,
                componentName,
                static proxy => proxy.ComponentNameVariable(),
                cancellationToken);
        }

        /// <summary>
        /// Writes the application-assigned location, not the manufacturer's physical address.
        /// </summary>
        public ValueTask WriteLocationAsync(
            NodeId pump,
            string location,
            CancellationToken cancellationToken = default)
        {
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }
            return WriteIdentificationAsync(
                pump,
                Opc.Ua.Machinery.Namespaces.Machinery,
                Opc.Ua.Machinery.BrowseNames.Location,
                location,
                static proxy => proxy.LocationVariable(),
                cancellationToken);
        }

        /// <summary>
        /// Resolves an instance-writable field with the existing Pumps bounds and validates the complete write result.
        /// </summary>
        /// <typeparam name="T">The generated identity member's CLR value type.</typeparam>
        private async ValueTask WriteIdentificationAsync<T>(
            NodeId pump,
            string namespaceUri,
            string browseName,
            T value,
            Func<PumpIdentificationTypeClient, ClientVariable<T>> variable,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pump.IsNull)
            {
                throw new ArgumentException("A pump NodeId is required.", nameof(pump));
            }
            NodeId identification = await ResolveChildAsync(
                pump,
                new QualifiedName(PumpsModel.DiGroups.Identification, NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi)),
                cancellationToken,
                requireCompleteTarget: true).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (identification.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "The identification group is absent.");
            }
            int namespaceIndex = Session.NamespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    "The server does not publish the identity member's namespace.");
            }
            NodeId target = await ResolveChildAsync(
                identification,
                new QualifiedName(browseName, (ushort)namespaceIndex),
                cancellationToken,
                requireCompleteTarget: true).ConfigureAwait(false);
            if (target.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "The identification field is absent.");
            }
            var proxy = new PumpIdentificationTypeClient(Session, identification, Telemetry);
            await variable(proxy).WriteInstanceAsync(target, value, cancellationToken).ConfigureAwait(false);
        }
    }
}
