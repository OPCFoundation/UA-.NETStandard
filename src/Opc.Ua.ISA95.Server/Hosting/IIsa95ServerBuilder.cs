// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.ISA95.Server.Builders;

namespace Opc.Ua.ISA95.Server.Hosting
{
    /// <summary>
    /// Fluent registration surface returned by <c>AddIsa95Server</c>.
    /// </summary>
    public interface IIsa95ServerBuilder
    {
        /// <summary>
        /// Underlying dependency-injection service collection.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Registers a common-model configuration callback.
        /// </summary>
        IIsa95ServerBuilder ConfigureModel(
            Func<IIsa95ModelBuilder, CancellationToken, ValueTask> configure);
    }
}
