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

using System;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Computes the value of the server's OPC UA <c>ServiceLevel</c>
    /// variable (0–255). In a redundant set, clients connect to the server
    /// reporting the highest service level (see Part 4 §6.6.2 and the
    /// client-side <c>DefaultServerRedundancyHandler</c>), so a healthy
    /// active leader reports the maximum and standbys report a lower value.
    /// </summary>
    /// <remarks>
    /// Wire this into the server so the <c>Server.ServiceLevel</c> node is
    /// initialized from <see cref="GetServiceLevel"/> and updated whenever
    /// <see cref="ServiceLevelChanged"/> fires. The default
    /// (<see cref="ConstantServiceLevelProvider"/>) reports a fixed 255,
    /// preserving single-instance behavior.
    /// </remarks>
    public interface IServiceLevelProvider
    {
        /// <summary>
        /// The current service level (0 = out of service, 255 = highest).
        /// </summary>
        byte GetServiceLevel();

        /// <summary>
        /// Raised with the new service level whenever it changes.
        /// </summary>
        event Action<byte>? ServiceLevelChanged;
    }
}
