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
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    public sealed partial class GdsServerHostedServiceTests
    {
        /// <summary>
        /// The startup background worker owns its source read until cancellation has drained it.
        /// </summary>
        [Test]
        public async Task AliasBackgroundRefreshDrainsBeforeDisposedStateIsReleasedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var server = new Mock<IServerInternal>();
            var registry = new Mock<IAliasNameStoreRegistry>(MockBehavior.Strict);
            server.As<IAliasNameStoreRegistryProvider>()
                .SetupGet(value => value.AliasNameStoreRegistry).Returns(registry.Object);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            server.SetupGet(value => value.ServerUris).Returns(context.ServerUris);
            server.SetupGet(value => value.Factory).Returns(context.Factory);
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(context.NamespaceUris));
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.SetupGet(value => value.IsRunning).Returns(true);

            var applicationId = new NodeId(9001, 2);
            var application = new ApplicationRecordDataType
            {
                ApplicationId = applicationId,
                ApplicationUri = "urn:tests:alias-background-drain",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://unused.invalid:4840"],
                ServerCapabilities = ["ALIAS"]
            };
            var nextApplicationId = new NodeId(9002, 2);
            var nextApplication = new ApplicationRecordDataType
            {
                ApplicationId = nextApplicationId,
                ApplicationUri = "urn:tests:alias-background-drain-next",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://unused.invalid:4841"],
                ServerCapabilities = ["ALIAS"]
            };
            var database = new Mock<IApplicationsDatabase>();
            DateTimeUtc lastReset = DateTimeUtc.MinValue;
            uint nextRecordId = 0;
            database.Setup(value => value.QueryApplications(
                    0, 1000, null!, null!, 0, null!, It.IsAny<ArrayOf<string>>(),
                    out lastReset, out nextRecordId))
                .Returns([
                    new ApplicationDescription { ApplicationUri = application.ApplicationUri },
                    new ApplicationDescription { ApplicationUri = nextApplication.ApplicationUri }
                ]);
            database.Setup(value => value.FindApplications(application.ApplicationUri!)).Returns([application]);
            database.Setup(value => value.FindApplications(nextApplication.ApplicationUri!))
                .Returns([nextApplication]);
            database.Setup(value => value.GetApplication(applicationId)).Returns(application);
            database.Setup(value => value.GetApplication(nextApplicationId)).Returns(nextApplication);
            var reader = new BlockingAliasSourceReader();
            registry.Setup(value => value.RegisterContributor(It.IsAny<IAliasNameStore>()));
            registry.Setup(value => value.UnregisterContributor(It.IsAny<IAliasNameStore>()))
                .Callback(() => Assert.That(reader.Release.Task.IsCompleted, Is.True,
                    "The contributor must stay registered until the background source read drains."));

            await using var manager = new ApplicationsNodeManager(
                server.Object,
                new ApplicationConfiguration(telemetry),
                database.Object,
                new StubCertificateRequest(),
                new StubCertificateGroup())
            {
                AliasNameAggregationEnabled = true,
                AliasNameSourceReader = reader
            };
            try
            {
                // No explicit RefreshAliasNameSourceAsync call: readiness launches the real worker,
                // whose first pass discovers the persisted application through the fake database.
                await manager.OnServerReadyAsync().ConfigureAwait(false);
                await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task stop = manager.DisposeAsync().AsTask();
                await reader.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(stop.IsCompleted, Is.False,
                    "Stop must not dispose alias state while the background reader still owns it.");
                registry.Verify(value => value.UnregisterContributor(It.IsAny<IAliasNameStore>()), Times.Never);

                reader.Release.TrySetResult(true);
                await stop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                registry.Verify(value => value.UnregisterContributor(manager.AliasNameAggregator!), Times.Once);
                database.Verify(value => value.GetApplication(applicationId), Times.Once,
                    "The canceled worker must not re-read the database to apply a snapshot after stop.");
                database.Verify(value => value.GetApplication(nextApplicationId), Times.Never,
                    "The refresh loop must not start another source after stop closes admission.");
                Assert.That(manager.AliasNameAggregator!.Sources, Is.Empty);
            }
            finally
            {
                reader.Release.TrySetResult(true);
            }
        }
    }
}
