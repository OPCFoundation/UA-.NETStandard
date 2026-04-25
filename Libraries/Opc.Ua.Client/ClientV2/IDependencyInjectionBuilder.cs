#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.DependencyInjection;

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
#endif
