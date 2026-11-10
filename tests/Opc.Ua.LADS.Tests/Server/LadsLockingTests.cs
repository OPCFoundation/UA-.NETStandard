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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.LADS.Client;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;
using ClientDevice = Opc.Ua.LADS.Client.LadsDevice;
using ServerTemplate = Opc.Ua.LADS.Server.LadsProgramTemplate;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Exercises optional DI locking through distinct real UA sessions and generated child handlers.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class LadsLockingTests
    {
        /// <summary>
        /// Legacy devices do not materialize Lock and keep ordinary commands available.
        /// </summary>
        [Test]
        public async Task LegacyDeviceOmitsLockAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(LegacyDeviceOmitsLockAsync)).ConfigureAwait(false);
            ILadsDevice hardware = await connection.Server.DeviceAsync().ConfigureAwait(false);
            Assert.That(hardware.LockNodeId.IsNull, Is.True);
            Assert.That(await connection.Server.ChildNamesAsync(hardware.NodeId).ConfigureAwait(false),
                Does.Not.Contain("Lock"));
            ClientDevice device = await connection.Client.GetFirstDeviceAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadNotSupported, () => device.GetLockAsync())
                .ConfigureAwait(false);
            await (await device.GetFirstFunctionalUnitAsync().ConfigureAwait(false)).StartAsync()
                .ConfigureAwait(false);
            Assert.That(hardware.GetFunctionalUnit(LadsIncubator.UnitName).State,
                Is.EqualTo(LadsFunctionalStateValue.Running));
        }

        /// <summary>
        /// Every UA mutation is admitted at the DI owner, including generated asset and dynamic file methods.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClientLockProtectsDeviceSubtreeAsync(bool explicitService)
        {
            using var locks = new DefaultLockService();
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ClientLockProtectsDeviceSubtreeAsync),
                builder =>
                {
                    builder.Services.AddSingleton<ILockActivityService>(locks);
                    builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                        context, LadsIncubator.DeviceName, device =>
                        {
                            if (explicitService)
                            {
                                device.Locking(locks);
                            }
                            else
                            {
                                device.Locking();
                            }
                            device.Identification(id => id.ProductInstanceUri = "urn:lads:locking:device")
                                .AssetManagement(asset => asset.WithConfigurableAssetId("Lab-1"));
                        }));
                }).ConfigureAwait(false);
            await using ManagedSession peer = await connection.ConnectPeerAsync().ConfigureAwait(false);
            LadsClient other = peer.GetLadsClient(connection.Telemetry);
            ClientDevice device = await connection.Client.GetFirstDeviceAsync().ConfigureAwait(false);
            ClientDevice otherDevice = await other.GetFirstDeviceAsync().ConfigureAwait(false);
            DiLockClient ownerLock = await device.GetLockAsync().ConfigureAwait(false);
            DiLockClient otherLock = await other.GetLockAsync(otherDevice.NodeId).ConfigureAwait(false);
            ILadsDevice hardware = await connection.Server.DeviceAsync().ConfigureAwait(false);
            Assert.That(ownerLock.LockNodeId, Is.EqualTo(hardware.LockNodeId));
            Assert.That(await ownerLock.InitLockAsync("owner").ConfigureAwait(false), Is.Zero);
            Assert.That(await otherLock.InitLockAsync("owner").ConfigureAwait(false), Is.EqualTo(-1));
            Assert.That(await otherLock.RenewLockAsync().ConfigureAwait(false), Is.EqualTo(-2));
            Assert.That(await otherLock.ExitLockAsync().ConfigureAwait(false), Is.EqualTo(-2));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadUserAccessDenied, () => otherLock.BreakLockAsync())
                .ConfigureAwait(false);

            Opc.Ua.LADS.Client.LadsFunctionalUnit otherUnit =
                await otherDevice.GetFirstFunctionalUnitAsync().ConfigureAwait(false);
            Opc.Ua.LADS.Client.LadsFunction otherControl =
                await otherUnit.GetFunctionAsync("Temperature").ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadLocked, () => otherControl.StartAsync(Variant.From(40.0)))
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadLocked, () => otherUnit.StartAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadLocked, () => otherDevice.GotoSleepAsync())
                .ConfigureAwait(false);
            Opc.Ua.LADS.Client.LadsMaintenanceTask maintenance =
                (await otherDevice.GetMaintenanceTasksAsync().ConfigureAwait(false))[0];
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadLocked,
                () => other.StartMaintenanceTaskAsync(maintenance.NodeId))
                .ConfigureAwait(false);

            ILadsFunctionalUnit unit = hardware.GetFunctionalUnit(LadsIncubator.UnitName);
            IAnalogControlFunction control = unit.GetFunction<IAnalogControlFunction>("Temperature");
            NodeId target = await connection.Server.ChildAsync(control.NodeId, "TargetValue").ConfigureAwait(false);
            NodeId assetId = await connection.Server.ChildAsync(hardware.NodeId, "AssetId").ConfigureAwait(false);
            Assert.That(await WriteAsync(peer, target, Variant.From(42.0)).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadLocked));
            Assert.That(await WriteAsync(peer, assetId, Variant.From("Unwanted")).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadLocked));
            Assert.That(control.TargetValue, Is.EqualTo(37));
            Assert.That(hardware.Asset!.AssetId, Is.EqualTo("Lab-1"));
            Assert.That((await otherDevice.ReadAsync().ConfigureAwait(false)).Identification.AssetId,
                Is.EqualTo("Lab-1"));
            Assert.That((await otherControl.ReadAsync().ConfigureAwait(false)).Kind,
                Is.EqualTo(LadsFunctionKind.AnalogControl));

            await unit.Programs!.AddTemplateAsync(new ServerTemplate("Dynamic").Step("Added", TimeSpan.Zero))
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadLocked, () =>
                otherUnit.RemoveProgramTemplateAsync("Dynamic")).ConfigureAwait(false);
            Assert.That(unit.Programs.Templates.Count, Is.EqualTo(3));
            string runId = await unit.Programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult result = await unit.Programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Opc.Ua.LADS.Client.LadsResult clientResult =
                (await otherUnit.GetResultsAsync().ConfigureAwait(false))[0];
            Assert.That(clientResult.Files.Count, Is.EqualTo(1));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadLocked, () =>
                other.ReadResultFileAsync(clientResult.Files[0])).ConfigureAwait(false);
            ByteString content = await connection.Client.ReadResultFileAsync(clientResult.Files[0])
                .ConfigureAwait(false);
            Assert.That(content, Is.EqualTo(result.Files[0].Content));

            Assert.That(await WriteAsync(connection.Session, assetId, Variant.From("Lab-2")).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(hardware.Asset.AssetId, Is.EqualTo("Lab-2"));
            Assert.That(await ownerLock.ExitLockAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await WriteAsync(peer, target, Variant.From(43.0)).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(control.TargetValue, Is.EqualTo(43));
            Assert.That(await otherLock.InitLockAsync("peer").ConfigureAwait(false), Is.Zero);
            Assert.That(await otherLock.ExitLockAsync().ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Expiry cannot cut through an admitted asynchronous hardware request, including provider rejection.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClientActivityRetainsLockUntilProviderCompletesAsync(bool reject)
        {
            var clock = new Clock();
            using var locks = new DefaultLockService(TimeSpan.FromSeconds(30), clock);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<ILadsFunctionProvider>(MockBehavior.Strict);
            provider.Setup(value => value.OnCommandAsync(
                It.IsAny<LadsFunctionCommand>(), It.IsAny<CancellationToken>()))
                .Returns((LadsFunctionCommand _, CancellationToken ct) => new ValueTask<ServiceResult>(WaitAsync(ct)));
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ClientActivityRetainsLockUntilProviderCompletesAsync), builder =>
                {
                    builder.Services.AddSingleton(provider.Object);
                    builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                        context, LadsIncubator.DeviceName, device => device.Locking(locks)));
                }).ConfigureAwait(false);
            await using ManagedSession peer = await connection.ConnectPeerAsync().ConfigureAwait(false);
            ClientDevice device = await connection.Client.GetFirstDeviceAsync().ConfigureAwait(false);
            DiLockClient owner = await device.GetLockAsync().ConfigureAwait(false);
            DiLockClient other = await (await peer.GetLadsClient(connection.Telemetry).GetFirstDeviceAsync()
                .ConfigureAwait(false)).GetLockAsync().ConfigureAwait(false);
            Assert.That(await owner.InitLockAsync("owner").ConfigureAwait(false), Is.Zero);
            ILadsDevice hardware = await connection.Server.DeviceAsync().ConfigureAwait(false);
            IAnalogControlFunction control = hardware.GetFunctionalUnit(LadsIncubator.UnitName)
                .GetFunction<IAnalogControlFunction>("Temperature");
            NodeId target = await connection.Server.ChildAsync(control.NodeId, "TargetValue").ConfigureAwait(false);
            Task<StatusCode> write = WriteAsync(connection.Session, target, Variant.From(41.0));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                clock.Advance(300);
                Assert.That(write.IsCompleted, Is.False);
                Assert.That(await other.InitLockAsync("other").ConfigureAwait(false), Is.EqualTo(-1));
            }
            finally
            {
                release.TrySetResult(true);
                await write.ConfigureAwait(false);
            }
            Assert.That(await write.ConfigureAwait(false),
                Is.EqualTo(reject ? StatusCodes.BadResourceUnavailable : StatusCodes.Good));
            Assert.That(control.TargetValue, Is.EqualTo(reject ? 37 : 41));
            clock.Advance(30);
            Assert.That(await other.InitLockAsync("other").ConfigureAwait(false), Is.Zero);
            provider.Verify(value => value.OnCommandAsync(
                It.IsAny<LadsFunctionCommand>(), It.IsAny<CancellationToken>()), Times.Once);

            async Task<ServiceResult> WaitAsync(CancellationToken ct)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return reject ? new ServiceResult(StatusCodes.BadResourceUnavailable) : ServiceResult.Good;
            }
        }

        /// <summary>
        /// Browsing the protected subtree renews only the initiating owner's inactivity period.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OwnerBrowseRenewsOnlyItsOwnLockAsync(bool ownerBrowse)
        {
            var clock = new Clock();
            using var locks = new DefaultLockService(TimeSpan.FromSeconds(30), clock);
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(OwnerBrowseRenewsOnlyItsOwnLockAsync), builder => builder.AddLads().ConfigureLads(context =>
                    LadsIncubator.Configure(context, LadsIncubator.DeviceName, device => device.Locking(locks))))
                .ConfigureAwait(false);
            await using ManagedSession peer = await connection.ConnectPeerAsync().ConfigureAwait(false);
            ClientDevice device = await connection.Client.GetFirstDeviceAsync().ConfigureAwait(false);
            ClientDevice otherDevice = await peer.GetLadsClient(connection.Telemetry).GetFirstDeviceAsync()
                .ConfigureAwait(false);
            DiLockClient owner = await device.GetLockAsync().ConfigureAwait(false);
            DiLockClient other = await otherDevice.GetLockAsync().ConfigureAwait(false);
            Assert.That(await owner.InitLockAsync("owner").ConfigureAwait(false), Is.Zero);
            clock.Advance(20);
            ArrayOf<Opc.Ua.LADS.Client.LadsFunctionalUnit> units =
                await (ownerBrowse ? device : otherDevice).GetFunctionalUnitsAsync().ConfigureAwait(false);
            Assert.That(units.Count, Is.EqualTo(1));
            clock.Advance(20);
            Assert.That(await other.InitLockAsync("other").ConfigureAwait(false),
                Is.EqualTo(ownerBrowse ? -1 : 0));
        }

        /// <summary>
        /// Session shutdown releases idle ownership; explicit device disposal withdraws lock registration.
        /// </summary>
        [Test]
        public async Task ClosingOwnerSessionReleasesLockAsync()
        {
            using var locks = new DefaultLockService();
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ClosingOwnerSessionReleasesLockAsync), builder => builder.AddLads().ConfigureLads(context =>
                    LadsIncubator.Configure(context, LadsIncubator.DeviceName, device => device.Locking(locks))))
                .ConfigureAwait(false);
            await using ManagedSession peer = await connection.ConnectPeerAsync().ConfigureAwait(false);
            ClientDevice device = await connection.Client.GetFirstDeviceAsync().ConfigureAwait(false);
            DiLockClient owner = await device.GetLockAsync().ConfigureAwait(false);
            DiLockClient other = await (await peer.GetLadsClient(connection.Telemetry).GetFirstDeviceAsync()
                .ConfigureAwait(false)).GetLockAsync().ConfigureAwait(false);
            Assert.That(await owner.InitLockAsync("owner").ConfigureAwait(false), Is.Zero);
            await connection.Session.CloseAsync().ConfigureAwait(false);
            Assert.That(await other.InitLockAsync("other").ConfigureAwait(false), Is.Zero);
            var hardware = (Opc.Ua.LADS.Server.LadsDevice)await connection.Server.DeviceAsync().ConfigureAwait(false);
            await hardware.DisposeAsync().ConfigureAwait(false);
            Assert.That(locks.GetState(hardware.NodeId).Locked, Is.False);
        }

        /// <summary>
        /// Invalid opt-ins do not occupy the device name or silently substitute another provider.
        /// </summary>
        [Test]
        public async Task LockingConfigurationRejectsInvalidSelectionAsync()
        {
            LadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(LockingConfigurationRejectsInvalidSelectionAsync),
                builder => builder.AddLads().ConfigureLads(context =>
                {
                    setup = (LadsSetupContext)context;
                    context.AddDevice("Seed", _ => { });
                }), "Seed").ConfigureAwait(false);
            using var locks = new DefaultLockService();
            Assert.That(() => setup!.AddDevice("Candidate", device => device.Locking(null!)),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => setup!.AddDevice("Candidate", device => device.Locking(locks).Locking(locks)),
                Throws.TypeOf<InvalidOperationException>());
            var context = new Mock<IMachineryBuildContext>(MockBehavior.Strict);
            context.Setup(value => value.GetService<ILockActivityService>()).Returns((ILockActivityService?)null);
            var direct = new LadsSetupContext(setup!.Runtime, context.Object);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadConfigurationError, () =>
                direct.AddDeviceAsync("Candidate", device => device.Locking())).ConfigureAwait(false);
            ILadsDevice legacy = await setup.AddDeviceAsync("Candidate", _ => { }).ConfigureAwait(false);
            Assert.That(legacy.LockNodeId.IsNull, Is.True);
        }

        /// <summary>
        /// A failed timeout registration releases the device name without removing existing admission.
        /// </summary>
        [Test]
        public async Task ConflictingTimeoutRollsBackDevicePublicationAsync()
        {
            using var locks = new DefaultLockService(TimeSpan.FromSeconds(30));
            using var conflicting = new DefaultLockService(TimeSpan.FromSeconds(31));
            LadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(ConflictingTimeoutRollsBackDevicePublicationAsync), builder =>
                    builder.AddLads().ConfigureLads(context =>
                    {
                        setup = (LadsSetupContext)context;
                        context.AddDevice("Seed", device => device.Locking(locks));
                    }), "Seed").ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadConfigurationError, () =>
                setup!.AddDeviceAsync("Candidate", device => device.Locking(conflicting))).ConfigureAwait(false);
            ILadsDevice candidate = await setup!.AddDeviceAsync("Candidate", device => device.Locking(locks))
                .ConfigureAwait(false);
            Assert.That(candidate.LockNodeId.IsNull, Is.False);
            ILadsDevice seed = await server.DeviceAsync("Seed").ConfigureAwait(false);
            Assert.That(seed.LockNodeId, Is.Not.EqualTo(candidate.LockNodeId));
        }

        /// <summary>
        /// Writes through the actual UA service rather than directly invoking a node callback.
        /// </summary>
        private static async Task<StatusCode> WriteAsync(ManagedSession session, NodeId node, Variant value)
        {
            WriteResponse response = await session.WriteAsync(null,
            [
                new WriteValue { NodeId = node, AttributeId = Attributes.Value, Value = new DataValue(value) }
            ], CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }

        /// <summary>
        /// Advances only the selected lock service's authority clock.
        /// </summary>
        private sealed class Clock : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow() => m_now;

            /// <summary>
            /// Advances inactivity time without delaying the test.
            /// </summary>
            public void Advance(int seconds) => m_now += TimeSpan.FromSeconds(seconds);

            /// <summary>The current test time.</summary>
            private DateTimeOffset m_now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
