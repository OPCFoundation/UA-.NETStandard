// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for Kubernetes readiness mapping.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesReadinessServerTests
    {
        [Test]
        public void HealthyServiceLevelIsReady()
        {
            Assert.That(KubernetesReadinessServer.IsReady(200, 200), Is.True);
            Assert.That(KubernetesReadinessServer.IsReady(255, 200), Is.True);
        }

        [Test]
        public void MaintenanceAndNoDataAreNotReady()
        {
            Assert.That(KubernetesReadinessServer.IsReady(0, 200), Is.False);
            Assert.That(KubernetesReadinessServer.IsReady(1, 200), Is.False);
        }
    }
}
