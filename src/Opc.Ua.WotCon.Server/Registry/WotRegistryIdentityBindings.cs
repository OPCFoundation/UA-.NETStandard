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

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Explicit authority bindings for the unchanged generic xRegistry Methods.
    /// Typed provisioning needs no configured aliases.
    /// </summary>
    public sealed class WotRegistryIdentityBindings
    {
        /// <summary>
        /// Gets or sets the exact supplied group identifier bindings.
        /// </summary>
        public ArrayOf<WotRegistryGroupIdentity> Groups { get; set; } = [];

        /// <summary>
        /// Gets or sets the exact supplied resource identifier bindings, scoped by owning group.
        /// </summary>
        public ArrayOf<WotRegistryResourceIdentity> Resources { get; set; } = [];
    }

    /// <summary>
    /// Binds a generic group identifier to one exact catalogue authority and document kind.
    /// An existing unambiguous allocation is preserved; a new allocation uses the typed domain.
    /// </summary>
    /// <param name="GroupId">The configured generic identifier or an established group identifier.</param>
    /// <param name="Kind">ThingDescription or ThingModel, never All.</param>
    /// <param name="CatalogUri">The exact absolute catalogue URI.</param>
    public sealed record WotRegistryGroupIdentity(string GroupId, WoTDocumentKindEnum Kind, string CatalogUri);

    /// <summary>
    /// Binds a generic resource identifier to its exact source identity within one owning group.
    /// </summary>
    /// <param name="GroupId">The owning assigned group identifier or its configured alias.</param>
    /// <param name="ResourceId">The supplied resource identifier.</param>
    /// <param name="SourceId">The exact absolute ThingId or ModelId.</param>
    public sealed record WotRegistryResourceIdentity(string GroupId, string ResourceId, string SourceId);
}
