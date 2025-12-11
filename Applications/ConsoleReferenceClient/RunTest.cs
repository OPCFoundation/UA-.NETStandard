using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace SecurityTestClient
{
    internal sealed class RunTest
    {
        private readonly Lock m_lock = new();
        private SessionReconnectHandler m_reconnectHandler;
        private ILogger m_logger;
        private ITelemetryContext m_context;
        private ApplicationConfiguration m_configuration;
        private ISession m_session;

        const string ServerUrl = "opc.tcp://localhost:62541";
        //const string ServerUrl = "opc.tcp://WhiteCat:4880/Softing/OpcUa/TestServer";
        const int kMaxSearchDepth = 128;
        const int ReconnectPeriod = 1000;
        const int ReconnectPeriodExponentialBackoff = 15000;

        public RunTest(ApplicationConfiguration configuration, ITelemetryContext context)
        {
            m_context = context;
            m_configuration = configuration;
            m_logger = context.CreateLogger("Test");

            m_reconnectHandler = new SessionReconnectHandler(
                context,
                true,
                ReconnectPeriodExponentialBackoff);
        }

        private string GetUserCertificateFile(string securityPolicyUri)
        {
            var securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

            switch (securityPolicy.CertificateKeyAlgorithm)
            {
                default:
                case CertificateKeyAlgorithm.RSA:
                case CertificateKeyAlgorithm.RSADH:
                    return $"iama.tester.rsa.der";
                case CertificateKeyAlgorithm.BrainpoolP256r1:
                    return $"iama.tester.brainpoolP256r1.der";
                case CertificateKeyAlgorithm.BrainpoolP384r1:
                    return $"iama.tester.brainpoolP384r1.der";
                case CertificateKeyAlgorithm.NistP256:
                    return $"iama.tester.nistP256.der";
                case CertificateKeyAlgorithm.NistP384:
                    return $"iama.tester.nistP384.der";
            }
        }

        public async Task<bool> RunAsync(ManualResetEvent quitEvent, CancellationToken ct)
        {
            try
            {
                m_logger.LogInformation("OPC UA Security Test Client");

                // The application name and config file names
                const string applicationName = "ConsoleReferenceClient";
                const string configSectionName = "Quickstarts.ReferenceClient";

                // Define the UA Client application
                var passwordProvider = new CertificatePasswordProvider([]);

                var application = new ApplicationInstance(m_context)
                {
                    ApplicationName = applicationName,
                    ApplicationType = ApplicationType.Client,
                    ConfigSectionName = configSectionName,
                    CertificatePasswordProvider = passwordProvider
                };

                // load the application configuration.
                var configuration = m_configuration = await application
                    .LoadApplicationConfigurationAsync(silent: false, ct: ct)
                    .ConfigureAwait(false);

                m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;

                // check the application certificate.
                bool haveAppCertificate = await application
                    .CheckApplicationInstanceCertificatesAsync(false, ct: ct)
                    .ConfigureAwait(false);

                if (!haveAppCertificate)
                {
                    throw new InvalidOperationException("Application instance certificate invalid!");
                }

                m_logger.LogInformation("Connecting to... {ServerUrl}", ServerUrl);

                var endpoints = await GetEndpoints(
                    m_configuration,
                    ServerUrl,
                    ct).ConfigureAwait(false);

                //endpoints = endpoints.Where(x => x.SecurityPolicyUri == SecurityPolicies.RSA_DH_ChaChaPoly).ToList();

                var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                var sessionFactory = new DefaultSessionFactory(m_context);
                var userNameidentity = new UserIdentity("sysadmin", new UTF8Encoding(false).GetBytes("demo"));
                //var userNameidentity = new UserIdentity("usr", new UTF8Encoding(false).GetBytes("pwd"));

                foreach (var ii in endpoints)
                {
                    var userCertificateFile = GetUserCertificateFile(ii.SecurityPolicyUri);
                    var x509 = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(".\\pki\\trustedUser\\certs", userCertificateFile));
                    var thumbprint = x509.Thumbprint;

                    var certificateIdentity = await LoadUserCertificateAsync(thumbprint, "password", ct).ConfigureAwait(false);

                    foreach (var identity in new UserIdentity[] { userNameidentity, certificateIdentity })
                    {
                        try
                        {
                            m_logger.LogWarning("{Line}", new string('=', 80));

                            m_logger.LogWarning(
                                "SECURITY-POLICY={SecurityPolicyUri} {SecurityMode}",
                                SecurityPolicies.GetDisplayName(ii.SecurityPolicyUri),
                                ii.SecurityMode);

                            m_logger.LogWarning(
                                "IDENTITY={DisplayName} {TokenType}",
                                identity.DisplayName,
                                identity.TokenType);

                            ISession session = await RunTestAsync(
                                endpointConfiguration,
                                sessionFactory,
                                ii,
                                identity,
                                ct).ConfigureAwait(false);

                            m_logger.LogWarning("Waiting for SecureChannel renew");
                            await session.UpdateSessionAsync(identity, null, ct).ConfigureAwait(false);

                            for (int count = 0; count < 8; count++)
                            {
                                var result = await session.ReadAsync(
                                    null,
                                    0,
                                    TimestampsToReturn.Neither,
                                    new ReadValueIdCollection()
                                    {
                                        new ReadValueId()
                                        {
                                            NodeId = Opc.Ua.VariableIds.Server_ServerStatus_CurrentTime,
                                            AttributeId = Attributes.Value
                                        }
                                    },
                                    ct).ConfigureAwait(false);

                                m_logger.LogWarning(
                                    "CurrentTime: {CurrentTime}",
                                    result.Results[0].GetValueOrDefault<DateTime>().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));

                                await Task.Delay(5000, ct).ConfigureAwait(false);
                            }

                            await session.UpdateSessionAsync(identity, null, ct).ConfigureAwait(false);

                            await session.CloseAsync(true, ct: ct).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Exception: {0}", e.Message);
                            Console.WriteLine("StackTrace: {0}", e.StackTrace);
                            quitEvent.WaitOne(20000);
                        }

                        m_logger.LogWarning(
                            "TEST COMPLETE: {SecurityPolicyUri} {SecurityMode}",
                            SecurityPolicies.GetDisplayName(ii.SecurityPolicyUri),
                            ii.SecurityMode);

                        m_logger.LogWarning("{Line}", new string('=', 80));
                        //break;
                    }

                    //break;
                }

                Console.WriteLine("Ctrl-C to stop.");
                quitEvent.WaitOne();
            }
            catch (Exception e)
            {
                m_logger.LogError("Exception: {Message}", e.Message);
                m_logger.LogTrace("StackTrace: {StackTrace}", e.StackTrace);
            }

            return true;
        }

        private async Task<UserIdentity> LoadUserCertificateAsync(string thumbprint, string password, CancellationToken ct)
        {
#if NET8_0_OR_GREATER
            var store = m_configuration.SecurityConfiguration.TrustedUserCertificates;

            // get user certificate with matching thumbprint
            var hit = (
                await store.GetCertificatesAsync(m_context, ct).ConfigureAwait(false)
            ).Find(X509FindType.FindByThumbprint, thumbprint, false).FirstOrDefault();

            // create Certificate Identifier
            var cid = new CertificateIdentifier(hit)
            {
                StorePath = store.StorePath,
                StoreType = store.StoreType
            };

            return await UserIdentity.CreateAsync(
                cid,
                new CertificatePasswordProvider(new UTF8Encoding(false).GetBytes(password)),
                m_context,
                ct
            ).ConfigureAwait(false);
#else
            await Task.Delay(1, ct).ConfigureAwait(false);
            throw new NotSupportedException("User certificate identity is only supported on .NET 8 or greater.");
#endif
        }

        public async Task<ISession> RunTestAsync(
            EndpointConfiguration endpointConfiguration,
            DefaultSessionFactory sessionFactory,
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
                    //new UserIdentity(),
                    (endpointDescription.SecurityMode != MessageSecurityMode.None) ? identity : new UserIdentity(),
                    null,
                    ct
                )
                .ConfigureAwait(false);

            // Assign the created session
            if (session == null || !session.Connected)
            {
                throw new InvalidOperationException("Could not connect to server at " + ServerUrl);
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
        private async ValueTask<IList<EndpointDescription>> GetEndpoints(
            ApplicationConfiguration application,
            string discoveryUrl,
            CancellationToken ct = default)
        {
            var endpointConfiguration = EndpointConfiguration.Create(application);

            using var client = await DiscoveryClient.CreateAsync(
                application,
                new Uri(discoveryUrl),
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);

            return await client.GetEndpointsAsync(null, ct).ConfigureAwait(false);
        }

        private void CertificateValidation(
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
            m_logger.LogInformation("{ServiceResult}", error);
            if (error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                certificateAccepted = true;
            }

            if (certificateAccepted)
            {
                m_logger.LogInformation(
                    "Untrusted Certificate accepted. Subject = {Subject}",
                    e.Certificate.Subject);
                e.Accept = true;
            }
            else
            {
                m_logger.LogInformation(
                    "Untrusted Certificate rejected. Subject = {Subject}",
                    e.Certificate.Subject);
            }
        }

        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
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
                    SessionReconnectHandler.ReconnectState state = m_reconnectHandler
                        .BeginReconnect(
                            m_session,
                            null,
                            ReconnectPeriod,
                            Client_ReconnectComplete);

                    if (state == SessionReconnectHandler.ReconnectState.Triggered)
                    {
                        m_logger.LogInformation(
                            "KeepAlive status {Status}, reconnect status {State}, reconnect period {ReconnectPeriod}ms.",
                            e.Status,
                            state,
                            ReconnectPeriod
                        );
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "KeepAlive status {Status}, reconnect status {State}.",
                            e.Status,
                            state);
                    }

                    // cancel sending a new keep alive request, because reconnect is triggered.
                    e.CancelKeepAlive = true;
                }
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Error in OnKeepAlive.");
            }
        }
        private void Client_ReconnectComplete(object sender, EventArgs e)
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
                        m_logger.LogInformation(
                            "--- RECONNECTED TO NEW SESSION --- {SessionId}",
                            m_reconnectHandler.Session.SessionId
                        );
                        ISession session = m_session;
                        m_session = m_reconnectHandler.Session;
                        Utils.SilentDispose(session);
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "--- REACTIVATED SESSION --- {SessionId}",
                            m_reconnectHandler.Session.SessionId);
                    }
                }
                else
                {
                    m_logger.LogInformation("--- RECONNECT KeepAlive recovered ---");
                }
            }
        }

        private async Task<ReferenceDescriptionCollection> BrowseFullAddressSpaceAsync(
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
            var random = new Random(11211); // use a fixed seed for test reproducibility

            int searchDepth = 0;
            uint maxNodesPerBrowse = session.OperationLimits.MaxNodesPerBrowse;
            while (browseDescriptionCollection.Count > 0 && searchDepth < kMaxSearchDepth)
            {
                searchDepth++;
                m_logger.LogInformation(
                    "{SearchDepth}: Browse {Count} nodes after {ElapsedMilliseconds}ms",
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
                        RequestHeader requestHeader = null;

                        // a random pattern to obscure the message size
                        // (only useful for application running over untrusted networks).
                        if (session.ConfiguredEndpoint.Description.SecurityMode == MessageSecurityMode.SignAndEncrypt)
                        {
                            // a real application needs to use secure randomness
#pragma warning disable CA5394 // Do not use insecure randomness
                            var padding = new byte[random.Next() % 128];
                            random.NextBytes(padding);
#pragma warning restore CA5394 // Do not use insecure randomness

                            m_logger.LogWarning("Sending Padding with {Length} Bytes", padding.Length);

                            requestHeader = new RequestHeader
                            {
                                AdditionalHeader = new ExtensionObject(new Opc.Ua.AdditionalParametersType()
                                {
                                    Parameters = new KeyValuePairCollection([
                                        new Opc.Ua.KeyValuePair() {
                                        Key = AdditionalParameterNames.Padding,
                                        Value = new Variant(padding)
                                    }
                                    ])
                                })
                            };
                        }

                        BrowseResponse browseResponse = await
                            session.BrowseAsync(
                                requestHeader,
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
                            m_logger.LogError("Browse error: {Message}", sre.Message);
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
                    m_logger.LogInformation("BrowseNext {Count} continuation points.", continuationPoints.Count);
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
                    m_logger.LogInformation("Browse Result {Duplicates} duplicate nodes were ignored.", duplicates);
                }
                browseDescriptionCollection.AddRange(
                    CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate));

                // add unprocessed nodes if any
                browseDescriptionCollection.AddRange(unprocessedOperations);
            }

            stopWatch.Stop();

            var result = new ReferenceDescriptionCollection(referenceDescriptions.Values);
            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            m_logger.LogWarning(
                "BrowseFullAddressSpace found {Count} references on server in {ElapsedMilliseconds}ms.",
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
