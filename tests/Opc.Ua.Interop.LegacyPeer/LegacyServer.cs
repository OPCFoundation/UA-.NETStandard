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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Interop.LegacyPeer
{
    /// <summary>
    /// Hosts the 1.5.x server side of the interop tests.
    /// </summary>
    public static class LegacyServerHost
    {
        public const string InteropApplicationName = "LegacyInteropServer";
        public const string ReferenceApplicationName = "ReferenceServer";

        public static async Task<int> RunAsync(PeerOptions options)
        {
            int port = int.Parse(options.Get("port"), CultureInfo.InvariantCulture);
            bool reference = options.Get("kind", "interop") switch
            {
                "interop" => false,
                "reference" => true,
                string kind => throw new ArgumentException("unknown --kind " + kind)
            };
            string applicationName = reference ? ReferenceApplicationName : InteropApplicationName;
            string endpointUrl = $"opc.tcp://localhost:{port}/{applicationName}";

            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));

            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = applicationName,
                ApplicationType = ApplicationType.Server
            };

            // The quotas and limits are part of what the interop tests check:
            // the interop server is deliberately tight so the tests can cross
            // its limits; the reference server mirrors the limits of the 2.0
            // reference server test fixture, which the reused 2.0 client tests
            // are written against.
            IApplicationConfigurationBuilderServerSelected server = application
                .Build(
                    "urn:localhost:opcfoundation.org:" + applicationName,
                    "uri:opcfoundation.org:" + applicationName)
                .SetMaxMessageSize(reference ? 16 * 1024 * 1024 : InteropLimits.MaxMessageSize)
                .SetMaxByteStringLength(reference ? 4 * 1024 * 1024 : InteropLimits.MaxByteStringLength)
                .SetMaxStringLength(reference ? 4 * 1024 * 1024 : InteropLimits.MaxByteStringLength)
                .SetMaxArrayLength(reference ? 1024 * 1024 : InteropLimits.MaxArrayLength)
                .AsServer(new[] { endpointUrl });

            IApplicationConfigurationBuilderServerPolicies policies = server
                .AddUnsecurePolicyNone()
                .AddSignPolicies()
                .AddSignAndEncryptPolicies();
            if (options.Ecc)
            {
                policies = policies
                    .AddEccSignPolicies()
                    .AddEccSignAndEncryptPolicies();
            }

            await policies
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .AddUserTokenPolicy(UserTokenType.Certificate)
                .SetOperationLimits(reference
                    ? new OperationLimits
                    {
                        MaxNodesPerBrowse = 2500,
                        MaxNodesPerRead = 1000,
                        MaxNodesPerWrite = 1000,
                        MaxNodesPerMethodCall = 1000,
                        MaxMonitoredItemsPerCall = 1000,
                        MaxNodesPerHistoryReadData = 1000,
                        MaxNodesPerHistoryReadEvents = 1000,
                        MaxNodesPerHistoryUpdateData = 1000,
                        MaxNodesPerHistoryUpdateEvents = 1000,
                        MaxNodesPerNodeManagement = 1000,
                        MaxNodesPerRegisterNodes = 1000,
                        MaxNodesPerTranslateBrowsePathsToNodeIds = 1000
                    }
                    : new OperationLimits
                    {
                        MaxNodesPerBrowse = InteropLimits.MaxNodesPerOperation,
                        MaxNodesPerRead = InteropLimits.MaxNodesPerOperation,
                        MaxNodesPerWrite = InteropLimits.MaxNodesPerOperation,
                        MaxNodesPerMethodCall = InteropLimits.MaxNodesPerOperation,
                        MaxMonitoredItemsPerCall = InteropLimits.MaxNodesPerOperation,
                        MaxNodesPerTranslateBrowsePathsToNodeIds = InteropLimits.MaxNodesPerOperation
                    })
                .SetDiagnosticsEnabled(true)
                .SetShutdownDelay(0)
                .AddSecurityConfiguration(
                    LegacyPki.ApplicationCertificates(
                        "CN=" + applicationName + ", O=OPC Foundation, DC=localhost",
                        options.PkiRoot,
                        options.Ecc),
                    options.PkiRoot)
                .SetAutoAcceptUntrustedCertificates(options.AutoAccept)
                .CreateAsync()
                .ConfigureAwait(false);

            bool haveCertificate = await application
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            if (!haveCertificate)
            {
                throw new InvalidOperationException("The application certificate is invalid.");
            }
            if (options.GetBool("init-only", false))
            {
                Console.WriteLine("LEGACY-PKI-READY " + application.ApplicationConfiguration.ApplicationUri);
                return Program.ExitSuccess;
            }

            StandardServer instance;
            if (reference)
            {
                var referenceServer = new ReferenceServer();
                Quickstarts.Servers.Utils.AddDefaultNodeManagers(referenceServer);
                instance = referenceServer;
            }
            else
            {
                instance = new LegacyInteropServer();
            }
            await application.StartAsync(instance).ConfigureAwait(false);

            // What the tests compare the server against, then the exact line
            // the test harness waits for before it connects.
            Console.WriteLine("PEER-INFO " + System.Text.Json.JsonSerializer.Serialize(new
            {
                stack = "UA-.NETStandard",
                version = Utils.GetAssemblySoftwareVersion(),
                applicationUri = application.ApplicationConfiguration.ApplicationUri,
                softwareVersion = Utils.GetAssemblySoftwareVersion()
            }));
            Console.WriteLine("LEGACY-SERVER-READY " + endpointUrl);
            Console.Out.Flush();

            // Run until the harness closes standard input or asks to stop.
            string line;
            while ((line = await Console.In.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (string.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            await application.StopAsync().ConfigureAwait(false);
            Console.WriteLine("LEGACY-SERVER-STOPPED");
            return Program.ExitSuccess;
        }
    }

    /// <summary>
    /// The transport quotas and operation limits of the interop server.
    /// Mirrored by the 2.0 tests, which cross them on purpose.
    /// </summary>
    public static class InteropLimits
    {
        public const int MaxMessageSize = 4 * 1024 * 1024;
        public const int MaxByteStringLength = 1024 * 1024;
        public const int MaxArrayLength = 100_000;
        public const int MaxNodesPerOperation = 100;
    }

    /// <summary>
    /// A 1.5.378 server exposing the interop address space and accepting
    /// the interop user name credentials.
    /// </summary>
    public sealed class LegacyInteropServer : StandardServer
    {
        public const string UserName = "interop";
        public const string Password = "interop-password";

        protected override MasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            var nodeManagers = new INodeManager[] { new InteropNodeManager(server, configuration) };
            return new MasterNodeManager(server, configuration, null, nodeManagers);
        }

        protected override ServerProperties LoadServerProperties()
        {
            // SoftwareVersion and BuildNumber come from the 1.5.378 stack
            // assemblies, so a client can tell which stack it talks to.
            return new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "1.5.378 Interop Server",
                ProductUri = "http://opcfoundation.org/UA/Interop/LegacyServer",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = DateTime.UtcNow
            };
        }

        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);
            server.SessionManager.ImpersonateUser += OnImpersonateUser;
        }

        private static void OnImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            if (args.NewIdentity is UserNameIdentityToken userName)
            {
                if (userName.UserName == UserName &&
                    PasswordMatches(userName))
                {
                    args.Identity = new UserIdentity(userName);
                    return;
                }
                throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Invalid user name or password.");
            }
            if (args.NewIdentity is X509IdentityToken x509)
            {
                // The certificate was validated by the session manager (user
                // trust list, auto-accept for the interop tests).
                args.Identity = new UserIdentity(x509);
            }
        }

        private static bool PasswordMatches(UserNameIdentityToken token)
        {
            object decrypted = token.DecryptedPassword;
            string password = decrypted is byte[] bytes
                ? System.Text.Encoding.UTF8.GetString(bytes)
                : decrypted as string;
            return password == Password;
        }
    }

    /// <summary>
    /// The address space of the legacy server: a folder "Interop" below the
    /// Objects folder holding scalar, array and structure variables, a
    /// counter that changes every 100 ms, and an Add method.
    /// </summary>
    public sealed class InteropNodeManager : CustomNodeManager2
    {
        public const string NamespaceUri = "urn:opcfoundation.org:interop:legacy";

        // Owned by the predefined node table of the base class, which disposes it.
#pragma warning disable CA2213
        private BaseDataVariableState m_counter;
#pragma warning restore CA2213
        private Timer m_timer;

        public InteropNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : base(server, configuration, NamespaceUri)
        {
        }

        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference> references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                var folder = new FolderState(null)
                {
                    SymbolicName = "Interop",
                    ReferenceTypeId = ReferenceTypes.Organizes,
                    TypeDefinitionId = ObjectTypeIds.FolderType,
                    NodeId = new NodeId("Interop", NamespaceIndex),
                    BrowseName = new QualifiedName("Interop", NamespaceIndex),
                    DisplayName = new LocalizedText("en", "Interop"),
                    WriteMask = AttributeWriteMask.None,
                    UserWriteMask = AttributeWriteMask.None,
                    EventNotifier = EventNotifiers.None
                };
                folder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, folder.NodeId));

                CreateVariable(folder, "Boolean", DataTypeIds.Boolean, ValueRanks.Scalar, true);
                CreateVariable(folder, "Int32", DataTypeIds.Int32, ValueRanks.Scalar, 42);
                CreateVariable(folder, "UInt64", DataTypeIds.UInt64, ValueRanks.Scalar, ulong.MaxValue);
                CreateVariable(folder, "Double", DataTypeIds.Double, ValueRanks.Scalar, 3.25);
                CreateVariable(folder, "String", DataTypeIds.String, ValueRanks.Scalar, "legacy");
                CreateVariable(folder, "DateTime", DataTypeIds.DateTime, ValueRanks.Scalar,
                    new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
                CreateVariable(folder, "Guid", DataTypeIds.Guid, ValueRanks.Scalar,
                    new Uuid(new Guid("8d2b5c6e-6c0e-4a4c-9f2b-0b6a3a6b1e01")));
                CreateVariable(folder, "ByteString", DataTypeIds.ByteString, ValueRanks.Scalar,
                    new byte[] { 1, 2, 3, 4, 5 });
                CreateVariable(folder, "LocalizedText", DataTypeIds.LocalizedText, ValueRanks.Scalar,
                    new LocalizedText("de", "Hallo"));
                CreateVariable(folder, "QualifiedName", DataTypeIds.QualifiedName, ValueRanks.Scalar,
                    new QualifiedName("Name", NamespaceIndex));
                CreateVariable(folder, "NodeId", DataTypeIds.NodeId, ValueRanks.Scalar,
                    new NodeId("Interop", NamespaceIndex));
                int[] int32Array = [1, 2, 3];
                CreateVariable(folder, "Int32Array", DataTypeIds.Int32, ValueRanks.OneDimension, int32Array);
                string[] stringArray = ["a", "b", "c"];
                CreateVariable(folder, "StringArray", DataTypeIds.String, ValueRanks.OneDimension, stringArray);
                CreateVariable(folder, "Range", DataTypeIds.Range, ValueRanks.Scalar,
                    new Range(100, 0));
                m_counter = CreateVariable(folder, "Counter", DataTypeIds.Int32, ValueRanks.Scalar, 0);
                m_counter.AccessLevel = AccessLevels.CurrentRead;
                m_counter.UserAccessLevel = AccessLevels.CurrentRead;

                CreateAddMethod(folder);
                CreateRaiseEventMethod(folder);

                AddPredefinedNode(SystemContext, folder);
                AddRootNotifier(folder);

                m_timer = new Timer(OnTick, null, 100, 100);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_timer?.Dispose();
                m_timer = null;
            }
            base.Dispose(disposing);
        }

        private void OnTick(object state)
        {
            lock (Lock)
            {
                if (m_counter == null)
                {
                    return;
                }
                m_counter.Value = (int)m_counter.Value + 1;
                m_counter.Timestamp = DateTime.UtcNow;
                m_counter.ClearChangeMasks(SystemContext, false);
            }
        }

        private BaseDataVariableState CreateVariable(
            NodeState parent,
            string name,
            NodeId dataType,
            int valueRank,
            object value)
        {
            var variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(name, NamespaceIndex),
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                DataType = dataType,
                ValueRank = valueRank,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Historizing = false,
                Value = value,
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }
            parent.AddChild(variable);
            return variable;
        }

        private void CreateAddMethod(NodeState parent)
        {
            var method = new MethodState(parent)
            {
                SymbolicName = "Add",
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId("Add", NamespaceIndex),
                BrowseName = new QualifiedName("Add", NamespaceIndex),
                DisplayName = new LocalizedText("en", "Add"),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true
            };

            method.InputArguments = CreateArguments(method, "InputArguments", BrowseNames.InputArguments,
                new Argument { Name = "a", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                new Argument { Name = "b", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar });
            method.OutputArguments = CreateArguments(method, "OutputArguments", BrowseNames.OutputArguments,
                new Argument { Name = "sum", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar });

            method.OnCallMethod = (context, m, inputs, outputs) =>
            {
                outputs[0] = (int)inputs[0] + (int)inputs[1];
                return ServiceResult.Good;
            };

            parent.AddChild(method);
        }

        /// <summary>
        /// RaiseEvent() reports one BaseEventType event with the message
        /// "interop event" and Severity 500 through the Server object.
        /// </summary>
        private void CreateRaiseEventMethod(NodeState parent)
        {
            var method = new MethodState(parent)
            {
                SymbolicName = "RaiseEvent",
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId("RaiseEvent", NamespaceIndex),
                BrowseName = new QualifiedName("RaiseEvent", NamespaceIndex),
                DisplayName = new LocalizedText("en", "RaiseEvent"),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true
            };
            method.OnCallMethod = (context, m, inputs, outputs) =>
            {
                var e = new BaseEventState(null);
                e.Initialize(
                    SystemContext,
                    null,
                    EventSeverity.Medium,
                    new LocalizedText("en", "interop event"));
                e.SetChildValue(SystemContext, BrowseNames.SourceNode, parent.NodeId, false);
                e.SetChildValue(SystemContext, BrowseNames.SourceName, "Interop", false);
                e.Severity.Value = 500;
                Server.ReportEvent(SystemContext, e);
                return ServiceResult.Good;
            };
            parent.AddChild(method);
        }

        private PropertyState<Argument[]> CreateArguments(
            MethodState method,
            string suffix,
            string browseName,
            params Argument[] arguments)
        {
            foreach (Argument argument in arguments)
            {
                argument.Description = new LocalizedText(argument.Name);
            }
            var property = new PropertyState<Argument[]>(method)
            {
                NodeId = new NodeId(method.SymbolicName + "_" + suffix, NamespaceIndex),
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName),
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = arguments
            };
            property.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { (uint)arguments.Length });
            return property;
        }
    }
}
