// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Retirement withdraws an overlapping explicit contribution without reading the disposed manager.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        [Category("NodeManagerLifecycle")]
        [NoFixtureServer]
        public async Task ConformancePublishDoesNotReadRetiredLifecycleContributorAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string namespaceUri = kModelNamespaceUri + ":ConformanceRetirement";
            var retainedContributor = new FixedConformanceContributor(
                new QualifiedName("Retained Lifecycle Test Unit"),
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:retained");
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<NodeManagerLifecycleTests>();
            ConformanceReadProbeNodeManager? createdManager = null;
            var factory = new CountingNodeManagerFactory(
                [namespaceUri],
                (server, configuration, _) =>
                {
                    var manager = new ConformanceReadProbeNodeManager(
                        server,
                        configuration,
                        logger,
                        namespaceUri);
                    createdManager = manager;
                    return new ValueTask<IAsyncNodeManager>(manager);
                });
            var fixture = new ServerFixture<LifecycleTestServer>(telemetry =>
            {
                var server = new LifecycleTestServer(telemetry);
                server.AddNodeManager(factory);
                return server;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                ReuseCachedCertificates = true,
                AutoAccept = true
            };
            LifecycleTestServer? server = null;
            try
            {
                server = await fixture.StartAsync(Path.Combine(m_pkiRoot, "conformance-" + Guid.NewGuid().ToString("N")))
                    .ConfigureAwait(false);
                ConformanceReadProbeNodeManager manager = createdManager ??
                    throw new InvalidOperationException("The startup factory did not create its NodeManager.");
                Assert.That(manager.ConformanceReadCount, Is.GreaterThan(0));

                IConformanceUnitsManagerProvider conformanceProvider =
                    server.CurrentInstance as IConformanceUnitsManagerProvider ??
                    throw new InvalidOperationException("The server does not expose its conformance manager.");
                ConformanceUnitsManager conformance = conformanceProvider.ConformanceUnitsManager ??
                    throw new InvalidOperationException("The server conformance manager is unavailable.");
                conformance.Register(manager);
                conformance.Register(retainedContributor);
                await conformance.PublishAsync(timeout.Token).ConfigureAwait(false);

                BaseVariableState publishedUnits = server.CurrentInstance.DiagnosticsNodeManager
                    .FindPredefinedNode<BaseVariableState>(
                        VariableIds.Server_ServerCapabilities_ConformanceUnits);
                BaseVariableState publishedProfiles = server.CurrentInstance.DiagnosticsNodeManager
                    .FindPredefinedNode<BaseVariableState>(
                        VariableIds.Server_ServerCapabilities_ServerProfileArray);
                Assert.That(
                    publishedUnits.Value.TryGetValue(out ArrayOf<QualifiedName> initialUnits) &&
                        initialUnits.ToArray()!.Any(unit => unit == retainedContributor.ConformanceUnit),
                    Is.True);
                Assert.That(
                    publishedProfiles.Value.TryGetValue(out ArrayOf<string> initialProfiles) &&
                        initialProfiles.ToArray()!.Contains(retainedContributor.ServerProfile),
                    Is.True);
                Assert.That(
                    publishedUnits.Value.TryGetValue(out ArrayOf<QualifiedName> initialUnitsWithManager) &&
                        initialUnitsWithManager.ToArray()!.Any(unit => unit == manager.ConformanceUnit),
                    Is.True);
                Assert.That(
                    publishedProfiles.Value.TryGetValue(out ArrayOf<string> initialProfilesWithManager) &&
                        initialProfilesWithManager.ToArray()!.Contains(manager.ServerProfile),
                    Is.True);

                INodeManagerLifecycle lifecycle = server.NodeManagerLifecycle;
                NodeManagerRegistration registration = lifecycle.Registrations.Find(
                    candidate => ReferenceEquals(candidate.NodeManager, manager)) ??
                    throw new InvalidOperationException("The startup NodeManager was not registered.");
                await using IPreparedChanges removal = await lifecycle.PrepareAsync(
                    [LifecycleChange.Remove(registration.Slot)], timeout.Token).ConfigureAwait(false);
                ServerSystemContext readContext = server.CurrentInstance.DefaultSystemContext;
                bool includedBeforeCommit = false;
                bool excludedAtCommit = false;
                bool preservedAtCommit = false;
                LifecycleResult result = await removal.CommitAsync(
                    _ =>
                    {
                        includedBeforeCommit = ReadUnits().Contains(manager.ConformanceUnit) &&
                            ReadProfiles().Contains(manager.ServerProfile);
                        return default;
                    },
                    () =>
                    {
                        excludedAtCommit = !ReadUnits().Contains(manager.ConformanceUnit) &&
                            !ReadProfiles().Contains(manager.ServerProfile);
                        preservedAtCommit = ReadUnits().Contains(retainedContributor.ConformanceUnit) &&
                            ReadProfiles().Contains(retainedContributor.ServerProfile);
                    },
                    timeout.Token).ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(includedBeforeCommit, Is.True);
                Assert.That(excludedAtCommit, Is.True);
                Assert.That(preservedAtCommit, Is.True);
                await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.IsDisposed, Is.True);

                ArrayOf<QualifiedName> ReadUnits()
                {
                    Variant value = publishedUnits.Value;
                    Assert.That(publishedUnits.OnSimpleReadValue, Is.Not.Null);
                    publishedUnits.OnSimpleReadValue!(readContext, publishedUnits, ref value);
                    Assert.That(value.TryGetValue(out ArrayOf<QualifiedName> units), Is.True);
                    return units;
                }

                ArrayOf<string> ReadProfiles()
                {
                    Variant value = publishedProfiles.Value;
                    Assert.That(publishedProfiles.OnSimpleReadValue, Is.Not.Null);
                    publishedProfiles.OnSimpleReadValue!(readContext, publishedProfiles, ref value);
                    Assert.That(value.TryGetValue(out ArrayOf<string> profiles), Is.True);
                    return profiles;
                }

                int postDisposalReads = manager.PostDisposalConformanceReadCount;
                await conformance.PublishAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(manager.PostDisposalConformanceReadCount, Is.EqualTo(postDisposalReads));
                Assert.That(
                    publishedUnits.Value.TryGetValue(out ArrayOf<QualifiedName> units) &&
                        units.ToArray()!.Any(unit => unit == retainedContributor.ConformanceUnit),
                    Is.True);
                Assert.That(
                    publishedProfiles.Value.TryGetValue(out ArrayOf<string> profiles) &&
                        profiles.ToArray()!.Contains(retainedContributor.ServerProfile),
                    Is.True);
                Assert.That(
                    publishedUnits.Value.TryGetValue(out ArrayOf<QualifiedName> unitsAfterRetirement) &&
                        !unitsAfterRetirement.ToArray()!.Any(unit => unit == manager.ConformanceUnit),
                    Is.True);
                Assert.That(
                    publishedProfiles.Value.TryGetValue(out ArrayOf<string> profilesAfterRetirement) &&
                        !profilesAfterRetirement.ToArray()!.Contains(manager.ServerProfile),
                    Is.True);
            }
            finally
            {
                server?.Dispose();
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tracks reads of a lifecycle-owned conformance contribution after manager disposal.
        /// </summary>
        private sealed class ConformanceReadProbeNodeManager :
            NodeManagementLifecycleNodeManager,
            IConformanceContributor
        {
            /// <summary>
            /// Creates a NodeManager whose conformance contribution records every property read.
            /// </summary>
            public ConformanceReadProbeNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                string namespaceUri)
                : base(server, configuration, logger, namespaceUri)
            {
            }

            /// <summary>
            /// Gets the unique conformance unit reported by this test manager.
            /// </summary>
            public QualifiedName ConformanceUnit => new("Retired Lifecycle Test Unit");

            /// <summary>
            /// Gets the unique server profile reported by this test manager.
            /// </summary>
            public string ServerProfile => "urn:opcfoundation.org:Tests:NodeManagerLifecycle:retired";

            /// <summary>
            /// Gets whether asynchronous disposal has completed.
            /// </summary>
            public bool IsDisposed => Volatile.Read(ref m_disposed) != 0;

            /// <summary>
            /// Gets the total number of conformance contribution reads.
            /// </summary>
            public int ConformanceReadCount => Volatile.Read(ref m_conformanceReadCount);

            /// <summary>
            /// Gets the number of conformance contribution reads after disposal completed.
            /// </summary>
            public int PostDisposalConformanceReadCount => Volatile.Read(ref m_postDisposalReadCount);

            /// <inheritdoc/>
            public ArrayOf<QualifiedName> ConformanceUnits
            {
                get
                {
                    RecordConformanceRead();
                    return [ConformanceUnit];
                }
            }

            /// <inheritdoc/>
            public ArrayOf<string> ServerProfiles
            {
                get
                {
                    RecordConformanceRead();
                    return [ServerProfile];
                }
            }

            /// <inheritdoc/>
            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync().ConfigureAwait(false);
                Volatile.Write(ref m_disposed, 1);
            }

            /// <summary>
            /// Records each contribution getter invocation and whether disposal has completed.
            /// </summary>
            private void RecordConformanceRead()
            {
                Interlocked.Increment(ref m_conformanceReadCount);
                if (IsDisposed)
                {
                    Interlocked.Increment(ref m_postDisposalReadCount);
                }
            }

            /// <summary>Whether disposal has completed.</summary>
            private int m_disposed;

            /// <summary>The total contribution getter count.</summary>
            private int m_conformanceReadCount;

            /// <summary>The contribution getter count after disposal.</summary>
            private int m_postDisposalReadCount;
        }

        /// <summary>
        /// Provides one stable conformance unit and profile for lifecycle-retirement assertions.
        /// </summary>
        private sealed class FixedConformanceContributor : IConformanceContributor
        {
            /// <summary>
            /// Creates a contributor with uniquely identifiable support values.
            /// </summary>
            public FixedConformanceContributor(QualifiedName conformanceUnit, string serverProfile)
            {
                ConformanceUnit = conformanceUnit;
                ServerProfile = serverProfile;
            }

            /// <summary>
            /// Gets the conformance unit that remains advertised while this contributor is registered.
            /// </summary>
            public QualifiedName ConformanceUnit { get; }

            /// <summary>
            /// Gets the server profile that remains advertised while this contributor is registered.
            /// </summary>
            public string ServerProfile { get; }

            /// <inheritdoc/>
            public ArrayOf<QualifiedName> ConformanceUnits => [ConformanceUnit];

            /// <inheritdoc/>
            public ArrayOf<string> ServerProfiles => [ServerProfile];
        }
    }
}
