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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Application-information payload of a discovery response per
    /// Part 14 §7.2.4.6.7.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.4.6.7">
    /// Part 14 §7.2.4.6.7</see>. Carried in the
    /// <see cref="UadpDiscoveryResponseMessage.ApplicationInformation"/>
    /// slot when
    /// <see cref="UadpDiscoveryResponseMessage.DiscoveryType"/> is
    /// <see cref="UadpDiscoveryType.ApplicationInformation"/>.
    /// </remarks>
    public sealed record UadpApplicationInformation
    {
        /// <summary>
        /// Display name of the publishing application.
        /// </summary>
        public LocalizedText ApplicationName { get; init; } = LocalizedText.Null;

        /// <summary>
        /// Application URI (must match the URI in the publisher's
        /// certificate, if signed).
        /// </summary>
        public string ApplicationUri { get; init; } = string.Empty;

        /// <summary>
        /// Product URI of the publisher's product.
        /// </summary>
        public string ProductUri { get; init; } = string.Empty;

        /// <summary>
        /// ApplicationType of the publisher.
        /// </summary>
        public ApplicationType ApplicationType { get; init; }
            = ApplicationType.Server;

        /// <summary>
        /// Optional capability identifiers (e.g. <c>UAMA</c>, <c>NA</c>).
        /// </summary>
        public IReadOnlyList<string> Capabilities { get; init; } = [];

        /// <summary>
        /// Supported transport profile URIs.
        /// </summary>
        public IReadOnlyList<string> SupportedTransportProfiles { get; init; } = [];

        /// <summary>
        /// Supported PubSub security policy URIs.
        /// </summary>
        public IReadOnlyList<string> SupportedSecurityPolicies { get; init; } = [];
    }
}
