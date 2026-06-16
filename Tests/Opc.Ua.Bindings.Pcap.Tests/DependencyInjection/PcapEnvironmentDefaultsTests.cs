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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.DependencyInjection;

namespace Opc.Ua.Bindings.Pcap.Tests.DependencyInjection
{
    /// <summary>
    /// Tests for the env-var snapshot helper that drives the env-var
    /// auto-start hosted service.
    /// </summary>
    [TestFixture]
    public sealed class PcapEnvironmentDefaultsTests
    {
        [Test]
        public void NoVariablesSetProducesEmptySnapshot()
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);

            PcapEnvironmentSnapshot snapshot = PcapEnvironmentDefaults
                .ReadFromEnvironment(name => values.TryGetValue(name, out string? v) ? v : null);

            Assert.That(snapshot.PcapFilePath, Is.Null);
            Assert.That(snapshot.KeyLogFilePath, Is.Null);
            Assert.That(snapshot.HasAny, Is.False);
            Assert.That(snapshot.IsKeyLogOnly, Is.False);
        }

        [Test]
        public void OnlyPcapFileSet()
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [PcapEnvironmentVariableNames.OpcuaPcapFile] = "/tmp/cap.pcap"
            };

            PcapEnvironmentSnapshot snapshot = PcapEnvironmentDefaults
                .ReadFromEnvironment(name => values.TryGetValue(name, out string? v) ? v : null);

            Assert.That(snapshot.PcapFilePath, Is.EqualTo("/tmp/cap.pcap"));
            Assert.That(snapshot.KeyLogFilePath, Is.Null);
            Assert.That(snapshot.HasAny, Is.True);
            Assert.That(snapshot.IsKeyLogOnly, Is.False);
        }

        [Test]
        public void OnlyKeyLogFileSet()
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [PcapEnvironmentVariableNames.OpcuaKeyLogFile] = "/tmp/keys.uakeys.json"
            };

            PcapEnvironmentSnapshot snapshot = PcapEnvironmentDefaults
                .ReadFromEnvironment(name => values.TryGetValue(name, out string? v) ? v : null);

            Assert.That(snapshot.PcapFilePath, Is.Null);
            Assert.That(snapshot.KeyLogFilePath, Is.EqualTo("/tmp/keys.uakeys.json"));
            Assert.That(snapshot.HasAny, Is.True);
            Assert.That(snapshot.IsKeyLogOnly, Is.True);
        }

        [Test]
        public void BothVariablesSet()
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [PcapEnvironmentVariableNames.OpcuaPcapFile] = "/tmp/cap.pcap",
                [PcapEnvironmentVariableNames.OpcuaKeyLogFile] = "/tmp/keys.uakeys.json"
            };

            PcapEnvironmentSnapshot snapshot = PcapEnvironmentDefaults
                .ReadFromEnvironment(name => values.TryGetValue(name, out string? v) ? v : null);

            Assert.That(snapshot.PcapFilePath, Is.EqualTo("/tmp/cap.pcap"));
            Assert.That(snapshot.KeyLogFilePath, Is.EqualTo("/tmp/keys.uakeys.json"));
            Assert.That(snapshot.HasAny, Is.True);
            Assert.That(snapshot.IsKeyLogOnly, Is.False);
        }

        [Test]
        public void WhitespaceValueIsTreatedAsUnset()
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [PcapEnvironmentVariableNames.OpcuaPcapFile] = "   ",
                [PcapEnvironmentVariableNames.OpcuaKeyLogFile] = string.Empty
            };

            PcapEnvironmentSnapshot snapshot = PcapEnvironmentDefaults
                .ReadFromEnvironment(name => values.TryGetValue(name, out string? v) ? v : null);

            Assert.That(snapshot.PcapFilePath, Is.Null);
            Assert.That(snapshot.KeyLogFilePath, Is.Null);
            Assert.That(snapshot.HasAny, Is.False);
        }

        [Test]
        public void NullValueIsTreatedAsUnset()
        {
            PcapEnvironmentSnapshot snapshot = PcapEnvironmentDefaults
                .ReadFromEnvironment(static _ => null);

            Assert.That(snapshot.PcapFilePath, Is.Null);
            Assert.That(snapshot.KeyLogFilePath, Is.Null);
            Assert.That(snapshot.HasAny, Is.False);
        }

        [Test]
        public void ReadFromEnvironmentThrowsOnNullDelegate()
        {
            Func<string, string?>? lookup = null;
            Assert.That(
                () => PcapEnvironmentDefaults.ReadFromEnvironment(lookup!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstantNamesMatchSpec()
        {
            // Operators rely on these exact spellings - regression guard.
            Assert.That(PcapEnvironmentVariableNames.OpcuaPcapFile, Is.EqualTo("OPCUA_PCAP_FILE"));
            Assert.That(PcapEnvironmentVariableNames.OpcuaKeyLogFile, Is.EqualTo("OPCUA_KEYLOGFILE"));
        }
    }
}
