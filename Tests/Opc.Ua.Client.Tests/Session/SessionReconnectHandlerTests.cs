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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Unit tests for SessionReconnectHandler, focusing on endpoint fallback behavior
    /// when the server's endpoint configuration changes.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SessionReconnectHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionReconnectHandlerTests
    {
        private ITelemetryContext _telemetry;

        [SetUp]
        public void SetUp()
        {
            _telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// Verifies that the reconnect handler calls UpdateEndpointFromServerAsync
        /// when m_updateFromServer is set and session recreation succeeds.
        /// </summary>
        [Test]
        public async Task DoReconnectAsync_WhenUpdateFromServerIsSet_CallsUpdateEndpointFromServerAsync()
        {
            // Arrange
            int updateCallCount = 0;
            var mockNewSession = new Mock<ISession>();
            var mockFactory = new Mock<ISessionFactory>();

            ConfiguredEndpoint configuredEndpoint = CreateConfiguredEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256);

            Mock<ISession> mockSession = CreateMockSession(configuredEndpoint, mockFactory);
            mockFactory.Setup(f => f.RecreateAsync(mockSession.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockNewSession.Object);

            using var handler = new TestableSessionReconnectHandler(
                _telemetry,
                (endpoint, connection) =>
                {
                    updateCallCount++;
                    return Task.CompletedTask;
                });

            SetSessionHandlerField(handler, "m_reconnectFailed", true);
            SetSessionHandlerField(handler, "m_updateFromServer", true);
            SetHandlerSession(handler, mockSession.Object);

            // Act
            bool result = await InvokeDoReconnectAsync(handler).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(updateCallCount, Is.EqualTo(1));
            Assert.That(handler.Session, Is.SameAs(mockNewSession.Object));
        }

        /// <summary>
        /// Verifies that DoReconnectAsync does not call UpdateEndpointFromServerAsync
        /// when m_updateFromServer is false.
        /// </summary>
        [Test]
        public async Task DoReconnectAsync_WhenUpdateFromServerIsFalse_SkipsEndpointUpdate()
        {
            // Arrange
            int updateCallCount = 0;
            var mockNewSession = new Mock<ISession>();
            var mockFactory = new Mock<ISessionFactory>();

            ConfiguredEndpoint configuredEndpoint = CreateConfiguredEndpoint(
                MessageSecurityMode.None,
                SecurityPolicies.None);

            Mock<ISession> mockSession = CreateMockSession(configuredEndpoint, mockFactory);
            mockFactory.Setup(f => f.RecreateAsync(mockSession.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockNewSession.Object);

            using var handler = new TestableSessionReconnectHandler(
                _telemetry,
                (endpoint, connection) =>
                {
                    updateCallCount++;
                    return Task.CompletedTask;
                });

            SetSessionHandlerField(handler, "m_reconnectFailed", true);
            SetSessionHandlerField(handler, "m_updateFromServer", false);
            SetHandlerSession(handler, mockSession.Object);

            // Act
            bool result = await InvokeDoReconnectAsync(handler).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(updateCallCount, Is.Zero);
        }

        /// <summary>
        /// Verifies that when UpdateEndpointFromServerAsync fails with a ServiceResultException,
        /// the session recreation is skipped, m_updateFromServer is set for the next attempt,
        /// and false is returned.
        /// </summary>
        [Test]
        public async Task DoReconnectAsync_WhenUpdateEndpointFails_SetsUpdateFromServerAndReturnsFalse()
        {
            // Arrange
            var mockFactory = new Mock<ISessionFactory>();
            ConfiguredEndpoint configuredEndpoint = CreateConfiguredEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Aes256_Sha256_RsaPss);

            Mock<ISession> mockSession = CreateMockSession(configuredEndpoint, mockFactory);

            using var handler = new TestableSessionReconnectHandler(
                _telemetry,
                (endpoint, connection) =>
                    throw new ServiceResultException(StatusCodes.BadNoCommunication));

            SetSessionHandlerField(handler, "m_reconnectFailed", true);
            SetSessionHandlerField(handler, "m_updateFromServer", true);
            SetHandlerSession(handler, mockSession.Object);

            // Act
            bool result = await InvokeDoReconnectAsync(handler).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.False);
            bool updateFromServer = GetSessionHandlerField<bool>(handler, "m_updateFromServer");
            Assert.That(updateFromServer, Is.True);
        }

        /// <summary>
        /// Verifies that UpdateEndpointFromServerAsync is a protected virtual method
        /// that can be overridden to customize the endpoint update behavior.
        /// </summary>
        [Test]
        public void UpdateEndpointFromServerAsync_IsProtectedVirtual()
        {
            MethodInfo method = typeof(SessionReconnectHandler).GetMethod(
                "UpdateEndpointFromServerAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(method, Is.Not.Null);
            Assert.That(method.IsVirtual, Is.True);
            Assert.That(method.IsFamily, Is.True); // protected
        }

        /// <summary>
        /// Verifies that UpdateEndpointFromServerAsync accepts a ConfiguredEndpoint
        /// and a nullable ITransportWaitingConnection parameter (for reverse connect support).
        /// </summary>
        [Test]
        public void UpdateEndpointFromServerAsync_HasCorrectSignature()
        {
            MethodInfo method = typeof(SessionReconnectHandler).GetMethod(
                "UpdateEndpointFromServerAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(method, Is.Not.Null);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.That(parameters.Length, Is.EqualTo(2));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(ConfiguredEndpoint)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(ITransportWaitingConnection)));
        }

        /// <summary>
        /// Verifies that BeginReconnect with a valid session sets the state to Triggered.
        /// </summary>
        [Test]
        public void BeginReconnect_WithValidSession_SetsTriggeredState()
        {
            // Arrange
            var mockSession = new Mock<ISession>();
            mockSession.SetupGet(s => s.SessionId).Returns(NodeId.Null);

            using var handler = new SessionReconnectHandler(_telemetry);

            // Act
            SessionReconnectHandler.ReconnectState state = handler.BeginReconnect(
                mockSession.Object,
                1000,
                (_, _) => { });

            // Assert
            Assert.That(state, Is.EqualTo(SessionReconnectHandler.ReconnectState.Triggered));

            handler.CancelReconnect();
        }

        /// <summary>
        /// Verifies that when the endpoint update succeeds after a simulated
        /// BadSecurityPolicyRejected fallback (modeled via TestableSessionReconnectHandler),
        /// the session is recreated and the reconnect succeeds.
        /// This simulates a server restarting with a different endpoint configuration where
        /// the fallback selects the best available endpoint.
        /// </summary>
        [Test]
        public async Task DoReconnectAsync_WhenEndpointUpdateSucceedsAfterFallback_SessionIsRecreated()
        {
            // Arrange
            var mockNewSession = new Mock<ISession>();
            var mockFactory = new Mock<ISessionFactory>();

            ConfiguredEndpoint configuredEndpoint = CreateConfiguredEndpoint(
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Aes256_Sha256_RsaPss);

            Mock<ISession> mockSession = CreateMockSession(configuredEndpoint, mockFactory);
            mockFactory.Setup(f => f.RecreateAsync(mockSession.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockNewSession.Object);

            // Simulate: the handler successfully updates the endpoint
            // (the base class would have fallen back to best available endpoint here)
            using var handler = new TestableSessionReconnectHandler(
                _telemetry,
                (endpoint, connection) => Task.CompletedTask);

            SetSessionHandlerField(handler, "m_reconnectFailed", true);
            SetSessionHandlerField(handler, "m_updateFromServer", true);
            SetHandlerSession(handler, mockSession.Object);

            // Act
            bool result = await InvokeDoReconnectAsync(handler).ConfigureAwait(false);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(handler.Session, Is.SameAs(mockNewSession.Object));
        }

        private static ConfiguredEndpoint CreateConfiguredEndpoint(
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            return new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri
            });
        }

        private static Mock<ISession> CreateMockSession(
            ConfiguredEndpoint configuredEndpoint,
            Mock<ISessionFactory> mockFactory)
        {
            var mockSession = new Mock<ISession>();
            mockSession.SetupGet(s => s.ConfiguredEndpoint).Returns(configuredEndpoint);
            mockSession.SetupGet(s => s.SessionFactory).Returns(mockFactory.Object);
            mockSession.SetupGet(s => s.SessionId).Returns(NodeId.Null);
            return mockSession;
        }

        private static Task<bool> InvokeDoReconnectAsync(SessionReconnectHandler handler)
        {
            MethodInfo method = typeof(SessionReconnectHandler)
                .GetMethod("DoReconnectAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("DoReconnectAsync method not found");

            return (Task<bool>)method.Invoke(handler, null);
        }

        private static void SetSessionHandlerField(
            SessionReconnectHandler handler,
            string fieldName,
            object value)
        {
            FieldInfo field = typeof(SessionReconnectHandler)
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Field {fieldName} not found");
            field.SetValue(handler, value);
        }

        private static void SetHandlerSession(SessionReconnectHandler handler, ISession session)
        {
            PropertyInfo prop = typeof(SessionReconnectHandler)
                .GetProperty("Session", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Session property not found");
            MethodInfo setter = prop.GetSetMethod(nonPublic: true)
                ?? throw new InvalidOperationException("Session property setter not found");
            setter.Invoke(handler, [session]);
        }

        private static T GetSessionHandlerField<T>(SessionReconnectHandler handler, string fieldName)
        {
            FieldInfo field = typeof(SessionReconnectHandler)
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Field {fieldName} not found");
            return (T)field.GetValue(handler);
        }

        /// <summary>
        /// A testable subclass of SessionReconnectHandler that replaces the
        /// UpdateEndpointFromServerAsync with a user-supplied delegate, enabling
        /// unit tests to control the behavior without requiring network access.
        /// </summary>
        private sealed class TestableSessionReconnectHandler : SessionReconnectHandler
        {
            private readonly Func<ConfiguredEndpoint, ITransportWaitingConnection, Task> _updateDelegate;

            public TestableSessionReconnectHandler(
                ITelemetryContext telemetry,
                Func<ConfiguredEndpoint, ITransportWaitingConnection, Task> updateDelegate)
                : base(telemetry)
            {
                _updateDelegate = updateDelegate;
            }

            protected override Task UpdateEndpointFromServerAsync(
                ConfiguredEndpoint endpoint,
                ITransportWaitingConnection connection = null)
            {
                return _updateDelegate(endpoint, connection);
            }
        }
    }
}
