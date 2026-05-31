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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Verifies the new <see cref="ISoftwareUpdateBuilder"/>
    /// state-changed hooks fire in the right order and the
    /// underlying state machines' <c>CurrentState</c> / <c>LastTransition</c>
    /// values walk through the standard
    /// Idle&#x2192;Installing&#x2192;Idle (or &#x2192;Error) sequence.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    [Category("SoftwareUpdate")]
    public sealed class DeviceBuilderSoftwareUpdateStateChangeTests
    {
        private DiServerFixture m_fixture = null!;
        private ISoftwarePackageStore m_store = null!;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_store = new MemoryPackageStore();
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task InstallationFsmInitialStateIsIdle()
        {
            (NodeState su, InstallationStateMachineState install) =
                await CreateDeviceWithInstallationAsync(
                    "InitialStateDevice", su => { }).ConfigureAwait(false);

            _ = su;
            Assert.That(install.CurrentState, Is.Not.Null,
                "Installation FSM must have CurrentState materialised by the factory.");
            Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Idle"));
            Assert.That(install.CurrentState.Id, Is.Not.Null);
            Assert.That(install.CurrentState.Id!.Value,
                Is.EqualTo(new NodeId(
                    Opc.Ua.Di.Objects.InstallationStateMachineType_Idle,
                    m_fixture.Manager.DiNamespaceIndex)));
        }

        [Test]
        public async Task InstallSoftwarePackageSuccessFiresStartedAndCompleted()
        {
            var phases = new List<SoftwareUpdatePhase>();

            (NodeState su, InstallationStateMachineState install) =
                await CreateDeviceWithInstallationAsync(
                    "InstallSuccessDevice",
                    su => su.OnInstallationStateChanged((_, change) =>
                    {
                        phases.Add(change.Phase);
                        return default;
                    })).ConfigureAwait(false);

            ServiceResult result = await InvokeInstallSoftwarePackageAsync(
                install, su.NodeId, "urn:acme", "1.0.0").ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(phases, Is.EqualTo(new[]
            {
                SoftwareUpdatePhase.Started,
                SoftwareUpdatePhase.Completed,
            }));
            Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Idle"),
                "Installation FSM must return to Idle on success.");
            Assert.That(install.LastTransition, Is.Not.Null,
                "LastTransition must be materialised when a transition occurs.");
            Assert.That(install.LastTransition!.Value.Text, Is.EqualTo("InstallingToIdle"));
        }

        [Test]
        public async Task InstallSoftwarePackageFailureMovesToErrorAndFiresFailed()
        {
            var phases = new List<SoftwareUpdatePhase>();
            var messages = new List<string>();

            (NodeState su, InstallationStateMachineState install) =
                await CreateDeviceWithInstallationAsync(
                    "InstallFailureDevice",
                    su => su
                        .OnInstall((_, _, _) =>
                            throw new InvalidOperationException("flash failed"))
                        .OnInstallationStateChanged((_, change) =>
                        {
                            phases.Add(change.Phase);
                            messages.Add(change.Message);
                            return default;
                        })).ConfigureAwait(false);

            ServiceResult result = await InvokeInstallSoftwarePackageAsync(
                install, su.NodeId, "urn:acme", "1.0.0").ConfigureAwait(false);

            Assert.That(result, Is.Not.EqualTo(ServiceResult.Good));
            Assert.That(phases, Is.EqualTo(new[]
            {
                SoftwareUpdatePhase.Started,
                SoftwareUpdatePhase.Failed,
            }));
            Assert.That(messages[0], Is.EqualTo(string.Empty),
                "Started phase has no message.");
            Assert.That(messages[1], Is.EqualTo("flash failed"),
                "Failed phase forwards the exception message.");
            Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Error"),
                "Failed install must leave the FSM in the Error state.");
            Assert.That(install.LastTransition!.Value.Text,
                Is.EqualTo("InstallingToError"));
        }

        [Test]
        public async Task PrepareForUpdateSuccessFiresStartedAndCompleted()
        {
            var phases = new ConcurrentQueue<SoftwareUpdatePhase>();

            (NodeState su, PrepareForUpdateStateMachineState prep) =
                await CreateDeviceWithPrepareAsync(
                    "PrepareSuccessDevice",
                    su => su.OnPrepareStateChanged((_, change) =>
                    {
                        phases.Enqueue(change.Phase);
                        return default;
                    })).ConfigureAwait(false);

            ServiceResult result = await InvokeMethodAsync(
                prep.Prepare!, su.NodeId,
                global::Opc.Ua.ArrayOf.Empty<Variant>()).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(phases, Is.EqualTo(new[]
            {
                SoftwareUpdatePhase.Started,
                SoftwareUpdatePhase.Completed,
            }));
            Assert.That(prep.CurrentState!.Value.Text, Is.EqualTo("PreparedForUpdate"));
            Assert.That(prep.LastTransition!.Value.Text,
                Is.EqualTo("PreparingToPreparedForUpdate"));
        }

        [Test]
        public async Task StateChangeHookExceptionDoesNotAbortInstallation()
        {
            (NodeState su, InstallationStateMachineState install) =
                await CreateDeviceWithInstallationAsync(
                    "HookExceptionDevice",
                    su => su.OnInstallationStateChanged((_, _) =>
                        throw new InvalidOperationException("hook broken"))).ConfigureAwait(false);

            ServiceResult result = await InvokeInstallSoftwarePackageAsync(
                install, su.NodeId, "urn:acme", "1.0.0").ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good),
                "Hook exceptions must be swallowed so the SU method still succeeds.");
            Assert.That(install.CurrentState!.Value.Text, Is.EqualTo("Idle"));
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private async Task<(NodeState Su, InstallationStateMachineState Install)>
            CreateDeviceWithInstallationAsync(
                string deviceName,
                Action<ISoftwareUpdateBuilder> configure)
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    deviceName, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store, configure);

            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            var install = (InstallationStateMachineState)su.FindChild(
                ctx, new QualifiedName("Installation", diNs))!;

            return (su, install);
        }

        private async Task<(NodeState Su, PrepareForUpdateStateMachineState Prep)>
            CreateDeviceWithPrepareAsync(
                string deviceName,
                Action<ISoftwareUpdateBuilder> configure)
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    deviceName, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store, configure);

            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            var prep = (PrepareForUpdateStateMachineState)su.FindChild(
                ctx, new QualifiedName("PrepareForUpdate", diNs))!;

            return (su, prep);
        }

        private static Task<ServiceResult> InvokeInstallSoftwarePackageAsync(
            InstallationStateMachineState install,
            NodeId objectId,
            string manufacturerUri,
            string softwareRevision)
        {
            var inputs = new ArrayOf<Variant>(new[]
            {
                new Variant(manufacturerUri),
                new Variant(softwareRevision),
                new Variant(Array.Empty<string>()),
                new Variant(Array.Empty<byte>()),
            });
            return InvokeMethodAsync(install.InstallSoftwarePackage!, objectId, inputs);
        }

        private static async Task<ServiceResult> InvokeMethodAsync(
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs)
        {
            var outputs = new List<Variant>();
            if (method.OnCallMethod2Async is not null)
            {
                return await method.OnCallMethod2Async(
                    null!, method, objectId, inputs, outputs,
                    CancellationToken.None).ConfigureAwait(false);
            }
            if (method.OnCallMethod2 is not null)
            {
                return method.OnCallMethod2(
                    null!, method, objectId, inputs, outputs);
            }
            return ServiceResult.Good;
        }
    }
}
