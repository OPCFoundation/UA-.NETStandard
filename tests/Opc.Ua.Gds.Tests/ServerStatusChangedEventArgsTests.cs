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

using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ServerStatusChangedEventArgs"/>, the payload
    /// the GDS and push configuration clients raise for every data change of
    /// the monitored <c>Server_ServerStatus</c> variable.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerStatusChangedEventArgsTests
    {
        [Test]
        public void DecodesTheServerStatusBody()
        {
            var status = new ServerStatusDataType
            {
                State = ServerState.Running,
                SecondsTillShutdown = 42
            };

            var args = new ServerStatusChangedEventArgs(
                new DataValue(new ExtensionObject(status)));

            Assert.That(args.Status, Is.Not.Null);
            Assert.That(args.Status.State, Is.EqualTo(ServerState.Running));
            Assert.That(args.Status.SecondsTillShutdown, Is.EqualTo(42));
        }

        [Test]
        public void KeepsTheRawNotification()
        {
            var value = new DataValue(
                new Variant(new ExtensionObject(new ServerStatusDataType())),
                StatusCodes.UncertainLastUsableValue);

            var args = new ServerStatusChangedEventArgs(value);

            Assert.That(args.Value.StatusCode, Is.EqualTo(
                (StatusCode)StatusCodes.UncertainLastUsableValue));
        }

        [Test]
        public void LeavesStatusNullForABadNotification()
        {
            // A bad status code carries no usable body, so consumers must be
            // able to tell the difference without inspecting the variant.
            var args = new ServerStatusChangedEventArgs(
                DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));

            Assert.That(args.Status, Is.Null);
            Assert.That(
                StatusCode.IsBad(args.Value.StatusCode),
                Is.True);
        }
    }
}
