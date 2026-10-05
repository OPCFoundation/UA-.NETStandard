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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// The registry of the manageable assets of a server (OPC 10000-110
    /// §6), shared by every node manager that owns an asset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An asset stays in the node manager that created it - a Device
    /// Integration device, a Machinery machine, a pump or a scale - and is
    /// made manageable by registering it here from that manager's setup. The
    /// Asset Management Basics node manager is a sidecar: it owns the AMB
    /// model and the discovery entry points, and finds the assets through
    /// this registry.
    /// </para>
    /// <para>
    /// Registration is possible as soon as the AMB node manager exists, also
    /// before its address space is created; work that needs that address
    /// space is then deferred until it is.
    /// </para>
    /// </remarks>
    public interface IAssetManagement
    {
        /// <summary>
        /// Gets the registered assets, in registration order.
        /// </summary>
        ArrayOf<IAssetHandle> Assets { get; }

        /// <summary>
        /// Makes an object of the address space a manageable asset.
        /// </summary>
        /// <remarks>
        /// A registration that fails or is cancelled leaves the object
        /// unregistered and out of the alias categories, so it can be
        /// registered again.
        /// </remarks>
        /// <param name="asset">
        /// The builder of the asset, as its owning node manager hands it out,
        /// for example <c>IDeviceBuilder&lt;T&gt;.Node</c> or
        /// <c>IMachineHandle&lt;T&gt;.AsNode()</c>.
        /// </param>
        /// <param name="configure">
        /// Adds the AMB building blocks to the asset; <see langword="null"/>
        /// registers the asset as it is.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The handle through which the application drives the asset.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="asset"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the AMB node
        /// manager is not part of the server, the Device Integration namespace
        /// is missing or the asset has no <c>ProductInstanceUri</c> while one
        /// is required; <see cref="StatusCodes.BadNodeIdExists"/> when the
        /// object is already registered.
        /// </exception>
        ValueTask<IAssetHandle> RegisterAssetAsync(
            INodeBuilder<BaseObjectState> asset,
            Action<IAssetBuilder>? configure = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a registered asset by the NodeId of its object.
        /// </summary>
        /// <param name="nodeId">The NodeId of the asset object.</param>
        /// <param name="asset">The asset, when registered.</param>
        /// <returns><see langword="true"/> when the object is a registered asset.</returns>
        bool TryGetAsset(NodeId nodeId, [NotNullWhen(true)] out IAssetHandle? asset);

        /// <summary>
        /// Finds or creates a location below <c>HierarchicalLocations</c> or
        /// <c>OperationalLocations</c> (OPC 10000-110 §13.3.3, §13.4.3).
        /// </summary>
        /// <remarks>
        /// The first level is organized by the entry point, every deeper level
        /// is a component of the one above. Assets are put into a location
        /// with <see cref="IAssetBuilder.LocatedIn"/>, which creates it as
        /// well. Locations can be defined once the AMB address space exists,
        /// for example in <c>ConfigureAssetManagement</c>.
        /// </remarks>
        /// <param name="kind">
        /// <see cref="AssetLocationKind.Hierarchical"/> or
        /// <see cref="AssetLocationKind.Operational"/>.
        /// </param>
        /// <param name="path">The levels, separated by a slash, for example <c>Plant1/Hall3/Line2</c>.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The NodeId of the deepest level.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="kind"/> has no location objects, or
        /// <paramref name="path"/> is empty or has an empty level.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the AMB node
        /// manager is not part of the server.
        /// </exception>
        ValueTask<NodeId> DefineLocationAsync(
            AssetLocationKind kind,
            string path,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds or creates the dictionary entry of an IRDI (OPC 10000-19),
        /// so assets can be classified with it (OPC 10000-110 §11).
        /// </summary>
        /// <remarks>
        /// The entry is an <c>IrdiDictionaryEntryType</c> object below
        /// <c>Server/Dictionaries</c> whose NodeId is the IRDI in
        /// <see cref="AmbServerOptions.IrdiNamespaceUri"/>, so an asset can be
        /// classified with <c>new ExpandedNodeId(irdi, AmbServerOptions.IrdiNamespaceUri)</c>
        /// before the entry exists. "AMB Classification" counts only
        /// <c>HasDictionaryEntry</c> references to such entries, or to
        /// another <c>DictionaryEntryType</c> object of the server. Entries can
        /// be defined once the AMB address space exists, for example in
        /// <c>ConfigureAssetManagement</c>.
        /// </remarks>
        /// <param name="irdi">The IRDI, for example the ECLASS class <c>0173-1#01-AKE798#019</c>.</param>
        /// <param name="displayName">The display name; the IRDI when empty.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The NodeId of the entry.</returns>
        /// <exception cref="ArgumentException"><paramref name="irdi"/> is empty.</exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the AMB node
        /// manager is not part of the server.
        /// </exception>
        ValueTask<NodeId> DefineDictionaryEntryAsync(
            string irdi,
            LocalizedText displayName = default,
            CancellationToken cancellationToken = default);
    }
}
