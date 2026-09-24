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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.AuditRegressions
{
    /// <summary>
    /// Regressions for the endpoint selection defects reported by the
    /// Opc.Ua.Client audit.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("AuditRegression")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CoreClientUtilsAuditRegressionTests
    {
        private static readonly Uri s_url = new("opc.tcp://localhost:4840");

        /// <summary>
        /// L9-9: when no endpoint passed the security filter, the "first
        /// available endpoint" fallback returned a None endpoint although the
        /// caller asked for security - a silent downgrade a rogue discovery
        /// answer could force.
        /// </summary>
        [Test]
        public void SelectEndpointWithSecurityNeverFallsBackToNone()
        {
            ArrayOf<EndpointDescription> endpoints =
            [
                CreateEndpoint(MessageSecurityMode.None, SecurityPolicies.None)
            ];

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_url,
                endpoints,
                useSecurity: true,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.Null);
        }

        /// <summary>
        /// L9-9: the fallback must not pick a None endpoint when the only
        /// secure endpoint uses a policy the client does not know.
        /// </summary>
        [Test]
        public void SelectEndpointWithSecurityFallsBackOnlyToSecureEndpoints()
        {
            ArrayOf<EndpointDescription> endpoints =
            [
                CreateEndpoint(MessageSecurityMode.None, SecurityPolicies.None),
                CreateEndpoint(MessageSecurityMode.SignAndEncrypt, "http://unknown/policy")
            ];

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_url,
                endpoints,
                useSecurity: true,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        /// <summary>
        /// Without security requested the fallback keeps its legacy
        /// behaviour and still returns a secure endpoint when that is all the
        /// server offers.
        /// </summary>
        [Test]
        public void SelectEndpointWithoutSecurityStillFallsBack()
        {
            ArrayOf<EndpointDescription> endpoints =
            [
                CreateEndpoint(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
            ];

            EndpointDescription? selected = CoreClientUtils.SelectEndpoint(
                null!,
                s_url,
                endpoints,
                useSecurity: false,
                NUnitTelemetryContext.Create());

            Assert.That(selected, Is.Not.Null);
        }

        private static EndpointDescription CreateEndpoint(
            MessageSecurityMode mode,
            string policyUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = s_url.ToString(),
                SecurityMode = mode,
                SecurityPolicyUri = policyUri
            };
        }
    }
}
