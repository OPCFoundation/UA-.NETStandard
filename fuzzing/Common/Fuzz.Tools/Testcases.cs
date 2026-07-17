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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Opc.Ua.Fuzzing
{
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable RCS1043
    public static partial class Testcases
#pragma warning restore RCS1043, IDE0079
    {
        public delegate void MessageEncoder(IEncoder encoder);

        public static readonly MessageEncoder[] MessageEncoders = [ReadRequest, ReadResponse];

        public static string[] DiscoverTestcaseEncoderSuffixes(string testcasesRoot)
        {
            if (string.IsNullOrEmpty(testcasesRoot))
            {
                return [];
            }

            string rootName = Path.GetFileName(
                testcasesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string parent = Path.GetDirectoryName(testcasesRoot);
            var directories = new List<string>();

            if (Directory.Exists(testcasesRoot))
            {
                directories.AddRange(Directory.EnumerateDirectories(testcasesRoot, "Testcases.*"));
            }

            if (parent != null && Directory.Exists(parent))
            {
                directories.AddRange(Directory.EnumerateDirectories(parent, rootName + ".*"));
            }

            return
            [
                .. directories
                    .Select(Path.GetFileName)
                    .Where(name => name.StartsWith(rootName + ".", StringComparison.OrdinalIgnoreCase))
                    .Select(name => name[rootName.Length..])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(suffix => suffix, StringComparer.OrdinalIgnoreCase)
            ];
        }

        public static void ReadRequest(IEncoder encoder)
        {
            var nodeId = new NodeId(1000);
            var readRequest = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 42,
                    AdditionalHeader = new ExtensionObject()
                },
                NodesToRead =
                [
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Description },
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DisplayName },
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.AccessLevel },
                    new ReadValueId { NodeId = nodeId, AttributeId = Attributes.RolePermissions }
                ]
            };
            encoder.EncodeMessage(readRequest);
        }

        public static void ReadResponse(IEncoder encoder)
        {
            DateTimeUtc now = DateTimeUtc.Now;

            var readRequest = new ReadResponse
            {
                Results =
                [
                    new DataValue(
                        Variant.From("Hello World"),
                        StatusCodes.Good,
                        now.AddMilliseconds(60000),
                        now,
                        10,
                        100),
                    new DataValue(
                        Variant.From((uint)12345678),
                        StatusCodes.BadDataLost,
                        now.AddMilliseconds(60000),
                        now),
                    new DataValue(
                        Variant.From(ByteString.From([0, 1, 2, 3, 4, 5, 6])),
                        StatusCodes.Good,
                        now.AddMilliseconds(60000),
                        now),
                    new DataValue(
                        Variant.From((byte)42),
                        StatusCodes.Good,
                        now)
                ],
                DiagnosticInfos =
                [
                    new DiagnosticInfo
                    {
                        AdditionalInfo = "Hello World",
                        InnerStatusCode = StatusCodes.BadCertificateHostNameInvalid,
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            AdditionalInfo = "Hello World",
                            InnerStatusCode = StatusCodes.BadNodeIdUnknown
                        }
                    }
                ],
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 42,
                    ServiceResult = StatusCodes.Good,
                    ServiceDiagnostics = new DiagnosticInfo
                    {
                        AdditionalInfo = "NodeId not found",
                        InnerStatusCode = StatusCodes.BadNodeIdExists,
                        InnerDiagnosticInfo = new DiagnosticInfo
                        {
                            AdditionalInfo = "Hello World",
                            InnerStatusCode = StatusCodes.BadNodeIdUnknown,
                            InnerDiagnosticInfo = new DiagnosticInfo
                            {
                                AdditionalInfo = "Hello World",
                                InnerStatusCode = StatusCodes.BadNodeIdUnknown,
                                InnerDiagnosticInfo = new DiagnosticInfo
                                {
                                    AdditionalInfo = "Hello World",
                                    InnerStatusCode = StatusCodes.BadNodeIdUnknown
                                }
                            }
                        }
                    }
                }
            };
            encoder.EncodeMessage(readRequest);
        }
    }
}
