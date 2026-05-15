using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

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
            return base.OneTimeSetUpCoreAsync(
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
            ClientFixture?.Dispose();
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<RequestHeaderTest>();
            logger.LogInformation("GlobalSetup: Start Server");
            OneTimeSetUpCoreAsync(
                    enableClientSideTracing: true,
                    enableServerSideTracing: true,
                    disableActivityLogging: true)
                .GetAwaiter()
                .GetResult();
            logger.LogInformation("GlobalSetup: Connecting");
            InitializeSession(
                ClientFixture.ConnectAsync(ServerUrl, SecurityPolicy).GetAwaiter().GetResult());
            logger.LogInformation("GlobalSetup: Ready");
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
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetFullSimulation(namespaceUris));
            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) =
                await Session.ReadValuesAsync(testSet).ConfigureAwait(false);
            Assert.That(values.Count, Is.EqualTo(testSet.Count));
            Assert.That(errors.Count, Is.EqualTo(testSet.Count));
        }
    }
}
