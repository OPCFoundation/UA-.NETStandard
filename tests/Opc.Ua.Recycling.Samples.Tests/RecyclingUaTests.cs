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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Configuration;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// End-to-end tests exercise real encrypted endpoints, generated typed methods and real monitored items.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class RecyclingUaTests
    {
        /// <summary>
        /// The compiled sample type definitions and typed subscription agree with the durable coordinator.
        /// </summary>
        [Test]
        public async Task SecureTypedCallsRegistrationAndSubscriptionsExposeCommittedState()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            ApplicationConfiguration configuration = environment.ServerHost.Services
                .GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration;
            Assert.That(configuration.SecurityConfiguration.ApplicationCertificates.Count, Is.EqualTo(1));
            Assert.That(configuration.SecurityConfiguration.ApplicationCertificates[0].CertificateType,
                Is.EqualTo(ObjectTypeIds.RsaSha256ApplicationCertificateType));
            RecyclingCoordinatorClient observer = await environment.ConnectAsync("observer", timeout.Token)
                .ConfigureAwait(false);
            Assert.That(observer.CertificateSha256,
                Is.EqualTo(RecyclingAuthorization.CertificateHash(observer.Session.Identity)));
            Assert.That(observer.Session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            int namespaceIndex = observer.Session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri);
            ReadResponse definition = await observer.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(Model.DataTypes.SnapshotDataType, (ushort)namespaceIndex),
                        AttributeId = Attributes.DataTypeDefinition
                    }
                ],
                timeout.Token).ConfigureAwait(false);
            Assert.That(definition.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(definition.Results[0].WrappedValue.TryGetValue<StructureDefinition>(
                out StructureDefinition? structure, observer.Session.MessageContext), Is.True);
            Assert.That(structure!.Fields.Count, Is.GreaterThan(10));

            await using IAsyncEnumerator<CoordinatorNotification> notifications =
                observer.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
            Assert.That(await notifications.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(notifications.Current.IsAvailable, Is.True);
            Assert.That(notifications.Current.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(notifications.Current.SourceTimestampUtc,
                Is.EqualTo(notifications.Current.Snapshot.SourceTimestampUtc));
            Assert.That(notifications.Current.Snapshot.Tick, Is.Zero);
            await environment.InitializeRolesAsync(timeout.Token).ConfigureAwait(false);
            await environment.Coordinator.TickAsync(timeout.Token).ConfigureAwait(false);
            long revision = environment.Coordinator.Current.Revision;
            while (notifications.Current.Snapshot.Revision < revision)
            {
                Assert.That(await notifications.MoveNextAsync().ConfigureAwait(false), Is.True);
            }
            Assert.That(notifications.Current.IsAvailable, Is.True);
            Assert.That(notifications.Current.Snapshot.SourceBatches, Is.EqualTo(1));
            Assert.That(notifications.Current.Snapshot.Parcels,
                Is.EqualTo(environment.Coordinator.Current.Parcels));
            ArrayOf<MaterialExecution> genealogy = await observer.ReadHistoryAsync(0, 16, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(genealogy.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Authentication does not imply permission to impersonate another process or administer the simulator.
        /// </summary>
        [Test]
        public async Task ProcessAndObserverIdentitiesCannotExecutePrivilegedTypedCalls()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            RecyclingCoordinatorClient process = await environment.ConnectAsync("view-Collection", timeout.Token)
                .ConfigureAwait(false);
            RecyclingCoordinatorClient observer = await environment.ConnectAsync("observer", timeout.Token)
                .ConfigureAwait(false);
            RecyclingCommand checkpoint =
                CoordinatorTests.Command(environment.Coordinator, RecyclingCommandKind.Checkpoint);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(() =>
                process.ExecuteAsync(checkpoint, timeout.Token).AsTask())!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            ServiceResultException impersonation = Assert.ThrowsAsync<ServiceResultException>(() =>
                process.ExecuteAsync(CoordinatorTests.Command(
                    environment.Coordinator, RecyclingCommandKind.Register, RecyclingProcess.Sorting),
                    timeout.Token).AsTask())!;
            Assert.That(impersonation.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            ServiceResultException observerDenied = Assert.ThrowsAsync<ServiceResultException>(() =>
                observer.ExecuteAsync(checkpoint, timeout.Token).AsTask())!;
            Assert.That(observerDenied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(environment.Coordinator.Current.Revision, Is.Zero);
        }

        /// <summary>
        /// Reissuing a committed typed request after endpoint restart returns its original outcome,
        /// without an extra material transfer.
        /// </summary>
        [Test]
        public async Task TypedRetryAfterRestartPreservesPausedStateAndOriginalOutcome()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(timeout.Token)
                .ConfigureAwait(false);
            await environment.InitializeRolesAsync(timeout.Token).ConfigureAwait(false);
            RecyclingCoordinatorClient administrator = await environment.ConnectAsync("administrator", timeout.Token)
                .ConfigureAwait(false);
            await environment.Coordinator.TickAsync(timeout.Token).ConfigureAwait(false);
            RecyclingCommand request = CoordinatorTests.Command(environment.Coordinator, RecyclingCommandKind.Pause);
            RecyclingCommandOutcome committed = await administrator.ExecuteAsync(request, timeout.Token)
                .ConfigureAwait(false);
            CommittedSnapshot before = environment.Coordinator.Current;
            await environment.RestartAsync(timeout.Token).ConfigureAwait(false);
            RecyclingCoordinatorClient reconnected = await environment.ConnectAsync("administrator", timeout.Token)
                .ConfigureAwait(false);
            RecyclingCommandOutcome retried = await reconnected.ExecuteAsync(request, timeout.Token)
                .ConfigureAwait(false);
            CommittedSnapshot recovered = await reconnected.ReadSnapshotAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(retried, Is.EqualTo(committed));
            Assert.That(recovered.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
            Assert.That(recovered.Tick, Is.EqualTo(before.Tick));
            Assert.That(recovered.SourceTimestampUtc, Is.EqualTo(before.SourceTimestampUtc));
            Assert.That(recovered.Parcels, Is.EqualTo(before.Parcels));
            Assert.That(recovered.SourceMass, Is.EqualTo(before.SourceMass));
            ushort namespaceIndex = checked((ushort)reconnected.Session.NamespaceUris
                .GetIndex(RecyclingModelMapping.NamespaceUri));
            ReadResponse sourceTime = await reconnected.Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(Model.Variables.Coordinator_State, namespaceIndex),
                        AttributeId = Attributes.Value
                    }
                ],
                timeout.Token).ConfigureAwait(false);
            Assert.That(sourceTime.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(sourceTime.Results[0].SourceTimestamp.ToDateTime(), Is.EqualTo(before.SourceTimestampUtc));
        }
    }
}
