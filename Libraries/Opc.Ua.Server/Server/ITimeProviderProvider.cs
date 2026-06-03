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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that exposes a server's
    /// <see cref="TimeProvider"/> so subordinate components can resolve
    /// the server-wide clock without any change to
    /// <see cref="IServerInternal"/>; external/mocked
    /// <see cref="IServerInternal"/> implementations remain unaffected
    /// and can fall back to <see cref="TimeProvider.System"/> when this
    /// interface is not implemented.
    /// </summary>
    /// <remarks>
    /// Consumers should resolve the time provider in a null-safe manner,
    /// e.g.:
    /// <code>
    /// var tp = (server as ITimeProviderProvider)?.TimeProvider
    ///          ?? TimeProvider.System;
    /// </code>
    /// </remarks>
    public interface ITimeProviderProvider
    {
        /// <summary>
        /// Gets the server's <see cref="TimeProvider"/>; never
        /// <c>null</c>. Defaults to <see cref="TimeProvider.System"/>
        /// when the server was constructed without an explicit provider.
        /// </summary>
        TimeProvider TimeProvider { get; }
    }
}
