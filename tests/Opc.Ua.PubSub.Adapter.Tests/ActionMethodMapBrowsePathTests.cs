// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for browse-path overloads on <see cref="ActionMethodMap"/>.
    /// </summary>
    [TestFixture]
    public sealed class ActionMethodMapBrowsePathTests
    {
        [Test]
        public void AddBrowsePathOverloadStoresBrowsePathSentinels()
        {
            ActionMethodMap map = new ActionMethodMap().Add(
                "Reset",
                "/2:Demo",
                "/2:Demo/2:Reset");

            bool resolved = map.TryResolve(
                new PubSubActionTarget { ActionName = "Reset" },
                out ActionMethodBinding binding);

            Assert.That(resolved, Is.True);
            Assert.That(NodeBrowsePath.IsBrowsePath(binding.ObjectId), Is.True);
            Assert.That(NodeBrowsePath.IsBrowsePath(binding.MethodId), Is.True);
            Assert.That(binding.ObjectId.IdentifierAsString, Is.EqualTo("/2:Demo"));
            Assert.That(binding.MethodId.IdentifierAsString, Is.EqualTo("/2:Demo/2:Reset"));
        }
    }
}
