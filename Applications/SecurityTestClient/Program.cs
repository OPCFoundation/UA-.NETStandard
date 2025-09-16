using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace SecurityTestClient
{
    internal class Program
    {
        private static readonly Lock m_lock = new();
        private static ApplicationConfiguration m_configuration;
        private static SessionReconnectHandler m_reconnectHandler = new SessionReconnectHandler();
        private static TextWriter m_output;
        private static ISession m_session;

        const string ServerUrl = "opc.tcp://localhost:62541";
        const string SecurityPolicy = SecurityPolicies.ECC_nistP256_ChaChaPoly;
        const MessageSecurityMode SecurityMode = MessageSecurityMode.SignAndEncrypt;
        const int kMaxSearchDepth = 128;

        //static int KeepAliveInterval = 5000;
        static int ReconnectPeriod = 1000;
        //static int ReconnectPeriodExponentialBackoff = 15000;
        //static uint SessionLifeTime = 60 * 1000;

        static async Task Main(string[] args)
        {
            try
            {
                var output = m_output = Console.Out;
                output.WriteLine("OPC UA Security Test Client");

                output.WriteLine(
                    "OPC UA library: {0} @ {1} -- {2}",
                    Utils.GetAssemblyBuildNumber(),
                    Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
                    Utils.GetAssemblySoftwareVersion()
                );

                // The application name and config file names
                const string applicationName = "SecurityTestClient";
                const string configSectionName = "SecurityTestClient";

                // Define the UA Client application
                var passwordProvider = new CertificatePasswordProvider("");

                var application = new ApplicationInstance
                {
                    ApplicationName = applicationName,
                    ApplicationType = ApplicationType.Client,
                    ConfigSectionName = configSectionName,
                    CertificatePasswordProvider = passwordProvider
                };

                // load the application configuration.
                var configuration = m_configuration = await application
                    .LoadApplicationConfigurationAsync(silent: false)
                    .ConfigureAwait(false);

                m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;

                // check the application certificate.
                bool haveAppCertificate = await application
                    .CheckApplicationInstanceCertificatesAsync(false)
                    .ConfigureAwait(false);

                if (!haveAppCertificate)
                {
                    throw new ApplicationException("Application instance certificate invalid!");
                }

                // wait for timeout or Ctrl-C
                var quitCTS = new CancellationTokenSource();
                ManualResetEvent quitEvent = CtrlCHandler(quitCTS);
                CancellationToken ct = quitCTS.Token;

                m_output.WriteLine("Connecting to... {0}", ServerUrl);

                var endpoints = await GetEndpoints(
                    m_configuration,
                    ServerUrl,
                    ct).ConfigureAwait(false);

                var endpointConfiguration = EndpointConfiguration.Create(m_configuration);

                TraceableSessionFactory sessionFactory = TraceableSessionFactory.Instance;

                var userNameidentity = new UserIdentity("sysadmin", "demo");

                // will fail if PKI type does not match with selected channel SecurityPolicyUri.
                var certificateIdentity = await LoadUserCertificate("iama.tester@example.com", "password", ct).ConfigureAwait(false);

                foreach (var ii in endpoints)
                {
                    foreach (var identity in  new UserIdentity[] { certificateIdentity })
                    {
                        try
                        {
                            output.WriteLine(new string('=', 80));
                            output.WriteLine($"SECURITY-POLICY={SecurityPolicies.GetDisplayName(ii.SecurityPolicyUri)} {ii.SecurityMode}");
                            output.WriteLine($"IDENTITY={identity.DisplayName} {identity.TokenType}");

                            ISession session = await RunTest(
                                endpointConfiguration,
                                sessionFactory,
                                ii,
                                identity,
                                ct).ConfigureAwait(false);

                            quitEvent.WaitOne(2000);
                            await session.CloseAsync(true).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Exception: {0}", e.Message);
                            Console.WriteLine("StackTrace: {0}", e.StackTrace);
                            quitEvent.WaitOne(20000);
                        }

                        output.WriteLine($"TEST COMPLETE: {SecurityPolicies.GetDisplayName(ii.SecurityPolicyUri)} {ii.SecurityMode}");
                        output.WriteLine(new string('=', 80));
                        output.WriteLine("");
                    }
                }

                output.WriteLine("Ctrl-C to stop.");
                quitEvent.WaitOne();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: {0}", e.Message);
                Console.WriteLine("StackTrace: {0}", e.StackTrace);
            }
        }

        private static async Task<ISession> RunTest(
            EndpointConfiguration endpointConfiguration,
            TraceableSessionFactory sessionFactory,
            EndpointDescription endpointDescription,
            UserIdentity identity,
            CancellationToken ct)
        {
            var endpoint = new ConfiguredEndpoint(
                null,
                endpointDescription,
                endpointConfiguration);

            // Create the session
            ISession session = await sessionFactory
                .CreateAsync(
                    m_configuration,
                    endpoint,
                    false,
                    false,
                    m_configuration.ApplicationName,
                    600000,
                    (endpointDescription.SecurityMode != MessageSecurityMode.None) ? identity : new UserIdentity(),
                    null,
                    ct
                )
                .ConfigureAwait(false);

            // Assign the created session
            if (session == null || !session.Connected)
            {
                throw new ApplicationException("Could not connect to server at " + ServerUrl);
            }

            session.KeepAliveInterval = 10000;
            session.KeepAlive += Session_KeepAlive;

            var nodes = await BrowseFullAddressSpaceAsync(
                session,
                ObjectIds.ObjectsFolder,
                null,
                ct).ConfigureAwait(false);

            return session;
        }

        private static ManualResetEvent CtrlCHandler(CancellationTokenSource cts)
        {
            var quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (_, eArgs) =>
                {
                    cts.Cancel();
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
                // intentionally left blank
            }
            return quitEvent;
        }

        private static async ValueTask<IList<EndpointDescription>> GetEndpoints(
            ApplicationConfiguration application,
            string discoveryUrl,
            CancellationToken ct = default)
        {
            var endpointConfiguration = EndpointConfiguration.Create(application);

            using var client = DiscoveryClient.Create(
                application,
                new Uri(discoveryUrl),
                endpointConfiguration);

            return await client.GetEndpointsAsync(null, ct).ConfigureAwait(false);
        }

        private static void CertificateValidation(
            CertificateValidator sender,
            CertificateValidationEventArgs e)
        {
            bool certificateAccepted = false;

            // ****
            // Implement a custom logic to decide if the certificate should be
            // accepted or not and set certificateAccepted flag accordingly.
            // The certificate can be retrieved from the e.Certificate field
            // ***

            ServiceResult error = e.Error;
            m_output.WriteLine(error);
            if (error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                certificateAccepted = true;
            }

            if (certificateAccepted)
            {
                m_output.WriteLine(
                    "Untrusted Certificate accepted. Subject = {0}",
                    e.Certificate.Subject);
                e.Accept = true;
            }
            else
            {
                m_output.WriteLine(
                    "Untrusted Certificate rejected. Subject = {0}",
                    e.Certificate.Subject);
            }
        }

        private static void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            try
            {
                // check for events from discarded sessions.
                if (m_session == null || !m_session.Equals(session))
                {
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    if (ReconnectPeriod <= 0)
                    {
                        Utils.LogWarning(
                            "KeepAlive status {0}, but reconnect is disabled.",
                            e.Status);
                        return;
                    }

                    SessionReconnectHandler.ReconnectState state = m_reconnectHandler
                        .BeginReconnect(
                            m_session,
                            null,
                            ReconnectPeriod,
                            Client_ReconnectComplete);

                    if (state == SessionReconnectHandler.ReconnectState.Triggered)
                    {
                        Utils.LogInfo(
                            "KeepAlive status {0}, reconnect status {1}, reconnect period {2}ms.",
                            e.Status,
                            state,
                            ReconnectPeriod
                        );
                    }
                    else
                    {
                        Utils.LogInfo(
                            "KeepAlive status {0}, reconnect status {1}.",
                            e.Status,
                            state);
                    }

                    // cancel sending a new keep alive request, because reconnect is triggered.
                    e.CancelKeepAlive = true;
                }
            }
            catch (Exception exception)
            {
                Utils.LogError(exception, "Error in OnKeepAlive.");
            }
        }

        private static void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!ReferenceEquals(sender, m_reconnectHandler))
            {
                return;
            }

            lock (m_lock)
            {
                // if session recovered, Session property is null
                if (m_reconnectHandler.Session != null)
                {
                    // ensure only a new instance is disposed
                    // after reactivate, the same session instance may be returned
                    if (!ReferenceEquals(m_session, m_reconnectHandler.Session))
                    {
                        m_output.WriteLine(
                            "--- RECONNECTED TO NEW SESSION --- {0}",
                            m_reconnectHandler.Session.SessionId
                        );
                        ISession session = m_session;
                        m_session = m_reconnectHandler.Session;
                        Utils.SilentDispose(session);
                    }
                    else
                    {
                        m_output.WriteLine(
                            "--- REACTIVATED SESSION --- {0}",
                            m_reconnectHandler.Session.SessionId);
                    }
                }
                else
                {
                    m_output.WriteLine("--- RECONNECT KeepAlive recovered ---");
                }
            }
        }

        private static async Task<UserIdentity> LoadUserCertificate(string subjectName, string password, CancellationToken ct)
        {
            var store = m_configuration.SecurityConfiguration.TrustedUserCertificates;

            // get user certificate with matching thumbprint
            var hit = (
                await store.GetCertificatesAsync(ct).ConfigureAwait(false)
            ).Find(X509FindType.FindBySubjectName, subjectName, false).FirstOrDefault();

            // create Certificate Identifier
            var cid = new CertificateIdentifier(hit)
            {
                StorePath = store.StorePath,
                StoreType = store.StoreType
            };

            return new UserIdentity(
                cid,
                new CertificatePasswordProvider(password ?? string.Empty)
            );
        }

        private static async Task<ReferenceDescriptionCollection> BrowseFullAddressSpaceAsync(
            ISession session,
            NodeId startingNode = null,
            BrowseDescription browseDescription = null,
            CancellationToken ct = default)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            // Browse template
            const int kMaxReferencesPerNode = 1000;
            BrowseDescription browseTemplate =
                browseDescription
                ?? new BrowseDescription
                {
                    NodeId = startingNode ?? ObjectIds.RootFolder,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                };
            BrowseDescriptionCollection browseDescriptionCollection
                = CreateBrowseDescriptionCollectionFromNodeId(
                [.. new NodeId[] { startingNode ?? ObjectIds.RootFolder }],
                browseTemplate);

            // Browse
            var referenceDescriptions = new Dictionary<ExpandedNodeId, ReferenceDescription>();

            int searchDepth = 0;
            uint maxNodesPerBrowse = session.OperationLimits.MaxNodesPerBrowse;
            while (browseDescriptionCollection.Count > 0 && searchDepth < kMaxSearchDepth)
            {
                searchDepth++;
                Utils.LogInfo(
                    "{0}: Browse {1} nodes after {2}ms",
                    searchDepth,
                    browseDescriptionCollection.Count,
                    stopWatch.ElapsedMilliseconds);

                var allBrowseResults = new BrowseResultCollection();
                bool repeatBrowse;
                var browseResultCollection = new BrowseResultCollection();
                var unprocessedOperations = new BrowseDescriptionCollection();
                DiagnosticInfoCollection diagnosticsInfoCollection;
                do
                {
                    BrowseDescriptionCollection browseCollection =
                        maxNodesPerBrowse == 0
                            ? browseDescriptionCollection
                            : browseDescriptionCollection.Take((int)maxNodesPerBrowse).ToArray();
                    repeatBrowse = false;
                    try
                    {
                        BrowseResponse browseResponse = await
                            session.BrowseAsync(
                                null,
                                null,
                                kMaxReferencesPerNode,
                                browseCollection,
                                ct)
                            .ConfigureAwait(false);
                        browseResultCollection = browseResponse.Results;
                        diagnosticsInfoCollection = browseResponse.DiagnosticInfos;
                        ClientBase.ValidateResponse(browseResultCollection, browseCollection);
                        ClientBase.ValidateDiagnosticInfos(
                            diagnosticsInfoCollection,
                            browseCollection);

                        // separate unprocessed nodes for later
                        int ii = 0;
                        foreach (BrowseResult browseResult in browseResultCollection)
                        {
                            // check for error.
                            StatusCode statusCode = browseResult.StatusCode;
                            if (StatusCode.IsBad(statusCode))
                            {
                                // this error indicates that the server does not have enough simultaneously active
                                // continuation points. This request will need to be resent after the other operations
                                // have been completed and their continuation points released.
                                if (statusCode == StatusCodes.BadNoContinuationPoints)
                                {
                                    unprocessedOperations.Add(browseCollection[ii++]);
                                    continue;
                                }
                            }

                            // save results.
                            allBrowseResults.Add(browseResult);
                            ii++;
                        }
                    }
                    catch (ServiceResultException sre)
                    {
                        if (sre.StatusCode is StatusCodes.BadEncodingLimitsExceeded or StatusCodes
                            .BadResponseTooLarge)
                        {
                            // try to address by overriding operation limit
                            maxNodesPerBrowse =
                                maxNodesPerBrowse == 0
                                    ? (uint)browseCollection.Count / 2
                                    : maxNodesPerBrowse / 2;
                            repeatBrowse = true;
                        }
                        else
                        {
                            m_output.WriteLine("Browse error: {0}", sre.Message);
                            throw;
                        }
                    }
                } while (repeatBrowse);

                if (maxNodesPerBrowse == 0)
                {
                    browseDescriptionCollection.Clear();
                }
                else
                {
                    browseDescriptionCollection = browseDescriptionCollection
                        .Skip(browseResultCollection.Count)
                        .ToArray();
                }

                // Browse next
                ByteStringCollection continuationPoints = PrepareBrowseNext(browseResultCollection);
                while (continuationPoints.Count > 0)
                {
                    Utils.LogInfo("BrowseNext {0} continuation points.", continuationPoints.Count);
                    BrowseNextResponse browseNextResult = await
                        session.BrowseNextAsync(null, false, continuationPoints, ct)
                        .ConfigureAwait(false);
                    BrowseResultCollection browseNextResultCollection = browseNextResult.Results;
                    diagnosticsInfoCollection = browseNextResult.DiagnosticInfos;
                    ClientBase.ValidateResponse(browseNextResultCollection, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(
                        diagnosticsInfoCollection,
                        continuationPoints);
                    allBrowseResults.AddRange(browseNextResultCollection);
                    continuationPoints = PrepareBrowseNext(browseNextResultCollection);
                }

                // Build browse request for next level
                var browseTable = new NodeIdCollection();
                int duplicates = 0;
                foreach (BrowseResult browseResult in allBrowseResults)
                {
                    foreach (ReferenceDescription reference in browseResult.References)
                    {
                        if (!referenceDescriptions.ContainsKey(reference.NodeId))
                        {
                            referenceDescriptions[reference.NodeId] = reference;
                            if (reference.ReferenceTypeId != ReferenceTypeIds.HasProperty)
                            {
                                browseTable.Add(
                                    ExpandedNodeId.ToNodeId(
                                        reference.NodeId,
                                        session.NamespaceUris));
                            }
                        }
                        else
                        {
                            duplicates++;
                        }
                    }
                }
                if (duplicates > 0)
                {
                    Utils.LogInfo("Browse Result {0} duplicate nodes were ignored.", duplicates);
                }
                browseDescriptionCollection.AddRange(
                    CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate));

                // add unprocessed nodes if any
                browseDescriptionCollection.AddRange(unprocessedOperations);
            }

            stopWatch.Stop();

            var result = new ReferenceDescriptionCollection(referenceDescriptions.Values);
            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            m_output.WriteLine(
                "BrowseFullAddressSpace found {0} references on server in {1}ms.",
                referenceDescriptions.Count,
                stopWatch.ElapsedMilliseconds);

            return result;
        }

        private static BrowseDescriptionCollection CreateBrowseDescriptionCollectionFromNodeId(
            NodeIdCollection nodeIdCollection,
            BrowseDescription template)
        {
            var browseDescriptionCollection = new BrowseDescriptionCollection();
            foreach (NodeId nodeId in nodeIdCollection)
            {
                var browseDescription = (BrowseDescription)template.MemberwiseClone();
                browseDescription.NodeId = nodeId;
                browseDescriptionCollection.Add(browseDescription);
            }
            return browseDescriptionCollection;
        }

        private static ByteStringCollection PrepareBrowseNext(
            BrowseResultCollection browseResultCollection)
        {
            var continuationPoints = new ByteStringCollection();
            foreach (BrowseResult browseResult in browseResultCollection)
            {
                if (browseResult.ContinuationPoint != null)
                {
                    continuationPoints.Add(browseResult.ContinuationPoint);
                }
            }
            return continuationPoints;
        }
    }
}
