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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Identity;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Implements a sample Global Discovery Server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// </para>
    /// <para>
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the ApplicationNodeManager which provides access to the data exposed by the
    /// Global Discovery Server.
    /// </para>
    /// </remarks>
    public class GlobalDiscoverySampleServer : StandardServer
    {
        public GlobalDiscoverySampleServer(
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            ITelemetryContext telemetry,
            bool autoApprove = true)
            : this(
                database,
                request,
                certificateGroup,
                userDatabase,
                telemetry,
                autoApprove,
                enableApplicationSelfAdminProvider: true)
        {
        }

        public GlobalDiscoverySampleServer(
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            ITelemetryContext telemetry,
            bool autoApprove,
            bool enableApplicationSelfAdminProvider)
            : base(telemetry)
        {
            m_database = database;
            m_request = request;
            m_certificateGroup = certificateGroup;
            m_userDatabase = userDatabase;
            m_autoApprove = autoApprove;
            m_enableApplicationSelfAdminProvider = enableApplicationSelfAdminProvider;
        }

        /// <summary>
        /// Back-compat ctor matching the 1.5.378 signature (no <see cref="ITelemetryContext"/>).
        /// Forwards to the modern ctor with a null telemetry context.
        /// </summary>
        /// <remarks>
        /// Preserved so 1.5.378-style sample code (`new GlobalDiscoverySampleServer(database,
        /// request, certificateGroup, userDatabase, autoApprove)`) continues to compile against
        /// 2.0 without re-ordering the call site. Consumers should pass an explicit
        /// <see cref="ITelemetryContext"/> via the non-obsolete ctor.
        /// </remarks>
        [Obsolete("Use the constructor that takes an ITelemetryContext parameter instead.")]
        public GlobalDiscoverySampleServer(
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            bool autoApprove = true)
            : this(database, request, certificateGroup, userDatabase, telemetry: null!, autoApprove)
        {
        }

        /// <summary>
        /// Called before the server starts. Registers GDS-specific
        /// encodeable types in the server's message context factory,
        /// which is required for NativeAOT where reflection-based
        /// assembly scanning does not discover types automatically.
        /// </summary>
        protected override void OnServerStarting(
            ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);

            if (!MessageContext.Factory.ContainsEncodeableType(
                DataTypeIds.ApplicationRecordDataType))
            {
                MessageContext.Factory.Builder
                    .AddOpcUaGds()
                    .AddOpcUaGdsServerDataTypes()
                    .Commit();
            }
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            server.IdentityRegistry.Register(new AnonymousAuthenticator());
            server.IdentityRegistry.Register(new GlobalDiscoverySampleUserNameAuthenticator(this));
            server.IdentityRegistry.Register(new GlobalDiscoverySampleX509Authenticator(this));
            if (m_enableApplicationSelfAdminProvider)
            {
                server.IdentityRegistry.RegisterAugmenter(
                    new GdsApplicationSelfAdminProvider(
                        m_database,
                        server.Telemetry.CreateLogger<GdsApplicationSelfAdminProvider>(),
                        server.NamespaceUris,
                        IsApplicationCertificateRegistered));
            }
        }

        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses.
        /// The SDK always creates a CoreNodeManager which handles the built-in nodes defined
        /// by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// </remarks>
        protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            m_logger.LogInformation("Creating the Node Managers.");

            // create the custom node managers.
            var nodeManagers = new IAsyncNodeManager[]
            {
                new ApplicationsNodeManager(
                    server,
                    configuration,
                    m_database,
                    m_request,
                    m_certificateGroup,
                    m_autoApprove)
            };

            // create master node manager.
#pragma warning disable CA2000 // ownership of MasterNodeManager transfers to the caller via the returned ValueTask<IMasterNodeManager>
            return new ValueTask<IMasterNodeManager>(
                new MasterNodeManager(server, configuration, null, nodeManagers));
#pragma warning restore CA2000
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            return new ServerProperties
            {
                ManufacturerName = "Some Company Inc",
                ProductName = "Global Discovery Server",
                ProductUri = "http://somecompany.com/GlobalDiscoveryServer",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };
        }

        /// <summary>
        /// This method is called at the being of the thread that processes a request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected override async ValueTask<OperationContext> ValidateRequestAsync(
            SecureChannelContext secureChannelContext,
            [NotNull] RequestHeader? requestHeader,
            RequestType requestType,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await base.ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                requestType,
                requestLifetime).ConfigureAwait(false);

            if (requestType == RequestType.Write)
            {
                // reject all writes if no user provided.
                if (context.UserIdentity.TokenType == UserTokenType.Anonymous)
                {
                    // construct translation object with default text.
                    var info = new TranslationInfo(
                        "NoWriteAllowed",
                        "en-US",
                        "Must provide a valid user before calling write.");

                    // create an exception with a vendor defined sub-code.
                    throw new ServiceResultException(
                        new ServiceResult(
                            Namespaces.OpcUaGds,
                            new StatusCode(StatusCodes.BadUserAccessDenied.Code, "NoWriteAllowed"),
                            new LocalizedText(info)));
                }
            }

            return context;
        }

        private bool IsApplicationCertificateRegistered(Certificate applicationInstanceCertificate)
        {
            if (applicationInstanceCertificate == null)
            {
                throw new ArgumentNullException(nameof(applicationInstanceCertificate));
            }

            GlobalDiscoveryServerConfiguration configuration =
                Configuration!.ParseExtension<GlobalDiscoveryServerConfiguration>()
                ?? new GlobalDiscoveryServerConfiguration();
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.ApplicationCertificatesStorePath!);
            using (ICertificateStore applicationsStore =
                certificateStoreIdentifier.OpenStore(MessageContext.Telemetry))
            {
                using CertificateCollection matchingCerts = applicationsStore
                    .FindByThumbprintAsync(applicationInstanceCertificate.Thumbprint)
                    .GetAwaiter()
                    .GetResult();

                if (matchingCerts.Count == 0)
                {
                    return false;
                }
            }

            certificateStoreIdentifier = new CertificateStoreIdentifier(
                configuration.AuthoritiesStorePath!);
            using ICertificateStore authoritiesStore =
                certificateStoreIdentifier.OpenStore(MessageContext.Telemetry);
            IEnumerable<X509CRL> certificateRevocationLists = authoritiesStore
                .EnumerateCRLsAsync()
                .GetAwaiter()
                .GetResult();
            foreach (X509CRL crl in certificateRevocationLists)
            {
                if (crl.IsRevoked(applicationInstanceCertificate))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void VerifyX509IdentityToken(X509IdentityToken token)
        {
            using Certificate? userCertificate = token.CertificateData.IsEmpty
                ? null
                : Certificate.FromRawData(token.CertificateData);
            try
            {
                // Validate against the Users trust list using the new
                // CertificateManager pipeline. Throws on validation failure.
                // CA2025: task awaited via GetAwaiter().GetResult(); the disposable's
                // using scope extends past the await.
#pragma warning disable CA2025
                CertificateValidationResult result = CertificateManager!
                    .ValidateAsync(
                        userCertificate!,
                        TrustListIdentifier.Users)
                    .GetAwaiter().GetResult();
#pragma warning restore CA2025
                if (!result.IsValid)
                {
                    throw new ServiceResultException(result.StatusCode);
                }
            }
            catch (Exception e)
            {
                TranslationInfo info;
                StatusCode result = StatusCodes.BadIdentityTokenRejected;
                if (e is ServiceResultException se &&
                    se.StatusCode == StatusCodes.BadCertificateUseNotAllowed)
                {
                    info = new TranslationInfo(
                        "InvalidCertificate",
                        "en-US",
                        "'{0}' is an invalid user certificate.",
                        userCertificate?.Subject ?? string.Empty);

                    result = StatusCodes.BadIdentityTokenInvalid;
                }
                else
                {
                    // construct translation object with default text.
                    info = new TranslationInfo(
                        "UntrustedCertificate",
                        "en-US",
                        "'{0}' is not a trusted user certificate.",
                        userCertificate?.Subject ?? string.Empty);
                }

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(
                    new ServiceResult(
                        LoadServerProperties().ProductUri,
                        new StatusCode(result.Code, info.Key),
                        new LocalizedText(info)));
            }
        }

        private bool VerifyPassword(UserNameIdentityTokenHandler userTokenHandler)
        {
            return m_userDatabase.CheckCredentials(
                userTokenHandler.UserName,
                userTokenHandler.DecryptedPassword);
        }

        private sealed class GlobalDiscoverySampleUserNameAuthenticator : IUserTokenAuthenticator
        {
            private readonly GlobalDiscoverySampleServer m_server;

            public GlobalDiscoverySampleUserNameAuthenticator(GlobalDiscoverySampleServer server)
            {
                m_server = server ?? throw new ArgumentNullException(nameof(server));
            }

            public UserTokenType TokenType => UserTokenType.UserName;

            public string? IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (context.TokenHandler is UserNameIdentityTokenHandler userNameToken)
                {
                    return new ValueTask<AuthenticationResult>(AuthenticateUserName(userNameToken));
                }

                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }

            private AuthenticationResult AuthenticateUserName(
                UserNameIdentityTokenHandler userNameToken)
            {
                try
                {
                    if (!m_server.VerifyPassword(userNameToken))
                    {
                        return AuthenticationResult.Reject(
                            new ServiceResult(
                                StatusCodes.BadUserAccessDenied,
                                new LocalizedText("Invalid username or password.")));
                    }

                    IEnumerable<Role> roles = m_server.m_userDatabase.GetUserRoles(userNameToken.UserName);
                    IReadOnlyList<NodeId>? administeredAppIds =
                        (m_server.m_userDatabase as IGdsUserDatabase)?
                            .GetAdministeredApplicationIds(userNameToken.UserName);
                    var identity = new GdsRoleBasedIdentity(
                        new UserIdentity(userNameToken),
                        roles,
                        default,
                        administeredAppIds,
                        m_server.ServerInternal.MessageContext.NamespaceUris);
                    return AuthenticationResult.Accept(identity);
                }
                catch (ServiceResultException ex)
                {
                    return AuthenticationResult.Reject(ex.Result);
                }
                catch (ArgumentException ex)
                {
                    return AuthenticationResult.Reject(
                        new ServiceResult(
                            StatusCodes.BadIdentityTokenRejected,
                            new LocalizedText(ex.Message)));
                }
            }
        }

        private sealed class GlobalDiscoverySampleX509Authenticator : IUserTokenAuthenticator
        {
            private readonly GlobalDiscoverySampleServer m_server;

            public GlobalDiscoverySampleX509Authenticator(GlobalDiscoverySampleServer server)
            {
                m_server = server ?? throw new ArgumentNullException(nameof(server));
            }

            public UserTokenType TokenType => UserTokenType.Certificate;

            public string? IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (context.TokenHandler is not X509IdentityTokenHandler x509Token)
                {
                    return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
                }

                return new ValueTask<AuthenticationResult>(AuthenticateX509(x509Token));
            }

            private AuthenticationResult AuthenticateX509(X509IdentityTokenHandler x509Token)
            {
                try
                {
                    m_server.VerifyX509IdentityToken((X509IdentityToken)x509Token.Token);
                    var identity = new GdsRoleBasedIdentity(
                        new UserIdentity(x509Token),
                        [Role.AuthenticatedUser],
                        m_server.ServerInternal.MessageContext.NamespaceUris);
                    m_server.m_logger.LogInformation(
                        "X509 Token Accepted: {Identity} as {Role}",
                        identity.DisplayName,
                        Role.AuthenticatedUser);
                    return AuthenticationResult.Accept(identity);
                }
                catch (ServiceResultException ex)
                {
                    return AuthenticationResult.Reject(ex.Result);
                }
            }
        }

        private readonly IApplicationsDatabase m_database;
        private readonly ICertificateRequest m_request;
        private readonly ICertificateGroup m_certificateGroup;
        private readonly IUserDatabase m_userDatabase;
        private readonly bool m_autoApprove;
        private readonly bool m_enableApplicationSelfAdminProvider;
    }
}
