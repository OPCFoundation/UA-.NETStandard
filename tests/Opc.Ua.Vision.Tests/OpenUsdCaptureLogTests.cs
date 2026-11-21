// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Vision.OpenUsd;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Exercises the source-generated logging extension methods on
    /// <see cref="OpenUsdCaptureLog"/>. Each method must accept its
    /// declared arguments and dispatch to <see cref="ILogger"/> without
    /// throwing.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdCaptureLogTests
    {
        [Test]
        public void BackendSelectedDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.BackendSelected("D3D12", "WARP", isSoftware: true);

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void BackendUnavailableDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.BackendUnavailable("D3D12", "No device");

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void NoBackendAvailableDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.NoBackendAvailable("no gpu");

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void CaptureSucceededDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.CaptureSucceeded(1920, 1080, elapsedMs: 5, drawCount: 3,
                meshCount: 2, backendName: "D3D12");

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void BlankFrameDetectedDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.BlankFrameDetected(drawCount: 0, meshCount: 0, isUniform: true);

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void StageOpenFailedDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.StageOpenFailed("stage.usda", new InvalidOperationException("boom"));

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void CameraResolveFailedDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.CameraResolveFailed("/World/Cam", "stage.usda",
                new InvalidOperationException("boom"));

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void RenderFailedDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.RenderFailed("D3D12", new InvalidOperationException("boom"));

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void EncodingFailedDoesNotThrow()
        {
            var logger = new CapturingLogger();
            logger.EncodingFailed(1920, 1080, new InvalidOperationException("boom"));

            Assert.That(logger.Entries.Count, Is.GreaterThanOrEqualTo(1));
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, EventId Id, string Message)> Entries { get; } = [];

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, eventId, formatter(state, exception)));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
