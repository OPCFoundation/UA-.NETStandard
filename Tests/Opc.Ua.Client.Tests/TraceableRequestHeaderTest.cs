using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    public class TraceableRequestHeaderTest : ClientTestFramework
    {
        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            return base.OneTimeSetUpAsync(
                writer: null,
                securityNone: false,
                enableClientSideTracing: true,
                enableServerSideTracing: true);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            Utils.SilentDispose(ClientFixture);
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public override void GlobalSetup()
        {
            Console.WriteLine("GlobalSetup: Start Server");
            OneTimeSetUpAsync(
                    Console.Out,
                    enableClientSideTracing: true,
                    enableServerSideTracing: true,
                    disableActivityLogging: true)
                .GetAwaiter()
                .GetResult();
            Console.WriteLine("GlobalSetup: Connecting");
            InitializeSession(
                ClientFixture.ConnectAsync(ServerUrl, SecurityPolicy).GetAwaiter().GetResult());
            Console.WriteLine("GlobalSetup: Ready");
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public override void GlobalCleanup()
        {
            base.GlobalCleanup();
        }

        [Test]
        [Benchmark]
        public async Task ReadValuesWithTracingAsync()
        {
            NamespaceTable namespaceUris = Session.NamespaceUris;
            var testSet = new NodeIdCollection(GetTestSetStatic(namespaceUris));
            testSet.AddRange(GetTestSetFullSimulation(namespaceUris));
            DataValueCollection values;
            IList<ServiceResult> errors;
            (values, errors) = await Session.ReadValuesAsync(testSet).ConfigureAwait(false);
            Assert.AreEqual(testSet.Count, values.Count);
            Assert.AreEqual(testSet.Count, errors.Count);
        }
    }
}
