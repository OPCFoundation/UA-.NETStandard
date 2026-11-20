/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
