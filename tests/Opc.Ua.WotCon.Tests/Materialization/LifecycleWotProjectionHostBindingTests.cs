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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Verifies that <see cref="LifecycleWotProjectionHost"/> installs
    /// <see cref="RuntimeNodeSetOptions.ConfigureAsync"/> so each runtime
    /// NodeSet generation owns its own binding runtime, built from the
    /// injected <see cref="IWotProjectionBindingRuntimeFactory"/> and the
    /// document's prepared binding plans.
    /// </summary>
    [TestFixture]
    public sealed class LifecycleWotProjectionHostBindingTests
    {
        [Test]
        public async Task ConfigureAsyncIsInstalledAndForwardsBindingPlansToTheRuntimeFactory()
        {
            var runtimeFactory = new RecordingProjectionBindingRuntimeFactory();
            var host = new LifecycleWotProjectionHost(Mock.Of<INodeManagerLifecycle>(), runtimeFactory);

            var harness = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: harness.ScalarNodeIdText));
            ArrayOf<WotBindingPlan> plans = new[]
            {
                WotProjectionBindingRuntimeTestHarness.Plan(form)
            }.ToArrayOf();
            var document = new WotProjectionDocument(
                "closure-a", [], plans);

            RuntimeNodeSetOptions options = InvokeBuildOptions(host, document);

            Assert.That(options.ConfigureAsync, Is.Not.Null,
                "The host must install ConfigureAsync when a runtime factory is supplied.");

            IAsyncDisposable? result = await options.ConfigureAsync!(
                harness.Builder, CancellationToken.None).ConfigureAwait(false);

            Assert.That(runtimeFactory.LastBuilder, Is.SameAs(harness.Builder));
            Assert.That(runtimeFactory.LastBindingPlans.Count, Is.EqualTo(1));
            Assert.That(runtimeFactory.LastBindingPlans[0], Is.SameAs(plans[0]));
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ConfigureAsyncIsNotInstalledWhenNoRuntimeFactoryIsSupplied()
        {
            var host = new LifecycleWotProjectionHost(Mock.Of<INodeManagerLifecycle>());
            var document = new WotProjectionDocument("closure-a", []);

            RuntimeNodeSetOptions options = InvokeBuildOptions(host, document);

            Assert.That(options.ConfigureAsync, Is.Null,
                "Without a runtime factory the host must preserve the pre-existing (data-only) behavior.");
        }

        private static RuntimeNodeSetOptions InvokeBuildOptions(
            LifecycleWotProjectionHost host, WotProjectionDocument document)
        {
            MethodInfo method = typeof(LifecycleWotProjectionHost).GetMethod(
                "BuildOptions", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (RuntimeNodeSetOptions)method.Invoke(host, [document])!;
        }

        private sealed class RecordingProjectionBindingRuntimeFactory : IWotProjectionBindingRuntimeFactory
        {
            public INodeManagerBuilder? LastBuilder { get; private set; }

            public ArrayOf<WotBindingPlan> LastBindingPlans { get; private set; }

            public ValueTask<IAsyncDisposable?> CreateAsync(
                INodeManagerBuilder builder,
                ArrayOf<WotBindingPlan> bindingPlans,
                CancellationToken cancellationToken = default)
            {
                LastBuilder = builder;
                LastBindingPlans = bindingPlans;
                return new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null);
            }
        }
    }
}
