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

#if NET10_0
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class MethodServiceToolsTests
    {
        private const string kUnknownSessionName = "definitely-not-connected";

        [Test]
        public async Task CallAsyncWithValidSessionReturnsStatusJsonAsync()
        {
            string objectId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string methodId = MethodIds.Server_GetMonitoredItems.ToString(null, CultureInfo.InvariantCulture);
            string[] inputArguments = ["0"];

            string json = await MethodServiceTools.CallAsync(
                McpTestEnvironment.SessionManager,
                objectId,
                methodId,
                inputArguments,
                McpTestEnvironment.SessionName).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(json, Is.Not.EqualTo(string.Empty));
            Assert.That(
                document.RootElement.TryGetProperty("statusCode", out JsonElement statusCode),
                Is.True);
            Assert.That(statusCode.GetString(), Is.Not.Null.And.Not.EqualTo(string.Empty));

            if (document.RootElement.TryGetProperty("responseHeader", out JsonElement responseHeader))
            {
                Assert.That(responseHeader.ValueKind, Is.EqualTo(JsonValueKind.Object));
            }
            else
            {
                Assert.That(
                    document.RootElement.TryGetProperty("error", out JsonElement error),
                    Is.True);
                Assert.That(error.GetBoolean(), Is.True);
            }
        }

        [Test]
        public void CallAsyncWithUnknownSessionThrowsInvalidOperationException()
        {
            string objectId = ObjectIds.Server.ToString(null, CultureInfo.InvariantCulture);
            string methodId = MethodIds.Server_GetMonitoredItems.ToString(null, CultureInfo.InvariantCulture);
            string[] inputArguments = ["0"];

            Assert.That(
                () => MethodServiceTools.CallAsync(
                    McpTestEnvironment.SessionManager,
                    objectId,
                    methodId,
                    inputArguments,
                    kUnknownSessionName),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contain("not found or not connected"));
        }
    }
}
#endif
