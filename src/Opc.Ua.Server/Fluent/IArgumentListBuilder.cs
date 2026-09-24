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
    /// Defines a fluent builder for a list of OPC UA method arguments.
    /// </summary>
    public interface IArgumentListBuilder
    {
        /// <summary>
        /// Gets the built argument list.
        /// </summary>
        Argument[] Items { get; }

        /// <summary>
        /// Adds an argument to the list.
        /// </summary>
        /// <param name="argument">
        /// The argument to add.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentListBuilder Add(Argument argument);

        /// <summary>
        /// Adds an argument configured by an argument builder callback.
        /// </summary>
        /// <param name="configureArgument">
        /// The callback used to configure a newly created argument builder.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentListBuilder Add(Action<IArgumentBuilder> configureArgument);

        /// <summary>
        /// Adds arguments configured by argument builder callbacks.
        /// </summary>
        /// <param name="configureArguments">
        /// The callbacks used to configure newly created argument builders.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentListBuilder Add(params Action<IArgumentBuilder>[] configureArguments);
    }
}
