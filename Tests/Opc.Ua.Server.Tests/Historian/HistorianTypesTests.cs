/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Tests for the small DTO / record / option types in the historian area.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianTypesTests
    {
        [Test]
        public void ResumeTokenDefaultIsEmpty()
        {
            var token = default(HistorianResumeToken);

            Assert.That(token.IsEmpty, Is.True);
            Assert.That(token.State.IsEmpty, Is.True);
        }

        [Test]
        public void ResumeTokenWithBytesIsNotEmpty()
        {
            var token = new HistorianResumeToken(new byte[] { 0xCA, 0xFE });

            Assert.That(token.IsEmpty, Is.False);
            Assert.That(token.State.Length, Is.EqualTo(2));
        }

        [Test]
        public void ResumeTokenRecordEqualityComparesByState()
        {
            byte[] data = [1, 2, 3];
            var a = new HistorianResumeToken(data.AsMemory());
            var b = new HistorianResumeToken(data.AsMemory());

            // record struct uses default memory equality (same underlying array + range)
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

            // a token from a different array (even with same content) is NOT equal
            // because ReadOnlyMemory equality is referential
            var c = new HistorianResumeToken(new byte[] { 1, 2, 3 });
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void PageEmptyIsFinalWithNoValues()
        {
            var page = HistorianPage<HistoricalDataValue>.Empty;

            Assert.That(page.Values, Has.Count.EqualTo(0));
            Assert.That(page.IsFinal, Is.True);
            Assert.That(page.NextToken.IsEmpty, Is.True);
        }

        [Test]
        public void PageWithNextTokenIsNotFinal()
        {
            var token = new HistorianResumeToken(new byte[] { 0x01 });
            var dv = new DataValue(new Variant(42), StatusCodes.Good, DateTime.UtcNow);
            var page = new HistorianPage<HistoricalDataValue>(
                [new HistoricalDataValue(dv)],
                token);

            Assert.That(page.IsFinal, Is.False);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.NextToken.IsEmpty, Is.False);
        }

        [Test]
        public void NodeCapabilitiesReadOnlyHasNoUpdateFlags()
        {
            var caps = HistorianNodeCapabilities.ReadOnly;

            Assert.That(caps.ReadRawData, Is.True);
            Assert.That(caps.ReadModifiedData, Is.True);
            Assert.That(caps.ReadAtTime, Is.True);
            Assert.That(caps.ReadProcessedData, Is.True);
            Assert.That(caps.SupportsAnyUpdate, Is.False);
        }

        [Test]
        public void NodeCapabilitiesReadWriteHasAllUpdateFlags()
        {
            var caps = HistorianNodeCapabilities.ReadWrite;

            Assert.That(caps.InsertData, Is.True);
            Assert.That(caps.ReplaceData, Is.True);
            Assert.That(caps.UpdateData, Is.True);
            Assert.That(caps.DeleteRaw, Is.True);
            Assert.That(caps.DeleteAtTime, Is.True);
            Assert.That(caps.InsertAnnotation, Is.True);
            Assert.That(caps.ServerTimestampSupported, Is.True);
            Assert.That(caps.SupportsAnyUpdate, Is.True);
        }

        [Test]
        public void NodeCapabilitiesSupportsAnyUpdateIsTrueForSingleFlag()
        {
            // Each individual update flag should make SupportsAnyUpdate true
            Assert.That(new HistorianNodeCapabilities { InsertData = true }.SupportsAnyUpdate, Is.True);
            Assert.That(new HistorianNodeCapabilities { ReplaceData = true }.SupportsAnyUpdate, Is.True);
            Assert.That(new HistorianNodeCapabilities { UpdateData = true }.SupportsAnyUpdate, Is.True);
            Assert.That(new HistorianNodeCapabilities { DeleteRaw = true }.SupportsAnyUpdate, Is.True);
            Assert.That(new HistorianNodeCapabilities { DeleteAtTime = true }.SupportsAnyUpdate, Is.True);
            Assert.That(new HistorianNodeCapabilities { InsertAnnotation = true }.SupportsAnyUpdate, Is.True);

            // Purely read-only (no write flags) should be false
            Assert.That(new HistorianNodeCapabilities().SupportsAnyUpdate, Is.False);
        }
    }
}
