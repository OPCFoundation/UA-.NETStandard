/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Copies of a <see cref="QueuedEvent"/> keep the handle and only keep the admission
    /// stamp while the field values are unchanged.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public sealed class QueuedEventTests
    {
        [Test]
        public void WithClientHandleKeepsHandleAndStamp()
        {
            object handle = new();
            object stamp = new();
            var queued = new QueuedEvent(new EventFieldList { ClientHandle = 1, EventFields = [Variant.From(5)] }, handle, stamp);

            QueuedEvent copy = queued.WithClientHandle(2);

            Assert.That(copy.ClientHandle, Is.EqualTo(2u));
            Assert.That(copy.EventFields, Is.EqualTo(queued.EventFields));
            Assert.That(copy.Handle, Is.SameAs(handle));
            Assert.That(copy.AdmissionStamp, Is.SameAs(stamp));
            Assert.That(queued.ClientHandle, Is.EqualTo(1u), "the original is unchanged");
        }

        [Test]
        public void WithEventFieldsKeepsHandleAndDropsStamp()
        {
            object handle = new();
            var queued = new QueuedEvent(new EventFieldList { ClientHandle = 1, EventFields = [Variant.From(5)] }, handle, new object());

            QueuedEvent copy = queued.WithEventFields([Variant.From(6), Variant.From(7)]);

            Assert.That(copy.ClientHandle, Is.EqualTo(1u));
            Assert.That(copy.EventFields.Count, Is.EqualTo(2));
            Assert.That(copy.EventFields[0], Is.EqualTo(Variant.From(6)));
            Assert.That(copy.Handle, Is.SameAs(handle));
            Assert.That(copy.AdmissionStamp, Is.Null);
            Assert.That(queued.EventFields.Count, Is.EqualTo(1), "the original is unchanged");
        }
    }
}
