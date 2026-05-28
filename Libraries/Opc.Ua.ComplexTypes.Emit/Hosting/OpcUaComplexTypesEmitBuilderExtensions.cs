/* ========================================================================
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

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.ComplexTypes;
using Opc.Ua.ComplexTypes.Emit;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Opt-in <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.ComplexTypes.Emit</c>: swap the default AOT-friendly
    /// <see cref="ComplexTypeSystemFactory"/> registration for one
    /// that materialises custom DataTypes as runtime .NET classes via
    /// <see cref="ComplexTypeBuilderFactory"/>.
    /// </summary>
    public static class OpcUaComplexTypesEmitBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="ComplexTypeSystemFactory"/> that
        /// produces <see cref="ComplexTypeSystem"/> instances backed by
        /// the Reflection.Emit-based
        /// <see cref="ComplexTypeBuilderFactory"/>. Replaces the
        /// AOT-friendly default registration installed by
        /// <c>AddComplexTypes()</c>.
        /// </summary>
        /// <remarks>
        /// Calling this method also runs <c>AddComplexTypes()</c> so
        /// callers do not need to chain both. Idempotent — calling
        /// twice does not stack registrations.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddComplexTypesWithReflectionEmit(
            this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            OpcUaComplexTypesBuilderExtensions.AddComplexTypes(builder);
            builder.Services.Replace(
                ServiceDescriptor.Singleton(
                    static sp => new ComplexTypeSystemFactory(
                        sp.GetRequiredService<ITelemetryContext>(),
                        static () => new ComplexTypeBuilderFactory())));
            return builder;
        }
    }
}
