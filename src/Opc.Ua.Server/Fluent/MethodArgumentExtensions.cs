// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

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
            this INodeBuilder method,
            params Argument[] arguments)
        {
            INodeBuilder<MethodState> methodState = method.As<MethodState>();
            bool created = methodState.Node.InputArguments == null;
            methodState.Node.AddInputArguments(method.Builder.Context, arguments);
            if (created)
            {
                // the method is usually indexed already; index the new
                // property too so a client can read the argument list.
                FluentNodeRegistration.RegisterCreatedNode(
                    method.Builder,
                    methodState.Node.InputArguments!);
            }
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
            this MethodState method,
            ISystemContext context,
            params Argument[] arguments)
        {
            PropertyState<ArrayOf<Argument>> args = method.CreateOrReplaceInputArguments(
                context,
                replacement: null,
                assignInstanceNodeIds: true);

            args.SetArgumentProperties(arguments);

            return method;
        }

        /// <summary>
        /// Creates an <see cref="IArgumentBuilder"/>, applies the provided configuration,
        /// builds a single argument and adds it as input argument.
        /// </summary>
        /// <param name="method">
        /// The method node builder.
        /// </param>
        /// <param name="configureArgument">
        /// The argument configuration action.
        /// </param>
        /// <returns>
        /// The typed method node builder.
        /// </returns>
        public static INodeBuilder<MethodState> AddInputArguments(
            this INodeBuilder method,
            Action<IArgumentBuilder> configureArgument)
        {
            var argumentBuilder = new ArgumentBuilder();
            configureArgument(argumentBuilder);

            return method.AddInputArguments(argumentBuilder.Item);
        }

        /// <summary>
        /// Creates an <see cref="IArgumentListBuilder"/>, applies the provided configurations,
        /// builds the arguments and adds them as input arguments.
        /// </summary>
        /// <param name="method">
        /// The method node builder.
        /// </param>
        /// <param name="configureArgument">
        /// The argument configuration actions.
        /// </param>
        /// <returns>
        /// The typed method node builder.
        /// </returns>
        public static INodeBuilder<MethodState> AddInputArguments(
            this INodeBuilder method,
            params Action<IArgumentBuilder>[] configureArgument)
        {
            var argumentListBuilder = new ArgumentListBuilder();
            argumentListBuilder.Add(configureArgument);

            return method.AddInputArguments(argumentListBuilder.Items);
        }

        /// <summary>
        /// Creates an <see cref="IArgumentListBuilder"/>, applies the provided configuration
        /// and adds the built input arguments.
        /// </summary>
        /// <param name="method">
        /// The method node builder.
        /// </param>
        /// <param name="configureArguments">
        /// The argument list configuration action.
        /// </param>
        /// <returns>
        /// The typed method node builder.
        /// </returns>
        public static INodeBuilder<MethodState> AddInputArguments(
            this INodeBuilder method,
            Action<IArgumentListBuilder> configureArguments)
        {
            var argumentListBuilder = new ArgumentListBuilder();
            configureArguments(argumentListBuilder);

            return method.AddInputArguments(argumentListBuilder.Items);
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
            this INodeBuilder method,
            params Argument[] arguments)
        {
            INodeBuilder<MethodState> methodState = method.As<MethodState>();
            bool created = methodState.Node.OutputArguments == null;
            methodState.Node.AddOutputArguments(method.Builder.Context, arguments);
            if (created)
            {
                // the method is usually indexed already; index the new
                // property too so a client can read the argument list.
                FluentNodeRegistration.RegisterCreatedNode(
                    method.Builder,
                    methodState.Node.OutputArguments!);
            }
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
            this MethodState method,
            ISystemContext context,
            params Argument[] arguments)
        {
            PropertyState<ArrayOf<Argument>> args = method.CreateOrReplaceOutputArguments(
                context,
                replacement: null,
                assignInstanceNodeIds: true);

            args.SetArgumentProperties(arguments);

            return method;
        }

        /// <summary>
        /// Creates an <see cref="IArgumentBuilder"/>, applies the provided configuration,
        /// builds a single argument and adds it as output argument.
        /// </summary>
        /// <param name="method">
        /// The method node builder.
        /// </param>
        /// <param name="configureArgument">
        /// The argument configuration action.
        /// </param>
        /// <returns>
        /// The typed method node builder.
        /// </returns>
        public static INodeBuilder<MethodState> AddOutputArguments(
            this INodeBuilder method,
            Action<IArgumentBuilder> configureArgument)
        {
            var argumentBuilder = new ArgumentBuilder();
            configureArgument(argumentBuilder);

            return method.AddOutputArguments(argumentBuilder.Item);
        }

        /// <summary>
        /// Creates an <see cref="IArgumentListBuilder"/>, applies the provided configurations,
        /// builds the arguments and adds them as output arguments.
        /// </summary>
        /// <param name="method">
        /// The method node builder.
        /// </param>
        /// <param name="configureArgument">
        /// The argument configuration actions.
        /// </param>
        /// <returns>
        /// The typed method node builder.
        /// </returns>
        public static INodeBuilder<MethodState> AddOutputArguments(
            this INodeBuilder method,
            params Action<IArgumentBuilder>[] configureArgument)
        {
            var argumentListBuilder = new ArgumentListBuilder();
            argumentListBuilder.Add(configureArgument);

            return method.AddOutputArguments(argumentListBuilder.Items);
        }

        /// <summary>
        /// Creates an <see cref="IArgumentListBuilder"/>, applies the provided configuration
        /// and adds the built output arguments.
        /// </summary>
        /// <param name="method">
        /// The method node builder.
        /// </param>
        /// <param name="configureArguments">
        /// The argument list configuration action.
        /// </param>
        /// <returns>
        /// The typed method node builder.
        /// </returns>
        public static INodeBuilder<MethodState> AddOutputArguments(
            this INodeBuilder method,
            Action<IArgumentListBuilder> configureArguments)
        {
            var argumentListBuilder = new ArgumentListBuilder();
            configureArguments(argumentListBuilder);

            return method.AddOutputArguments(argumentListBuilder.Items);
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
            this PropertyState<ArrayOf<Argument>> args,
            Argument[] arguments)
        {
            args.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            args.TypeDefinitionId = VariableTypeIds.PropertyType;

            args.DataType = DataTypeIds.Argument;
            args.ValueRank = ValueRanks.OneDimension;

            args.Value = arguments;
        }
    }
}
