// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ServerEventIds"/>.
    /// </summary>
    [TestFixture]
    public class EventIdsTests
    {
        /// <summary>
        /// Every per-class event id offset in <see cref="ServerEventIds"/> must be unique,
        /// otherwise the ids emitted by two different <c>&lt;ClassName&gt;Log</c> classes
        /// collide and can no longer be attributed to a single log message.
        /// </summary>
        [Test]
        public void OffsetsAreDistinct()
        {
            FieldInfo[] fields = typeof(ServerEventIds).GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            var offsets = fields
                .Where(f => f.IsLiteral && f.FieldType == typeof(int))
                .Select(f => (Name: f.Name, Offset: (int)f.GetRawConstantValue()!))
                .ToList();

            Assert.That(offsets, Is.Not.Empty);

            var duplicates = offsets
                .GroupBy(f => f.Offset)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(f => f.Name))}")
                .ToList();

            Assert.That(duplicates, Is.Empty,
                $"Duplicate ServerEventIds offsets found: {string.Join("; ", duplicates)}");
        }
    }
}
