// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
