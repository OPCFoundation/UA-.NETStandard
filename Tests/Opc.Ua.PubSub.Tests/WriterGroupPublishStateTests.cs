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

using NUnit.Framework;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WriterGroupPublishStateTests
    {
        /// <summary>
        /// Tests HasMetaDataChanged returns false for null metadata
        /// </summary>
        [Test]
        public void HasMetaDataChangedReturnsFalseForNullMetadata()
        {
            var state = new WriterGroupPublishState();
            var writer = new DataSetWriterDataType { Enabled = true, DataSetWriterId = 1 };

            bool result = state.HasMetaDataChanged(writer, null);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests ExcludeUnchangedFields returns dataset on first call
        /// </summary>
        [Test]
        public void ExcludeUnchangedFieldsReturnsDataSetOnFirstCall()
        {
            var state = new WriterGroupPublishState();
            var writer = new DataSetWriterDataType { Enabled = true, DataSetWriterId = 1 };

            var dataset = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42)) },
                    new Field { Value = new DataValue(new Variant("hello")) }
                ]
            };

            DataSet result = state.ExcludeUnchangedFields(writer, dataset);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(dataset));
        }

        /// <summary>
        /// Tests ExcludeUnchangedFields returns null when no fields changed
        /// </summary>
        [Test]
        public void ExcludeUnchangedFieldsReturnsNullWhenNoChange()
        {
            var state = new WriterGroupPublishState();
            var writer = new DataSetWriterDataType { Enabled = true, DataSetWriterId = 1 };

            var dataset1 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Good) },
                    new Field { Value = new DataValue(new Variant("hello"), StatusCodes.Good) }
                ]
            };

            state.ExcludeUnchangedFields(writer, dataset1);

            var dataset2 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Good) },
                    new Field { Value = new DataValue(new Variant("hello"), StatusCodes.Good) }
                ]
            };

            DataSet result = state.ExcludeUnchangedFields(writer, dataset2);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests ExcludeUnchangedFields detects value change
        /// </summary>
        [Test]
        public void ExcludeUnchangedFieldsDetectsValueChange()
        {
            var state = new WriterGroupPublishState();
            var writer = new DataSetWriterDataType { Enabled = true, DataSetWriterId = 1 };

            var dataset1 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Good) },
                    new Field { Value = new DataValue(new Variant("hello"), StatusCodes.Good) }
                ]
            };

            state.ExcludeUnchangedFields(writer, dataset1);

            var dataset2 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Good) },
                    new Field { Value = new DataValue(new Variant("changed"), StatusCodes.Good) }
                ]
            };

            DataSet result = state.ExcludeUnchangedFields(writer, dataset2);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields[0], Is.Null, "Unchanged field should be nulled");
            Assert.That(result.Fields[1], Is.Not.Null, "Changed field should be kept");
        }

        /// <summary>
        /// Tests ExcludeUnchangedFields detects status code change
        /// </summary>
        [Test]
        public void ExcludeUnchangedFieldsDetectsStatusCodeChange()
        {
            var state = new WriterGroupPublishState();
            var writer = new DataSetWriterDataType { Enabled = true, DataSetWriterId = 1 };

            var dataset1 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Good) }
                ]
            };

            state.ExcludeUnchangedFields(writer, dataset1);

            var dataset2 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Bad) }
                ]
            };

            DataSet result = state.ExcludeUnchangedFields(writer, dataset2);

            Assert.That(result, Is.Not.Null);
        }

        /// <summary>
        /// Tests ExcludeUnchangedFields handles null field in second dataset
        /// </summary>
        [Test]
        public void ExcludeUnchangedFieldsHandlesNullFieldInSecondDataSet()
        {
            var state = new WriterGroupPublishState();
            var writer = new DataSetWriterDataType { Enabled = true, DataSetWriterId = 1 };

            var dataset1 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Good) },
                    new Field { Value = new DataValue(new Variant(99), StatusCodes.Good) }
                ]
            };

            state.ExcludeUnchangedFields(writer, dataset1);

            var dataset2 = new DataSet("Test")
            {
                Fields =
                [
                    null,
                    new Field { Value = new DataValue(new Variant(99), StatusCodes.Good) }
                ]
            };

            DataSet result = state.ExcludeUnchangedFields(writer, dataset2);

            Assert.That(result, Is.Not.Null);
        }

        /// <summary>
        /// Tests ExcludeUnchangedFields handles null field in first (last) dataset
        /// </summary>
        [Test]
        public void ExcludeUnchangedFieldsHandlesNullFieldInLastDataSet()
        {
            var state = new WriterGroupPublishState();
            var writer = new DataSetWriterDataType { Enabled = true, DataSetWriterId = 1 };

            var dataset1 = new DataSet("Test")
            {
                Fields =
                [
                    null,
                    new Field { Value = new DataValue(new Variant(99), StatusCodes.Good) }
                ]
            };

            state.ExcludeUnchangedFields(writer, dataset1);

            var dataset2 = new DataSet("Test")
            {
                Fields =
                [
                    new Field { Value = new DataValue(new Variant(42), StatusCodes.Good) },
                    new Field { Value = new DataValue(new Variant(99), StatusCodes.Good) }
                ]
            };

            DataSet result = state.ExcludeUnchangedFields(writer, dataset2);

            Assert.That(result, Is.Not.Null);
        }
    }
}