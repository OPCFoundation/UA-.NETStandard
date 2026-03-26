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
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Tests for ConfiguredEndpoint matching.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConfiguredEndpointTests
    {
        /// <summary>
        /// Test that when a requested security policy is not supported by the server,
        /// BadSecurityPolicyRejected is thrown instead of BadUserAccessDenied.
        /// </summary>
        [Test]
        public void MatchEndpoints_ThrowsBadSecurityPolicyRejected_WhenPolicyNotSupported()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Create server endpoints with only None and Basic256Sha256
            var serverEndpoints = new EndpointDescriptionCollection
            {
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            };

            // Try to match with a security policy that doesn't exist (Aes256_Sha256_RsaPss)
            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(
                () => InvokeMatchEndpoints(
                    serverEndpoints,
                    new Uri("opc.tcp://localhost:4840"),
                    MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Aes256_Sha256_RsaPss
                ));

            Assert.IsInstanceOf<ServiceResultException>(ex.InnerException);
            var serviceException = (ServiceResultException)ex.InnerException;
            Assert.AreEqual(StatusCodes.BadSecurityPolicyRejected, serviceException.StatusCode);
            Assert.That(serviceException.Message, Does.Contain(SecurityPolicies.Aes256_Sha256_RsaPss));
        }

        /// <summary>
        /// Test that when a requested security mode is not supported by the server,
        /// BadSecurityModeRejected is thrown.
        /// </summary>
        [Test]
        public void MatchEndpoints_ThrowsBadSecurityModeRejected_WhenModeNotSupported()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Create server endpoints with only None security mode
            var serverEndpoints = new EndpointDescriptionCollection
            {
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            };

            // Try to match with SignAndEncrypt mode that doesn't exist
            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() => InvokeMatchEndpoints(
                    serverEndpoints,
                    new Uri("opc.tcp://localhost:4840"),
                    MessageSecurityMode.SignAndEncrypt,
                    null // no specific policy requested, only mode
                ));

            Assert.IsInstanceOf<ServiceResultException>(ex.InnerException);
            var serviceException = (ServiceResultException)ex.InnerException;
            Assert.AreEqual(StatusCodes.BadSecurityModeRejected, serviceException.StatusCode);
            Assert.That(serviceException.Message, Does.Contain("SignAndEncrypt"));
        }

        /// <summary>
        /// Test that when both security policy and mode are not supported,
        /// BadSecurityPolicyRejected is thrown with information about both.
        /// </summary>
        [Test]
        public void MatchEndpoints_ThrowsBadSecurityPolicyRejected_WhenBothPolicyAndModeNotSupported()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Create server endpoints with only None
            var serverEndpoints = new EndpointDescriptionCollection
            {
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            };

            // Try to match with both policy and mode that don't exist
            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() => InvokeMatchEndpoints(
                    serverEndpoints,
                    new Uri("opc.tcp://localhost:4840"),
                    MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Basic256Sha256
                ));

            Assert.IsInstanceOf<ServiceResultException>(ex.InnerException);
            var serviceException = (ServiceResultException)ex.InnerException;
            Assert.AreEqual(StatusCodes.BadSecurityPolicyRejected, serviceException.StatusCode);
            Assert.That(serviceException.Message, Does.Contain(SecurityPolicies.Basic256Sha256));
            Assert.That(serviceException.Message, Does.Contain("SignAndEncrypt"));
        }

        /// <summary>
        /// Test that when no specific security parameters are requested,
        /// the method returns available endpoints without throwing.
        /// </summary>
        [Test]
        public void MatchEndpoints_ReturnsEndpoints_WhenNoSecurityParametersSpecified()
        {
            // Create server endpoints
            var serverEndpoints = new EndpointDescriptionCollection
            {
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            };

            // Match without specifying security parameters
            EndpointDescriptionCollection matches = InvokeMatchEndpoints(
                serverEndpoints,
                new Uri("opc.tcp://localhost:4840"),
                MessageSecurityMode.Invalid,
                null
            );

            // Should return available endpoints
            Assert.IsNotNull(matches);
            Assert.Greater(matches.Count, 0);
        }

        /// <summary>
        /// Test that matching works correctly when the requested security parameters exist.
        /// </summary>
        [Test]
        public void MatchEndpoints_ReturnsMatchingEndpoint_WhenSecurityParametersMatch()
        {
            // Create server endpoints
            var serverEndpoints = new EndpointDescriptionCollection
            {
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            };

            // Match with existing security parameters
            EndpointDescriptionCollection matches = InvokeMatchEndpoints(
                serverEndpoints,
                new Uri("opc.tcp://localhost:4840"),
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256
            );

            // Should return the matching endpoint
            Assert.IsNotNull(matches);
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual(SecurityPolicies.Basic256Sha256, matches[0].SecurityPolicyUri);
            Assert.AreEqual(MessageSecurityMode.SignAndEncrypt, matches[0].SecurityMode);
        }

        /// <summary>
        /// Helper method to invoke the private MatchEndpoints method via reflection.
        /// </summary>
        private static EndpointDescriptionCollection InvokeMatchEndpoints(
            EndpointDescriptionCollection collection,
            Uri endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            Type configuredEndpointType = typeof(ConfiguredEndpoint);
            MethodInfo matchEndpointsMethod = configuredEndpointType.GetMethod(
                "MatchEndpoints",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                [typeof(EndpointDescriptionCollection), typeof(Uri), typeof(MessageSecurityMode), typeof(string)],
                null
            );

            Assert.IsNotNull(matchEndpointsMethod, "MatchEndpoints method not found");

            return (EndpointDescriptionCollection)matchEndpointsMethod.Invoke(
                null,
                [collection, endpointUrl, securityMode, securityPolicyUri]
            );
        }
    }
}
