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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Verifies exclusive complete-result registration across all built-in node-builder families.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class MethodInvocationBuilderExtensionsTests
    {
        /// <summary>
        /// Verifies that both ordinary overloads reject complete handlers in either registration order.
        /// </summary>
        /// <param name="family">The public builder entry point exercised by this case.</param>
        /// <param name="asynchronous">Whether the ordinary handler is asynchronous.</param>
        /// <param name="completeFirst">Whether the complete-result handler is registered first.</param>
        [Test]
        public void BuiltInBuildersRejectMixedHandlersInEitherOrder(
            [Values("predefined", "instance", "reference-child", "resolved")] string family,
            [Values] bool asynchronous,
            [Values] bool completeFirst)
        {
            using var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Root", kNamespace),
                BrowseName = new QualifiedName("Root", kNamespace)
            };
            INodeBuilder<MethodState> builder = CreateBuilder(family, root);
            MethodState method = builder.Node;
            GenericMethodCalledEventHandler2 ordinary = static (_, _, _, _, _) => ServiceResult.Good;
            GenericMethodCalledEventHandler2Async ordinaryAsync =
                static (_, _, _, _, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good);

            void RegisterOrdinary()
            {
                if (asynchronous)
                {
                    builder.OnCall(ordinaryAsync);
                }
                else
                {
                    builder.OnCall(ordinary);
                }
            }

            ServiceResultException? error;
            if (completeFirst)
            {
                Assert.That(builder.OnCallWithResult(s_completeHandler), Is.SameAs(builder));
                error = Assert.Throws<ServiceResultException>(RegisterOrdinary);
                Assert.That(method.OnCallMethodWithResultAsync, Is.SameAs(s_completeHandler));
                Assert.That(method.OnCallMethod2, Is.Null);
                Assert.That(method.OnCallMethod2Async, Is.Null);
            }
            else
            {
                RegisterOrdinary();
                error = Assert.Throws<ServiceResultException>(() => builder.OnCallWithResult(s_completeHandler));
                Assert.That(method.OnCallMethodWithResultAsync, Is.Null);
                if (asynchronous)
                {
                    Assert.That(method.OnCallMethod2Async, Is.SameAs(ordinaryAsync));
                }
                else
                {
                    Assert.That(method.OnCallMethod2, Is.SameAs(ordinary));
                }
            }
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Verifies ordinary registration remains available when no complete-result handler is installed.
        /// </summary>
        /// <param name="family">The public builder entry point exercised by this case.</param>
        /// <param name="asynchronous">Whether the ordinary handler is asynchronous.</param>
        [Test]
        public void BuiltInBuildersAllowOrdinaryHandlersWithoutACompleteHandler(
            [Values("predefined", "instance", "reference-child", "resolved")] string family,
            [Values] bool asynchronous)
        {
            using var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("Root", kNamespace),
                BrowseName = new QualifiedName("Root", kNamespace)
            };
            INodeBuilder<MethodState> builder = CreateBuilder(family, root);
            GenericMethodCalledEventHandler2 handler = static (_, _, _, _, _) => ServiceResult.Good;
            GenericMethodCalledEventHandler2Async asyncHandler =
                static (_, _, _, _, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good);

            INodeBuilder configured = asynchronous ? builder.OnCall(asyncHandler) : builder.OnCall(handler);

            Assert.That(configured, Is.SameAs(builder));
            Assert.That(builder.Node.OnCallMethodWithResultAsync, Is.Null);
            if (asynchronous)
            {
                Assert.That(builder.Node.OnCallMethod2Async, Is.SameAs(asyncHandler));
            }
            else
            {
                Assert.That(builder.Node.OnCallMethod2, Is.SameAs(handler));
            }
        }

        /// <summary>
        /// Creates each built-in builder through its public entry point.
        /// </summary>
        /// <param name="family">The public entry point to use.</param>
        /// <param name="root">The owning root that releases the method's node graph.</param>
        /// <exception cref="ArgumentOutOfRangeException">The family is not a supported test case.</exception>
        private static INodeBuilder<MethodState> CreateBuilder(string family, BaseObjectState root)
        {
            var namespaces = new NamespaceTable();
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaces,
                TypeTable = new TypeTable(namespaces)
            };
            var manager = new NodeManagerBuilder(
                context, FluentTestNodeManager.Create(kNamespace), kNamespace,
                _ => root, id => id == root.NodeId ? root : null!, _ => []);
            INodeBuilder parent = manager.Node(root.NodeId);
            if (family == "instance")
            {
                return parent.CreateInstance(
                    new QualifiedName("Run", kNamespace), static owner => new MethodState(owner)).AsNode();
            }
            if (family == "reference-child")
            {
                INodeBuilder<BaseObjectState> group = parent.AddObject("Group");
                var method = new MethodState(group.Node)
                {
                    NodeId = new NodeId("Run", kNamespace),
                    BrowseName = new QualifiedName("Run", kNamespace)
                };
                group.Node.AddChild(method);
                return group.Child<MethodState>(method.BrowseName);
            }
            var resolved = new MethodState(root)
            {
                NodeId = new NodeId("Run", kNamespace),
                BrowseName = new QualifiedName("Run", kNamespace)
            };
            root.AddChild(resolved);
            if (family == "predefined")
            {
                return manager.Node(resolved);
            }
            if (family == "resolved")
            {
                var facade = new Mock<INodeManagerBuilder>();
                facade.SetupGet(value => value.Context).Returns(context);
                facade.SetupGet(value => value.NodeManager).Returns(manager.NodeManager);
                facade.SetupGet(value => value.DefaultNamespaceIndex).Returns(kNamespace);
                return facade.Object.Node(resolved);
            }
            throw new ArgumentOutOfRangeException(nameof(family));
        }

        /// <summary>The namespace used for the authored node graph.</summary>
        private const ushort kNamespace = 2;

        /// <summary>The complete-result handler whose registration must remain exclusive.</summary>
        private static readonly MethodCalledWithResultEventHandlerAsync s_completeHandler =
            static (_, _, _, _, _) =>
                new ValueTask<MethodInvocationResult>(new MethodInvocationResult(ServiceResult.Good));
    }
}
