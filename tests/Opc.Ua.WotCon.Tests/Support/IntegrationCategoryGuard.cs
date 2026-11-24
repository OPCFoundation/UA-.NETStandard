// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Keeps <c>[Category("Integration")]</c> meaning exactly "this test
    /// starts an OPC UA server", so <c>TestCategory!=Integration</c> stays a
    /// fast, server-free subset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A test that starts a server without the category fails. A passing test
    /// that carries the category but starts no server fails too. The category
    /// may sit on the test or on any enclosing fixture (a nested class counts
    /// as enclosed by its outer fixture, so a server-free test of an
    /// Integration fixture moves into a sibling <c>...ServerFreeTests</c> class).
    /// </para>
    /// <para>
    /// Every server start goes through <c>ServerBase.StartAsync</c>, which
    /// creates the request queue and with it the
    /// <c>opcua.server.request_queue.rejections</c> instrument. A
    /// <see cref="MeterListener"/> is told about a new instrument
    /// synchronously on the creating thread, so the NUnit execution context
    /// there names the test (or the fixture, for a one-time setup) that
    /// started the server, also when tests run in parallel. A client session
    /// in these tests always connects to a server the same test started.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class IntegrationCategoryGuardAttribute : Attribute, ITestAction
    {
        /// <summary>
        /// The category that marks a test starting a real server.
        /// </summary>
        public const string Category = "Integration";

        /// <inheritdoc/>
        public ActionTargets Targets => ActionTargets.Test;

        /// <inheritdoc/>
        public void BeforeTest(ITest test)
        {
            ServerStartRecorder.EnsureListening();
        }

        /// <inheritdoc/>
        public void AfterTest(ITest test)
        {
            bool startedServer = false;
            for (ITest? current = test; current != null; current = current.Parent)
            {
                if (ServerStartRecorder.StartedServer(current))
                {
                    startedServer = true;
                    break;
                }
            }
            bool categorized = HasCategory(test);
            if (startedServer && !categorized)
            {
                Assert.Fail(
                    $"{test.FullName} starts an OPC UA server but carries no " +
                    $"[Category(\"{Category}\")]. Add it to the test, or to its " +
                    "fixture when all of the fixture's tests start a server.");
            }
            if (!startedServer && categorized &&
                TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Passed)
            {
                Assert.Fail(
                    $"{test.FullName} carries [Category(\"{Category}\")] but " +
                    "starts no OPC UA server. Remove the category, or move the " +
                    "test into a sibling (not nested) fixture without it; a " +
                    "nested fixture inherits the outer fixture's category.");
            }
        }

        private static bool HasCategory(ITest test)
        {
            for (ITest? current = test; current != null; current = current.Parent)
            {
                // The indexer adds a missing key to the (shared) property bag,
                // which corrupts a fixture's bag under parallel tests.
                if (!current.Properties.TryGet(PropertyNames.Category, out IList? categories))
                {
                    continue;
                }
                foreach (object category in categories)
                {
                    if (Category.Equals(category as string, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static class ServerStartRecorder
        {
            public static void EnsureListening()
            {
                _ = s_listener;
            }

            public static bool StartedServer(ITest test)
            {
                return s_started.ContainsKey(test.Id);
            }

            private static MeterListener Create()
            {
                var listener = new MeterListener
                {
                    InstrumentPublished = (instrument, _) =>
                    {
                        if (instrument.Name == ServerStartInstrument &&
                            TestExecutionContext.CurrentContext.CurrentTest is ITest test)
                        {
                            s_started.TryAdd(test.Id, 0);
                        }
                    }
                };
                listener.Start();
                return listener;
            }

            private const string ServerStartInstrument = "opcua.server.request_queue.rejections";
            private static readonly ConcurrentDictionary<string, byte> s_started = new(StringComparer.Ordinal);
            private static readonly MeterListener s_listener = Create();
        }
    }
}
