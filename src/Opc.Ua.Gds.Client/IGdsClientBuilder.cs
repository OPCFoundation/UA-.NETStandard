// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Fluent helper returned by
    /// <c>OpcUaGdsClientBuilderExtensions.AddGdsClient</c> for further
    /// configuration of the OPC UA GDS client services.
    /// </summary>
    public interface IGdsClientBuilder
    {
        /// <summary>
        /// The underlying <see cref="IServiceCollection"/>.
        /// </summary>
        IServiceCollection Services { get; }
    }
}
