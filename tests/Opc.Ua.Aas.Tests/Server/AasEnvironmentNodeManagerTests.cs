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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Server;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Exercises the metamodel half of the AAS server: the NodeManager that turns whatever the
    /// environment provider yields into live projections, and the conformance it may claim.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasEnvironmentNodeManagerTests
    {
        /// <summary>
        /// Every environment the provider yields has to reach the projection host, in order, and
        /// carrying the value provider and operation handler the NodeManager was configured with -
        /// otherwise a projected element would read and invoke against the wrong backend.
        /// </summary>
        [Test]
        public async Task CreateAddressSpaceProjectsEveryProvidedEnvironmentAsync()
        {
            AasEnvironment first = AasServerTestData.CreateEnvironment();
            AasEnvironment second = AasServerTestData.CreateEnvironment();
            var valueProvider = new RecordingValueProvider();
            var operationHandler = new RecordingOperationHandler();
            var projectionHost = new RecordingProjectionHost();
            using AasEnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasEnvironmentProvider(new ArrayOf<AasEnvironment>(new[] { first, second })),
                valueProvider,
                operationHandler,
                projectionHost);

            await nodeManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(projectionHost.Environments, Is.EqualTo(new[] { first, second }));
                Assert.That(projectionHost.ValueProviders,
                    Is.EqualTo(new IAasValueProvider[] { valueProvider, valueProvider }));
                Assert.That(projectionHost.OperationHandlers,
                    Is.EqualTo(new IAasOperationHandler[] { operationHandler, operationHandler }));
            });
        }

        /// <summary>
        /// A provider that yields nothing must leave the address space free of projections rather
        /// than publish an empty shell.
        /// </summary>
        [Test]
        public async Task CreateAddressSpaceProjectsNothingForAnEmptyProviderAsync()
        {
            var projectionHost = new RecordingProjectionHost();
            using AasEnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasEnvironmentProvider(default(ArrayOf<AasEnvironment>)),
                new RecordingValueProvider(),
                new DefaultAasOperationHandler(),
                projectionHost);

            await nodeManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            Assert.That(projectionHost.Environments, Is.Empty);
        }

        /// <summary>
        /// The document value provider only knows the values it has been handed, so the NodeManager
        /// has to seed it from the provided environment; without that the projected Value variable
        /// would answer BadNodeIdUnknown.
        /// </summary>
        [Test]
        public async Task CreateAddressSpaceSeedsTheDocumentValueProviderAsync()
        {
            var valueProvider = new DocumentAasValueProvider();
            NodeId valueNodeId = AasServerTestData.MemberNodeId(
                AasServerTestData.ElementNodeId(AasServerTestData.PropertyName), "Value");
            AasValueReadResult before = await valueProvider.ReadValueAsync(valueNodeId)
                .ConfigureAwait(false);
            using AasEnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasEnvironmentProvider(AasServerTestData.CreateEnvironment()),
                valueProvider,
                new DefaultAasOperationHandler(),
                new RecordingProjectionHost());

            await nodeManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            AasValueReadResult after = await valueProvider.ReadValueAsync(valueNodeId)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(before.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(StatusCode.IsGood(after.StatusCode), Is.True);
                Assert.That(after.Value.TryGetValue(out string? value), Is.True);
                Assert.That(value, Is.EqualTo("42"));
            });
        }

        /// <summary>
        /// A value provider that is not the document one owns its own backing store, so the
        /// NodeManager must hand the environment straight to the projection host without trying to
        /// populate the provider behind its back.
        /// </summary>
        [Test]
        public async Task CreateAddressSpaceLeavesACustomValueProviderUntouchedAsync()
        {
            var valueProvider = new RecordingValueProvider();
            var projectionHost = new RecordingProjectionHost();
            using AasEnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasEnvironmentProvider(AasServerTestData.CreateEnvironment()),
                valueProvider,
                new DefaultAasOperationHandler(),
                projectionHost);

            await nodeManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(valueProvider.Reads, Is.Empty);
                Assert.That(valueProvider.Writes, Is.Empty);
                Assert.That(projectionHost.ValueProviders, Is.EqualTo(new[] { valueProvider }));
            });
        }

        /// <summary>
        /// The default handler cannot execute an Operation, so claiming AAS-OperationInvoke while it
        /// is wired up would be a false conformance claim.
        /// </summary>
        [Test]
        public void ConformanceUnitsOmitOperationInvokeForTheDefaultHandler()
        {
            using AasEnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasEnvironmentProvider(default(ArrayOf<AasEnvironment>)),
                new RecordingValueProvider(),
                new DefaultAasOperationHandler(),
                new RecordingProjectionHost());

            List<string> units = Names(nodeManager.ConformanceUnits);

            Assert.Multiple(() =>
            {
                Assert.That(units, Is.EquivalentTo(s_metamodelUnits));
                Assert.That(nodeManager.ServerProfiles, Is.Empty,
                    "Clause 10 assigns no server profile URI to the metamodel half.");
            });
        }

        /// <summary>
        /// A real handler can execute an Operation, which is exactly the condition clause 6.2.5
        /// attaches to AAS-OperationInvoke.
        /// </summary>
        [Test]
        public void ConformanceUnitsClaimOperationInvokeForARealHandler()
        {
            using AasEnvironmentNodeManager nodeManager = CreateNodeManager(
                new InMemoryAasEnvironmentProvider(default(ArrayOf<AasEnvironment>)),
                new RecordingValueProvider(),
                new RecordingOperationHandler(),
                new RecordingProjectionHost());

            List<string> units = Names(nodeManager.ConformanceUnits);

            Assert.That(units, Is.EquivalentTo(s_metamodelUnits.Append("AAS-OperationInvoke")));
        }

        /// <summary>
        /// Every collaborator is dereferenced while the address space is being built, so a missing
        /// one has to be reported at construction rather than as a NullReferenceException later.
        /// </summary>
        [Test]
        public void ConstructorRejectsAMissingCollaborator()
        {
            Mock<IServerInternal> server = AasServerTestHarness.CreateServer(ControlNamespaceUri);
            var options = new AasServerOptions { ControlNamespaceUri = ControlNamespaceUri };
            var provider = new InMemoryAasEnvironmentProvider(default(ArrayOf<AasEnvironment>));
            var valueProvider = new RecordingValueProvider();
            var handler = new DefaultAasOperationHandler();
            var host = new RecordingProjectionHost();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new AasEnvironmentNodeManager(
                        server.Object, null!, null!, provider, valueProvider, handler, host),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("options"));
                Assert.That(
                    () => new AasEnvironmentNodeManager(
                        server.Object, null!, options, null!, valueProvider, handler, host),
                    Throws.ArgumentNullException.With.Property("ParamName")
                        .EqualTo("environmentProvider"));
                Assert.That(
                    () => new AasEnvironmentNodeManager(
                        server.Object, null!, options, provider, null!, handler, host),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("valueProvider"));
                Assert.That(
                    () => new AasEnvironmentNodeManager(
                        server.Object, null!, options, provider, valueProvider, null!, host),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("operationHandler"));
                Assert.That(
                    () => new AasEnvironmentNodeManager(
                        server.Object, null!, options, provider, valueProvider, handler, null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("projectionHost"));
            });
        }

        private const string ControlNamespaceUri = "http://opcfoundation.org/UA/I4AAS/Server/";

        private static readonly string[] s_metamodelUnits =
        [
            "AAS-Metamodel",
            "AAS-SubmodelElements",
            "AAS-ValueFidelity",
            "AAS-InstanceMaterialization",
            "AAS-LosslessRoundTrip"
        ];

        private static List<string> Names(ArrayOf<QualifiedName> units)
        {
            var names = new List<string>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                names.Add(units[i].Name ?? string.Empty);
            }
            return names;
        }

        private static AasEnvironmentNodeManager CreateNodeManager(
            IAasEnvironmentProvider environmentProvider,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            IAasEnvironmentProjectionHost projectionHost)
        {
            Mock<IServerInternal> server = AasServerTestHarness.CreateServer(ControlNamespaceUri);
            return new AasEnvironmentNodeManager(
                server.Object,
                null!,
                new AasServerOptions { ControlNamespaceUri = ControlNamespaceUri },
                environmentProvider,
                valueProvider,
                operationHandler,
                projectionHost);
        }

        /// <summary>
        /// Records what the NodeManager hands to the projection host.
        /// </summary>
        private sealed class RecordingProjectionHost : IAasEnvironmentProjectionHost
        {
            public List<AasEnvironment> Environments { get; } = [];

            public List<IAasValueProvider> ValueProviders { get; } = [];

            public List<IAasOperationHandler> OperationHandlers { get; } = [];

            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                Environments.Add(environment);
                ValueProviders.Add(valueProvider);
                OperationHandlers.Add(operationHandler);
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasEnvironmentProjectionHandle>(CreateHandle());
            }

            public ValueTask RemoveAsync(
                AasEnvironmentProjectionHandle handle,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            private static AasEnvironmentProjectionHandle CreateHandle()
            {
#pragma warning disable SYSLIB0050
                // TODO: Replace FormatterServices when NodeManagerRegistration exposes a test handle factory.
                var registration = (NodeManagerRegistration)FormatterServices.GetUninitializedObject(
                    typeof(NodeManagerRegistration));
#pragma warning restore SYSLIB0050
                return new AasEnvironmentProjectionHandle(registration);
            }
        }

        /// <summary>
        /// A value provider that is deliberately not the document one, recording every access.
        /// </summary>
        private sealed class RecordingValueProvider : IAasValueProvider
        {
            public List<NodeId> Reads { get; } = [];

            public List<NodeId> Writes { get; } = [];

            public ValueTask<AasValueReadResult> ReadValueAsync(
                NodeId valueNodeId,
                CancellationToken cancellationToken = default)
            {
                Reads.Add(valueNodeId);
                return new ValueTask<AasValueReadResult>(
                    new AasValueReadResult(ServiceResult.Good, Variant.Null, StatusCodes.Good, DateTime.UtcNow));
            }

            public ValueTask<ServiceResult> WriteValueAsync(
                NodeId valueNodeId,
                Variant value,
                CancellationToken cancellationToken = default)
            {
                Writes.Add(valueNodeId);
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        /// <summary>
        /// An operation handler that is not the default one, so Invoke may be claimed.
        /// </summary>
        private sealed class RecordingOperationHandler : IAasOperationHandler
        {
            public ValueTask<AasOperationInvokeResult> InvokeAsync(
                AasOperationInvokeRequest request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<AasOperationInvokeResult>(
                    new AasOperationInvokeResult(default, default, success: true, string.Empty));
            }
        }
    }
}
