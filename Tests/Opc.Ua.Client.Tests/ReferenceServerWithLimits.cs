/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// <para>
    /// Reference Server modification which allows to change the maximum number
    /// of (browse) continuation points during the execution of a test.
    /// This makes it easier to compare browse results without any restriction
    /// with results where the server may allocate only a limited number of
    /// continuation points.
    /// To make this work some other classes must be derived and modified, as
    /// well (see the ServerSessionWithLimits, SessionManagerWithLimits and
    /// MasterNodeManagerWithLimits classes).
    /// </para>
    /// <para>
    /// Use with care. This class and especially its dedicated functionality
    /// should be used for test purposes only.
    /// </para>
    /// </summary>
    public class ReferenceServerWithLimits : ReferenceServer
    {
        public uint TestMaxBrowseReferencesPerNode { get; set; } = 10u;
        private MasterNodeManager MasterNodeManagerReference { get; set; }
        private SessionManagerWithLimits SessionManagerForTest { get; set; }

        public override ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            return base.Browse(
                requestHeader,
                view,
                TestMaxBrowseReferencesPerNode,
                nodesToBrowse,
                out results,
                out diagnosticInfos);
        }

        public void SetMaxNumberOfContinuationPoints(uint maxNumberOfContinuationPoints)
        {
            Configuration.ServerConfiguration.MaxBrowseContinuationPoints
                = (int)maxNumberOfContinuationPoints;
            ((MasterNodeManagerWithLimits)MasterNodeManagerReference)
                .MaxContinuationPointsPerBrowseForUnitTest =
                maxNumberOfContinuationPoints;
            foreach (Server.ISession session in SessionManagerForTest.GetSessions().ToList())
            {
                try
                {
                    ((ServerSessionWithLimits)session).SetMaxNumberOfContinuationPoints(
                        maxNumberOfContinuationPoints);
                }
                catch
                {
                }
            }
        }

        protected override MasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            Utils.LogInfo(
                Utils.TraceMasks.StartStop,
                "Creating the Reference Server Node Manager.");

            IList<INodeManager> nodeManagers =
            [
                // create the custom node manager.
                new ReferenceNodeManager(server, configuration)
            ];

            foreach (INodeManagerFactory nodeManagerFactory in NodeManagerFactories)
            {
                nodeManagers.Add(nodeManagerFactory.Create(server, configuration));
            }
            //this.MasterNodeManagerReference = new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
            MasterNodeManagerReference = new MasterNodeManagerWithLimits(
                server,
                configuration,
                null,
                [.. nodeManagers]);
            // create master node manager.
            return MasterNodeManagerReference;
        }

        protected override ISessionManager CreateSessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            SessionManagerForTest = new SessionManagerWithLimits(server, configuration);
            return SessionManagerForTest;
        }
    }

    /// <summary>
    /// provide a means to set the maximum number of browse continuation points to
    /// the (Server) session.
    /// </summary>
    public class ServerSessionWithLimits : Server.Session
    {
        public ServerSessionWithLimits(
            OperationContext context,
            IServerInternal server,
            X509Certificate2 serverCertificate,
            NodeId authenticationToken,
            byte[] clientNonce,
            Nonce serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            double sessionTimeout,
            uint maxResponseMessageSize,
            double maxRequestAge,
            int maxBrowseContinuationPoints,
            int maxHistoryContinuationPoints)
            : base(
                context,
                server,
                serverCertificate,
                authenticationToken,
                clientNonce,
                serverNonce,
                sessionName,
                clientDescription,
                endpointUrl,
                clientCertificate,
                clientCertificateChain,
                sessionTimeout,
                maxBrowseContinuationPoints,
                maxHistoryContinuationPoints)
        {
        }

        public void SetMaxNumberOfContinuationPoints(uint maxNumberOfContinuationPoints)
        {
            MaxBrowseContinuationPoints = (int)maxNumberOfContinuationPoints;
        }
    }

    /// <summary>
    /// ensures that the (Server) session is the derived one for the test.
    /// </summary>
    public class SessionManagerWithLimits : SessionManager
    {
        private readonly IServerInternal m_4TestServer;
        private readonly int m_4TestMaxRequestAge;
        private readonly int m_4TestMaxBrowseContinuationPoints;
        private readonly int m_4TestMaxHistoryContinuationPoints;

        public SessionManagerWithLimits(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration)
        {
            m_4TestServer = server;
            m_4TestMaxRequestAge = configuration.ServerConfiguration.MaxRequestAge;
            m_4TestMaxBrowseContinuationPoints = configuration.ServerConfiguration
                .MaxBrowseContinuationPoints;
            m_4TestMaxHistoryContinuationPoints = configuration.ServerConfiguration
                .MaxHistoryContinuationPoints;
        }

        /// <summary>
        /// TBD - Remove unused parameter.
        /// </summary>
        protected override Server.ISession CreateSession(
            OperationContext context,
            IServerInternal server,
            X509Certificate2 serverCertificate,
            NodeId sessionCookie,
            byte[] clientNonce,
            Nonce serverNonceObject,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            double sessionTimeout,
            uint maxResponseMessageSize,
            int maxRequestAge, // TBD - Remove unused parameter.
            int maxContinuationPoints)
        {
            return new ServerSessionWithLimits(
                context,
                m_4TestServer,
                serverCertificate,
                sessionCookie,
                clientNonce,
                serverNonceObject,
                sessionName,
                clientDescription,
                endpointUrl,
                clientCertificate,
                clientCertificateChain,
                sessionTimeout,
                maxResponseMessageSize,
                m_4TestMaxRequestAge,
                m_4TestMaxBrowseContinuationPoints,
                m_4TestMaxHistoryContinuationPoints);
        }
    }

    /// <summary>
    /// Hack which ensures the injected maximum number of browse continuation points
    /// is really used when the browse service call is executed.
    /// </summary>
    public class MasterNodeManagerWithLimits : MasterNodeManager
    {
        public MasterNodeManagerWithLimits(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string dynamicNamespaceUri,
            params INodeManager[] additionalManagers)
            : base(server, configuration, dynamicNamespaceUri, additionalManagers)
        {
        }

        public uint MaxContinuationPointsPerBrowseForUnitTest { get; set; }

        /// <summary>
        /// Returns the set of references that meet the filter criteria.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public override void Browse(
            OperationContext context,
            ViewDescription view,
            uint maxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (nodesToBrowse == null)
            {
                throw new ArgumentNullException(nameof(nodesToBrowse));
            }

            if (view != null && !NodeId.IsNull(view.ViewId))
            {
                object viewHandle =
                    GetManagerHandle(view.ViewId, out INodeManager viewManager)
                    ?? throw new ServiceResultException(StatusCodes.BadViewIdUnknown);

                NodeMetadata metadata = viewManager.GetNodeMetadata(
                    context,
                    viewHandle,
                    BrowseResultMask.NodeClass);

                if (metadata == null || metadata.NodeClass != NodeClass.View)
                {
                    throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
                }

                // validate access rights and role permissions
                ServiceResult validationResult = ValidatePermissions(
                    context,
                    viewManager,
                    viewHandle,
                    PermissionType.Browse,
                    null,
                    true);
                if (ServiceResult.IsBad(validationResult))
                {
                    throw new ServiceResultException(validationResult);
                }
                view.Handle = viewHandle;
            }

            bool diagnosticsExist = false;
            results = new BrowseResultCollection(nodesToBrowse.Count);
            diagnosticInfos = new DiagnosticInfoCollection(nodesToBrowse.Count);

            uint continuationPointsAssigned = 0;

            for (int ii = 0; ii < nodesToBrowse.Count; ii++)
            {
                // check if request has timed out or been cancelled.
                if (StatusCode.IsBad(context.OperationStatus))
                {
                    // release all allocated continuation points.
                    foreach (BrowseResult current in results)
                    {
                        if (current != null &&
                            current.ContinuationPoint != null &&
                            current.ContinuationPoint.Length > 0)
                        {
                            ContinuationPoint cp = context.Session
                                .RestoreContinuationPoint(current.ContinuationPoint);
                            cp.Dispose();
                        }
                    }

                    throw new ServiceResultException(context.OperationStatus);
                }

                BrowseDescription nodeToBrowse = nodesToBrowse[ii];

                // initialize result.
                var result = new BrowseResult { StatusCode = StatusCodes.Good };
                results.Add(result);

                ServiceResult error;

                // need to trap unexpected exceptions to handle bugs in the node managers.
                try
                {
                    error = Browse(
                        context,
                        view,
                        maxReferencesPerNode,
                        MaxContinuationPointsPerBrowseForUnitTest <= 0 ||
                        continuationPointsAssigned < MaxContinuationPointsPerBrowseForUnitTest,
                        nodeToBrowse,
                        result);
                }
                catch (Exception e)
                {
                    error = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error browsing node.");
                }

                // check for continuation point.
                if (result.ContinuationPoint != null && result.ContinuationPoint.Length > 0)
                {
                    continuationPointsAssigned++;
                }

                // check for error.
                result.StatusCode = error.StatusCode;

                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    DiagnosticInfo diagnosticInfo = null;

                    if (error != null && error.Code != StatusCodes.Good)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(Server, context, error);
                        diagnosticsExist = true;
                    }

                    diagnosticInfos.Add(diagnosticInfo);
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }
    }
}
