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
using System.Collections.Generic;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// A builder for creating OPC UA method argument metadata lists.
    /// </summary>
    public sealed class ArgumentListBuilder : IArgumentListBuilder
    {
        /// <summary>
        /// Initializes an instance of the argument list builder.
        /// </summary>
        public ArgumentListBuilder()
        {
        }

        /// <inheritdoc/>
        public Argument[] Items => [.. m_items];

        /// <summary>
        /// Creates a new argument list builder.
        /// </summary>
        /// <returns>
        /// A new argument list builder.
        /// </returns>
        public static IArgumentListBuilder Create()
        {
            return new ArgumentListBuilder();
        }

        /// <summary>
        /// Creates a new argument list builder and applies argument configuration callbacks.
        /// </summary>
        /// <param name="configureArguments">
        /// The callbacks used to configure newly created argument builders.
        /// </param>
        /// <returns>
        /// A new argument list builder.
        /// </returns>
        public static IArgumentListBuilder Create(params Action<IArgumentBuilder>[] configureArguments)
        {
            return new ArgumentListBuilder().Add(configureArguments);
        }

        /// <inheritdoc/>
        public IArgumentListBuilder Add(Argument argument)
        {
            _ = argument ?? throw new ArgumentNullException(nameof(argument));
            m_items.Add(argument);
            return this;
        }

        /// <inheritdoc/>
        public IArgumentListBuilder Add(Action<IArgumentBuilder> configureArgument)
        {
            _ = configureArgument ?? throw new ArgumentNullException(nameof(configureArgument));

            IArgumentBuilder argumentBuilder = ArgumentBuilder.Create();

            configureArgument.Invoke(argumentBuilder);

            m_items.Add(argumentBuilder.Item);

            return this;
        }

        /// <inheritdoc/>
        public IArgumentListBuilder Add(params Action<IArgumentBuilder>[] configureArguments)
        {
            _ = configureArguments ?? throw new ArgumentNullException(nameof(configureArguments));

            foreach (Action<IArgumentBuilder> configureArgument in configureArguments)
            {
                Add(configureArgument);
            }

            return this;
        }

        private readonly List<Argument> m_items = [];
    }
}
