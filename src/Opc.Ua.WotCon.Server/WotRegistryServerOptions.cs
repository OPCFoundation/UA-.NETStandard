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
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Options for the WoT Connectivity V2 registry NodeManager. These are
    /// bindable from configuration (only the simple-typed members) and augmented
    /// at runtime with the persistence store, binder registry and management
    /// access policy.
    /// </summary>
    public sealed class WotRegistryServerOptions
    {
        /// <summary>
        /// Gets or sets the folder used by the file-backed registry store. When
        /// <c>null</c> the registry is kept in memory only.
        /// </summary>
        public string? StorageFolder { get; set; }

        /// <summary>
        /// Gets or sets whether the registry automatically re-projects after every
        /// content mutation. Defaults to <c>true</c>.
        /// </summary>
        public bool AutoRefresh { get; set; } = true;

        /// <summary>
        /// Gets or sets whether unsupported binding forms fail a strict closure
        /// (rather than materializing degraded nodes).
        /// </summary>
        public bool StrictBindings { get; set; }

        /// <summary>
        /// Gets or sets the id of the group into which legacy 1.02 assets are
        /// registered as Thing Description resources.
        /// </summary>
        public string LegacyGroupId { get; set; } = WotRegistryGroups.ThingDescriptions;

        /// <summary>Gets the resource bounds enforced by the registry service.</summary>
        public WotRegistryPersistenceBounds Bounds { get; } = new WotRegistryPersistenceBounds();

        /// <summary>
        /// Gets or sets the management access policy used to secure the registry
        /// management Methods.
        /// </summary>
        public WotManagementAccessPolicy ManagementAccess { get; set; }
            = new WotManagementAccessPolicy();

        /// <summary>
        /// Gets the WoT binding capabilities advertised by the registry
        /// <c>SupportedBindings</c> object. Empty in this phase (no concrete
        /// protocol binders are registered).
        /// </summary>
        public IList<V2.WoTBindingCapabilityDataType> SupportedBindings { get; }
            = new List<V2.WoTBindingCapabilityDataType>();
    }
}
