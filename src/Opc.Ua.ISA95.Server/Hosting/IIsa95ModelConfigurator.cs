// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Builders;

namespace Opc.Ua.ISA95.Server.Hosting
{
    /// <summary>
    /// Configures OPC-10030 instances after the ISA-95 root is registered.
    /// </summary>
    public interface IIsa95ModelConfigurator
    {
        /// <summary>
        /// Applies model configuration.
        /// </summary>
        ValueTask ConfigureAsync(
            IIsa95ModelBuilder builder,
            CancellationToken cancellationToken);
    }
}
