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
