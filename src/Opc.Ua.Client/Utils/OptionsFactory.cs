// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua
{
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
