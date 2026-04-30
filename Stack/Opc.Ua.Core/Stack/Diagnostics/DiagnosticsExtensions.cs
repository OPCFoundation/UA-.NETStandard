
<<<<<<< TODO: Unmerged change from project 'Opc.Ua.Core(net48)', Before:
/* ========================================================================
=======
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using System;/* ========================================================================
>>>>>>> After
using System;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Opc.Ua;/* ========================================================================
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

namespace Microsoft.Extensions.DependencyInjection
{

    /// <summary>
    /// Service collection extensions
    /// </summary>
    public static class DiagnosticsExtensions
    {
        /// <summary>
        /// Add logging to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddLogging(
            this IDependencyInjectionBuilder builder)
        {
            builder.Services.AddLogging();
            return builder;
        }


        /// <summary>
        /// Add logging to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddLogging(
            this IDependencyInjectionBuilder builder, Action<ILoggingBuilder> configure)
        {
            builder.Services.AddLogging(configure);
            return builder;
        }

        /// <summary>
        /// Add metrics support to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddMetrics(
            this IDependencyInjectionBuilder builder)
        {
            builder.Services.AddMetrics();
            return builder;
        }

        /// <summary>
        /// Add metrics support to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddMetrics(
            this IDependencyInjectionBuilder builder, Action<IMetricsBuilder> configure)
        {
            builder.Services.AddMetrics(configure);
            return builder;
        }
    }
}
