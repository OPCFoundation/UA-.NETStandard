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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PackML;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Exercises complete scale construction and lifetime on the stock Machinery owner.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    [NonParallelizable]
    public sealed class ScalesCompositionTests
    {
        [Test]
        public void AdditiveHostingKeepsOneOwnerAndOneProviderPerModel()
        {
            IOpcUaServerBuilder builder = new ServiceCollection().AddOpcUa().AddServer<StandardServer>(_ => { });
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
                .AddScalesToMachinery()
                .AddPumpsToMachinery()
                .AddScalesToMachinery();

            using ServiceProvider services = builder.Services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(services.GetServices<DiAddressSpaceOwnership>().Single().OwnerName,
                    Is.EqualTo("AddMachinery"));
                Assert.That(services.GetServices<IMachineryModelProvider>()
                    .Count(model => model is ScalesMachineryModelProvider), Is.EqualTo(1));
                Assert.That(services.GetService<ScalesNodeManagerFactory>(), Is.Null);
                Assert.That(services.GetService<PumpsNodeManagerFactory>(), Is.Null);
                Assert.Throws<InvalidOperationException>(() => builder.AddScales());
                Assert.Throws<InvalidOperationException>(() => builder.AddPumps());
            });
        }

        [Test]
        public void CompositionRejectsInvalidRegistration()
        {
            IOpcUaServerBuilder builder = new ServiceCollection().AddOpcUa().AddServer<StandardServer>(_ => { });
            Assert.Throws<InvalidOperationException>(() => builder.AddScalesToMachinery());
            Assert.Throws<ArgumentNullException>(() => OpcUaScalesCompositionExtensions.AddScalesToMachinery(null!));
            Assert.Throws<ArgumentNullException>(() => builder.ConfigureScalesFor<MachineryNodeManager>(null!));
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.AddScalesToMachinery(options => options.ZeroSettingRange = 2));
        }

        [Test]
        public async Task EveryScaleKindUsesTheStockOwnerAndRetainsItsRuntimeAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            ScalesRuntime runtime = fixture.Runtime;
            Assert.That(fixture.Manager.CreateScalesRuntime(), Is.SameAs(runtime));
            Assert.That(fixture.Manager.CreatePumpsRuntime().Manager, Is.SameAs(runtime.Manager));

            for (int ii = 0; ii < ScalesModel.AllKinds.Count; ii++)
            {
                ScaleKind kind = ScalesModel.AllKinds[ii];
                ScaleHandle scale = await runtime.CreateScaleAsync(
                    new QualifiedName(kind.ToString(), runtime.InstanceNamespaceIndex), kind,
                    builder => ScalesNodeManagerTests.FullScale(builder, kind.ToString()));
                scale.PublishLoad(1.25);
                Assert.Multiple(() =>
                {
                    Assert.That(scale.Kind, Is.EqualTo(kind));
                    Assert.That(scale.Scale.TypeDefinitionId, Is.EqualTo(new NodeId(
                        ScalesModel.ObjectTypeOf(kind), runtime.NamespaceIndices.Scales)));
                    Assert.That(scale.Scale.CurrentWeight!.Value.Gross, Is.EqualTo(1.25).Within(0.002));
                    Assert.That(scale.PackML!.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
                    Assert.That(runtime.FindScale(scale.NodeId), Is.SameAs(scale));
                    Assert.That(scale.Scale.SetTare!.OnCallMethod2, Is.Not.Null);
                    Assert.That(ServiceResult.IsGood(scale.SetTare()), Is.True);
                });
                AssertOwnedSubtree(fixture.Manager, scale.Scale);
            }
            Assert.That(runtime.Scales.Count, Is.EqualTo(ScalesModel.AllKinds.Count));
            Assert.That(fixture.Manager.ServerProfiles.ToArray(), Does.Contain(ScalesProfiles.BaseScale));
            Assert.That(fixture.Manager.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName("Scales DataChange", runtime.NamespaceIndices.Scales)));
        }

        [Test]
        public async Task ModelProvidersAreOrderedAndDoNotDuplicateSharedDependenciesAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            var nodes = new NodeStateCollection();
            var scales = new ScalesMachineryModelProvider();
            var pumps = new PumpsMachineryModelProvider();
            ServerSystemContext context = fixture.Manager.SystemContext;
            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => scales.AddPredefinedNodes(nodes, context))!;
            Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));

            new MachineryModelProvider(MachineryParts.BuildingBlocks).AddPredefinedNodes(nodes, context);
            scales.AddPredefinedNodes(nodes, context);
            pumps.AddPredefinedNodes(nodes, context);
            int count = nodes.Count;
            pumps.AddPredefinedNodes(nodes, context);
            scales.AddPredefinedNodes(nodes, context);
            Assert.That(nodes, Has.Count.EqualTo(count));
            Assert.That(nodes.Select(node => node.NodeId).Distinct().Count(), Is.EqualTo(count));
            Assert.That(fixture.Manager.FindPredefinedNode(ExpandedNodeId.ToNodeId(
                PackML.ObjectTypeIds.PackMLBaseStateMachineType, context.NamespaceUris)), Is.Not.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OwnerDisposalClosesRecipeFilesAndStopsCreationAsync(bool composed)
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync(composed);
            ScalesRuntime runtime = fixture.Runtime;
            ScaleHandle scale = await runtime.CreateScaleAsync(
                new QualifiedName("Recipes", runtime.InstanceNamespaceIndex), ScaleKind.Recipe,
                builder =>
                {
                    ScalesNodeManagerTests.FullScale(builder, "R1");
                    builder.WithRecipeFiles();
                });
            RecipeState recipe = scale.Recipes!.AddRecipe("Shutdown", new LocalizedText("Shutdown"));
            FileState file = recipe.RecipeFile!;
            uint handle = 0;
            Assert.That(ServiceResult.IsGood(file.Open!.OnCall!(
                runtime.SystemContext, file.Open, file.NodeId, 6, ref handle)), Is.True);
            Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1));
            Assert.That(ServiceResult.IsGood(file.Write!.OnCall!(
                runtime.SystemContext, file.Write, file.NodeId, handle, ByteString.From([1, 2, 3]))), Is.True);

            await fixture.Manager.DisposeAsync();
            Assert.That(file.OpenCount.Value, Is.Zero);
            Assert.That(file.Open.OnCall!(
                runtime.SystemContext, file.Open, file.NodeId, 1, ref handle).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadShutdown));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await runtime.CreateScaleAsync(new QualifiedName("Stopped"), ScaleKind.Simple,
                    builder => ScalesNodeManagerTests.FullScale(builder, "S")));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await runtime.CreateScaleSystemAsync(new QualifiedName("StoppedSystem"),
                    builder => builder.WithIdentification(ScalesNodeManagerTests.Identity("SS"))));
            await runtime.DisposeAsync();
        }

        [Test]
        public async Task SharedRuntimeHonorsCancellationAndDuplicateNamesAsync()
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync();
            ScalesRuntime runtime = fixture.Runtime;
            var name = new QualifiedName("Shared", runtime.InstanceNamespaceIndex);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(async () =>
                await runtime.CreateScaleAsync(name, ScaleKind.Simple,
                    builder => ScalesNodeManagerTests.FullScale(builder, "S"), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            await runtime.CreateScaleSystemAsync(name,
                builder => builder.WithIdentification(ScalesNodeManagerTests.Identity("System")));
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.CreateScaleAsync(name, ScaleKind.Simple,
                    builder => ScalesNodeManagerTests.FullScale(builder, "S"))))!;
            Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(runtime.Scales, Is.Empty);
            Assert.That(runtime.ScaleSystems, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// An upload finishing after its owner's shutdown must not acknowledge discarded content as committed.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task UploadCompletionRespectsOwnerShutdownAsync(bool composed, bool shutdown)
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync(composed);
            ScalesRuntime runtime = fixture.Runtime;
            ScaleHandle scale = await runtime.CreateScaleAsync(
                new QualifiedName("UploadRace", runtime.InstanceNamespaceIndex), ScaleKind.Recipe,
                builder =>
                {
                    ScalesNodeManagerTests.FullScale(builder, "UploadRace");
                    builder.WithRecipeFiles();
                });
            RecipeController recipes = scale.Recipes!;
            RecipeState recipe = recipes.AddRecipe("Upload", new LocalizedText("Upload"));
            FileState file = recipe.RecipeFile!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var resume = new ManualResetEventSlim(false);
            recipes.RecipeFileHandler = (_, _) =>
            {
                entered.TrySetResult(true);
                if (!resume.Wait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("The upload callback was not released.");
                }
                return ServiceResult.Good;
            };
            uint handle = 0;
            Assert.That(ServiceResult.IsGood(file.Open!.OnCall!(
                runtime.SystemContext, file.Open, file.NodeId, 6, ref handle)), Is.True);
            ByteString payload = ByteString.From([1, 2, 3]);
            Assert.That(ServiceResult.IsGood(file.Write!.OnCall!(
                runtime.SystemContext, file.Write, file.NodeId, handle, payload)), Is.True);
            Task<ServiceResult> closing = Task.Run(() => file.Close!.OnCall!(
                runtime.SystemContext, file.Close, file.NodeId, handle));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                if (shutdown)
                {
                    await fixture.Manager.DisposeAsync();
                }
                resume.Set();
                ServiceResult result = await closing.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.That(result.StatusCode, Is.EqualTo(
                    (StatusCode)(shutdown ? StatusCodes.BadShutdown : StatusCodes.Good)));
                Assert.That(file.OpenCount!.Value, Is.Zero);
                if (!shutdown)
                {
                    Assert.That(recipes.ReadRecipeFile(recipe), Is.EqualTo(payload));
                }
            }
            finally
            {
                resume.Set();
                await closing.WaitAsync(TimeSpan.FromSeconds(15));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposalDrainsAnInFlightConfigurationAsync(bool composed)
        {
            await using var fixture = new ComposedOwner();
            await fixture.StartAsync(composed);
            using var entered = new SemaphoreSlim(0, 1);
            using var resume = new ManualResetEventSlim(false);
            Task<ScaleHandle> creation = Task.Run(() => fixture.Runtime.CreateScaleAsync(
                new QualifiedName("InFlight", fixture.Runtime.InstanceNamespaceIndex), ScaleKind.Simple,
                builder =>
                {
                    ScalesNodeManagerTests.FullScale(builder, "InFlight");
                    entered.Release();
                    Assert.That(resume.Wait(TimeSpan.FromSeconds(15)), Is.True);
                }).AsTask());
            try
            {
                Assert.That(await entered.WaitAsync(TimeSpan.FromSeconds(15)), Is.True);
                ValueTask stopping = fixture.Runtime.DisposeAsync();
                Assert.That(stopping.IsCompleted, Is.False,
                    "the owner must retain resources while an application configurator is using them");
                resume.Set();
                Assert.That(() => creation, Throws.InstanceOf<OperationCanceledException>());
                await stopping;
                Assert.That(fixture.Runtime.Scales, Is.Empty);
            }
            finally
            {
                resume.Set();
                try
                {
                    await creation;
                }
                catch (OperationCanceledException)
                {
                    // Shutdown cancels publication after the configurator has left.
                }
            }
        }

        /// <summary>
        /// Checks instance ownership independently of model namespace assignments.
        /// </summary>
        private static void AssertOwnedSubtree(DiNodeManager manager, NodeState node)
        {
            var machinery = (MachineryNodeManager)manager;
            Assert.That(node.NodeId.NamespaceIndex, Is.EqualTo(machinery.MachineryInstanceNamespaceIndex));
            Assert.That(manager.FindPredefinedNode(node.NodeId), Is.SameAs(node));
            var children = new List<BaseInstanceState>();
            node.GetChildren(manager.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                AssertOwnedSubtree(manager, child);
            }
        }

        /// <summary>
        /// Directly hosts a single standalone or composed DI owner.
        /// </summary>
        private sealed class ComposedOwner : IAsyncDisposable
        {
            /// <summary>
            /// Gets the selected address-space owner.
            /// </summary>
            public DiNodeManager Manager { get; private set; } = null!;

            /// <summary>
            /// Gets its shared Scales runtime.
            /// </summary>
            public ScalesRuntime Runtime { get; private set; } = null!;

            /// <summary>
            /// Loads the real companion models with providers deliberately out of order.
            /// </summary>
            public async Task StartAsync(bool composed = true)
            {
                m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                StandardServer server = await m_fixture.StartAsync();
                Manager = composed
                    ? new MachineryNodeManager(server.CurrentInstance, m_fixture.Config,
                        [
                            new ScalesMachineryModelProvider(),
                            new PumpsMachineryModelProvider(),
                            new MachineryModelProvider(MachineryParts.BuildingBlocks)
                        ],
                        new MachineryServerOptions
                        {
                            Parts = MachineryParts.BuildingBlocks,
                            InstanceNamespaceUri = "urn:tests:scales:composed:instances"
                        })
                    : new ScalesNodeManager(server.CurrentInstance, m_fixture.Config);
                await Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>());
                Runtime = Manager.CreateScalesRuntime();
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync();
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync();
                }
            }

            /// <summary>The hosting server fixture.</summary>
            private ServerFixture<StandardServer>? m_fixture;
        }
    }
}
