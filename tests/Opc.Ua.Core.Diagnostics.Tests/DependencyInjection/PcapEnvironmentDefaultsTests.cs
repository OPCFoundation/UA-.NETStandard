/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Pcap.Tests.DependencyInjection
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
