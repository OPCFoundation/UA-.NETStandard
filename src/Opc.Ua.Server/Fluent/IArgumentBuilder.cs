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
