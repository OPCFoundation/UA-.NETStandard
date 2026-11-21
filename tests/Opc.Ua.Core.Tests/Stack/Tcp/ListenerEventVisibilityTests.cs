// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Tcp
{
    [TestFixture]
    public sealed class ListenerEventVisibilityTests
    {
        [Test]
        public void OnTokenActivatedEventCarriesEditorBrowsableNeverAttribute()
        {
            EventInfo? evt = typeof(TcpListenerChannel).GetEvent(
                "OnTokenActivated",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.That(evt, Is.Not.Null);
            var attr = evt!.GetCustomAttribute<EditorBrowsableAttribute>();
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr!.State, Is.EqualTo(EditorBrowsableState.Never));
        }
    }
}
