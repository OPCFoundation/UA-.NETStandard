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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Plugins.Companions.Providers;
using Jobs = Opc.Ua.Machinery.Jobs;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class MachineryJobActionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NavigationSelectsOnlyTheRealControlForTheCurrentListWithoutPreparingOrStartingAsync(
            bool direct)
        {
            var test = new JobContext(direct);
            ArrayOf<CompanionTarget> targets = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(targets, Is.EqualTo((ArrayOf<CompanionTarget>)[test.Target]));
            CompanionInspection inspection = await test.Provider.InspectAsync(
                test.Context, targets[0], CancellationToken.None).ConfigureAwait(false);
            string[] expectedOperations = ["refresh", "start-job"];
            Assert.That(inspection.Operations.ToList().Select(operation => operation.Id),
                Is.EqualTo(expectedOperations));
            Assert.That(test.Selection.Item.ItemId, Is.EqualTo("selected-job"));
            Assert.That(test.Test.Services.Calls, Is.Empty);
            test.Inner.Verify(provider => provider.PrepareInputAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                It.IsAny<ArrayOf<CompanionValue>>(), It.IsAny<CancellationToken>()), Times.Never);
            test.Inner.Verify(provider => provider.ExecutePreparedAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                It.IsAny<CompanionTaskInput>(), It.IsAny<IProgress<CompanionTaskProgress>?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task TypedInputAndExecutionAreForwardedToTheExistingIsa95ProviderAsync()
        {
            var test = new JobContext();
            _ = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<CompanionValue> input =
            [
                new("jobOrderId", Variant.From("selected-job")),
                new("comment", Variant.From(ArrayOf<LocalizedText>.Empty))
            ];
            Isa95PreparedTask prepared = test.Task(test.Management);
            test.Inner.Setup(provider => provider.PrepareInputAsync(
                test.Context, test.Target, "start-job", input, It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared);
            CompanionTaskInput forwarded = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "start-job", input, CancellationToken.None).ConfigureAwait(false);
            Assert.That(forwarded, Is.SameAs(prepared));
            var result = new CompanionOperationResult("Existing ISA-95 provider result",
                [new("ISA-95 return status", Variant.From(1UL))]);
            test.Inner.Setup(provider => provider.ExecutePreparedAsync(
                test.Context, test.Target, "start-job", prepared, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            CompanionOperationResult executed = await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(executed, Is.SameAs(result));
            test.Inner.Verify(provider => provider.ExecutePreparedAsync(
                test.Context, test.Target, "start-job", prepared, null, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(test.Test.Services.Calls, Is.Empty,
                "The navigation adapter must not implement another dispatcher.");
        }

        [TestCase("wrong-list")]
        [TestCase("wrong-source")]
        [TestCase("wrong-section")]
        public async Task UnrelatedSelectedItemsCannotNavigateIntoAnotherControlAsync(string changed)
        {
            var test = new JobContext();
            MachineryItem item = test.Selection.Item with
            {
                NodeId = changed == "wrong-list" ? new NodeId(888u) : test.Selection.Item.NodeId,
                SourceNodeId = changed == "wrong-source" ? new NodeId(999u) : test.Selection.Item.SourceNodeId
            };
            MachineryActionSelection selection = test.Test.Select(
                changed == "wrong-section" ? MachinerySection.Results : MachinerySection.Jobs,
                item, test.Selection.Values);
            var provider = new MachineryJobTaskProvider(selection, test.Inner.Object);
            await Assert.ThatAsync(async () => await provider.DiscoverAsync(test.Context, CancellationToken.None)
                .ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            test.Inner.Verify(value => value.DiscoverAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase("selection")]
        [TestCase("peer")]
        [TestCase("control")]
        public async Task ChangedParentContextPreventsTaskPreparationAsync(string changed)
        {
            var test = new JobContext();
            _ = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None).ConfigureAwait(false);
            if (changed == "selection")
            {
                test.Test.Current = false;
            }
            else if (changed == "peer")
            {
                test.Test.Endpoint.ServerCertificate = ByteString.From("another-peer"u8);
            }
            else
            {
                test.Test.Services.TranslateHandler = (path, _) =>
                    path.RelativePath.Elements[0].TargetName.Name == Jobs.BrowseNames.JobOrderControl
                        ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                        : test.Test.Services.Translate(path);
            }
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "start-job", [], CancellationToken.None).ConfigureAwait(false),
                Throws.Exception.TypeOf<InvalidOperationException>().Or.TypeOf<ServiceResultException>())
                .ConfigureAwait(false);
            test.Inner.Verify(provider => provider.PrepareInputAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                It.IsAny<ArrayOf<CompanionValue>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase("store-sample-job")]
        [TestCase("request-sample-response")]
        [TestCase("call")]
        [TestCase("begin-execution")]
        public async Task SampleGuardsAndInventedCommandsDoNotBypassThePreparedWorkflowAsync(string operation)
        {
            var test = new JobContext();
            _ = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecuteAsync(
                test.Context, test.Target, operation, "selected-job", CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, test.Target, operation, [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Test.Services.Calls, Is.Empty);
        }

        [Test]
        public async Task PreparedJobBindingMustMatchTheExactMachineryManagementOwnerAsync()
        {
            var test = new JobContext();
            _ = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None).ConfigureAwait(false);
            Isa95PreparedTask other = test.Task(new NodeId(987u));
            test.Inner.Setup(provider => provider.PrepareInputAsync(
                test.Context, test.Target, "start-job", It.IsAny<ArrayOf<CompanionValue>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(other);
            await Assert.ThatAsync(async () => await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "start-job", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Target, "start-job", other, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            test.Inner.Verify(provider => provider.ExecutePreparedAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<string>(),
                It.IsAny<CompanionTaskInput>(), It.IsAny<IProgress<CompanionTaskProgress>?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ParentCancellationIsPassedIntoExistingProviderAndItsCleanupIsDrainedAsync()
        {
            var test = new JobContext();
            using var parent = new CancellationTokenSource();
            var provider = new MachineryJobTaskProvider(test.Selection, test.Inner.Object, parent.Token);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            test.Inner.Setup(value => value.DiscoverAsync(test.Context, It.IsAny<CancellationToken>()))
                .Returns(async (CompanionContext _, CancellationToken token) =>
                {
                    entered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        cleaning.TrySetResult();
                        await release.Task.ConfigureAwait(false);
                    }
                    return [test.Target];
                });
            Task<ArrayOf<CompanionTarget>> work = provider.DiscoverAsync(test.Context, CancellationToken.None).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await parent.CancelAsync().ConfigureAwait(false);
                await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(work.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult();
            }
            await Assert.ThatAsync(() => work,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(test.Test.Services.Calls, Is.Empty);
        }

        private sealed class JobContext
        {
            public JobContext(bool direct = false)
            {
                Context = Test.Context;
                MachineryMachine machine = direct
                    ? new MachineryMachine(ObjectIds.ObjectsFolder, "Direct management objects", "Management owner")
                    : Test.Machine;
                Management = Test.Services.Id("management");
                Test.Services.AddObject(Management, Jobs.ObjectTypeIds.JobManagementType);
                Test.Services.AddChild(
                    machine.NodeId, Management, Jobs.Namespaces.MachineryJobs, Jobs.BrowseNames.JobManagement);
                NodeId control = Test.Services.Id("control");
                Test.Services.AddObject(control, V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
                Test.Services.AddChild(
                    Management, control, Jobs.Namespaces.MachineryJobs, Jobs.BrowseNames.JobOrderControl);
                NodeId list = Test.Services.AddProperty(control, V2.Namespaces.ISA95JobControlV2,
                    V2.BrowseNames.JobOrderList, Variant.Null);
                Selection = Test.Select(MachinerySection.Jobs,
                    new MachineryItem(list, "Selected job display", "State", string.Empty)
                    {
                        SourceNodeId = list,
                        ItemId = "selected-job"
                    },
                    [new("Job ID", Variant.From("selected-job")) { SourceNodeId = list }], machine);
                Target = new CompanionTarget("isa95", control, "Real control", "V2 order receiver");
                Inner.SetupGet(value => value.Descriptor).Returns(new Isa95CompanionProvider().Descriptor);
                Inner.Setup(value => value.DiscoverAsync(It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(
                        [Target, Target with { NodeId = Test.Services.Id("unrelated") }]);
                CompanionOperation job = Isa95JobTask.Find(true, "start-job").Operation;
                Inner.Setup(value => value.InspectAsync(
                    It.IsAny<CompanionContext>(), Target, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CompanionInspection([],
                    [
                        new("refresh", "Refresh", CompanionOperationSafety.ReadOnly),
                        job,
                        new("store-sample-job", "Legacy sample task", CompanionOperationSafety.SampleMutation),
                        new("call", "Raw method", CompanionOperationSafety.DeploymentMutation)
                    ], "Existing ISA-95 workflow"));
                Provider = new MachineryJobTaskProvider(Selection, Inner.Object);
            }

            public MachineryActionTestContext Test { get; } = new();
            public CompanionContext Context { get; }
            public Mock<IPreparedCompanionProvider> Inner { get; } = new(MockBehavior.Strict);
            public MachineryActionSelection Selection { get; }
            public CompanionTarget Target { get; }
            public MachineryJobTaskProvider Provider { get; }
            public NodeId Management { get; }

            public Isa95PreparedTask Task(NodeId management)
            {
                return new Isa95PreparedTask(Target, true, Isa95JobTask.Find(true, "start-job"),
                    new Isa95TaskBinding(Target.NodeId, Test.Services.Id("responses"), NodeId.Null)
                    {
                        MachineryOwner = management
                    }, Test.Services.Id("start-method"), "selected-job", Variant.From("selected-job"),
                    Variant.From(ArrayOf<LocalizedText>.Empty), null, Test.Services.MessageContext);
            }
        }
    }
}
