/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Fluent extensions on <see cref="NodeManagerBuilder"/> that let
    /// hand-written managers express the standard
    /// <c>CreateFluentBuilder(ns).Configure(Configure).Seal()</c>
    /// pipeline as a single chained expression rather than an
    /// imperative four-step block.
    /// </summary>
    public static class FluentNodeManagerBuilderExtensions
    {
        /// <summary>
        /// Invokes the supplied <paramref name="configure"/> delegate
        /// with this <paramref name="builder"/>, returning the builder
        /// for further chaining. Equivalent to writing
        /// <c>configure(builder); return builder;</c> at the callsite
        /// but lets the caller compose the fluent pipeline without
        /// breaking the chain.
        /// </summary>
        /// <param name="builder">The fluent node-manager builder.</param>
        /// <param name="configure">
        /// The user's <c>Configure</c> partial (typically the
        /// <c>partial void Configure(INodeManagerBuilder builder)</c>
        /// method group on the manager).
        /// </param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="configure"/> is null.
        /// </exception>
        public static NodeManagerBuilder Configure(
            this NodeManagerBuilder builder,
            Action<INodeManagerBuilder> configure)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(builder);
            return builder;
        }
    }
}
