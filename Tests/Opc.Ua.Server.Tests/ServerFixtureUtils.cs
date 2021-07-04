/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Extensions for server fixture.
    /// </summary>
    public static class ServerFixtureUtils
    {
        public const double DefaultSessionTimeout = 120000;
        public const uint DefaultMaxResponseMessageSize = 128 * 1024;

        #region Public Methods
        /// <summary>
        /// Create and Activate a session without security.
        /// </summary>
        /// <remarks>
        /// The request header is used to call services directly,
        /// without establishing a session with a client.
        /// </remarks>
        /// <param name="server">The server to connect to.</param>
        /// <param name="sessionName">A session name.</param>
        /// <returns>The request header for the session.</returns>
        public static RequestHeader CreateAndActivateSession(
            this SessionServerBase server,
            string sessionName,
            double sessionTimeout = DefaultSessionTimeout,
            uint maxResponseMessageSize = DefaultMaxResponseMessageSize)
        {
            // Find TCP endpoint
            var endpoints = server.GetEndpoints();
            var endpoint = endpoints.FirstOrDefault(e => e.TransportProfileUri == Profiles.UaTcpTransport);

            // no security
            endpoint.SecurityMode = MessageSecurityMode.None;
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            var context = new SecureChannelContext(
                sessionName,
                endpoint,
                RequestEncoding.Binary);

            // set security context
            SecureChannelContext.Current = context;
            var requestHeader = new RequestHeader();

            // Create session
            var response = server.CreateSession(
                requestHeader,
                null, null, null,
                sessionName,
                null, null, sessionTimeout, maxResponseMessageSize,
                out var sessionId, out var authenticationToken, out sessionTimeout,
                out var serverNonce, out var serverCertificate, out var endpointDescriptions,
                out var serverSoftwareCertificates, out var signatureData, out var maxRequestMessageSize);
            ValidateResponse(response);

            // Activate session
            requestHeader.AuthenticationToken = authenticationToken;
            response = server.ActivateSession(requestHeader, signatureData, null, new StringCollection(), null, null,
                out serverNonce, out var results, out var diagnosticInfos);
            ValidateResponse(response);

            return requestHeader;
        }

        /// <summary>
        /// Close a session.
        /// </summary>
        /// <param name="server">The server where the session is active.</param>
        /// <param name="requestHeader">The request header of the session.</param>
        public static void CloseSession(this SessionServerBase server, RequestHeader requestHeader)
        {
            // close session
            var response = server.CloseSession(requestHeader, true);
            ValidateResponse(response);
        }

        /// <summary>
        /// Validate the response of a service call.
        /// </summary>
        /// <param name="header">The response header of the service call.</param>
        public static void ValidateResponse(ResponseHeader header)
        {
            if (header == null)
            {
                throw new ServiceResultException(StatusCodes.BadUnknownResponse, "Null header in response.");
            }

            if (StatusCode.IsBad(header.ServiceResult))
            {
                throw new ServiceResultException(new ServiceResult(header.ServiceResult, header.ServiceDiagnostics, header.StringTable));
            }
        }

        /// <summary>
        /// Validate the diagnostic response of a service call.
        /// </summary>
        /// <param name="response">The diagnostic info response.</param>
        /// <param name="request">The request items of the service call.</param>
        public static void ValidateDiagnosticInfos(DiagnosticInfoCollection response, IList request)
        {
            // returning an empty list for diagnostic info arrays is allowed.
            if (response != null && response.Count != 0 && response.Count != request.Count)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "The server forgot to fill in the DiagnosticInfos array correctly when returning an operation level error.");
            }
        }

        /// <summary>
        /// Create a browse description from a node id collection.
        /// </summary>
        /// <param name="nodeIdCollection">The node id collection.</param>
        /// <param name="template">The template for the browse description for each node id.</param>
        public static BrowseDescriptionCollection CreateBrowseDescriptionCollectionFromNodeId(
            NodeIdCollection nodeIdCollection,
            BrowseDescription template)
        {
            var browseDescriptionCollection = new BrowseDescriptionCollection();
            foreach (var nodeId in nodeIdCollection)
            {
                BrowseDescription browseDescription = (BrowseDescription)template.MemberwiseClone();
                browseDescription.NodeId = nodeId;
                browseDescriptionCollection.Add(browseDescription);
            }
            return browseDescriptionCollection;
        }

        /// <summary>
        /// Create the continuation point collection from the browse result
        /// collection for the BrowseNext service.
        /// </summary>
        /// <param name="browseResultCollection">The browse result collection to use.</param>
        /// <returns>The collection of continuation points for the BrowseNext service.</returns>
        public static ByteStringCollection PrepareBrowseNext(BrowseResultCollection browseResultCollection)
        {
            var continuationPoints = new ByteStringCollection();
            foreach (var browseResult in browseResultCollection)
            {
                if (browseResult.ContinuationPoint != null)
                {
                    continuationPoints.Add(browseResult.ContinuationPoint);
                }
            }
            return continuationPoints;
        }

        /// <summary>
        /// A dictionary of all node attributes.
        /// </summary>
        public static readonly ReadOnlyDictionary<uint, DataValue> AttributesIds = new ReadOnlyDictionary<uint, DataValue>(
            new SortedDictionary<uint, DataValue> {
                { Attributes.NodeId, null },
                { Attributes.NodeClass, null },
                { Attributes.BrowseName, null },
                { Attributes.DisplayName, null },
                { Attributes.Description, null },
                { Attributes.WriteMask, null },
                { Attributes.UserWriteMask, null },
                { Attributes.DataType, null },
                { Attributes.ValueRank, null },
                { Attributes.ArrayDimensions, null },
                { Attributes.AccessLevel, null },
                { Attributes.UserAccessLevel, null },
                { Attributes.Historizing, null },
                { Attributes.MinimumSamplingInterval, null },
                { Attributes.EventNotifier, null },
                { Attributes.Executable, null },
                { Attributes.UserExecutable, null },
                { Attributes.IsAbstract, null },
                { Attributes.InverseName, null },
                { Attributes.Symmetric, null },
                { Attributes.ContainsNoLoops, null },
                { Attributes.DataTypeDefinition, null },
                { Attributes.RolePermissions, null },
                { Attributes.UserRolePermissions, null },
                { Attributes.AccessRestrictions, null },
                { Attributes.AccessLevelEx, null }
            });
        #endregion
    }
}
