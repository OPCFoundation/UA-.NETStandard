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
