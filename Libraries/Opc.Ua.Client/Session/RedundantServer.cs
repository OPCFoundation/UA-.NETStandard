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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Information about a Server in a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// Corresponds to the OPC UA <c>RedundantServerDataType</c> defined in OPC 10000-5 §12.5 and used by
    /// OPC 10000-4 §6.6.2 server redundancy.
    /// </remarks>
    public sealed class RedundantServer
    {
        /// <summary>
        /// The ServerUri that identifies the Server.
        /// </summary>
        public string ServerUri { get; init; } = string.Empty;

        /// <summary>
        /// <c>ServiceLevel</c> (0–255, higher is better; see OPC 10000-4 §6.6.2.4.2 Table 105).
        /// </summary>
        public byte ServiceLevel { get; init; }

        /// <summary>
        /// The current state of the Server.
        /// </summary>
        public ServerState ServerState { get; init; }

        /// <summary>
        /// The resolved endpoint for this redundant server.
        /// </summary>
        public ConfiguredEndpoint? Endpoint { get; init; }
    }
}
