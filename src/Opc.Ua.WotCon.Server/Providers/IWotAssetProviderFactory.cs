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
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Factory that creates <see cref="IWotAssetProvider"/> instances for
    /// a specific WoT protocol binding.
    /// </summary>
    /// <remarks>
    /// Implementations are registered through
    /// <see cref="WotConnectivityServerOptions.Bindings"/> and selected by
    /// <see cref="CanHandle(ThingDescription)"/> when the server materialises
    /// a new asset.
    /// </remarks>
    public interface IWotAssetProviderFactory
    {
        /// <summary>
        /// URIs of the WoT protocol bindings this factory understands.
        /// They are surfaced through the
        /// <c>WoTAssetConnectionManagementType.SupportedWoTBindings</c>
        /// property (OPC 10100-1 §6.3.1.1).
        /// </summary>
        IReadOnlyCollection<string> SupportedBindings { get; }

        /// <summary>
        /// Returns <c>true</c> if this factory can produce a provider for
        /// the supplied <see cref="ThingDescription"/>. Typically this is a
        /// check against the URI scheme of <see cref="ThingDescription.Base"/>.
        /// </summary>
        bool CanHandle(ThingDescription thingDescription);

        /// <summary>
        /// Creates and connects a provider for the supplied
        /// <see cref="ThingDescription"/>.
        /// </summary>
        ValueTask<IWotAssetProvider> ConnectAsync(
            ThingDescription thingDescription,
            CancellationToken ct);
    }
}
