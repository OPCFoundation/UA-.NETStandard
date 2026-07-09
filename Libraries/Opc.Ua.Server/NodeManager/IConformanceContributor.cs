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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Implemented by a node manager (or other server feature) that contributes
    /// the OPC UA conformance units and server profiles it enables.
    /// </summary>
    /// <remarks>
    /// A server can aggregate these contributions with its always-supported base
    /// set to publish <c>Server/ServerCapabilities/ConformanceUnits</c> and the
    /// <c>ServerProfileArray</c> derived from what is actually wired up (which
    /// node managers were added / which features were enabled via DI) rather than
    /// a fixed hard-coded list. See OPC UA Part 7 (Profiles) and Part 5
    /// (ServerCapabilities). Contributors that enable nothing return empty
    /// collections; the aggregating server de-duplicates across contributors.
    /// </remarks>
    public interface IConformanceContributor
    {
        /// <summary>
        /// The conformance units enabled by this contributor (may be empty).
        /// </summary>
        ArrayOf<QualifiedName> ConformanceUnits { get; }

        /// <summary>
        /// The server profile URIs enabled by this contributor (may be empty).
        /// </summary>
        ArrayOf<string> ServerProfiles { get; }
    }
}
