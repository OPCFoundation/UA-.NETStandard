// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the logger category of node managers created without a logger.
    /// </summary>
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// A node manager created without a logger logs under its concrete
        /// type, not under the deprecated <c>CustomNodeManager2</c> category.
        /// </summary>
        [Test]
        public void LoggerlessConstructorUsesConcreteTypeCategory()
        {
            var categories = new List<string>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory
                .Setup(factory => factory.CreateLogger(It.IsAny<string>()))
                .Callback<string>(categories.Add)
                .Returns(new Mock<ILogger>().Object);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.Setup(t => t.LoggerFactory).Returns(loggerFactory.Object);
            m_mockServer.Setup(s => s.Telemetry).Returns(telemetry.Object);

            using var manager = new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                false,
                m_testNamespaceUri);

            Assert.That(manager.Logger, Is.Not.Null);
            Assert.That(categories, Does.Contain(typeof(TestableAsyncCustomNodeManager).FullName));
            Assert.That(categories, Has.None.Contains("CustomNodeManager2"));
        }
    }
}
