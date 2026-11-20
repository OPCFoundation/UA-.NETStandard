// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Extension methods that adjust the access level of the variable
    /// currently focused by a fluent builder. Complements
    /// <see cref="PropertyInitBuilderExtensions.WithProperty(INodeBuilder, string, Variant)"/>,
    /// whose auto-created properties default to read-only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typical usage:
    /// <code>
    /// builder.Node("Devices/Pump #2/Diagnostics")
    ///        .WithProperty("LastError", string.Empty)
    ///        .Writable();
    /// </code>
    /// </para>
    /// </remarks>
    public static class AccessLevelBuilderExtensions
    {
        /// <summary>
        /// Grants or revokes the <see cref="AccessLevels.CurrentWrite"/>
        /// bit on the resolved variable's <c>AccessLevel</c> and
        /// <c>UserAccessLevel</c> attributes.
        /// </summary>
        /// <param name="builder">The owning node builder.</param>
        /// <param name="writable">
        /// <see langword="true"/> to allow writes (default);
        /// <see langword="false"/> to make the variable read-only.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The resolved node is not a <see cref="BaseVariableState"/>.
        /// </exception>
        public static INodeBuilder Writable(this INodeBuilder builder, bool writable = true)
        {
            if (builder == null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }
            if (builder.Node is not BaseVariableState variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Cannot change write access on '{0}' (not a BaseVariableState).",
                    builder.Node.BrowseName);
            }

            if (writable)
            {
                variable.AccessLevel = (byte)(variable.AccessLevel | AccessLevels.CurrentWrite);
                variable.UserAccessLevel = (byte)(variable.UserAccessLevel | AccessLevels.CurrentWrite);
            }
            else
            {
                variable.AccessLevel = (byte)(variable.AccessLevel & ~AccessLevels.CurrentWrite);
                variable.UserAccessLevel = (byte)(variable.UserAccessLevel & ~AccessLevels.CurrentWrite);
            }

            return builder;
        }

        /// <summary>
        /// Typed-view variant of
        /// <see cref="Writable(INodeBuilder, bool)"/> that preserves the
        /// strongly-typed builder return value.
        /// </summary>
        /// <typeparam name="TState">Concrete node state class.</typeparam>
        public static INodeBuilder<TState> Writable<TState>(
            this INodeBuilder<TState> builder, bool writable = true)
            where TState : NodeState
        {
            ((INodeBuilder)builder).Writable(writable);
            return builder;
        }
    }
}
