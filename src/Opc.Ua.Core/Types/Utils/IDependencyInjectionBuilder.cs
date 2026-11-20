// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua
{
    /// <summary>
    /// Add services to the container
    /// </summary>
    public interface IDependencyInjectionBuilder
    {
        /// <summary>
        /// DI services
        /// </summary>
        IServiceCollection Services { get; }
    }
}
