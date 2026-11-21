// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Defines a fluent builder for OPC UA method arguments.
    /// </summary>
    public interface IArgumentBuilder
    {
        /// <summary>
        /// Gets the argument being built.
        /// </summary>
        Argument Item { get; }

        /// <summary>
        /// Sets the argument name.
        /// </summary>
        /// <param name="name">
        /// The argument name.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentBuilder WithName(string name);

        /// <summary>
        /// Sets the argument data type node identifier.
        /// </summary>
        /// <param name="dataType">
        /// The OPC UA data type node identifier.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentBuilder WithDataType(NodeId dataType);

        /// <summary>
        /// Sets the argument data type using a CLR type resolved in the provided system context.
        /// </summary>
        /// <typeparam name="TDataType">
        /// The CLR type mapped to an OPC UA data type.
        /// </typeparam>
        /// <param name="context">
        /// The system context used to resolve type metadata.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentBuilder WithDataType<TDataType>(ISystemContext context);

        /// <summary>
        /// Sets the argument value rank.
        /// </summary>
        /// <param name="valueRank">
        /// The OPC UA value rank <see cref="ValueRanks"/> .
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentBuilder WithValueRank(int valueRank);

        /// <summary>
        /// Sets the argument description from a string value.
        /// </summary>
        /// <param name="description">
        /// The description text.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentBuilder WithDescription(string description);

        /// <summary>
        /// Sets the argument description from a localized text value.
        /// </summary>
        /// <param name="description">
        /// The localized description.
        /// </param>
        /// <returns>
        /// The current builder instance.
        /// </returns>
        IArgumentBuilder WithDescription(LocalizedText description);
    }
}
