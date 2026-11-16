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
