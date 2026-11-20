// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using Moq;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Tests
{
    /// <summary>
    /// Supplies a distinct captured client while retaining a fixture's arranged read and write responses.
    /// </summary>
    internal static class SessionBindingTestHelper
    {
        /// <summary>
        /// Adds binding capture before the session proxy is created.
        /// </summary>
        internal static Mock<ISessionBinding> Attach(Mock<ISession> session)
        {
            var binding = new Mock<ISessionBinding>(MockBehavior.Strict);
            binding.SetupGet(value => value.IsCurrent).Returns(true);
            binding.Setup(value => value.Dispose());
            binding.Setup(value => value.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, double maxAge, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> nodes, CancellationToken token) =>
                    session.Object.ReadAsync(header, maxAge, timestamps, nodes, token));
            binding.Setup(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, ArrayOf<WriteValue> nodes, CancellationToken token) =>
                    session.Object.WriteAsync(header, nodes, token));
            session.As<ISessionBindingProvider>()
                .Setup(value => value.CreateBindingAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(binding.Object);
            return binding;
        }
    }
}
