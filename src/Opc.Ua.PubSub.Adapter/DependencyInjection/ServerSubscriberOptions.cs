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

using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Options that configure an external-server PubSub subscriber wired through
    /// <c>AddServerAsSubscriber</c>. The subscriber writes the values
    /// received for each configured DataSetReader back to an external OPC UA
    /// server.
    /// </summary>
    /// <remarks>
    /// Simple properties are bindable from <c>IConfiguration</c>. Object-typed
    /// members, such as <see cref="ServerConnectionOptions.ApplicationConfiguration"/>
    /// and <see cref="ServerConnectionOptions.UserIdentity"/>, must be supplied
    /// from code.
    /// </remarks>
    public sealed class ServerSubscriberOptions
    {
        /// <summary>
        /// The connection options describing the external OPC UA server the
        /// subscriber writes to.
        /// </summary>
        public ServerConnectionOptions Connection { get; set; } = new();
    }
}
