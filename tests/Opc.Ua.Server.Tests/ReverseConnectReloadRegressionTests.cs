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
using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ReverseConnect")]
    public sealed class ReverseConnectReloadRegressionTests
    {
        [Test]
        public void ReloadRemovesEveryConfiguredEntryAndPreservesDynamicConnections()
        {
            using var server = new ReverseConnectServer(NUnitTelemetryContext.Create(), new FakeTimeProvider());
            var dynamicUrl = new Uri("opc.tcp://localhost:4840/dynamic");
            server.AddReverseConnection(dynamicUrl, timeout: 1234, maxSessionCount: 2, enabled: false);
            MethodInfo method = typeof(ReverseConnectServer).GetMethod(
                "UpdateConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!;
#if NET5_0_OR_GREATER
            Action<ApplicationConfiguration> update = method.CreateDelegate<Action<ApplicationConfiguration>>(server);
#else
            var update = (Action<ApplicationConfiguration>)method.CreateDelegate(
                typeof(Action<ApplicationConfiguration>), server);
#endif
            update(CreateConfiguration("first", "second", "third"));
            Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(4));
            update(CreateConfiguration("replacement"));
            Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(2));
            Assert.That(server.GetReverseConnections().ContainsKey(new Uri("opc.tcp://localhost:4840/replacement")),
                Is.True);
            update(CreateConfiguration());
            Assert.That(server.GetReverseConnections(), Has.Count.EqualTo(1));
            ReverseConnectProperty retained = server.GetReverseConnections()[dynamicUrl];
            Assert.That(retained.ConfigEntry, Is.False);
            Assert.That(retained.Timeout, Is.EqualTo(1234));
            Assert.That(retained.MaxSessionCount, Is.EqualTo(2));
            Assert.That(retained.Enabled, Is.False);
        }

        private static ApplicationConfiguration CreateConfiguration(params string[] names)
        {
            var clients = new ReverseConnectClient[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                clients[i] = new ReverseConnectClient
                {
                    EndpointUrl = "opc.tcp://localhost:4840/" + names[i],
                    Enabled = false
                };
            }
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    ReverseConnect = new ReverseConnectServerConfiguration
                    {
                        Clients = clients
                    }
                }
            };
        }
    }
}
