// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Api.Tests
{
    /// <summary>
    /// The generation entry points observe <see cref="GeneratorOptions.Cancellation"/>,
    /// so a cancelled IDE pass stops instead of generating the whole model.
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class GeneratorsCancellationTests
    {
        [Test]
        public void GenerateCodeCancelledTokenThrowsAndEmitsNothing()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            Assert.That(
                () => new DesignFileCollection
                {
                    Targets = [Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "Resources",
                        "TestDataDesign.xml")],
                    IdentifierFilePath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "Resources",
                        "TestDataDesign.csv"),
                    Options = new DesignFileOptions()
                }.GenerateCode(fileSystem, string.Empty, telemetry, new GeneratorOptions
                {
                    Cancellation = new CancellationToken(canceled: true)
                }),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(fileSystem.CreatedFiles, Is.Empty);
        }

        [Test]
        public void GenerateStackCancelledTokenThrowsAndEmitsNothing()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            using var fileSystem = new VirtualFileSystem();

            Assert.That(
                () => Generators.GenerateStack(
                    StackGenerationType.Stack | StackGenerationType.Models,
                    fileSystem,
                    string.Empty,
                    telemetry,
                    new GeneratorOptions
                    {
                        Cancellation = new CancellationToken(canceled: true)
                    }),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(fileSystem.CreatedFiles, Is.Empty);
        }
    }
}
