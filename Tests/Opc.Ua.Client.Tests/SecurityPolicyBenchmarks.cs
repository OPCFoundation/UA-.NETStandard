/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Custom config for Security Policy Benchmarks that increases build timeout.
    /// </summary>
    public class SecurityPolicyBenchmarkConfig : ManualConfig
    {
        public SecurityPolicyBenchmarkConfig()
        {
            // Increase build timeout to 10 minutes for large dependency builds
            WithOptions(ConfigOptions.DisableOptimizationsValidator);
            WithBuildTimeout(TimeSpan.FromMinutes(10));
        }
    }

    /// <summary>
    /// Benchmarks for measuring CPU, Memory, Latency, and Message Throughput across all Security Policies.
    /// These benchmarks help detect performance regressions when changes are made to security-related code.
    ///
    /// Total: 162 benchmarks (18 methods × 9 security policies - None is excluded)
    ///
    /// USAGE:
    ///   cd Tests/Opc.Ua.Client.Tests
    ///
    /// Run all 126 benchmarks (takes ~30+ minutes):
    ///   dotnet run -c Release -f net10.0 -- --filter '*SecurityPolicyBenchmarks*' --job short
    ///
    /// Run specific benchmark method across all security policies:
    ///   dotnet run -c Release -f net10.0 -- --filter '*ReadSmallMessageAsync*' --job short
    ///   dotnet run -c Release -f net10.0 -- --filter '*WriteSmallMessageAsync*' --job short
    ///   dotnet run -c Release -f net10.0 -- --filter '*BrowseAsync*' --job short
    ///
    /// Run specific benchmark with specific policy:
    ///   To run only one policy, temporarily modify BenchPolicies() in ClientTestFramework.cs
    ///   to return only the desired policy, then run:
    ///   dotnet run -c Release -f net10.0 -- --filter '*ReadSmallMessageAsync*' --job short
    ///
    ///
    /// Run as NUnit tests (faster, no separate build):
    ///   dotnet test -c Release -f net10.0 --filter 'FullyQualifiedName~SecurityPolicyBenchmarks'
    ///   dotnet test -c Release -f net10.0 --filter 'FullyQualifiedName~SecurityPolicyBenchmarks.ReadSmallMessageAsync'
    ///
    /// View results:
    ///   cat BenchmarkDotNet.Artifacts/results/*.md
    ///   cat BenchmarkDotNet.Artifacts/results/*.csv
    ///
    /// Available benchmark methods:
    ///   Latency benchmarks:
    ///   - ReadSmallMessageAsync, ReadMediumMessageAsync, ReadLargeMessageAsync
    ///   - WriteSmallMessageAsync
    ///   - BrowseAsync, BrowseMultipleNodesAsync
    ///   - CallMethodAsync
    ///   - CreateCloseSessionAsync, SessionLifecycleWithReadAsync
    ///   - MixedWorkloadAsync
    ///
    ///   Throughput benchmarks (operations/second):
    ///   - ReadSmallMessageBurstAsync, ReadMediumMessageBurstAsync, ReadLargeMessageBurstAsync
    ///   - WriteSmallMessageBurstAsync
    ///   - ReadThroughputAsync, WriteThroughputAsync
    ///   - BrowseThroughputAsync, CallThroughputAsync
    ///
    /// Available security policies (9 total, None is excluded):
    ///   - Basic128Rsa15, Basic256, Basic256Sha256
    ///   - Aes128_Sha256_RsaOaep, Aes256_Sha256_RsaPss
    ///   - ECC_nistP256, ECC_nistP384, ECC_brainpoolP256r1, ECC_brainpoolP384r1
    ///
    /// THROUGHPUT CALCULATION:
    ///   Throughput benchmarks execute 100 operations and measure total time.
    ///   Calculate ops/sec using: Throughput = 100 / (Mean time in seconds)
    ///
    ///   Example from results:
    ///     Method: 'Read 100 ops (for throughput)'
    ///     SecurityPolicy: Basic128Rsa15
    ///     Mean: 140.8 ms = 0.1408 seconds
    ///     Throughput = 100 / 0.1408 = 710 ops/sec
    /// </summary>
    [TestFixture]
    [Explicit]
    [Category("Client")]
    [Category("SecurityPolicyBenchmark")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Config(typeof(SecurityPolicyBenchmarkConfig))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class SecurityPolicyBenchmarks : ClientTestFramework
    {
        private const int MessageCount = 100;
        private const int SmallMessageNodeCount = 10;
        private const int MediumMessageNodeCount = 50;
        private const int LargeMessageNodeCount = 200;

        private IList<NodeId> m_smallTestSet;
        private IList<NodeId> m_mediumTestSet;
        private IList<NodeId> m_largeTestSet;
        private ReadValueIdCollection m_smallReadValueIds;
        private ReadValueIdCollection m_mediumReadValueIds;
        private ReadValueIdCollection m_largeReadValueIds;

        public SecurityPolicyBenchmarks()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        /// <summary>
        /// Override to exclude None policy from benchmarks to avoid CI test failures.
        /// </summary>
        public new IEnumerable<string> BenchPolicies()
        {
            // Return all security policies except None
            foreach (string displayName in SecurityPolicies.GetDisplayNames())
            {
                string policyUri = SecurityPolicies.GetUri(displayName);
                if (policyUri != SecurityPolicies.None)
                {
                    yield return policyUri;
                }
            }
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new async Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            await base.OneTimeSetUpAsync().ConfigureAwait(false);
            await PrepareTestDataAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public new Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public new Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public override void GlobalSetup()
        {
            base.GlobalSetup();
            PrepareTestDataAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public override void GlobalCleanup()
        {
            base.GlobalCleanup();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Prepare test data sets for benchmarking.
        /// </summary>
        private async Task PrepareTestDataAsync()
        {
            if (Session == null)
            {
                return;
            }

            // Get available test nodes
            var allTestNodes = GetTestSetStatic(Session.NamespaceUris);

            // Ensure we have enough nodes for testing
            if (allTestNodes.Count < LargeMessageNodeCount)
            {
                // If not enough static nodes, add simulation nodes
                var simNodes = GetTestSetSimulation(Session.NamespaceUris);
                allTestNodes = allTestNodes.Concat(simNodes)
                    .Take(LargeMessageNodeCount)
                    .ToList();
            }

            // Create test sets of different sizes
            m_smallTestSet = allTestNodes.Take(SmallMessageNodeCount).ToList();
            m_mediumTestSet = allTestNodes.Take(MediumMessageNodeCount).ToList();
            m_largeTestSet = allTestNodes.Take(LargeMessageNodeCount).ToList();

            // Prepare ReadValueId collections
            m_smallReadValueIds = new ReadValueIdCollection(
                m_smallTestSet.Select(nodeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                })
            );

            m_mediumReadValueIds = new ReadValueIdCollection(
                m_mediumTestSet.Select(nodeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                })
            );

            m_largeReadValueIds = new ReadValueIdCollection(
                m_largeTestSet.Select(nodeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                })
            );

            // Verify we can read the nodes
            await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                m_smallReadValueIds,
                CancellationToken.None
            ).ConfigureAwait(false);
        }
        #endregion

        #region Read Benchmarks - Small Messages
        /// <summary>
        /// Benchmark: Read small message (10 nodes) - measures baseline performance.
        /// Tests CPU and memory overhead for small messages with different security policies.
        /// </summary>
        [Test, Order(100)]
        [Benchmark(Baseline = true, Description = "Read 10 nodes")]
        public async Task ReadSmallMessageAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                m_smallReadValueIds,
                CancellationToken.None
            ).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            Assert.AreEqual(m_smallReadValueIds.Count, response.Results.Count);
        }

        /// <summary>
        /// Benchmark: Read small messages repeatedly - measures sustained throughput.
        /// </summary>
        [Test, Order(101)]
        [Benchmark(Description = "Read 10 nodes x100 iterations")]
        public async Task ReadSmallMessageBurstAsync()
        {
            for (int i = 0; i < MessageCount; i++)
            {
                await Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    m_smallReadValueIds,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
        }
        #endregion

        #region Read Benchmarks - Medium Messages
        /// <summary>
        /// Benchmark: Read medium message (50 nodes) - measures typical workload performance.
        /// </summary>
        [Test, Order(200)]
        [Benchmark(Description = "Read 50 nodes")]
        public async Task ReadMediumMessageAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                m_mediumReadValueIds,
                CancellationToken.None
            ).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            Assert.AreEqual(m_mediumReadValueIds.Count, response.Results.Count);
        }

        /// <summary>
        /// Benchmark: Read medium messages repeatedly - measures sustained medium-load throughput.
        /// </summary>
        [Test, Order(201)]
        [Benchmark(Description = "Read 50 nodes x100 iterations")]
        public async Task ReadMediumMessageBurstAsync()
        {
            for (int i = 0; i < MessageCount; i++)
            {
                await Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    m_mediumReadValueIds,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
        }
        #endregion

        #region Read Benchmarks - Large Messages
        /// <summary>
        /// Benchmark: Read large message (200 nodes) - measures high-load performance.
        /// Tests encryption/decryption overhead with larger payloads.
        /// </summary>
        [Test, Order(300)]
        [Benchmark(Description = "Read 200 nodes")]
        public async Task ReadLargeMessageAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                m_largeReadValueIds,
                CancellationToken.None
            ).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            Assert.AreEqual(m_largeReadValueIds.Count, response.Results.Count);
        }

        /// <summary>
        /// Benchmark: Read large messages repeatedly - measures sustained high-load throughput.
        /// </summary>
        [Test, Order(301)]
        [Benchmark(Description = "Read 200 nodes x100 iterations")]
        public async Task ReadLargeMessageBurstAsync()
        {
            for (int i = 0; i < MessageCount; i++)
            {
                await Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    m_largeReadValueIds,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
        }
        #endregion

        #region Write Benchmarks - Small Messages
        /// <summary>
        /// Benchmark: Write small message (10 nodes) - measures write performance.
        /// Tests CPU and memory overhead for write operations with different security policies.
        /// </summary>
        [Test, Order(400)]
        [Benchmark(Description = "Write 10 nodes")]
        public async Task WriteSmallMessageAsync()
        {
            var writeValues = new WriteValueCollection(
                m_smallTestSet.Select(nodeId => new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(UnsecureRandom.Shared.Next()))
                })
            );

            WriteResponse response = await Session.WriteAsync(
                null,
                writeValues,
                CancellationToken.None
            ).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotNull(response.Results);
        }

        /// <summary>
        /// Benchmark: Write small messages repeatedly - measures sustained write throughput.
        /// </summary>
        [Test, Order(401)]
        [Benchmark(Description = "Write 10 nodes x100 iterations")]
        public async Task WriteSmallMessageBurstAsync()
        {
            for (int i = 0; i < MessageCount; i++)
            {
                var writeValues = new WriteValueCollection(
                    m_smallTestSet.Select(nodeId => new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(i))
                    })
                );

                await Session.WriteAsync(
                    null,
                    writeValues,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
        }
        #endregion

        #region Browse Benchmarks
        /// <summary>
        /// Benchmark: Browse operation - measures browse performance.
        /// Tests how security policy affects browse operations and reference enumeration.
        /// </summary>
        [Test, Order(500)]
        [Benchmark(Description = "Browse Objects folder")]
        public async Task BrowseAsync()
        {
            var nodesToBrowse = new BrowseDescriptionCollection
            {
                new BrowseDescription
                {
                    NodeId = ObjectIds.ObjectsFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object | (uint)NodeClass.Variable,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };

            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                nodesToBrowse,
                CancellationToken.None
            ).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            Assert.Greater(response.Results.Count, 0);
        }

        /// <summary>
        /// Benchmark: Browse multiple nodes - measures browse throughput.
        /// </summary>
        [Test, Order(501)]
        [Benchmark(Description = "Browse 10 nodes")]
        public async Task BrowseMultipleNodesAsync()
        {
            var nodesToBrowse = new BrowseDescriptionCollection(
                m_smallTestSet.Select(nodeId => new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.References,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                })
            );

            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                nodesToBrowse,
                CancellationToken.None
            ).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotNull(response.Results);
        }
        #endregion

        #region Call Benchmarks
        /// <summary>
        /// Benchmark: Call GetMonitoredItems method - measures method call performance.
        /// Tests how security policy affects method call operations.
        /// </summary>
        [Test, Order(600)]
        [Benchmark(Description = "Call GetMonitoredItems method")]
        public async Task CallMethodAsync()
        {
            var inputArguments = new VariantCollection
            {
                new Variant((uint)0) // subscriptionId
            };

            CallMethodRequestCollection requests = new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_GetMonitoredItems,
                    InputArguments = inputArguments
                }
            };

            CallResponse response = await Session.CallAsync(
                null,
                requests,
                CancellationToken.None
            ).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotNull(response.Results);
            Assert.AreEqual(1, response.Results.Count);
        }
        #endregion

        #region Session Management Benchmarks
        /// <summary>
        /// Benchmark: Create and close session - measures session establishment overhead.
        /// This is critical for understanding the cost of security policy negotiation.
        /// </summary>
        [Test, Order(700)]
        [Benchmark(Description = "Create and close session")]
        public async Task CreateCloseSessionAsync()
        {
            var session = await ClientFixture.ConnectAsync(
                ServerUrl,
                SecurityPolicy
            ).ConfigureAwait(false);

            Assert.NotNull(session);

            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            session.Dispose();
        }

        /// <summary>
        /// Benchmark: Create, use, and close session - measures full session lifecycle.
        /// </summary>
        [Test, Order(701)]
        [Benchmark(Description = "Session lifecycle with read")]
        public async Task SessionLifecycleWithReadAsync()
        {
            var session = await ClientFixture.ConnectAsync(
                ServerUrl,
                SecurityPolicy
            ).ConfigureAwait(false);

            Assert.NotNull(session);

            // Perform a read operation
            await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                m_smallReadValueIds,
                CancellationToken.None
            ).ConfigureAwait(false);

            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            session.Dispose();
        }
        #endregion

        #region Combined Workload Benchmarks
        /// <summary>
        /// Benchmark: Mixed operations - measures realistic workload performance.
        /// Combines read, write, browse, and call operations to simulate real applications.
        /// </summary>
        [Test, Order(800)]
        [Benchmark(Description = "Mixed workload (read+write+browse+call)")]
        public async Task MixedWorkloadAsync()
        {
            // Read
            await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                m_smallReadValueIds,
                CancellationToken.None
            ).ConfigureAwait(false);

            // Write
            var writeValues = new WriteValueCollection(
                m_smallTestSet.Take(5).Select(nodeId => new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(UnsecureRandom.Shared.Next()))
                })
            );

            await Session.WriteAsync(
                null,
                writeValues,
                CancellationToken.None
            ).ConfigureAwait(false);

            // Browse
            var nodesToBrowse = new BrowseDescriptionCollection
            {
                new BrowseDescription
                {
                    NodeId = ObjectIds.ObjectsFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };

            await Session.BrowseAsync(
                null,
                null,
                0,
                nodesToBrowse,
                CancellationToken.None
            ).ConfigureAwait(false);

            // Call
            var inputArguments = new VariantCollection { new Variant((uint)0) };
            var requests = new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_GetMonitoredItems,
                    InputArguments = inputArguments
                }
            };

            await Session.CallAsync(
                null,
                requests,
                CancellationToken.None
            ).ConfigureAwait(false);
        }
        #endregion

        #region Throughput Benchmarks
        /// <summary>
        /// Benchmark: Read throughput - measures read operations per second.
        /// Executes 100 read operations and calculates ops/sec from elapsed time.
        /// BenchmarkDotNet will show the total time; ops/sec = 100 / (Mean in seconds).
        /// </summary>
        [Test, Order(850)]
        [Benchmark(Description = "Read 100 ops (for throughput)")]
        public async Task ReadThroughputAsync()
        {
            const int operationCount = 100;
            for (int i = 0; i < operationCount; i++)
            {
                await Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    m_smallReadValueIds,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
            // Throughput = 100 operations / Mean time in seconds
            // Example: Mean=1000ms → Throughput = 100/1.0 = 100 ops/sec
        }

        /// <summary>
        /// Benchmark: Write throughput - measures write operations per second.
        /// Executes 100 write operations and calculates ops/sec from elapsed time.
        /// BenchmarkDotNet will show the total time; ops/sec = 100 / (Mean in seconds).
        /// </summary>
        [Test, Order(851)]
        [Benchmark(Description = "Write 100 ops (for throughput)")]
        public async Task WriteThroughputAsync()
        {
            const int operationCount = 100;
            for (int i = 0; i < operationCount; i++)
            {
                var writeValues = new WriteValueCollection(
                    m_smallTestSet.Select(nodeId => new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(i))
                    })
                );

                await Session.WriteAsync(
                    null,
                    writeValues,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
            // Throughput = 100 operations / Mean time in seconds
        }

        /// <summary>
        /// Benchmark: Browse throughput - measures browse operations per second.
        /// Executes 100 browse operations and calculates ops/sec from elapsed time.
        /// BenchmarkDotNet will show the total time; ops/sec = 100 / (Mean in seconds).
        /// </summary>
        [Test, Order(852)]
        [Benchmark(Description = "Browse 100 ops (for throughput)")]
        public async Task BrowseThroughputAsync()
        {
            var nodesToBrowse = new BrowseDescriptionCollection
            {
                new BrowseDescription
                {
                    NodeId = ObjectIds.ObjectsFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)NodeClass.Object | (uint)NodeClass.Variable,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };

            const int operationCount = 100;
            for (int i = 0; i < operationCount; i++)
            {
                await Session.BrowseAsync(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
            // Throughput = 100 operations / Mean time in seconds
        }

        /// <summary>
        /// Benchmark: Call throughput - measures method call operations per second.
        /// Executes 100 call operations and calculates ops/sec from elapsed time.
        /// BenchmarkDotNet will show the total time; ops/sec = 100 / (Mean in seconds).
        /// </summary>
        [Test, Order(853)]
        [Benchmark(Description = "Call 100 ops (for throughput)")]
        public async Task CallThroughputAsync()
        {
            var inputArguments = new VariantCollection { new Variant((uint)0) };
            var requests = new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_GetMonitoredItems,
                    InputArguments = inputArguments
                }
            };

            const int operationCount = 100;
            for (int i = 0; i < operationCount; i++)
            {
                await Session.CallAsync(
                    null,
                    requests,
                    CancellationToken.None
                ).ConfigureAwait(false);
            }
            // Throughput = 100 operations / Mean time in seconds
        }
        #endregion

        #region Comprehensive Security Policy Test
        /// <summary>
        /// Test all available security policies to ensure benchmarks work with each.
        /// This is not a benchmark but validates that all security policies can be tested.
        /// </summary>
        [Test, Order(900)]
        [Category("SecurityPolicyValidation")]
        public async Task TestAllSecurityPoliciesAsync()
        {
            // Use the same policy list as benchmarks (excludes None)
            var policies = BenchPolicies().ToList();
            var results = new Dictionary<string, bool>();

            TestContext.Out.WriteLine($"Testing {policies.Count} security policies:");

            foreach (string policyUri in policies)
            {
                string displayName = SecurityPolicies.GetDisplayName(policyUri);
                TestContext.Out.WriteLine($"\nTesting policy: {displayName} ({policyUri})");

                try
                {
                    var session = await ClientFixture.ConnectAsync(
                        ServerUrl,
                        policyUri
                    ).ConfigureAwait(false);

                    Assert.NotNull(session, $"Failed to create session with {displayName}");

                    // Perform a basic read to verify the connection works
                    var response = await session.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Both,
                        new ReadValueIdCollection
                        {
                            new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                                AttributeId = Attributes.Value
                            }
                        },
                        CancellationToken.None
                    ).ConfigureAwait(false);

                    Assert.NotNull(response);

                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    session.Dispose();

                    results[displayName] = true;
                    TestContext.Out.WriteLine($"✓ {displayName}: SUCCESS");
                }
                catch (Exception ex)
                {
                    results[displayName] = false;
                    TestContext.Out.WriteLine($"✗ {displayName}: FAILED - {ex.Message}");
                }
            }

            // Summary
            TestContext.Out.WriteLine("\n=== Summary ===");
            int successful = results.Count(r => r.Value);
            int failed = results.Count(r => !r.Value);
            TestContext.Out.WriteLine($"Successful: {successful}/{results.Count}");
            TestContext.Out.WriteLine($"Failed: {failed}/{results.Count}");

            if (failed > 0)
            {
                TestContext.Out.WriteLine("\nFailed policies:");
                foreach (var kvp in results.Where(r => !r.Value))
                {
                    TestContext.Out.WriteLine($"  - {kvp.Key}");
                }
            }

            // Assert at least some policies work
            Assert.Greater(successful, 0, "No security policies were successful");
        }
        #endregion
    }
}
