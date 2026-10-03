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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// This stack's client against a 1.5.378 server (the legacy peer in
    /// server mode). The server exposes an "Interop" folder below Objects
    /// in the namespace <see cref="kLegacyNamespace"/>.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class LegacyServerInteropTests
    {
        private const string kLegacyNamespace = "urn:opcfoundation.org:interop:legacy";
        private const string kUserName = "interop";
        private const string kPassword = "interop-password";
        private static readonly TimeSpan s_startTimeout = TimeSpan.FromMinutes(2);

        private ITelemetryContext m_telemetry;
        private LegacyPeerProcess m_server;
        private ClientFixture m_clientFixture;
        private string m_pkiRoot;
        private Uri m_serverUrl;
        private ArrayOf<EndpointDescription> m_endpoints;
        private ISession m_session;
        private ushort m_ns;

        public static readonly object[] SecurityCases =
        [
            new object[] { SecurityPolicies.None, MessageSecurityMode.None, false },
            new object[] { SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign, false },
            new object[] { SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, false },
            new object[] { SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, true },
            new object[] { SecurityPolicies.Aes128_Sha256_RsaOaep, MessageSecurityMode.SignAndEncrypt, false },
            new object[] { SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.Sign, false },
            new object[] { SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt, true }
        ];

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = InteropPki.CreateRoot();

            (m_server, string url) = await LegacyPeerProcess
                .StartServerAsync(InteropPki.ServerPki(m_pkiRoot), s_startTimeout)
                .ConfigureAwait(false);
            m_serverUrl = new Uri(url);

            // The fixture-wide session is idle between tests; the 10 s default
            // lets the 1.5 server, a separate process, expire it during a stall
            // of the test host (see ClientTestFramework.SharedSessionTimeout).
            m_clientFixture = new ClientFixture(telemetry: m_telemetry) { SessionTimeout = 120_000 };
            await m_clientFixture.LoadClientConfigurationAsync(InteropPki.ClientPki(m_pkiRoot))
                .ConfigureAwait(false);

            m_endpoints = await m_clientFixture.GetEndpointsAsync(m_serverUrl).ConfigureAwait(false);
            m_session = await ConnectAsync(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                null).ConfigureAwait(false);
            m_ns = m_session.NamespaceUris.GetIndexOrAppend(kLegacyNamespace);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            try
            {
                if (m_session != null)
                {
                    await m_session.CloseAsync().ConfigureAwait(false);
                    m_session.Dispose();
                }
                if (m_clientFixture != null)
                {
                    await m_clientFixture.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await InteropPki.StopAndDeleteAsync(m_server, m_pkiRoot).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(100)]
        public void GetEndpointsReturnsLegacyEndpoints()
        {
            Assert.That(m_endpoints.Count, Is.GreaterThanOrEqualTo(SecurityCases.Length - 1));
            foreach (EndpointDescription endpoint in m_endpoints)
            {
                Assert.That(endpoint.Server.ApplicationUri, Does.Contain("LegacyInteropServer"));
                if (endpoint.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(endpoint.ServerCertificate.IsEmpty, Is.False, endpoint.SecurityPolicyUri);
                }
            }
        }

        /// <summary>
        /// Opens a session for each security configuration, reads the server
        /// state and closes the session.
        /// </summary>
        [Test]
        [Order(110)]
        [TestCaseSource(nameof(SecurityCases))]
        public async Task ConnectReadAndCloseAsync(
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            bool userNameIdentity)
        {
            IUserIdentity identity = userNameIdentity
                ? new UserIdentity(kUserName, System.Text.Encoding.UTF8.GetBytes(kPassword))
                : null;
            ISession session = await ConnectAsync(securityPolicyUri, securityMode, identity)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Endpoint.SecurityPolicyUri, Is.EqualTo(securityPolicyUri));
                Assert.That(session.Endpoint.SecurityMode, Is.EqualTo(securityMode));

                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(state.StatusCode), Is.True, state.StatusCode.ToString());
                Assert.That(state.WrappedValue.TryGetValue(out int serverState), Is.True);
                Assert.That((ServerState)serverState, Is.EqualTo(ServerState.Running));
            }
            finally
            {
                StatusCode closed = await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
                Assert.That(StatusCode.IsGood(closed), Is.True, closed.ToString());
            }
        }

        [Test]
        [Order(120)]
        public void ConnectWithWrongPasswordIsRejected()
        {
            var identity = new UserIdentity(kUserName, System.Text.Encoding.UTF8.GetBytes("wrong"));
            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                () => ConnectAsync(SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt, identity));
            Assert.That(
                sre.StatusCode.Code,
                Is.AnyOf(StatusCodes.BadUserAccessDenied, StatusCodes.BadIdentityTokenRejected),
                sre.ToString());
        }

        [Test]
        [Order(200)]
        public async Task ReadServerStatusStructureAsync()
        {
            DataValue value = await m_session.ReadValueAsync(VariableIds.Server_ServerStatus)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, value.StatusCode.ToString());
            Assert.That(value.WrappedValue.TryGetStructure(out ServerStatusDataType status), Is.True,
                value.WrappedValue.ToString());
            Assert.That(status.State, Is.EqualTo(ServerState.Running));
            Assert.That(status.BuildInfo.ProductUri, Is.Not.Empty);
            // A 1.5 release; -p:LegacyStackVersion selects which one.
            Assert.That(status.BuildInfo.SoftwareVersion, Does.StartWith("1.5."));
        }

        [Test]
        [Order(210)]
        public async Task BrowseInteropFolderAsync()
        {
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription
                {
                    NodeId = new NodeId("Interop", m_ns),
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            BrowseResponse response = await m_session
                .BrowseAsync(null, null, 0, nodesToBrowse, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            string[] names = [.. response.Results[0].References.ToArray().Select(r => r.BrowseName.Name)];
            Assert.That(
                names,
                Is.SupersetOf(new[] { "Int32", "Double", "String", "Range", "Counter", "Add", "Int32Array" }));
        }

        [Test]
        [Order(220)]
        public async Task TranslateBrowsePathAsync()
        {
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath
                {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                TargetName = new QualifiedName("Interop", m_ns)
                            },
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.Organizes,
                                TargetName = new QualifiedName("Int32", m_ns)
                            }
                        ]
                    }
                }
            ];
            TranslateBrowsePathsToNodeIdsResponse response = await m_session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                response.Results[0].StatusCode.ToString());
            Assert.That(
                ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, m_session.NamespaceUris),
                Is.EqualTo(new NodeId("Int32", m_ns)));
        }

        [Test]
        [Order(300)]
        public async Task ReadBuiltInTypesAsync()
        {
            DataValue[] values = await ReadValuesAsync(
                "Boolean", "Int32", "UInt64", "Double", "String", "DateTime", "Guid", "ByteString",
                "LocalizedText", "QualifiedName", "NodeId", "Int32Array", "StringArray").ConfigureAwait(false);

            Assert.That(values[0].WrappedValue.TryGetValue(out bool b) && b, Is.True, "Boolean");
            Assert.That(values[1].WrappedValue.TryGetValue(out int i32), Is.True, "Int32");
            Assert.That(i32, Is.EqualTo(42));
            Assert.That(values[2].WrappedValue.TryGetValue(out ulong u64), Is.True, "UInt64");
            Assert.That(u64, Is.EqualTo(ulong.MaxValue));
            Assert.That(values[3].WrappedValue.TryGetValue(out double d), Is.True, "Double");
            Assert.That(d, Is.EqualTo(3.25));
            Assert.That(values[4].WrappedValue.TryGetValue(out string s), Is.True, "String");
            Assert.That(s, Is.EqualTo("legacy"));
            Assert.That(values[5].WrappedValue.TryGetValue(out DateTimeUtc dt), Is.True, "DateTime");
            Assert.That((DateTime)dt, Is.EqualTo(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            Assert.That(values[6].WrappedValue.TryGetValue(out Uuid guid), Is.True, "Guid");
            Assert.That((Guid)guid, Is.EqualTo(new Guid("8d2b5c6e-6c0e-4a4c-9f2b-0b6a3a6b1e01")));
            Assert.That(values[7].WrappedValue.TryGetValue(out ByteString bytes), Is.True, "ByteString");
            Assert.That(bytes.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
            Assert.That(values[8].WrappedValue.TryGetValue(out LocalizedText text), Is.True, "LocalizedText");
            Assert.That(text.Locale, Is.EqualTo("de"));
            Assert.That(text.Text, Is.EqualTo("Hallo"));
            Assert.That(values[9].WrappedValue.TryGetValue(out QualifiedName qn), Is.True, "QualifiedName");
            Assert.That(qn, Is.EqualTo(new QualifiedName("Name", m_ns)));
            Assert.That(values[10].WrappedValue.TryGetValue(out NodeId nodeId), Is.True, "NodeId");
            Assert.That(nodeId, Is.EqualTo(new NodeId("Interop", m_ns)));
            Assert.That(values[11].WrappedValue.TryGetValue(out ArrayOf<int> ints), Is.True, "Int32Array");
            Assert.That(ints.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(values[12].WrappedValue.TryGetValue(out ArrayOf<string> strings), Is.True, "StringArray");
            Assert.That(strings.ToArray(), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        [Order(310)]
        public async Task ReadStructureAsync()
        {
            DataValue[] values = await ReadValuesAsync("Range").ConfigureAwait(false);
            Assert.That(values[0].WrappedValue.TryGetStructure(out Range range), Is.True,
                values[0].WrappedValue.ToString());
            Assert.That(range.Low, Is.Zero);
            Assert.That(range.High, Is.EqualTo(100));
        }

        [Test]
        [Order(320)]
        public async Task ReadNodeAttributesAsync()
        {
            var nodeId = new NodeId("Int32", m_ns);
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.NodeClass },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DataType },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.ValueRank },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.AccessLevel }
            ];
            ReadResponse response = await m_session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response.Results[0].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That((NodeClass)nodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(response.Results[1].WrappedValue.TryGetValue(out QualifiedName browseName), Is.True);
            Assert.That(browseName.Name, Is.EqualTo("Int32"));
            Assert.That(response.Results[2].WrappedValue.TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(response.Results[3].WrappedValue.TryGetValue(out int valueRank), Is.True);
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(response.Results[4].WrappedValue.TryGetValue(out byte accessLevel), Is.True);
            Assert.That(accessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
        }

        [Test]
        [Order(400)]
        public async Task WriteAndReadBackAsync()
        {
            ArrayOf<WriteValue> nodesToWrite =
            [
                Write("Double", new Variant(-1.5e-300)),
                Write("String", new Variant("written by 2.0 äöü 中")),
                Write("Int32Array", new Variant(new[] { 7, 8, 9, 10 })),
                Write("Range", new Variant(new ExtensionObject(new Range { Low = -5, High = 5 })))
            ];
            WriteResponse response = await m_session
                .WriteAsync(null, nodesToWrite, CancellationToken.None)
                .ConfigureAwait(false);
            for (int ii = 0; ii < nodesToWrite.Count; ii++)
            {
                Assert.That(StatusCode.IsGood(response.Results[ii]), Is.True,
                    $"{nodesToWrite[ii].NodeId}: {response.Results[ii]}");
            }

            DataValue[] values = await ReadValuesAsync("Double", "String", "Int32Array", "Range")
                .ConfigureAwait(false);
            Assert.That(values[0].WrappedValue.TryGetValue(out double d) && d == -1.5e-300, Is.True);
            Assert.That(values[1].WrappedValue.TryGetValue(out string s), Is.True);
            Assert.That(s, Is.EqualTo("written by 2.0 äöü 中"));
            Assert.That(values[2].WrappedValue.TryGetValue(out ArrayOf<int> ints), Is.True);
            Assert.That(ints.ToArray(), Is.EqualTo(new[] { 7, 8, 9, 10 }));
            Assert.That(values[3].WrappedValue.TryGetStructure(out Range range), Is.True);
            Assert.That(range.Low, Is.EqualTo(-5));
            Assert.That(range.High, Is.EqualTo(5));
        }

        [Test]
        [Order(410)]
        public async Task WriteWithWrongTypeIsRejectedAsync()
        {
            ArrayOf<WriteValue> nodesToWrite = [Write("Int32", new Variant("not a number"))];
            WriteResponse response = await m_session
                .WriteAsync(null, nodesToWrite, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(response.Results[0].Code, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        [Order(500)]
        public async Task CallMethodAsync()
        {
            ArrayOf<CallMethodRequest> requests =
            [
                new CallMethodRequest
                {
                    ObjectId = new NodeId("Interop", m_ns),
                    MethodId = new NodeId("Add", m_ns),
                    InputArguments = [new Variant(40), new Variant(2)]
                }
            ];
            CallResponse response = await m_session
                .CallAsync(null, requests, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                response.Results[0].StatusCode.ToString());
            Assert.That(response.Results[0].OutputArguments[0].TryGetValue(out int sum), Is.True);
            Assert.That(sum, Is.EqualTo(42));
        }

        [Test]
        [Order(510)]
        public async Task CallMethodWithBadArgumentsIsRejectedAsync()
        {
            ArrayOf<CallMethodRequest> requests =
            [
                new CallMethodRequest
                {
                    ObjectId = new NodeId("Interop", m_ns),
                    MethodId = new NodeId("Add", m_ns),
                    InputArguments = [new Variant(1)]
                }
            ];
            CallResponse response = await m_session
                .CallAsync(null, requests, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadArgumentsMissing));
        }

        [Test]
        [Order(600)]
        [CancelAfter(60_000)]
        public async Task SubscribeToDataChangesAsync(CancellationToken ct)
        {
            var subscription = new Subscription(m_session.DefaultSubscription)
            {
                PublishingInterval = 100,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true
            };
            Assert.That(m_session.AddSubscription(subscription), Is.True);
            try
            {
                await subscription.CreateAsync(ct).ConfigureAwait(false);

                int notifications = 0;
                var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var item = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = new NodeId("Counter", m_ns),
                    AttributeId = Attributes.Value,
                    SamplingInterval = 100,
                    QueueSize = 10
                };
                item.Notification += (_, _) =>
                {
                    if (Interlocked.Increment(ref notifications) >= 5)
                    {
                        received.TrySetResult(true);
                    }
                };
                subscription.AddItem(item);
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(item.Status.Error), Is.True, item.Status.Error?.ToString());

                Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(20), ct))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(received.Task),
                    $"Only {notifications} data change notifications from the 1.5.378 server in 20 s.");
            }
            finally
            {
                await subscription.DeleteAsync(true, ct).ConfigureAwait(false);
                await m_session.RemoveSubscriptionAsync(subscription, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An Int32 array of 90 000 elements (360 kB, many message chunks)
        /// below the server's MaxArrayLength of 100 000.
        /// </summary>
        [Test]
        [Order(700)]
        public async Task LargeArrayRoundTripAsync()
        {
            int[] values = [.. Enumerable.Range(0, 90_000).Select(i => (i * 7919) ^ 0x5A5A)];
            await WriteAndCompareAsync("Int32Array", new Variant(values)).ConfigureAwait(false);
        }

        /// <summary>
        /// A ByteString of 900 kB below the server's MaxByteStringLength of 1 MB.
        /// </summary>
        [Test]
        [Order(710)]
        public async Task LargeByteStringRoundTripAsync()
        {
            byte[] value = new byte[900 * 1024];
            new Random(4711).NextBytes(value);
            await WriteAndCompareAsync("ByteString", new Variant(ByteString.From(value))).ConfigureAwait(false);
        }

        /// <summary>
        /// Requests the 1.5 server cannot decode because a value crosses its
        /// MaxArrayLength or MaxByteStringLength are rejected with a status
        /// code, and the session stays usable.
        /// </summary>
        [Test]
        [Order(720)]
        public async Task ValueAboveServerLimitIsRejectedAsync(
            [Values("Int32Array", "ByteString")] string variable)
        {
            Variant value = variable == "Int32Array"
                ? new Variant(new int[150_000])
                : new Variant(ByteString.From(new byte[2 * 1024 * 1024]));

            StatusCode result;
            try
            {
                ArrayOf<WriteValue> nodesToWrite = [Write(variable, value)];
                WriteResponse response = await m_session
                    .WriteAsync(null, nodesToWrite, CancellationToken.None)
                    .ConfigureAwait(false);
                result = response.Results[0];
            }
            catch (ServiceResultException sre)
            {
                result = sre.StatusCode;
            }
            Assert.That(StatusCode.IsBad(result), Is.True, "the oversized write was accepted");
            TestContext.Out.WriteLine($"The 1.5 server rejected the oversized {variable} with {result}.");

            DataValue state = await m_session.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(state.StatusCode), Is.True,
                $"the session is unusable after the rejected write ({result}): {state.StatusCode}");
        }

        /// <summary>
        /// A client that announces a 256 kB MaxMessageSize reads a 900 kB
        /// value: the 1.5 server must answer with a status code instead of a
        /// response the client cannot receive, and the session stays usable.
        /// </summary>
        [Test]
        [Order(730)]
        public async Task ResponseAboveClientLimitIsRejectedAsync()
        {
            byte[] value = new byte[900 * 1024];
            await WriteAndCompareAsync("ByteString", new Variant(ByteString.From(value))).ConfigureAwait(false);

            await using var smallClient = new ClientFixture(telemetry: m_telemetry);
            await smallClient.LoadClientConfigurationAsync(InteropPki.ClientPki(m_pkiRoot)).ConfigureAwait(false);
            smallClient.Config.TransportQuotas.MaxMessageSize = 256 * 1024;
            ISession session = await ConnectAsync(
                smallClient,
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                null).ConfigureAwait(false);
            try
            {
                StatusCode result;
                try
                {
                    DataValue read = await session.ReadValueAsync(new NodeId("ByteString", m_ns))
                        .ConfigureAwait(false);
                    result = read.StatusCode;
                }
                catch (ServiceResultException sre)
                {
                    result = sre.StatusCode;
                }
                Assert.That(StatusCode.IsBad(result), Is.True, "a response above the client limit was delivered");
                TestContext.Out.WriteLine($"The 1.5 server answered the oversized response with {result}.");

                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(state.StatusCode), Is.True,
                    $"the session is unusable after the oversized response ({result}): {state.StatusCode}");
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
            }
        }

        /// <summary>
        /// Browses the Interop folder three references at a time with
        /// BrowseNext, compares with a browse without limit, and releases an
        /// open continuation point.
        /// </summary>
        [Test]
        [Order(740)]
        public async Task BrowseWithContinuationPointsAsync()
        {
            var folder = new NodeId("Interop", m_ns);
            List<ReferenceDescription> all = await BrowseAllAsync(folder, 0).ConfigureAwait(false);
            List<ReferenceDescription> paged = await BrowseAllAsync(folder, 3).ConfigureAwait(false);
            Assert.That(all, Has.Count.GreaterThan(10));
            Assert.That(
                paged.Select(r => r.NodeId.ToString()),
                Is.EqualTo(all.Select(r => r.NodeId.ToString())),
                "the paged browse returned different references");

            BrowseResponse first = await m_session
                .BrowseAsync(null, null, 3, [BrowseForward(folder)], CancellationToken.None)
                .ConfigureAwait(false);
            ByteString continuationPoint = first.Results[0].ContinuationPoint;
            Assert.That(continuationPoint.IsEmpty, Is.False, "no continuation point returned");

            BrowseNextResponse released = await m_session
                .BrowseNextAsync(null, true, [continuationPoint], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(released.Results[0].StatusCode), Is.True,
                released.Results[0].StatusCode.ToString());
            BrowseNextResponse reused = await m_session
                .BrowseNextAsync(null, false, [continuationPoint], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                reused.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadContinuationPointInvalid),
                "a released continuation point was accepted");
        }

        /// <summary>
        /// The session reads the 1.5 server's MaxNodesPerRead of 100 from its
        /// OperationLimits and splits a read of 250 nodes; with client
        /// splitting switched off the 1.5 server rejects the read with
        /// BadTooManyOperations.
        /// </summary>
        [Test]
        [Order(750)]
        public async Task ReadAboveOperationLimitAsync()
        {
            Assert.That(m_session.OperationLimits.MaxNodesPerRead, Is.EqualTo(100u),
                "the session did not take MaxNodesPerRead from the 1.5 server");

            ArrayOf<ReadValueId> nodesToRead =
            [
                .. Enumerable.Range(0, 250).Select(_ => new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_State,
                    AttributeId = Attributes.Value
                })
            ];
            ReadResponse split = await m_session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(split.Results.ToArray().Count(r => StatusCode.IsGood(r.StatusCode)), Is.EqualTo(250));

            uint limit = m_session.OperationLimits.MaxNodesPerRead;
            m_session.OperationLimits.MaxNodesPerRead = 0;
            StatusCode result;
            try
            {
                ReadResponse response = await m_session
                    .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None)
                    .ConfigureAwait(false);
                result = response.ResponseHeader.ServiceResult;
            }
            catch (ServiceResultException sre)
            {
                result = sre.StatusCode;
            }
            finally
            {
                m_session.OperationLimits.MaxNodesPerRead = limit;
            }
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        /// <summary>
        /// Keeps reading on a SignAndEncrypt channel through a security token
        /// renewal. Both stacks revise the lifetime to at least 60 s and the
        /// client renews at 75 %, so this runs about a minute.
        /// </summary>
        [Test]
        [Order(800)]
        [CancelAfter(180_000)]
        public async Task ReadThroughTokenRenewalAsync(CancellationToken ct)
        {
            await using var client = new ClientFixture(telemetry: m_telemetry);
            await client.LoadClientConfigurationAsync(InteropPki.ClientPki(m_pkiRoot)).ConfigureAwait(false);
            client.Config.TransportQuotas.SecurityTokenLifetime = 60_000;
            client.SessionTimeout = 120_000;
            ISession session = await ConnectAsync(
                client,
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                null).ConfigureAwait(false);
            try
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                int reads = 0;
                while (elapsed.Elapsed < TimeSpan.FromSeconds(65))
                {
                    DataValue value = await session
                        .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, ct)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(value.StatusCode), Is.True,
                        $"read {reads} after {elapsed.Elapsed.TotalSeconds:F0} s: {value.StatusCode}");
                    reads++;
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(ct).ConfigureAwait(false);
                session.Dispose();
            }
        }

        private async Task WriteAndCompareAsync(string name, Variant value)
        {
            ArrayOf<WriteValue> nodesToWrite = [Write(name, value)];
            WriteResponse response = await m_session
                .WriteAsync(null, nodesToWrite, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True, $"{name}: {response.Results[0]}");
            DataValue[] read = await ReadValuesAsync(name).ConfigureAwait(false);
            Assert.That(read[0].WrappedValue, Is.EqualTo(value), $"{name} read back a different value");
        }

        private static BrowseDescription BrowseForward(NodeId nodeId)
        {
            return new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
        }

        private async Task<List<ReferenceDescription>> BrowseAllAsync(NodeId nodeId, uint maxReferences)
        {
            BrowseResponse response = await m_session
                .BrowseAsync(null, null, maxReferences, [BrowseForward(nodeId)], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            var references = new List<ReferenceDescription>(response.Results[0].References.ToArray());
            ByteString continuationPoint = response.Results[0].ContinuationPoint;
            while (!continuationPoint.IsEmpty)
            {
                BrowseNextResponse next = await m_session
                    .BrowseNextAsync(null, false, [continuationPoint], CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(next.Results[0].StatusCode), Is.True,
                    next.Results[0].StatusCode.ToString());
                references.AddRange(next.Results[0].References.ToArray());
                continuationPoint = next.Results[0].ContinuationPoint;
            }
            return references;
        }

        private Task<ISession> ConnectAsync(
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            IUserIdentity identity)
        {
            return ConnectAsync(m_clientFixture, securityPolicyUri, securityMode, identity);
        }

        private async Task<ISession> ConnectAsync(
            ClientFixture clientFixture,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            IUserIdentity identity)
        {
            EndpointDescription description = m_endpoints.ToArray().FirstOrDefault(e =>
                e.SecurityPolicyUri == securityPolicyUri && e.SecurityMode == securityMode);
            Assert.That(description, Is.Not.Null,
                $"The 1.5.378 server offers no {securityPolicyUri}/{securityMode} endpoint.");

            var configuration = EndpointConfiguration.Create(clientFixture.Config);
            var endpoint = new ConfiguredEndpoint(null, description, configuration);
            return await clientFixture.ConnectAsync(endpoint, identity).ConfigureAwait(false);
        }

        private async Task<DataValue[]> ReadValuesAsync(params string[] names)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                .. names.Select(name => new ReadValueId
                {
                    NodeId = new NodeId(name, m_ns),
                    AttributeId = Attributes.Value
                })
            ];
            ReadResponse response = await m_session
                .ReadAsync(null, 0, TimestampsToReturn.Both, nodesToRead, CancellationToken.None)
                .ConfigureAwait(false);
            DataValue[] values = response.Results.ToArray();
            Assert.That(values, Has.Length.EqualTo(names.Length));
            for (int ii = 0; ii < names.Length; ii++)
            {
                Assert.That(StatusCode.IsGood(values[ii].StatusCode), Is.True,
                    $"{names[ii]}: {values[ii].StatusCode}");
            }
            return values;
        }

        private WriteValue Write(string name, Variant value)
        {
            return new WriteValue
            {
                NodeId = new NodeId(name, m_ns),
                AttributeId = Attributes.Value,
                Value = new DataValue(value)
            };
        }
    }
}
