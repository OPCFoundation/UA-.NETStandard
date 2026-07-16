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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Minimal client-channel surface that
    /// <see cref="ClientChannelManager"/> consumes when creating
    /// transport channels.
    /// </summary>
    /// <remarks>
    /// The full transport binding registry
    /// (<see cref="ITransportBindingRegistry"/>) implements this
    /// interface so a single
    /// <see cref="DefaultTransportBindingRegistry"/> instance satisfies
    /// both the channel-creation surface
    /// (<see cref="ITransportChannelBindings"/>) and the broader
    /// listener / channel registration surface
    /// (<see cref="ITransportBindingRegistry"/>).
    /// </remarks>
    public interface ITransportChannelBindings
    {
        /// <summary>
        /// Create a channel for the specified uri scheme. Returns
        /// <c>null</c> when no factory is registered for the scheme.
        /// </summary>
        /// <param name="uriScheme">The URI scheme of the transport.</param>
        /// <param name="telemetry">Telemetry context for the new channel.</param>
        ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry);
    }
}
