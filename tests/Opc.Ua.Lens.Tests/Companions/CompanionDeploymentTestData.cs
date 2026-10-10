/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions
{

    internal static class CompanionDeploymentTestData
    {
        public static CompanionPreparedOperationTestContext CreateContext(
            ICompanionDeploymentPolicy? policy = null,
            ITelemetryContext? telemetry = null,
            CompanionOperationSafety safety = CompanionOperationSafety.DeploymentMutation,
            string endpoint = Endpoint)
        {
            var context = new CompanionPreparedOperationTestContext(
                safety, endpoint, telemetry, deploymentPolicy: policy);
            context.Endpoint.Server = new ApplicationDescription { ApplicationUri = ApplicationUri };
            return context;
        }

        public static CompanionDeploymentRule CreateRule(
            CompanionPreparedOperationTestContext context,
            string id = "plant-rule",
            string revision = "revision-7",
            DateTimeOffset? expiresAt = null,
            Func<CompanionOperationDraft, bool>? acceptsInput = null)
        {
            IUserIdentity originalIdentity = context.Identity;
            string endpointUrl = context.Endpoint.EndpointUrl ??
                throw new AssertionException("The deployment test endpoint URL is missing.");
            string applicationUri = context.Endpoint.Server.ApplicationUri ??
                throw new AssertionException("The deployment test server application URI is missing.");
            return new CompanionDeploymentRule(
                id, revision, endpointUrl, applicationUri,
                SecurityPolicies.Basic256Sha256, context.Target.ProviderId,
                NodeId.ToExpandedNodeId(context.Target.NodeId, context.NamespaceUris), context.Operation.Id,
                expiresAt ?? context.UtcNow.AddMinutes(2),
                identity => ReferenceEquals(identity, originalIdentity),
                acceptsInput ?? (static draft => draft.Input == Input));
        }

        public static CompanionOperationDraft Capture(CompanionPreparedOperationTestContext context)
        {
            return new CompanionOperationDraft(
                context.Target, context.Operation, Input, context.Session.Object, context.UtcNow.AddMinutes(5));
        }

        public static void ChangeSession(CompanionPreparedOperationTestContext context, string change)
        {
            switch (change)
            {
                case "identity":
                    context.Identity = new Mock<IUserIdentity>(MockBehavior.Strict).Object;
                    break;
                case "application":
                    context.Endpoint.Server.ApplicationUri = "urn:ualens:deployment:replacement";
                    break;
                case "session":
                    context.SessionId = new NodeId(202u);
                    break;
                case "namespace":
                    context.NamespaceUris.Update([Namespaces.OpcUa, "urn:ualens:server", "urn:replacement"]);
                    break;
                case "namespace-order":
                    context.NamespaceUris.Update([Namespaces.OpcUa, "urn:ualens:test", "urn:ualens:server"]);
                    break;
                case "endpoint":
                    context.Endpoint.EndpointUrl = "opc.tcp://deployment.example:4840/Other";
                    break;
                case "mode":
                    context.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "policy":
                    context.Endpoint.SecurityPolicyUri =
                        "http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep";
                    break;
                case "disconnected":
                    context.Connected = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        public const string Endpoint = "opc.tcp://deployment.example:4840/Plant?profile=query-marker";
        public const string ApplicationUri = "urn:ualens:deployment:plant";
        public const string Input = "recipe=7";
    }
}
