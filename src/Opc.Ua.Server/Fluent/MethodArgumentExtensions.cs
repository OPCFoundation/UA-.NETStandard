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
using Opc.Ua.Types;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Adds OPC UA extension methods to add argument metadata to method nodes being configured with the fluent node builder.
    /// </summary>
    public static class MethodArgumentExtensions
    {
        /// <summary>
        /// Adds OPC UA input argument metadata to a method node being configured with the fluent node builder.
        /// </summary>
        /// <param name="method">
        /// The fluent node builder for the method state.
        /// </param>
        /// <param name="arguments">
        /// The input arguments to assign to the method.
        /// </param>
        /// <returns>
        /// A typed node builder for the <see cref="MethodState"/>.
        /// </returns>
        public static INodeBuilder<MethodState> AddInputArguments(
            this INodeBuilder method, params Argument[] arguments)
        {
            INodeBuilder<MethodState> methodState = method.As<MethodState>();
            methodState.Node.AddInputArguments(method.Builder.Context, arguments);
            return methodState;
        }

        /// <summary>
        /// Creates or replaces the input arguments properties of a method state and assigns the provided arguments.
        /// </summary>
        /// <param name="method">
        /// The method state.
        /// </param>
        /// <param name="context">The system context.</param>
        /// <param name="arguments">
        /// The input arguments to assign to the method.
        /// </param>
        /// <returns>
        /// The updated <see cref="MethodState"/>.
        /// </returns>
        public static MethodState AddInputArguments(
            this MethodState method, ISystemContext context, params Argument[] arguments)
        {
            PropertyState<ArrayOf<Argument>> args = method.CreateOrReplaceInputArguments(
                            context,
                            replacement: null,
                            assignInstanceNodeIds: true);

            args.SetArgumentProperties(arguments);

            return method;
        }

        /// <summary>
        /// Adds OPC UA output argument metadata to a method node being configured with the fluent node builder.
        /// </summary>
        /// <param name="method">
        /// The fluent node builder for the method state.
        /// </param>
        /// <param name="arguments">
        /// The output arguments to assign to the method.
        /// </param>
        /// <returns>
        /// A typed node builder for the <see cref="MethodState"/>.
        /// </returns>
        public static INodeBuilder<MethodState> AddOutputArguments(
            this INodeBuilder method, params Argument[] arguments)
        {
            INodeBuilder<MethodState> methodState = method.As<MethodState>();
            methodState.Node.AddOutputArguments(method.Builder.Context, arguments);
            return methodState;
        }

        /// <summary>
        /// Creates or replaces the output arguments property of a method state and assigns the provided arguments.
        /// </summary>
        /// <param name="method">
        /// The method state.
        /// </param>
        /// <param name="context">The system context.</param>
        /// <param name="arguments">
        /// The output arguments to assign to the method.
        /// </param>
        /// <returns>
        /// The updated <see cref="MethodState"/>.
        /// </returns>
        public static MethodState AddOutputArguments(
            this MethodState method, ISystemContext context, params Argument[] arguments)
        {
            PropertyState<ArrayOf<Argument>> args = method.CreateOrReplaceOutputArguments(
                            context,
                            replacement: null,
                            assignInstanceNodeIds: true);

            args.SetArgumentProperties(arguments);

            return method;
        }

        /// <summary>
        /// Applies standard OPC UA property metadata and assigns argument values to an arguments property state.
        /// </summary>
        /// <param name="args">
        /// The property state that stores method argument metadata.
        /// </param>
        /// <param name="arguments">
        /// The argument values to assign.
        /// </param>
        private static void SetArgumentProperties(
            this PropertyState<ArrayOf<Argument>> args, Argument[] arguments)
        {
            args.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            args.TypeDefinitionId = VariableTypeIds.PropertyType;

            args.Value = arguments;
        }
    }
}
