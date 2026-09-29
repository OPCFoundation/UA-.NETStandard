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

using System;
using System.Text;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class RegistryMetadataCommitTests
    {
        [Test]
        public void ExactChangesAdvanceOnlyTheAffectedResourceGroupAndRegistry()
        {
            RegistryObjectValueDataType source = Document();
            var request = new RegistryChangeRequestDataType
            {
                TargetXid = "/messagegroups/g/messages/m",
                ExpectedEpoch = 3,
                Changes = [Set("x-number", "1.0")]
            };
            RegistryMetadataCommit result = RegistryMetadataMutation.Apply(
                source, request, ["messagegroups"], [], _ => { });
            Assert.That(result.Changed, Is.True);
            Assert.That(result.TargetEpoch, Is.EqualTo(4));
            string expected = "{\"epoch\":8,\"messagegroups\":{\"g\":{\"epoch\":3,\"messages\":{" +
                "\"m\":{\"epoch\":4,\"messageid\":\"m\",\"versionid\":\"1\",\"x-number\":1.0}," +
                "\"other\":{\"epoch\":2}}}}}";
            Assert.That(RegistryValues.Identical(result.Document,
                RegistryValues.Parse(Encoding.UTF8.GetBytes(expected))), Is.True);
            Assert.That(RegistryValues.Identical(source, Document()), Is.True);
        }

        [Test]
        public void NoopAndFailedValidationDoNotAdvanceOrMutateSourceState()
        {
            RegistryObjectValueDataType source = Document();
            var request = new RegistryChangeRequestDataType
            {
                TargetXid = "/messagegroups/g/messages/m",
                ExpectedEpoch = 3,
                Changes = [Set("x-number", "1")]
            };
            RegistryMetadataCommit noChange = RegistryMetadataMutation.Apply(
                source, request, ["messagegroups"], [], _ => { });
            Assert.That(noChange.Changed, Is.False);
            Assert.That(noChange.TargetEpoch, Is.EqualTo(3));
            request.Changes = [Set("x-number", "2")];
            Assert.Throws<ArgumentException>(() => RegistryMetadataMutation.Apply(
                source, request, ["messagegroups"], [], _ => throw new ArgumentException("domain rejected")));
            Assert.That(RegistryValues.Identical(source, Document()), Is.True);
        }

        [Test]
        public void SurfacedDescendantsCannotChangeThroughAnAncestorBatch()
        {
            var request = new RegistryChangeRequestDataType
            {
                TargetXid = "/messagegroups/g",
                ExpectedEpoch = 2,
                Changes =
                [
                    Set("name", "\"renamed\""),
                    new RegistryChangeDataType
                    {
                        Operation = 0,
                        Path =
                        [
                            new RegistryPathElementDataType { Kind = 0, Name = "messages" },
                            new RegistryPathElementDataType { Kind = 0, Name = "m" },
                            new RegistryPathElementDataType { Kind = 0, Name = "x-number" }
                        ],
                        Value = RegistryValues.Parse(Encoding.UTF8.GetBytes("1.0"))
                    }
                ]
            };
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                RegistryMetadataMutation.Apply(Document(), request, ["messagegroups"],
                    ["/messagegroups/g/messages/m"], _ => { }))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void RawMergePatchRemovesNullWhileTypedSetPreservesNull()
        {
            var patch = (RegistryObjectValueDataType)RegistryValues.Parse(
                Encoding.UTF8.GetBytes("{\"x-number\":null}"));
            RegistryMetadataCommit removed = RegistryMetadataMutation.Patch(Document(),
                "/messagegroups/g/messages/m", patch, 3, ["messagegroups"], [], _ => { });
            RegistryMetadataCommit explicitNull = RegistryMetadataMutation.Apply(Document(),
                new RegistryChangeRequestDataType
                {
                    TargetXid = "/messagegroups/g/messages/m",
                    ExpectedEpoch = 3,
                    Changes = [Set("x-number", "null")]
                }, ["messagegroups"], [], _ => { });
            Assert.That(Encoding.UTF8.GetString(RegistryValues.ToJson(removed.Document).ToArray()),
                Does.Not.Contain("x-number"));
            Assert.That(Encoding.UTF8.GetString(RegistryValues.ToJson(explicitNull.Document).ToArray()),
                Does.Contain("\"x-number\":null"));
            Assert.That(removed.TargetEpoch, Is.EqualTo(4));
            Assert.That(explicitNull.TargetEpoch, Is.EqualTo(4));
        }

        private static RegistryObjectValueDataType Document()
        {
            return (RegistryObjectValueDataType)RegistryValues.Parse(Encoding.UTF8.GetBytes(
                "{\"epoch\":7,\"messagegroups\":{\"g\":{\"epoch\":2,\"messages\":{" +
                "\"m\":{\"epoch\":3,\"messageid\":\"m\",\"versionid\":\"1\",\"x-number\":1}," +
                "\"other\":{\"epoch\":2}}}}}"));
        }

        private static RegistryChangeDataType Set(string name, string json)
        {
            return new RegistryChangeDataType
            {
                Operation = 0,
                Path = [new RegistryPathElementDataType { Kind = 0, Name = name }],
                Value = RegistryValues.Parse(Encoding.UTF8.GetBytes(json))
            };
        }
    }
}
