// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// A builder for creating OPC UA method argument metadata.
    /// </summary>
    public class ArgumentBuilder : IArgumentBuilder
    {
        /// <summary>
        /// Initializes an instance of the argument builder.
        /// </summary>
        public ArgumentBuilder()
        {
            Item = new();
        }

        /// <summary>
        /// Initializes an instance of the argument builder.
        /// </summary>
        /// <param name="arg">The argument to initialize the builder with.</param>
        public ArgumentBuilder(Argument arg)
        {
            Item = arg ?? throw new ArgumentNullException(nameof(arg));
        }

        /// <summary>
        /// Initializes an instance of the argument builder.
        /// </summary>
        /// <param name="name">The name of the argument.</param>
        /// <param name="dataType">The data type of the argument.</param>
        /// <param name="valueRank">The value rank of the argument.</param>
        /// <param name="description">The description of the argument.</param>
        public ArgumentBuilder(
            string name,
            NodeId dataType,
            int valueRank = ValueRanks.Any,
            string? description = null)
        {
            Item = new(name, dataType, valueRank, description ?? string.Empty);
        }

        /// <inheritdoc/>
        public Argument Item { get; }

        /// <inheritdoc cref="ArgumentBuilder()"/>
        public static IArgumentBuilder Create()
        {
            return new ArgumentBuilder();
        }

        /// <inheritdoc cref="ArgumentBuilder(Argument)"/>
        public static IArgumentBuilder Create(Argument arg)
        {
            return new ArgumentBuilder(arg);
        }

        /// <inheritdoc cref="ArgumentBuilder(string, NodeId, int, string?)"/>
        public static IArgumentBuilder Create(
            string name,
            NodeId dataType,
            int valueRank = ValueRanks.Any,
            string? description = null)
        {
            return new ArgumentBuilder(name, dataType, valueRank, description);
        }

        /// <inheritdoc/>
        public IArgumentBuilder WithName(string name)
        {
            Item.Name = name;
            return this;
        }

        /// <inheritdoc/>
        public IArgumentBuilder WithDataType(NodeId dataType)
        {
            Item.DataType = dataType;
            return this;
        }

        /// <inheritdoc/>
        public IArgumentBuilder WithDataType<TDataType>(ISystemContext context)
        {
            Item.DataType = TypeInfo.GetDataTypeId(typeof(TDataType), context.NamespaceUris);
            return this;
        }

        /// <inheritdoc/>
        public IArgumentBuilder WithValueRank(int valueRank)
        {
            Item.ValueRank = valueRank;
            return this;
        }

        /// <inheritdoc/>
        public IArgumentBuilder WithDescription(string description)
        {
            Item.Description = new(description);
            return this;
        }

        /// <inheritdoc/>
        public IArgumentBuilder WithDescription(LocalizedText description)
        {
            Item.Description = description;
            return this;
        }
    }
}
