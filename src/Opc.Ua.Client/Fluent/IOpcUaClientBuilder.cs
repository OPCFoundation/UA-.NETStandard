/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Builder returned by
    /// <see cref="OpcUaClientBuilderExtensions.AddClient(IOpcUaBuilder, System.Action{OpcUaClientOptions})"/>
    /// for further configuration of the OPC UA client services.
    /// </summary>
    public interface IOpcUaClientBuilder
    {
        /// <summary>
        /// The underlying <see cref="IServiceCollection"/>.
        /// </summary>
        IServiceCollection Services { get; }
    }
}
