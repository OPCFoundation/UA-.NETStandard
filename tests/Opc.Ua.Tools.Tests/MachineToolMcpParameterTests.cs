// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
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
