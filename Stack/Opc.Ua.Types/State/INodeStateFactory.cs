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

namespace Opc.Ua
{
    /// <summary>
    /// A class that creates instances of nodes based on the parameters
    /// provided.
    /// </summary>
    public interface INodeStateFactory
    {
        /// <summary>
        /// Creates a node state instance
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="nodeClass">The node class.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="referenceTypeId">The reference type between the
        /// parent and the node.</param>
        /// <param name="typeDefinitionId">The type definition.</param>
        /// <returns></returns>
        NodeState CreateInstance(
            ISystemContext context,
            NodeState parent,
            NodeClass nodeClass,
            QualifiedName browseName,
            NodeId referenceTypeId,
            NodeId typeDefinitionId);
    }

    /// <summary>
    /// Builds a node state factory
    /// </summary>
    public interface INodeStateFactoryBuilder
    {
        /// <summary>
        /// Registers a node type definition with the specified factory for
        /// creating node state instances.
        /// </summary>
        /// <remarks>Use this method to dynamically associate node type
        /// definitions with custom factories, enabling flexible creation of
        /// node states based on type. This is useful when supporting extensible
        /// or user-defined node types.</remarks>
        /// <param name="typeDefinitionId">The unique identifier of the node
        /// type definition to register. This value cannot be a null node ID.</param>
        /// <param name="typeFactory">The factory used to create node state
        /// instances for the specified type definition.</param>
        /// <exception cref="ArgumentNullException">Thrown if
        /// <paramref name="typeDefinitionId"/> is a null node ID, or if
        /// <paramref name="typeFactory"/> is null or a <see cref="NodeStateFactory"/>.
        /// </exception>
        INodeStateFactoryBuilder RegisterType(
            ExpandedNodeId typeDefinitionId,
            INodeStateFactory typeFactory);
    }

    /// <summary>
    /// Base type for node state activators
    /// </summary>
    public abstract class NodeStateActivator : INodeStateFactory
    {
        /// <inheritdoc/>
        public NodeState CreateInstance(
            ISystemContext context,
            NodeState parent,
            NodeClass nodeClass,
            QualifiedName browseName,
            NodeId referenceTypeId,
            NodeId typeDefinitionId)
        {
            return CreateInstance(context, parent);
        }

        /// <summary>
        /// Override to create the instance
        /// </summary>
        /// <returns></returns>
        protected abstract NodeState CreateInstance(
            ISystemContext context,
            NodeState parent);
    }

    /// <summary>
    /// Reflection based activator
    /// </summary>
    internal class ReflectionBasedNodeStateActivator : NodeStateActivator
    {
        /// <summary>
        /// Creates the activator
        /// </summary>
        /// <param name="type"></param>
        public ReflectionBasedNodeStateActivator(Type type)
        {
            m_type = type ?? throw new ArgumentNullException(nameof(type));
        }

        /// <inheritdoc/>
        protected override NodeState CreateInstance(
            ISystemContext context, NodeState parent)
        {
            return Activator.CreateInstance(m_type, parent) as NodeState;
        }

        private readonly Type m_type;
    }

    /// <summary>
    /// Builder extensions
    /// </summary>
    public static class NodeStateFactoryBuilderExtensions
    {
        /// <summary>
        /// Registers a type with the factoryRegisters a node type with the
        /// specified type definition identifier, enabling the system to
        /// recognize and
        /// handle instances of the type.
        /// </summary>
        /// <remarks>Use this method to add custom node types to the system.
        /// The type parameter must represent a valid NodeState subclass and
        /// should be properly defined to ensure correct behavior when
        /// instances are created or managed.</remarks>
        /// <typeparam name="T">The type of node state to register. Must derive from
        /// NodeState.</typeparam>
        /// <param name="builder">The builder instance used to register the type.</param>
        /// <param name="typeDefinitionId">The unique identifier for the type
        /// definition that associates the registered node type with its´metadata.
        /// </param>
        /// <exception cref="ArgumentNullException"></exception>
        public static INodeStateFactoryBuilder RegisterType<T>(
            this INodeStateFactoryBuilder builder,
            ExpandedNodeId typeDefinitionId)
            where T : NodeState
        {
            return builder.RegisterType(
                typeDefinitionId,
                new ReflectionBasedNodeStateActivator(typeof(T)));
        }

        /// <summary>
        /// Registers a type with the factory.
        /// </summary>
        /// <param name="builder">The builder instance used to register the type.</param>
        /// <param name="typeDefinitionId">The type definition.</param>
        /// <param name="type">The system type.</param>
        /// <exception cref="ArgumentNullException"></exception>
        [Obsolete("Use RegisterType<T> or RegisterType with INodeStateFactory instead.")]
        public static void RegisterType(
            this INodeStateFactoryBuilder builder,
            NodeId typeDefinitionId,
            Type type)
        {
            if (typeDefinitionId.IsNullNodeId)
            {
                throw new ArgumentNullException(nameof(typeDefinitionId));
            }
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            var activator = new ReflectionBasedNodeStateActivator(type);
            builder.RegisterType(typeDefinitionId, activator);
        }
    }
}
