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

namespace Opc.Ua.WotCon.Client.Hosting
{
    /// <summary>
    /// Top-level options for
    /// <c>Microsoft.Extensions.DependencyInjection.OpcUaWotConClientBuilderExtensions.AddWotConClient</c>.
    /// </summary>
    /// <remarks>
    /// Configurable client-side knobs for the WoT Connectivity client
    /// surface. Today this is intentionally minimal: the connection
    /// itself is owned by the regular <c>.AddClient(...)</c> session
    /// factory and the WoT Connectivity client only composes on top of
    /// an established <see cref="Opc.Ua.Client.ISession"/>.
    /// </remarks>
    public sealed class WotConClientOptions
    {
        /// <summary>
        /// When <c>true</c> (the default), the WoT Connectivity client
        /// factory delegate registered with DI lazily connects the
        /// underlying <see cref="Opc.Ua.Client.ManagedSession"/> the
        /// first time it is awaited. Set to <c>false</c> when callers
        /// supply their own session lifecycle and only want a typed
        /// wrapper.
        /// </summary>
        public bool LazyConnect { get; set; } = true;
    }
}
