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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [NonParallelizable]
    public sealed partial class TraceLoggerCategoryRegressionTests
    {
        [TestCase(false, Utils.TraceMasks.Security)]
        [TestCase(true, Utils.TraceMasks.Security)]
        [TestCase(false, Utils.TraceMasks.ServiceDetail)]
        [TestCase(true, Utils.TraceMasks.OperationDetail)]
        [TestCase(false, Utils.TraceMasks.Security | Utils.TraceMasks.ServiceDetail)]
        [TestCase(true, Utils.TraceMasks.Security | Utils.TraceMasks.ServiceDetail)]
        [TestCase(false, Utils.TraceMasks.Information)]
        [TestCase(true, Utils.TraceMasks.Error)]
        [TestCase(false, Utils.TraceMasks.All)]
        [TestCase(true, Utils.TraceMasks.None)]
        public void LegacyMaskApiPreservesFilteredHandlerMask(bool useTrace, int traceMask)
        {
            int originalMask = Utils.LoggerProvider.TraceMask;
            int handlerMask = traceMask;
            var received = new List<TraceEventArgs>();
            void OnTrace(object? sender, TraceEventArgs args)
            {
                if (args.Format == "legacy category {0}" &&
                    (handlerMask == Utils.TraceMasks.None || (args.TraceMask & handlerMask) != 0))
                {
                    received.Add(args);
                }
            }
            Utils.LoggerProvider.SetTraceMask(Utils.TraceMasks.None);
            Tracing.Instance.TraceEventHandler += OnTrace;
            try
            {
#pragma warning disable CS0618 // Pin legacy API compatibility. TODO: remove when the legacy APIs are removed.
                if (useTrace)
                {
                    Utils.Trace(traceMask, "legacy category {0}", 739);
                }
                else
                {
                    Utils.Log(traceMask, "legacy category {0}", 739);
                }
#pragma warning restore CS0618

                Assert.That(received, Has.Count.EqualTo(1));
                Assert.That(received[0].TraceMask, Is.EqualTo(handlerMask));
                Assert.That(received[0].Arguments, Has.Length.EqualTo(1));
                Assert.That(received[0].Arguments[0], Is.EqualTo(739));
            }
            finally
            {
                Tracing.Instance.TraceEventHandler -= OnTrace;
                Utils.LoggerProvider.SetTraceMask(originalMask);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacyHandledMaskDoesNotDuplicateTraceEvents(bool handled)
        {
            int originalMask = Utils.LoggerProvider.TraceMask;
            var received = new List<TraceEventArgs>();
            var exception = new InvalidOperationException("legacy exception");
            void OnTrace(object? sender, TraceEventArgs args)
            {
                if (args.Format == "handled category {0}")
                {
                    received.Add(args);
                }
            }
            Utils.LoggerProvider.SetTraceMask(Utils.TraceMasks.None);
            Tracing.Instance.TraceEventHandler += OnTrace;
            try
            {
#pragma warning disable CS0618 // Pin legacy API compatibility. TODO: remove when the legacy APIs are removed.
                Utils.Trace(exception, Utils.TraceMasks.Security, "handled category {0}", handled, 731);
#pragma warning restore CS0618

                Assert.That(received, Has.Count.EqualTo(handled ? 0 : 1));
                if (!handled)
                {
                    Assert.That(received[0].TraceMask, Is.EqualTo(Utils.TraceMasks.Security));
                    Assert.That(received[0].Exception, Is.SameAs(exception));
                    Assert.That(received[0].Arguments[0], Is.EqualTo(731));
                }
            }
            finally
            {
                Tracing.Instance.TraceEventHandler -= OnTrace;
                Utils.LoggerProvider.SetTraceMask(originalMask);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedEventIdsUseLevelRatherThanCategoryBits(bool classOffset)
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-category-" + Guid.NewGuid().ToString("N") + ".log");
            using var provider = new TraceLoggerProvider();
            int handlerMask = classOffset ? Utils.TraceMasks.Error : Utils.TraceMasks.Information;
            var received = new List<TraceEventArgs>();
            void OnTrace(object? sender, TraceEventArgs args)
            {
                if (args.Format.Contains("generated category 739", StringComparison.Ordinal) &&
                    (args.TraceMask & handlerMask) != 0)
                {
                    received.Add(args);
                }
            }
            provider.SetTraceOutput(Utils.TraceOutput.FileOnly);
            provider.SetTraceMask(handlerMask);
            provider.SetTraceLog(path, deleteExisting: true);
            provider.Tracing.TraceEventHandler += OnTrace;
            try
            {
                ILogger logger = provider.CreateLogger("category-regression");
                if (classOffset)
                {
                    GeneratedClassOffsetError(logger, 739);
                }
                else
                {
                    GeneratedSecurityBitInformation(logger, 739);
                }

                Assert.That(received, Has.Count.EqualTo(1));
                Assert.That(received[0].TraceMask, Is.EqualTo(handlerMask));
                Assert.That(File.ReadAllText(path), Does.Contain("generated category 739"));
            }
            finally
            {
                provider.Tracing.TraceEventHandler -= OnTrace;
                provider.SetTraceLog(string.Empty, deleteExisting: false);
                File.Delete(path);
            }
        }

        [TestCase(Utils.TraceMasks.ServiceDetail, "ServiceDetail")]
        [TestCase(Utils.TraceMasks.Security, "Security")]
        [TestCase(Utils.TraceMasks.Error, "Error")]
        [TestCase(Utils.TraceMasks.Information, "Information")]
        [TestCase(Utils.TraceMasks.StackTrace, "StackTrace")]
        [TestCase(Utils.TraceMasks.Service, "Service")]
        [TestCase(Utils.TraceMasks.Operation, "Operation")]
        [TestCase(Utils.TraceMasks.OperationDetail, "OperationDetail")]
        [TestCase(Utils.TraceMasks.StartStop, "StartStop")]
        [TestCase(Utils.TraceMasks.ExternalSystem, "ExternalSystem")]
        [TestCase(Utils.TraceMasks.None, "None")]
        [TestCase(Utils.TraceMasks.All, "All")]
        public void NamedLegacyCategoryOverridesTheLogLevel(int traceMask, string category)
        {
            Assert.That(
                TraceLoggerProvider.GetTraceMask(new EventId(traceMask, category), LogLevel.Information),
                Is.EqualTo(traceMask));
        }

        [TestCase(Utils.TraceMasks.Security, null)]
        [TestCase(Utils.TraceMasks.Security, "GeneratedInformation")]
        [TestCase(Utils.TraceMasks.ServiceDetail, "Security")]
        [TestCase(CoreEventIds.TcpServerChannel + 18, "ServiceDetail")]
        [TestCase(-1, "ServiceDetail")]
        public void NumericEventIdsWithoutMatchingLegacyCategoryUseLogLevel(int eventId, string? name)
        {
            Assert.That(
                TraceLoggerProvider.GetTraceMask(new EventId(eventId, name), LogLevel.Information),
                Is.EqualTo(Utils.TraceMasks.Information));
        }

        [LoggerMessage(EventId = Utils.TraceMasks.Security, Level = LogLevel.Information,
            Message = "generated category {Value}")]
        private static partial void GeneratedSecurityBitInformation(ILogger logger, int value);

        [LoggerMessage(EventId = CoreEventIds.TcpServerChannel + 18, Level = LogLevel.Error,
            Message = "generated category {Value}")]
        private static partial void GeneratedClassOffsetError(ILogger logger, int value);
    }
}
