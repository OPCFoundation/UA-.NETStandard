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
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Configuration;

namespace Opc.Ua.Interop.LegacyPeer
{
    /// <summary>
    /// Runs the 1.5.x client side of the interop tests against a server
    /// that exposes the Quickstarts reference server address space.
    /// </summary>
    /// <remarks>
    /// Every check prints one line "RESULT {json}" with the properties
    /// check, outcome (Passed, Failed) and message. The 2.0 tests turn each
    /// line into a test result of its own. "Connect" and "CloseSession" run
    /// always; the other checks run when named by --checks.
    /// </remarks>
    public static partial class LegacyClientChecks
    {
        public const string ApplicationName = "LegacyInteropClient";
        public const string ReferenceServerNamespace = "http://opcfoundation.org/Quickstarts/ReferenceServer";
        public const string TestDataNamespace = "http://test.org/UA/Data/";

        /// <summary>
        /// The checks a client run can select, in the order they run.
        /// </summary>
        private static readonly (string Name, Func<ClientContext, Task> Check)[] s_checks =
        [
            ("NamespaceArray", NamespaceArrayAsync),
            ("ReadServerStatusStructure", ReadServerStatusStructureAsync),
            ("BrowseObjectsFolder", BrowseObjectsFolderAsync),
            ("TranslateBrowsePath", TranslateBrowsePathAsync),
            ("ReadScalars", ReadScalarsAsync),
            ("ReadAttributes", ReadAttributesAsync),
            ("WriteAndReadBack", WriteAndReadBackAsync),
            ("CallMethods", CallMethodsAsync),
            ("Subscription", SubscriptionAsync),
            ("ComplexTypes", ComplexTypesAsync),
            ("LargeArrayRoundTrip", LargeArrayRoundTripAsync),
            ("LargeByteStringRoundTrip", LargeByteStringRoundTripAsync),
            ("OversizedRequestRejected", OversizedRequestRejectedAsync),
            ("BrowseContinuationPoints", BrowseContinuationPointsAsync),
            ("ReadManyNodes", ReadManyNodesAsync),
            ("TokenRenewal", TokenRenewalAsync),
            // Events and alarms & conditions.
            ("EventSubscription", EventSubscriptionAsync),
            ("ConditionRefresh", ConditionRefreshAsync),
            ("AlarmAcknowledge", AlarmAcknowledgeAsync),
            // Subscription depth.
            ("DeadbandFilter", DeadbandFilterAsync),
            ("QueueOverflow", QueueOverflowAsync),
            ("Triggering", TriggeringAsync),
            ("Republish", RepublishAsync),
            ("TransferSubscription", TransferSubscriptionAsync),
            // Identity.
            ("WrongPasswordRejected", WrongPasswordRejectedAsync),
            ("X509UserToken", X509UserTokenAsync),
            // Services breadth.
            ("RegisterNodes", RegisterNodesAsync),
            ("HistoryReadRaw", HistoryReadRawAsync),
            ("NodeManagement", NodeManagementAsync),
            ("IndexRange", IndexRangeAsync),
            ("FindServers", FindServersAsync),
            ("SessionReconnect", SessionReconnectAsync)
        ];

        private const string kDefaultChecks =
            "NamespaceArray,ReadServerStatusStructure,BrowseObjectsFolder,TranslateBrowsePath," +
            "ReadScalars,ReadAttributes,WriteAndReadBack,CallMethods,Subscription";

        public static async Task<int> RunAsync(PeerOptions options)
        {
            string url = options.Get("url");
            string policy = options.Get("policy", SecurityPolicies.None);
            var mode = (MessageSecurityMode)Enum.Parse(
                typeof(MessageSecurityMode),
                options.Get("mode", nameof(MessageSecurityMode.None)),
                true);
            string user = options.Get("user", string.Empty);
            string password = options.Get("password", string.Empty);
            string expectedConnectError = options.Get("expect-connect-error", string.Empty);
            int tokenLifetime = options.GetInt("token-lifetime", 0);
            var selected = new HashSet<string>(
                options.Get("checks", kDefaultChecks).Split(',', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            // Connect and CloseSession always run; naming them is allowed.
            string[] unknown = [.. selected.Where(name =>
                name != "Connect" && name != "CloseSession" && s_checks.All(c => c.Name != name))];
            if (unknown.Length > 0)
            {
                throw new ArgumentException("unknown checks: " + string.Join(", ", unknown));
            }

            // The harness passes a deadline shorter than its own timeout, so the
            // checks end, and report, before the harness kills the process.
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(options.GetInt("timeout-seconds", 300)));
            CancellationToken ct = cts.Token;

            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));

            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            };
            // Quotas match the 2.0 reference server fixture so the large
            // message checks are limited by the server, not by this client.
            ApplicationConfiguration config = await application
                .Build(
                    "urn:localhost:opcfoundation.org:" + ApplicationName,
                    "uri:opcfoundation.org:" + ApplicationName)
                .SetMaxMessageSize(16 * 1024 * 1024)
                .SetMaxByteStringLength(16 * 1024 * 1024)
                .SetMaxStringLength(4 * 1024 * 1024)
                .SetMaxArrayLength(1024 * 1024)
                .AsClient()
                .AddSecurityConfiguration(
                    LegacyPki.ApplicationCertificates(
                        "CN=" + ApplicationName + ", O=OPC Foundation, DC=localhost",
                        options.PkiRoot,
                        options.Ecc),
                    options.PkiRoot)
                .SetAutoAcceptUntrustedCertificates(options.AutoAccept)
                .CreateAsync(ct)
                .ConfigureAwait(false);
            if (tokenLifetime > 0)
            {
                config.TransportQuotas.SecurityTokenLifetime = tokenLifetime;
            }
            bool haveCertificate = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(false);
            if (!haveCertificate)
            {
                throw new InvalidOperationException("The application certificate is invalid.");
            }
            if (options.GetBool("init-only", false))
            {
                Console.WriteLine("LEGACY-PKI-READY " + config.ApplicationUri);
                return Program.ExitSuccess;
            }

            var checks = new CheckRunner();

            ISession session = null;
            ConfiguredEndpoint configured = null;
            await checks.RunAsync("Connect", async () =>
            {
                try
                {
                    EndpointDescription endpoint = await SelectEndpointAsync(config, url, policy, mode, ct)
                        .ConfigureAwait(false);
                    IUserIdentity identity = string.IsNullOrEmpty(user)
                        ? new UserIdentity()
                        : new UserIdentity(user, System.Text.Encoding.UTF8.GetBytes(password));
                    configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(config));
                    session = await new DefaultSessionFactory(telemetry)
                        .CreateAsync(config, configured, false, false, ApplicationName, 60000, identity, null, ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre) when (!string.IsNullOrEmpty(expectedConnectError))
                {
                    string actual = StatusCodes.GetBrowseName(sre.StatusCode);
                    Require(expectedConnectError.Split(',').Contains(actual),
                        $"expected {expectedConnectError}, the connect failed with {actual}: {sre.Message}");
                    return;
                }
                Require(string.IsNullOrEmpty(expectedConnectError),
                    $"expected the connect to fail with {expectedConnectError}, but it succeeded");
                Require(session.Connected, "the session is not connected");
                Require(
                    session.Endpoint.SecurityPolicyUri == policy && session.Endpoint.SecurityMode == mode,
                    $"connected to {session.Endpoint.SecurityPolicyUri}/{session.Endpoint.SecurityMode}");
            }).ConfigureAwait(false);

            if (session == null)
            {
                return checks.Finish();
            }

            try
            {
                var context = new ClientContext(session, telemetry, options, ct)
                {
                    Config = config,
                    Url = url,
                    Endpoint = configured,
                    // Further sessions on the same endpoint (wrong password, X509
                    // user, subscription transfer) with another identity.
                    NewSessionAsync = identity => new DefaultSessionFactory(telemetry).CreateAsync(
                        config, configured, false, false, ApplicationName + " 2", 60000, identity, null, ct)
                };
                foreach ((string name, Func<ClientContext, Task> check) in s_checks)
                {
                    if (selected.Contains(name))
                    {
                        await checks.RunAsync(name, () => check(context)).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                await checks.RunAsync("CloseSession", async () =>
                {
                    StatusCode status = await ((Session)session).CloseAsync(ct).ConfigureAwait(false);
                    Require(StatusCode.IsGood(status), "CloseSession returned " + status);
                }).ConfigureAwait(false);
                session.Dispose();
            }

            return checks.Finish();
        }

        private static Task NamespaceArrayAsync(ClientContext c)
        {
            Require(c.Ns > 0 && c.Ns != ushort.MaxValue, "reference server namespace is missing");
            return Task.CompletedTask;
        }

        private static async Task ReadServerStatusStructureAsync(ClientContext c)
        {
            DataValue value = await c.Session
                .ReadValueAsync(VariableIds.Server_ServerStatus, c.Ct)
                .ConfigureAwait(false);
            Require(StatusCode.IsGood(value.StatusCode), "status " + value.StatusCode);
            var status = ExtensionObject.ToEncodeable(value.Value as ExtensionObject) as ServerStatusDataType;
            Require(status != null, "ServerStatus did not decode to ServerStatusDataType: " + value.Value);
            Require(status.State == ServerState.Running, "server state " + status.State);
            Require(status.BuildInfo != null && !string.IsNullOrEmpty(status.BuildInfo.ProductUri),
                "BuildInfo is empty");
        }

        private static async Task BrowseObjectsFolderAsync(ClientContext c)
        {
            ReferenceDescriptionCollection references = await BrowseAsync(c, ObjectIds.ObjectsFolder, 0)
                .ConfigureAwait(false);
            Require(references.Any(r => r.BrowseName.Name == "Server"), "Server object not found");
            Require(references.Any(r => r.BrowseName.Name == "CTT"), "CTT folder not found");
        }

        private static async Task TranslateBrowsePathAsync(ClientContext c)
        {
            var paths = new BrowsePathCollection
            {
                new BrowsePath
                {
                    StartingNode = ObjectIds.ObjectsFolder,
                    RelativePath = RelativePath.Parse("Server/ServerStatus/CurrentTime", c.Session.TypeTree)
                }
            };
            TranslateBrowsePathsToNodeIdsResponse response = await c.Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, c.Ct)
                .ConfigureAwait(false);
            Require(StatusCode.IsGood(response.Results[0].StatusCode), "status " + response.Results[0].StatusCode);
            Require(
                (NodeId)response.Results[0].Targets[0].TargetId == VariableIds.Server_ServerStatus_CurrentTime,
                "unexpected target " + response.Results[0].Targets[0].TargetId);
        }

        private static async Task ReadScalarsAsync(ClientContext c)
        {
            var nodes = new ReadValueIdCollection
            {
                Value(c.Id("Scalar_Static_Boolean")),
                Value(c.Id("Scalar_Static_Int32")),
                Value(c.Id("Scalar_Static_Double")),
                Value(c.Id("Scalar_Static_String")),
                Value(c.Id("Scalar_Static_DateTime")),
                Value(c.Id("Scalar_Static_Guid")),
                Value(c.Id("Scalar_Static_ByteString")),
                Value(c.Id("Scalar_Static_LocalizedText")),
                Value(c.Id("Scalar_Static_QualifiedName")),
                Value(c.Id("Scalar_Static_NodeId")),
                Value(c.Id("Scalar_Static_Arrays_Int32")),
                Value(c.Id("Scalar_Static_Arrays_String"))
            };
            ReadResponse response = await c.Session
                .ReadAsync(null, 0, TimestampsToReturn.Both, nodes, c.Ct)
                .ConfigureAwait(false);
            Require(response.Results.Count == nodes.Count, "result count " + response.Results.Count);
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                Require(StatusCode.IsGood(response.Results[ii].StatusCode),
                    $"{nodes[ii].NodeId}: {response.Results[ii].StatusCode}");
            }
        }

        private static async Task ReadAttributesAsync(ClientContext c)
        {
            NodeId nodeId = c.Id("Scalar_Static_Int32");
            var nodes = new ReadValueIdCollection
            {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.NodeClass },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DataType },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.AccessLevel }
            };
            ReadResponse response = await c.Session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodes, c.Ct)
                .ConfigureAwait(false);
            Require((int)response.Results[0].Value == (int)NodeClass.Variable,
                "NodeClass " + response.Results[0].Value);
            Require(((QualifiedName)response.Results[1].Value).Name == "Scalar_Static_Int32",
                "BrowseName " + response.Results[1].Value);
            Require((NodeId)response.Results[2].Value == DataTypeIds.Int32,
                "DataType " + response.Results[2].Value);
        }

        private static async Task WriteAndReadBackAsync(ClientContext c)
        {
            NodeId int32Id = c.Id("Scalar_Static_Int32");
            NodeId stringId = c.Id("Scalar_Static_String");
            NodeId doubleArrayId = c.Id("Scalar_Static_Arrays_Double");
            double[] doubles = [1.5, -2.25, 1e300];
            const string text = "written by 1.5 äöü";
            var writes = new WriteValueCollection
            {
                Write(int32Id, 1234567),
                Write(stringId, text),
                Write(doubleArrayId, doubles)
            };
            WriteResponse response = await c.Session.WriteAsync(null, writes, c.Ct).ConfigureAwait(false);
            for (int ii = 0; ii < writes.Count; ii++)
            {
                Require(StatusCode.IsGood(response.Results[ii]), $"{writes[ii].NodeId}: {response.Results[ii]}");
            }
            ReadResponse read = await c.Session
                .ReadAsync(null, 0, TimestampsToReturn.Neither,
                    new ReadValueIdCollection { Value(int32Id), Value(stringId), Value(doubleArrayId) }, c.Ct)
                .ConfigureAwait(false);
            Require(Equals(read.Results[0].Value, 1234567), "Int32 read back " + read.Results[0].Value);
            Require(Equals(read.Results[1].Value, text), "String read back " + read.Results[1].Value);
            Require(read.Results[2].Value is double[] readDoubles && readDoubles.SequenceEqual(doubles),
                "Double[] read back " + read.Results[2].Value);
        }

        private static async Task CallMethodsAsync(ClientContext c)
        {
            NodeId methods = c.Id("Methods");
            var requests = new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = methods,
                    MethodId = c.Id("Methods_Hello"),
                    InputArguments = new VariantCollection { new Variant("legacy") }
                },
                new CallMethodRequest
                {
                    ObjectId = methods,
                    MethodId = c.Id("Methods_Add"),
                    InputArguments = new VariantCollection { new Variant(1.5f), new Variant(2u) }
                }
            };
            CallResponse response = await c.Session.CallAsync(null, requests, c.Ct).ConfigureAwait(false);
            Require(StatusCode.IsGood(response.Results[0].StatusCode), "Hello " + response.Results[0].StatusCode);
            Require(Equals(response.Results[0].OutputArguments[0].Value, "hello legacy"),
                "Hello returned " + response.Results[0].OutputArguments[0]);
            Require(StatusCode.IsGood(response.Results[1].StatusCode), "Add " + response.Results[1].StatusCode);
            Require(Equals(response.Results[1].OutputArguments[0].Value, 3.5f),
                "Add returned " + response.Results[1].OutputArguments[0]);
        }

        private static async Task SubscriptionAsync(ClientContext c)
        {
            var subscription = new Subscription(c.Session.DefaultSubscription)
            {
                PublishingInterval = 100,
                KeepAliveCount = 10,
                LifetimeCount = 100
            };
            c.Session.AddSubscription(subscription);
            await subscription.CreateAsync(c.Ct).ConfigureAwait(false);

            int notifications = 0;
            var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                SamplingInterval = 100,
                QueueSize = 10
            };
            item.Notification += (_, e) =>
            {
                if (e.NotificationValue is MonitoredItemNotification &&
                    Interlocked.Increment(ref notifications) >= 3)
                {
                    received.TrySetResult(true);
                }
            };
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(c.Ct).ConfigureAwait(false);
            Require(ServiceResult.IsGood(item.Status.Error), "monitored item " + item.Status.Error);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(15), c.Ct))
                .ConfigureAwait(false);
            Require(completed == received.Task, $"only {notifications} data change notifications in 15 s");

            await subscription.DeleteAsync(true, c.Ct).ConfigureAwait(false);
            await c.Session.RemoveSubscriptionAsync(subscription, c.Ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads the server's custom data types with the 1.5 complex type
        /// system, decodes every variable whose data type is a custom
        /// structure, union or structure with optional fields,
        /// and writes a sample of the decoded values back unchanged.
        /// </summary>
        private static async Task ComplexTypesAsync(ClientContext c)
        {
            var typeSystem = new ComplexTypeSystem(c.Session, c.Telemetry);
            bool loaded;
            try
            {
                loaded = await typeSystem.LoadAsync(false, true, c.Ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                string diagnosis = await DiagnoseEncodingNodesAsync(c).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"loading the type system failed with {e.GetType().Name}: {e.Message}. {diagnosis}",
                    e);
            }
            Require(loaded, "the complex type system did not load every type");

            Type[] types = typeSystem.GetDefinedTypes();
            string[] typeNames = [.. types.Select(t => t.Name)];
            foreach (string required in new[] { "ScalarStructureDataType", "VectorUnion", "VectorWithOptionalFields" })
            {
                Require(typeNames.Contains(required),
                    $"type {required} was not created; created: {string.Join(", ", typeNames)}");
            }

            // Every variable below Objects whose data type is a custom structure.
            List<ReferenceDescription> variables = await BrowseTreeAsync(c, ObjectIds.ObjectsFolder, 30_000)
                .ConfigureAwait(false);
            var nodes = variables
                .Where(r => r.NodeClass == NodeClass.Variable && r.NodeId.NamespaceIndex != 0)
                .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, c.Session.NamespaceUris))
                .Distinct()
                .ToList();
            DataValueCollection dataTypes = await ReadAttributeAsync(c, nodes, Attributes.DataType)
                .ConfigureAwait(false);
            // Only structure data types: a BaseDataType-derived type such as
            // VariantDataType can hold a random ExtensionObject nobody can decode.
            var structureTypes = new HashSet<NodeId>(await BrowseStructureSubtypesAsync(c).ConfigureAwait(false));
            var custom = new List<NodeId>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (dataTypes[ii].Value is NodeId dataType &&
                    dataType.NamespaceIndex != 0 &&
                    structureTypes.Contains(dataType))
                {
                    custom.Add(nodes[ii]);
                }
            }
            Require(custom.Count >= 10, $"only {custom.Count} variables with a custom structure data type found");

            DataValueCollection values = await ReadAttributeAsync(c, custom, Attributes.Value).ConfigureAwait(false);
            DataValueCollection accessLevels = await ReadAttributeAsync(c, custom, Attributes.UserAccessLevel)
                .ConfigureAwait(false);
            var undecoded = new List<string>();
            var writes = new WriteValueCollection();
            int structures = 0;
            for (int ii = 0; ii < custom.Count; ii++)
            {
                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    continue;
                }
                foreach (ExtensionObject extension in ExtensionObjects(values[ii].Value))
                {
                    structures++;
                    if (extension.Body is not IEncodeable)
                    {
                        undecoded.Add($"{custom[ii]} ({extension.TypeId}, {extension.Encoding})");
                    }
                }
                if (writes.Count < 50 &&
                    accessLevels[ii].Value is byte access &&
                    (access & AccessLevels.CurrentWrite) != 0 &&
                    ExtensionObjects(values[ii].Value).Any())
                {
                    writes.Add(new WriteValue
                    {
                        NodeId = custom[ii],
                        AttributeId = Attributes.Value,
                        Value = new DataValue(values[ii].WrappedValue)
                    });
                }
            }
            Require(structures > 0, "no structure values were read");
            Require(undecoded.Count == 0,
                $"{undecoded.Count} of {structures} structures were not decoded: " +
                string.Join("; ", undecoded.Take(10)));

            Require(writes.Count > 0, "no writable structure variable found");
            WriteResponse response = await c.Session.WriteAsync(null, writes, c.Ct).ConfigureAwait(false);
            var rejected = new List<string>();
            for (int ii = 0; ii < writes.Count; ii++)
            {
                if (StatusCode.IsBad(response.Results[ii]))
                {
                    rejected.Add($"{writes[ii].NodeId}: {response.Results[ii]}");
                }
            }
            Require(rejected.Count == 0,
                $"{rejected.Count} of {writes.Count} structure writes were rejected: " +
                string.Join("; ", rejected.Take(10)));

            DataValueCollection readBack = await ReadAttributeAsync(
                c, [.. writes.Select(w => w.NodeId)], Attributes.Value).ConfigureAwait(false);
            var changed = new List<string>();
            for (int ii = 0; ii < writes.Count; ii++)
            {
                if (!Utils.IsEqual(writes[ii].Value.Value, readBack[ii].Value))
                {
                    changed.Add(writes[ii].NodeId.ToString());
                }
            }
            Require(changed.Count == 0,
                $"{changed.Count} structures changed in a write and read round trip: " +
                string.Join("; ", changed.Take(10)));
        }

        /// <summary>
        /// Reads every encoding node of every structure type the way the 1.5
        /// node cache does (all attributes including the optional ones,
        /// through ReadNodesAsync) and lists the nodes that fail, with the
        /// reason. The node cache keeps such a node as a placeholder without
        /// BrowseName, which the type loader then dereferences. A server that
        /// answers an unset optional attribute (RolePermissions,
        /// AccessRestrictions, ...) with Good and a null value instead of
        /// Bad_AttributeIdInvalid fails every node this way.
        /// </summary>
        private static async Task<string> DiagnoseEncodingNodesAsync(ClientContext c)
        {
            try
            {
                List<NodeId> structureTypes = await BrowseStructureSubtypesAsync(c).ConfigureAwait(false);
                var encodings = new List<NodeId>();
                foreach (NodeId type in structureTypes)
                {
                    encodings.AddRange((await BrowseAsync(c, type, 0, ReferenceTypeIds.HasEncoding)
                        .ConfigureAwait(false))
                        .Select(r => ExpandedNodeId.ToNodeId(r.NodeId, c.Session.NamespaceUris)));
                }
                (IList<Node> nodes, IList<ServiceResult> errors) = await c.Session
                    .ReadNodesAsync(encodings, NodeClass.Unspecified, true, c.Ct)
                    .ConfigureAwait(false);
                var failed = new List<string>();
                for (int ii = 0; ii < encodings.Count; ii++)
                {
                    if (ServiceResult.IsBad(errors[ii]) || nodes[ii]?.BrowseName == null)
                    {
                        failed.Add($"{encodings[ii]}: {errors[ii]}");
                    }
                }
                return $"Of {encodings.Count} encoding nodes of {structureTypes.Count} structure types, " +
                    $"{failed.Count} could not be read as nodes: {string.Join("; ", failed.Take(10))}";
            }
            catch (Exception e)
            {
                return "The encoding node diagnosis failed: " + e.Message;
            }
        }

        /// <summary>
        /// All subtypes of Structure, not including Structure itself.
        /// </summary>
        private static async Task<List<NodeId>> BrowseStructureSubtypesAsync(ClientContext c)
        {
            var structureTypes = new List<NodeId>();
            var queue = new Queue<NodeId>();
            queue.Enqueue(DataTypeIds.Structure);
            while (queue.Count > 0)
            {
                NodeId type = queue.Dequeue();
                foreach (ReferenceDescription subtype in await BrowseAsync(
                    c, type, 0, ReferenceTypeIds.HasSubtype).ConfigureAwait(false))
                {
                    var id = ExpandedNodeId.ToNodeId(subtype.NodeId, c.Session.NamespaceUris);
                    structureTypes.Add(id);
                    queue.Enqueue(id);
                }
            }
            return structureTypes;
        }

        private static async Task LargeArrayRoundTripAsync(ClientContext c)
        {
            // 200 000 Int32 = 800 kB, far more than one 64 kB message chunk.
            int[] values = [.. Enumerable.Range(0, 200_000).Select(i => (i * 7919) ^ 0x5A5A)];
            await WriteAndCompareAsync(c, c.Id("Scalar_Static_Arrays_Int32"), values).ConfigureAwait(false);
        }

        private static async Task LargeByteStringRoundTripAsync(ClientContext c)
        {
            // 3 MB, below the 4 MB ByteString limit of the reference server fixture.
            byte[] value = new byte[3 * 1024 * 1024];
            new Random(4711).NextBytes(value);
            await WriteAndCompareAsync(c, c.Id("Scalar_Static_ByteString"), value).ConfigureAwait(false);
        }

        /// <summary>
        /// A ByteString above the server's MaxByteStringLength but inside its
        /// MaxMessageSize must be rejected with a status code, and the
        /// session must remain usable afterwards.
        /// </summary>
        private static async Task OversizedRequestRejectedAsync(ClientContext c)
        {
            byte[] value = new byte[5 * 1024 * 1024];
            StatusCode result;
            try
            {
                WriteResponse response = await c.Session
                    .WriteAsync(null, new WriteValueCollection { Write(c.Id("Scalar_Static_ByteString"), value) }, c.Ct)
                    .ConfigureAwait(false);
                result = response.Results[0];
            }
            catch (ServiceResultException sre)
            {
                result = sre.StatusCode;
            }
            Require(StatusCode.IsBad(result), "a 5 MB ByteString write was accepted");

            DataValue status = await c.Session.ReadValueAsync(VariableIds.Server_ServerStatus_State, c.Ct)
                .ConfigureAwait(false);
            Require(StatusCode.IsGood(status.StatusCode),
                $"the session is unusable after the rejected write ({result}): {status.StatusCode}");
            Console.WriteLine("INFO OversizedRequestRejected: " + StatusCodes.GetBrowseName(result.Code));
        }

        /// <summary>
        /// Browses a folder one reference at a time with BrowseNext and
        /// compares with a browse without limit; then releases an open
        /// continuation point.
        /// </summary>
        private static async Task BrowseContinuationPointsAsync(ClientContext c)
        {
            NodeId folder = c.Id("Scalar_Static");
            ReferenceDescriptionCollection all = await BrowseAsync(c, folder, 0).ConfigureAwait(false);
            Require(all.Count > 10, $"only {all.Count} references below {folder}");

            ReferenceDescriptionCollection paged = await BrowseAsync(c, folder, 3).ConfigureAwait(false);
            Require(paged.Count == all.Count, $"paged browse returned {paged.Count} of {all.Count} references");
            Require(
                paged.Select(r => r.NodeId.ToString()).SequenceEqual(all.Select(r => r.NodeId.ToString())),
                "paged browse returned different references");

            BrowseResponse first = await c.Session
                .BrowseAsync(null, null, 3, new BrowseDescriptionCollection { BrowseAll(folder) }, c.Ct)
                .ConfigureAwait(false);
            Require(first.Results[0].ContinuationPoint != null, "no continuation point returned");
            BrowseNextResponse released = await c.Session
                .BrowseNextAsync(null, true, new ByteStringCollection { first.Results[0].ContinuationPoint }, c.Ct)
                .ConfigureAwait(false);
            Require(StatusCode.IsGood(released.Results[0].StatusCode),
                "releasing the continuation point returned " + released.Results[0].StatusCode);
            BrowseNextResponse reused = await c.Session
                .BrowseNextAsync(null, false, new ByteStringCollection { first.Results[0].ContinuationPoint }, c.Ct)
                .ConfigureAwait(false);
            Require(reused.Results[0].StatusCode == StatusCodes.BadContinuationPointInvalid,
                "a released continuation point returned " + reused.Results[0].StatusCode);
        }

        /// <summary>
        /// Reads more nodes than the server's MaxNodesPerRead. The session
        /// reads the limit from the server's OperationLimits and splits the
        /// request by it; with client splitting switched off, the server
        /// rejects the oversized request with BadTooManyOperations.
        /// </summary>
        private static async Task ReadManyNodesAsync(ClientContext c)
        {
            await ((Session)c.Session).FetchOperationLimitsAsync(c.Ct).ConfigureAwait(false);
            uint limit = c.Session.OperationLimits.MaxNodesPerRead;
            Require(limit > 0, "the server published no MaxNodesPerRead");

            var nodes = new ReadValueIdCollection();
            for (int ii = 0; ii < (limit * 2) + 1; ii++)
            {
                nodes.Add(Value(VariableIds.Server_ServerStatus_State));
            }
            ReadResponse split = await c.Session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodes, c.Ct)
                .ConfigureAwait(false);
            int good = split.Results.Count(r => StatusCode.IsGood(r.StatusCode));
            Require(good == nodes.Count,
                $"{good} of {nodes.Count} reads split by MaxNodesPerRead {limit} succeeded");

            StatusCode tooMany;
            c.Session.OperationLimits.MaxNodesPerRead = 0;
            try
            {
                ReadResponse response = await c.Session
                    .ReadAsync(null, 0, TimestampsToReturn.Neither, nodes, c.Ct)
                    .ConfigureAwait(false);
                tooMany = response.ResponseHeader.ServiceResult;
            }
            catch (ServiceResultException sre)
            {
                tooMany = sre.StatusCode;
            }
            finally
            {
                c.Session.OperationLimits.MaxNodesPerRead = limit;
            }
            Require(tooMany == StatusCodes.BadTooManyOperations,
                $"reading {nodes.Count} nodes in one request with MaxNodesPerRead {limit} returned {tooMany}");
        }

        /// <summary>
        /// Keeps reading for longer than the revised security token lifetime
        /// (the client renews at 75 %), so at least one token renewal happens
        /// between the two stacks while requests are in flight.
        /// </summary>
        private static async Task TokenRenewalAsync(ClientContext c)
        {
            int seconds = c.Options.GetInt("token-test-seconds", 65);
            var elapsed = Stopwatch.StartNew();
            int reads = 0;
            while (elapsed.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                DataValue value = await c.Session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, c.Ct)
                    .ConfigureAwait(false);
                Require(StatusCode.IsGood(value.StatusCode),
                    $"read {reads} after {elapsed.Elapsed.TotalSeconds:F0} s returned {value.StatusCode}");
                reads++;
                await Task.Delay(500, c.Ct).ConfigureAwait(false);
            }
            Require(c.Session.Connected, "the session disconnected");
        }

        private static async Task WriteAndCompareAsync(ClientContext c, NodeId nodeId, object value)
        {
            WriteResponse response = await c.Session
                .WriteAsync(null, new WriteValueCollection { Write(nodeId, value) }, c.Ct)
                .ConfigureAwait(false);
            Require(StatusCode.IsGood(response.Results[0]), $"write of {nodeId} returned {response.Results[0]}");
            DataValue read = await c.Session.ReadValueAsync(nodeId, c.Ct).ConfigureAwait(false);
            Require(StatusCode.IsGood(read.StatusCode), $"read of {nodeId} returned {read.StatusCode}");
            Require(Utils.IsEqual(value, read.Value), $"{nodeId} read back a different value");
        }

        private static IEnumerable<ExtensionObject> ExtensionObjects(object value)
        {
            switch (value)
            {
                case ExtensionObject extension:
                    yield return extension;
                    break;
                case ExtensionObject[] extensions:
                    foreach (ExtensionObject item in extensions.Where(e => e != null))
                    {
                        yield return item;
                    }
                    break;
            }
        }

        private static async Task<DataValueCollection> ReadAttributeAsync(
            ClientContext c,
            List<NodeId> nodes,
            uint attributeId)
        {
            var results = new DataValueCollection();
            for (int offset = 0; offset < nodes.Count; offset += 500)
            {
                var batch = new ReadValueIdCollection(nodes
                    .Skip(offset)
                    .Take(500)
                    .Select(n => new ReadValueId { NodeId = n, AttributeId = attributeId }));
                ReadResponse response = await c.Session
                    .ReadAsync(null, 0, TimestampsToReturn.Neither, batch, c.Ct)
                    .ConfigureAwait(false);
                results.AddRange(response.Results);
            }
            return results;
        }

        private static BrowseDescription BrowseAll(NodeId nodeId, NodeId referenceTypeId = null)
        {
            return new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = referenceTypeId ?? ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
        }

        /// <summary>
        /// Browses one node, following continuation points.
        /// </summary>
        private static async Task<ReferenceDescriptionCollection> BrowseAsync(
            ClientContext c,
            NodeId nodeId,
            uint maxReferences,
            NodeId referenceTypeId = null)
        {
            BrowseResponse response = await c.Session
                .BrowseAsync(
                    null,
                    null,
                    maxReferences,
                    new BrowseDescriptionCollection { BrowseAll(nodeId, referenceTypeId) },
                    c.Ct)
                .ConfigureAwait(false);
            BrowseResult result = response.Results[0];
            Require(StatusCode.IsGood(result.StatusCode), $"browse of {nodeId} returned {result.StatusCode}");
            var references = new ReferenceDescriptionCollection(result.References);
            byte[] continuationPoint = result.ContinuationPoint;
            while (continuationPoint != null && continuationPoint.Length > 0)
            {
                BrowseNextResponse next = await c.Session
                    .BrowseNextAsync(null, false, new ByteStringCollection { continuationPoint }, c.Ct)
                    .ConfigureAwait(false);
                Require(StatusCode.IsGood(next.Results[0].StatusCode),
                    $"browse next of {nodeId} returned {next.Results[0].StatusCode}");
                references.AddRange(next.Results[0].References);
                continuationPoint = next.Results[0].ContinuationPoint;
            }
            return references;
        }

        /// <summary>
        /// Breadth-first browse of the hierarchy below a node.
        /// </summary>
        private static async Task<List<ReferenceDescription>> BrowseTreeAsync(
            ClientContext c,
            NodeId root,
            int maxNodes)
        {
            var found = new List<ReferenceDescription>();
            var visited = new HashSet<NodeId> { root };
            var queue = new Queue<NodeId>();
            queue.Enqueue(root);
            while (queue.Count > 0 && found.Count < maxNodes)
            {
                NodeId nodeId = queue.Dequeue();
                if (nodeId == ObjectIds.Server)
                {
                    continue;
                }
                foreach (ReferenceDescription reference in await BrowseAsync(c, nodeId, 0).ConfigureAwait(false))
                {
                    if (reference.NodeId.IsAbsolute)
                    {
                        continue;
                    }
                    var target = ExpandedNodeId.ToNodeId(reference.NodeId, c.Session.NamespaceUris);
                    if (visited.Add(target))
                    {
                        found.Add(reference);
                        queue.Enqueue(target);
                    }
                }
            }
            return found;
        }

        private static async Task<EndpointDescription> SelectEndpointAsync(
            ApplicationConfiguration config,
            string url,
            string policy,
            MessageSecurityMode mode,
            CancellationToken ct)
        {
            using DiscoveryClient client = await DiscoveryClient
                .CreateAsync(config, new Uri(url), DiagnosticsMasks.None, ct)
                .ConfigureAwait(false);
            EndpointDescriptionCollection endpoints = await client
                .GetEndpointsAsync(null, ct)
                .ConfigureAwait(false);
            EndpointDescription endpoint = endpoints.FirstOrDefault(e =>
                e.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal) &&
                e.SecurityPolicyUri == policy &&
                e.SecurityMode == mode);
            if (endpoint == null)
            {
                throw new InvalidOperationException(
                    $"The server offers no {policy}/{mode} endpoint. Offered: " +
                    string.Join(", ", endpoints.Select(e => $"{e.SecurityPolicyUri}/{e.SecurityMode}")));
            }
            return endpoint;
        }

        private static ReadValueId Value(NodeId nodeId)
        {
            return new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
        }

        private static WriteValue Write(NodeId nodeId, object value)
        {
            return new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(value))
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// What the checks share.
        /// </summary>
        private sealed class ClientContext
        {
            public ClientContext(ISession session, ITelemetryContext telemetry, PeerOptions options, CancellationToken ct)
            {
                Session = session;
                Telemetry = telemetry;
                Options = options;
                Ct = ct;
                Ns = (ushort)session.NamespaceUris.GetIndex(ReferenceServerNamespace);
            }

            public ISession Session { get; }
            public ITelemetryContext Telemetry { get; }
            public PeerOptions Options { get; }
            public CancellationToken Ct { get; }
            public ushort Ns { get; }
            public ApplicationConfiguration Config { get; init; }
            public string Url { get; init; }
            public ConfiguredEndpoint Endpoint { get; init; }
            public Func<IUserIdentity, Task<ISession>> NewSessionAsync { get; init; }

            public NodeId Id(string name)
            {
                return new NodeId(name, Ns);
            }
        }

        /// <summary>
        /// Runs named checks and prints one result line per check.
        /// </summary>
        private sealed class CheckRunner
        {
            private int m_failed;
            private int m_passed;

            public async Task RunAsync(string name, Func<Task> check)
            {
                var elapsed = Stopwatch.StartNew();
                string outcome = "Passed";
                string message = string.Empty;
                try
                {
                    await check().ConfigureAwait(false);
                    m_passed++;
                }
                catch (Exception e)
                {
                    m_failed++;
                    outcome = "Failed";
                    message = e is ServiceResultException sre
                        ? StatusCodes.GetBrowseName(sre.StatusCode) + " " + sre.Message
                        : e.GetType().Name + " " + e.Message;
                    if (e is not InvalidOperationException)
                    {
                        message += Environment.NewLine + e.StackTrace;
                    }
                }
                string json = JsonSerializer.Serialize(new
                {
                    check = name,
                    outcome,
                    message,
                    milliseconds = elapsed.ElapsedMilliseconds
                });
                Console.WriteLine("RESULT " + json);
                Console.Out.Flush();
            }

            public int Finish()
            {
                Console.WriteLine($"SUMMARY passed={m_passed} failed={m_failed}");
                return m_failed == 0 ? Program.ExitSuccess : Program.ExitChecksFailed;
            }
        }
    }
}
