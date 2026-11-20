// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the validation of the LADS server options.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Parallelizable]
    public sealed class LadsServerOptionsTests
    {
        [Test]
        public void DefaultsAreValid()
        {
            var options = new LadsServerOptions();

            Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:opcua-netstandard:lads:instances"));
            Assert.That(options.MaxResultFileBytes, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(options.MaxRetainedResults, Is.EqualTo(100));
            Assert.That(options.ConfirmInterruptedDeviceReadinessAsync, Is.Null);
            Assert.That(options.RequiredMachineryParts, Is.EqualTo(MachineryParts.BuildingBlocks));
            Assert.That(options.Validate, Throws.Nothing);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveMaxResultFileBytesIsRejected(int value)
        {
            var options = new LadsServerOptions { MaxResultFileBytes = value };

            Assert.That(options.Validate, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void NonPositiveMaxRetainedResultsIsRejected(int value)
        {
            var options = new LadsServerOptions { MaxRetainedResults = value };

            Assert.That(options.Validate, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [TestCase("")]
        [TestCase("relative/path")]
        public void RelativeInstanceNamespaceIsRejected(string uri)
        {
            var options = new LadsServerOptions { InstanceNamespaceUri = uri };

            Assert.That(options.Validate, Throws.ArgumentException);
        }

        [TestCase(Namespaces.LADS)]
        [TestCase(Opc.Ua.Machinery.Namespaces.Machinery)]
        [TestCase(Opc.Ua.Di.Namespaces.OpcUaDi)]
        [TestCase(Opc.Ua.Namespaces.OpcUa)]
        public void ModelNamespaceIsRejected(string uri)
        {
            var options = new LadsServerOptions { InstanceNamespaceUri = uri };

            Assert.That(options.Validate, Throws.ArgumentException);
        }

        [Test]
        public void CustomNamespaceIsAccepted()
        {
            var options = new LadsServerOptions
            {
                InstanceNamespaceUri = "urn:example:incubators",
                MaxResultFileBytes = 1,
                MaxRetainedResults = 1
            };

            Assert.That(options.Validate, Throws.Nothing);
        }

        /// <summary>
        /// Direct option composition retains the injected asynchronous readiness callback.
        /// </summary>
        [Test]
        public void CopyPreservesReadinessCallback()
        {
            var source = new LadsServerOptions
            {
                ConfirmInterruptedDeviceReadinessAsync = (_, _) =>
                    new System.Threading.Tasks.ValueTask<bool>(false)
            };
            var copy = new LadsServerOptions();
            source.CopyTo(copy);
            Assert.That(copy.ConfirmInterruptedDeviceReadinessAsync,
                Is.SameAs(source.ConfirmInterruptedDeviceReadinessAsync));
        }
    }
}
