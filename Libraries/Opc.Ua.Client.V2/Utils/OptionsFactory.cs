// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Create options
    /// </summary>
    internal static class OptionsFactory
    {
        /// <summary>
        /// Create monitor
        /// </summary>
        /// <param name="options"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static OptionsMonitor<T> Create<[DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
            T options)
        {
            return new(options);
        }

        /// <summary>
        /// Create monitor
        /// </summary>
        /// <param name="configure"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static OptionsMonitor<T> Create<[DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
            Func<T, T>? configure = null) where T : new()
        {
            var options = new OptionsMonitor<T>(new T());
            if (configure != null)
            {
                options.CurrentValue = configure(options.CurrentValue);
            }
            return options;
        }
    }
}
