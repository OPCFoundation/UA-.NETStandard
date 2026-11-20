/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
