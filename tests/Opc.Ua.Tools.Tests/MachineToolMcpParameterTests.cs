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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Pins validation at the public tool boundary before session access or device operations.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class MachineToolMcpParameterTests
    {
        /// <summary>
        /// JSON escaping cannot expand a rejected identifier's diagnostic beyond the smallest allowed reply.
        /// </summary>
        [TestCase('\u2603')]
        [TestCase('\u0001')]
        public async Task InvalidUnicodeIdentifierKeepsTheExactErrorWithinTheResponseBudgetAsync(char character)
        {
            CallToolResult result = await MachineToolTools.ReadMachineAsync(
                null!, "i=" + new string(character, 512), maximumResponseBytes: 1024).ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            string text = result.Content.OfType<TextContentBlock>().Single().Text;
            Assert.That(Encoding.UTF8.GetByteCount(text), Is.LessThanOrEqualTo(1024));
            using JsonDocument json = JsonDocument.Parse(text);
            Assert.That(json.RootElement.GetProperty("statusCode").GetString(),
                Does.Contain("BadNodeIdInvalid"));
        }

        /// <summary>
        /// An invalid inventory bound is rejected before obtaining a client or performing a browse.
        /// </summary>
        [TestCase(0)]
        [TestCase(101)]
        public async Task InvalidInventoryBoundFailsBeforeSessionAccess(int maximumResults)
        {
            CallToolResult result = await MachineToolTools.ListDevicesAsync(
                null!, maximumResults: maximumResults).ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            using JsonDocument json = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
            Assert.That(json.RootElement.GetProperty("statusCode").GetString(), Does.Contain("BadOutOfRange"));
        }

        /// <summary>
        /// A null node is rejected at every passive machine workflow instead of becoming a root-wide operation.
        /// </summary>
        [TestCase("snapshot")]
        [TestCase("equipment")]
        [TestCase("production")]
        [TestCase("prognoses")]
        [TestCase("events")]
        public async Task NullMachineCannotReachASession(string workflow)
        {
            CallToolResult result = workflow switch
            {
                "snapshot" => await MachineToolTools.ReadMachineAsync(null!, "i=0").ConfigureAwait(false),
                "equipment" => await MachineToolTools.ReadEquipmentAsync(null!, "i=0").ConfigureAwait(false),
                "production" => await MachineToolTools.ReadProductionAsync(null!, "i=0").ConfigureAwait(false),
                "prognoses" => await MachineToolTools.ReadPrognosesAsync(null!, "i=0").ConfigureAwait(false),
                _ => await MachineToolTools.ObserveEventsAsync(null!, "i=0").ConfigureAwait(false)
            };
            Assert.That(result.IsError, Is.True);
            using JsonDocument json = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
            Assert.That(json.RootElement.GetProperty("statusCode").GetString(), Does.Contain("BadInvalidArgument"));
        }
    }
}
#endif
