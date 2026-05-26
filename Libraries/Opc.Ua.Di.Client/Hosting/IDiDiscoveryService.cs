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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Client.Hosting
{
    /// <summary>
    /// DI-hosting-friendly facade around
    /// <see cref="DiDiscoveryClient"/>. Resolved from the service
    /// provider after <c>AddOpcUaDi()</c> on the client builder, this
    /// service uses the application's lazy
    /// <c>Func&lt;CancellationToken, Task&lt;ManagedSession&gt;&gt;</c>
    /// to enumerate DI devices on the connected server.
    /// </summary>
    public interface IDiDiscoveryService
    {
        /// <summary>
        /// Enumerates all <c>DeviceType</c> instances (including
        /// subtypes) reachable from the <c>Objects</c> folder on the
        /// connected server.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A list of discovered devices, each carrying the device
        /// NodeId, display name, and detected type definition.
        /// </returns>
        ValueTask<IReadOnlyList<DeviceEntry>> EnumerateDevicesAsync(
            CancellationToken cancellationToken = default);
    }
}
