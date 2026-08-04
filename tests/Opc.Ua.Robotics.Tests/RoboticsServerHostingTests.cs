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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Robotics.Server.Tests
{
    [TestFixture]
    [Category("Robotics")]
    [Category("Hosting")]
    public sealed class RoboticsServerHostingTests
    {
        [Test]
        public void AddOpcUaDiThenAddRoboticsThrows()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "test")
                .AddOpcUaDi();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => builder.AddRobotics())!;

            Assert.That(exception.Message, Does.Contain(nameof(OpcUaServerDiBuilderExtensions.AddOpcUaDi)));
        }

        [Test]
        public void AddRoboticsThenAddOpcUaDiThrows()
        {
            IServiceCollection services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "test")
                .AddRobotics();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => builder.AddOpcUaDi())!;

            Assert.That(exception.Message, Does.Contain(nameof(OpcUaServerRoboticsBuilderExtensions.AddRobotics)));
        }

        [Test]
        public void AddRoboticsRegistersDiAddressSpaceOwnership()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "test")
                .AddRobotics();

            using ServiceProvider provider = services.BuildServiceProvider();
            DiAddressSpaceOwnership ownership =
                provider.GetRequiredService<DiAddressSpaceOwnership>();

            Assert.That(
                ownership.OwnerName,
                Is.EqualTo(nameof(OpcUaServerRoboticsBuilderExtensions.AddRobotics)));
        }

        [Test]
        public async Task ConfigureRoboticsAsyncLambdaReturnsAwaitableValueTask()
        {
            IServiceCollection services = new ServiceCollection();
            var completion = new TaskCompletionSource<bool>();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "test");

            builder.ConfigureRobotics(async context =>
            {
                _ = context;
                await completion.Task.ConfigureAwait(false);
            });

            ServiceDescriptor registration = services.Single(descriptor =>
                descriptor.ServiceType.Name == "IRoboticsConfigurationRegistration");
            object instance = registration.ImplementationInstance!;
            MethodInfo configureAsync = instance.GetType().GetMethod("ConfigureAsync")!;
            var context = new FakeRoboticsBuildContext();

            var task = (ValueTask)configureAsync.Invoke(instance, new object[] { context })!;

            Assert.That(task.IsCompleted, Is.False);
            completion.SetResult(true);
            await task.ConfigureAwait(false);
        }

        [Test]
        public async Task ConfigureRoboticsForAsyncLambdaReturnsAwaitableValueTask()
        {
            IServiceCollection services = new ServiceCollection();
            var completion = new TaskCompletionSource<bool>();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "test");

            builder.ConfigureRoboticsFor<DiNodeManager>(async context =>
            {
                _ = context;
                await completion.Task.ConfigureAwait(false);
            });

            ServiceDescriptor registration = services.Single(descriptor =>
                descriptor.ServiceType.Name == "IRoboticsConfigurationRegistration");
            object instance = registration.ImplementationInstance!;
            MethodInfo configureAsync = instance.GetType().GetMethod("ConfigureAsync")!;
            var context = new FakeRoboticsBuildContext();

            var task = (ValueTask)configureAsync.Invoke(instance, new object[] { context })!;

            Assert.That(task.IsCompleted, Is.False);
            completion.SetResult(true);
            await task.ConfigureAwait(false);
        }

        [Test]
        public void EmptyProviderListThrowsConfigurationError()
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => new RoboticsNodeManagerFactory(
                    ArrayOf<IRoboticsModelProvider>.Empty,
                    new RoboticsServerOptions()))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(exception.Message, Does.Contain("At least one Robotics model provider"));
        }

        [Test]
        public void CustomCoreProviderCanReplaceBuiltInProvider()
        {
            var factory = new RoboticsNodeManagerFactory(
                new IRoboticsModelProvider[] { new CustomCoreProvider() },
                new RoboticsServerOptions());

            string[] namespaceUris = factory.NamespacesUris.Memory.ToArray();
            Assert.That(
                namespaceUris,
                Does.Contain(Opc.Ua.IA.Namespaces.IA)
                    .And.Contain(Opc.Ua.Robotics.Namespaces.Robotics));
        }

        [Test]
        public void ProviderMissingCoreNamespaceThrowsConfigurationError()
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => new RoboticsNodeManagerFactory(
                    new IRoboticsModelProvider[] { new MissingCoreProvider() },
                    new RoboticsServerOptions()))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(exception.Message, Does.Contain("collectively advertise"));
        }

        [Test]
        public void BuiltInProviderUsesEarliestOrder()
        {
            Assert.That(new RoboticsModelProvider().Order, Is.EqualTo(int.MinValue));
        }

        [Test]
        public void DefaultInstanceNamespaceIsStableProjectNamespace()
        {
            Assert.That(
                RoboticsServerOptions.DefaultInstanceNamespaceUri,
                Is.EqualTo("urn:opcua-netstandard:robotics:instances"));
            Assert.DoesNotThrow(() => new RoboticsServerOptions().Validate());
        }

        [Test]
        public void StandardModelNamespacesCannotBeInstanceNamespace()
        {
            string[] reservedNamespaces =
            [
                global::Opc.Ua.Namespaces.OpcUa,
                DiNodeManager.DiNamespaceUri,
                Opc.Ua.IA.Namespaces.IA,
                Robotics.Namespaces.Robotics
            ];

            for (int ii = 0; ii < reservedNamespaces.Length; ii++)
            {
                var options = new RoboticsServerOptions
                {
                    InstanceNamespaceUri = reservedNamespaces[ii]
                };
                ServiceResultException exception = Assert.Throws<ServiceResultException>(
                    () => new RoboticsNodeManagerFactory(
                        new IRoboticsModelProvider[]
                        {
                            new RoboticsModelProvider()
                        },
                        options))!;

                Assert.That(
                    exception.StatusCode,
                    Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(exception.Message, Does.Contain(reservedNamespaces[ii]));
                Assert.That(exception.Message, Does.Contain("application-owned"));
            }
        }

        [Test]
        public void ProviderNamespaceCannotBeInstanceNamespace()
        {
            var options = new RoboticsServerOptions
            {
                InstanceNamespaceUri = CustomModelProvider.ModelNamespaceUri
            };

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => new RoboticsNodeManagerFactory(
                    new IRoboticsModelProvider[] { new CustomModelProvider() },
                    options))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(
                exception.Message,
                Does.Contain(CustomModelProvider.ModelNamespaceUri));
            Assert.That(exception.Message, Does.Contain(nameof(CustomModelProvider)));
        }

        [Test]
        public void BuiltInProviderGuardsNullArguments()
        {
            var provider = new RoboticsModelProvider();

            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => provider.AddPredefinedNodes(null!, null!))!.ParamName,
                Is.EqualTo("nodes"));
            Assert.That(
                Assert.Throws<ArgumentNullException>(
                    () => provider.AddPredefinedNodes(new NodeStateCollection(), null!))!.ParamName,
                Is.EqualTo("context"));
        }

        private sealed class CustomCoreProvider : IRoboticsModelProvider
        {
            public int Order => 0;

            public ArrayOf<string> NamespaceUris => new string[]
            {
                Opc.Ua.IA.Namespaces.IA,
                Opc.Ua.Robotics.Namespaces.Robotics
            };

            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
            }
        }

        private sealed class MissingCoreProvider : IRoboticsModelProvider
        {
            public int Order => 0;

            public ArrayOf<string> NamespaceUris => new string[]
            {
                Opc.Ua.IA.Namespaces.IA
            };

            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
            }
        }

        private sealed class CustomModelProvider : IRoboticsModelProvider
        {
            public const string ModelNamespaceUri = "urn:tests:robotics:custom-model";

            public int Order => 0;

            public ArrayOf<string> NamespaceUris => new string[]
            {
                Opc.Ua.IA.Namespaces.IA,
                Robotics.Namespaces.Robotics,
                ModelNamespaceUri
            };

            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
            }
        }

        private sealed class FakeRoboticsBuildContext : IRoboticsBuildContext
        {
            public DiNodeManager Manager => throw new NotSupportedException();

            public ISystemContext Context => throw new NotSupportedException();

            public INodeManagerBuilder Nodes => throw new NotSupportedException();

            public ushort InstanceNamespaceIndex => throw new NotSupportedException();

            public NodeState DeviceSet => throw new NotSupportedException();

            public CancellationToken CancellationToken => default;

            public T GetRequiredService<T>() where T : notnull
            {
                throw new NotSupportedException();
            }

            public void Seal()
            {
                throw new NotSupportedException();
            }
        }
    }
}
