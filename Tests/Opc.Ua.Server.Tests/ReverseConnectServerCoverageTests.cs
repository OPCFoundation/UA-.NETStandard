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
using System.Collections.ObjectModel;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Additional coverage for <see cref="ReverseConnectProperty"/> and
    /// <see cref="ReverseConnectServer.AddReverseConnection"/> option handling.
    /// </summary>
    [TestFixture]
    [Category("ReverseConnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReverseConnectServerCoverageTests
    {
        private static readonly Uri s_clientUrl = new("opc.tcp://localhost:4840");

        [Test]
        public void ReverseConnectPropertyStoresProvidedValues()
        {
            var property = new ReverseConnectProperty(
                s_clientUrl,
                timeout: 5000,
                maxSessionCount: 3,
                configEntry: true,
                enabled: true);

            Assert.Multiple(() =>
            {
                Assert.That(property.ClientUrl, Is.EqualTo(s_clientUrl));
                Assert.That(property.Timeout, Is.EqualTo(5000));
                Assert.That(property.MaxSessionCount, Is.EqualTo(3));
                Assert.That(property.ConfigEntry, Is.True);
                Assert.That(property.Enabled, Is.True);
                Assert.That(property.LastState, Is.EqualTo(ReverseConnectState.Closed));
            });
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-100)]
        public void ReverseConnectPropertyUsesDefaultTimeoutWhenNotPositive(int timeout)
        {
            var property = new ReverseConnectProperty(
                s_clientUrl,
                timeout,
                maxSessionCount: 0,
                configEntry: false);

            Assert.That(
                property.Timeout,
                Is.EqualTo(ReverseConnectServer.DefaultReverseConnectTimeout));
        }

        [Test]
        public void ReverseConnectPropertyEnabledDefaultsToTrue()
        {
            var property = new ReverseConnectProperty(
                s_clientUrl,
                timeout: 1000,
                maxSessionCount: 0,
                configEntry: false);

            Assert.That(property.Enabled, Is.True);
        }

        [Test]
        public void ReverseConnectPropertyCanBeDisabled()
        {
            var property = new ReverseConnectProperty(
                s_clientUrl,
                timeout: 1000,
                maxSessionCount: 0,
                configEntry: false,
                enabled: false);

            Assert.That(property.Enabled, Is.False);
        }

        [Test]
        public void ReverseConnectPropertyMutableFieldsCanBeUpdated()
        {
            var property = new ReverseConnectProperty(
                s_clientUrl,
                timeout: 1000,
                maxSessionCount: 0,
                configEntry: false);
            var rejectTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            property.LastState = ReverseConnectState.Rejected;
            property.MaxSessionCount = 7;
            property.Enabled = false;
            property.RejectTime = rejectTime;
            property.ServiceResult = new ServiceResult(StatusCodes.BadTimeout);

            Assert.Multiple(() =>
            {
                Assert.That(property.LastState, Is.EqualTo(ReverseConnectState.Rejected));
                Assert.That(property.MaxSessionCount, Is.EqualTo(7));
                Assert.That(property.Enabled, Is.False);
                Assert.That(property.RejectTime, Is.EqualTo(rejectTime));
                Assert.That(property.ServiceResult, Is.Not.Null);
                Assert.That(property.ServiceResult!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
            });
        }

        [TestCase(ReverseConnectState.Closed, 0)]
        [TestCase(ReverseConnectState.Connecting, 1)]
        [TestCase(ReverseConnectState.Connected, 2)]
        [TestCase(ReverseConnectState.Rejected, 3)]
        [TestCase(ReverseConnectState.Errored, 4)]
        public void ReverseConnectStateHasExpectedNumericValue(ReverseConnectState state, int expected)
        {
            Assert.That((int)state, Is.EqualTo(expected));
        }

        [Test]
        public void AddReverseConnectionUsesDefaultTimeoutWhenZero()
        {
            using var server = new ReverseConnectServer(NUnitTelemetryContext.Create());

            server.AddReverseConnection(s_clientUrl);

            ReadOnlyDictionary<Uri, ReverseConnectProperty> connections = server.GetReverseConnections();
            ReverseConnectProperty property = connections[s_clientUrl];
            Assert.Multiple(() =>
            {
                Assert.That(property.Timeout, Is.EqualTo(ReverseConnectServer.DefaultReverseConnectTimeout));
                Assert.That(property.MaxSessionCount, Is.Zero);
                Assert.That(property.Enabled, Is.True);
                Assert.That(property.ConfigEntry, Is.False);
            });
        }

        [Test]
        public void AddReverseConnectionReflectsProvidedOptions()
        {
            using var server = new ReverseConnectServer(NUnitTelemetryContext.Create());

            server.AddReverseConnection(
                s_clientUrl,
                timeout: 2500,
                maxSessionCount: 4,
                enabled: false);

            ReverseConnectProperty property = server.GetReverseConnections()[s_clientUrl];
            Assert.Multiple(() =>
            {
                Assert.That(property.Timeout, Is.EqualTo(2500));
                Assert.That(property.MaxSessionCount, Is.EqualTo(4));
                Assert.That(property.Enabled, Is.False);
            });
        }
    }
}
