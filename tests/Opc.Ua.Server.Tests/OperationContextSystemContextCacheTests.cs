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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the per-request system context copy cached on <see cref="OperationContext"/>,
    /// which node managers reuse for the per-node callbacks of one request.
    /// </summary>
    [TestFixture]
    [Category("OperationContext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class OperationContextSystemContextCacheTests
    {
        /// <summary>
        /// Repeated lookups with the same template return the same copy, bound to the operation.
        /// </summary>
        [Test]
        public void SameTemplateReturnsOneCopyBoundToTheOperation()
        {
            ServerSystemContext template = CreateTemplate();
            using OperationContext operation = CreateOperation();

            ServerSystemContext first = operation.GetSystemContext(template);
            ServerSystemContext second = operation.GetSystemContext(template);

            Assert.That(second, Is.SameAs(first));
            Assert.That(first, Is.Not.SameAs(template));
            Assert.That(first.OperationContext, Is.SameAs(operation));
        }

        /// <summary>
        /// A copy made from one node manager's template is never handed to another node manager.
        /// </summary>
        [Test]
        public void SwitchingTemplatesNeverReturnsAnotherManagersCopy()
        {
            ServerSystemContext templateA = CreateTemplate(systemHandle: "A");
            ServerSystemContext templateB = CreateTemplate(systemHandle: "B");
            using OperationContext operation = CreateOperation();

            ServerSystemContext a1 = operation.GetSystemContext(templateA);
            ServerSystemContext b1 = operation.GetSystemContext(templateB);
            ServerSystemContext a2 = operation.GetSystemContext(templateA);

            Assert.That(a1.SystemHandle, Is.EqualTo("A"));
            Assert.That(b1.SystemHandle, Is.EqualTo("B"));
            Assert.That(a2.SystemHandle, Is.EqualTo("A"));
            Assert.That(b1, Is.Not.SameAs(a1));
            Assert.That(b1.OperationContext, Is.SameAs(operation));
            Assert.That(a2.OperationContext, Is.SameAs(operation));
        }

        /// <summary>
        /// Concurrent lookups with alternating templates always get a copy of the template they
        /// passed, bound to the same operation.
        /// </summary>
        [Test]
        public void ConcurrentLookupsAlwaysReturnACopyOfTheRequestedTemplate()
        {
            ServerSystemContext[] templates =
            [
                CreateTemplate(systemHandle: "A"),
                CreateTemplate(systemHandle: "B"),
                CreateTemplate(systemHandle: "C")
            ];
            using OperationContext operation = CreateOperation();

            int mismatches = 0;
            Parallel.For(0, 30000, ii =>
            {
                ServerSystemContext template = templates[ii % templates.Length];
                ServerSystemContext copy = operation.GetSystemContext(template);
                if (!Equals(copy.SystemHandle, template.SystemHandle) ||
                    !ReferenceEquals(copy.OperationContext, operation))
                {
                    System.Threading.Interlocked.Increment(ref mismatches);
                }
            });

            Assert.That(mismatches, Is.Zero);
            Assert.That(
                templates.Select(t => operation.GetSystemContext(t).SystemHandle),
                Is.EqualTo(new object[] { "A", "B", "C" }));
        }

        /// <summary>
        /// Alternating access to more node-manager templates than the previous cache bound keeps
        /// each request copy stable.
        /// </summary>
        [Test]
        public void AlternatingLargeTemplateSetsReuseEachCopy()
        {
            const int templateCount = 24;
            ServerSystemContext[] templates = Enumerable.Range(0, templateCount)
                .Select(index => CreateTemplate(systemHandle: index))
                .ToArray();
            using OperationContext operation = CreateOperation();
            var copies = new ServerSystemContext[templateCount];

            for (int index = 0; index < templateCount; index++)
            {
                copies[index] = operation.GetSystemContext(templates[index]);
            }

            for (int round = 0; round < 4; round++)
            {
                for (int position = 0; position < templateCount; position++)
                {
                    int index = round % 2 == 0 ? templateCount - position - 1 : position;
                    Assert.That(operation.GetSystemContext(templates[index]), Is.SameAs(copies[index]));
                }
            }
        }

        /// <summary>
        /// Concurrent readers share the same copy for every template beyond the previous cache bound.
        /// </summary>
        [Test]
        public void ConcurrentLargeTemplateSetKeepsOneCopyPerTemplate()
        {
            const int templateCount = 24;
            ServerSystemContext[] templates = Enumerable.Range(0, templateCount)
                .Select(index => CreateTemplate(systemHandle: index))
                .ToArray();
            using OperationContext operation = CreateOperation();
            var copies = new ServerSystemContext?[templateCount];
            int mismatches = 0;

            Parallel.For(0, templateCount * 1000, iteration =>
            {
                int index = iteration % templateCount;
                ServerSystemContext copy = operation.GetSystemContext(templates[index]);
                ServerSystemContext? first = System.Threading.Interlocked.CompareExchange(
                    ref copies[index],
                    copy,
                    null);
                if (first != null && !ReferenceEquals(first, copy))
                {
                    System.Threading.Interlocked.Increment(ref mismatches);
                }
            });

            Assert.That(mismatches, Is.Zero);
            Assert.That(copies, Has.All.Not.Null);
        }

        /// <summary>
        /// A replacement template receives a new copy, and retired templates are not rooted by
        /// the operation's cache.
        /// </summary>
        [Test]
        public void ReplacedTemplateIsNotRetainedByOperationCache()
        {
            var retention = CreateReplacementReferences();
            try
            {
                CollectGarbage();

                Assert.Multiple(() =>
                {
                    Assert.That(retention.RetiredTemplate.IsAlive, Is.False);
                    Assert.That(retention.RetiredCopy.IsAlive, Is.False);
                });
                GC.KeepAlive(retention.Operation);
            }
            finally
            {
                retention.Operation.Dispose();
            }
        }

        /// <summary>
        /// Disposing the operation releases its first copy as well as cached copies.
        /// </summary>
        [Test]
        public void DisposingOperationReleasesContextCopiesAndTemplates()
        {
            var retention = CreateOperationReferences();
            GC.KeepAlive(retention.Operation);

            Assert.That(retention.FirstTemplate.IsAlive, Is.True);
            Assert.That(retention.FirstCopy.IsAlive, Is.True);
            retention.Operation.Dispose();
            CollectGarbage();

            Assert.Multiple(() =>
            {
                Assert.That(retention.FirstTemplate.IsAlive, Is.False);
                Assert.That(retention.FirstCopy.IsAlive, Is.False);
                Assert.That(retention.CachedTemplate.IsAlive, Is.False);
                Assert.That(retention.CachedCopy.IsAlive, Is.False);
            });
            GC.KeepAlive(retention.Operation);
        }

        /// <summary>
        /// A template copy is isolated to the operation's identity and secure-channel context.
        /// </summary>
        [Test]
        public void CopiesAreNotSharedAcrossIdentityOrChannelContexts()
        {
            ServerSystemContext template = CreateTemplate(systemHandle: "shared");
            var firstIdentity = new UserIdentity();
            var secondIdentity = new UserIdentity();
            var firstChannel = new SecureChannelContext("first", null, RequestEncoding.Binary);
            var secondChannel = new SecureChannelContext("second", null, RequestEncoding.Binary);
            using var firstOperation = new OperationContext(
                new RequestHeader(),
                firstChannel,
                RequestType.Read,
                RequestLifetime.None,
                firstIdentity);
            using var secondOperation = new OperationContext(
                new RequestHeader(),
                secondChannel,
                RequestType.Read,
                RequestLifetime.None,
                secondIdentity);

            ServerSystemContext firstCopy = firstOperation.GetSystemContext(template);
            ServerSystemContext secondCopy = secondOperation.GetSystemContext(template);

            Assert.Multiple(() =>
            {
                Assert.That(firstCopy, Is.Not.SameAs(secondCopy));
                Assert.That(firstCopy.OperationContext, Is.SameAs(firstOperation));
                Assert.That(secondCopy.OperationContext, Is.SameAs(secondOperation));
                Assert.That(firstCopy.OperationContext!.UserIdentity, Is.SameAs(firstIdentity));
                Assert.That(secondCopy.OperationContext!.UserIdentity, Is.SameAs(secondIdentity));
                Assert.That(firstCopy.OperationContext.ChannelContext, Is.SameAs(firstChannel));
                Assert.That(secondCopy.OperationContext.ChannelContext, Is.SameAs(secondChannel));
            });
        }

#if NETCOREAPP
        /// <summary>
        /// Reports context-copy churn and allocation for alternating access to a large manager set.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Performance")]
        public void ReportsLargeTemplateSetMeasurements()
        {
            const int templateCount = 24;
            const int rounds = 64;
            ServerSystemContext[] templates = Enumerable.Range(0, templateCount)
                .Select(index => CreateTemplate(systemHandle: index))
                .ToArray();
            using OperationContext operation = CreateOperation();
            var copies = new ServerSystemContext[templateCount];
            for (int index = 0; index < templateCount; index++)
            {
                copies[index] = operation.GetSystemContext(templates[index]);
            }

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int additionalCopies = 0;
            for (int round = 0; round < rounds; round++)
            {
                for (int position = 0; position < templateCount; position++)
                {
                    int index = round % 2 == 0 ? position : templateCount - position - 1;
                    ServerSystemContext copy = operation.GetSystemContext(templates[index]);
                    if (!ReferenceEquals(copies[index], copy))
                    {
                        additionalCopies++;
                        copies[index] = copy;
                    }
                }
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            using OperationContext singleOperation = CreateOperation();
            ServerSystemContext singleTemplate = CreateTemplate(systemHandle: "single");
            ServerSystemContext singleCopy = singleOperation.GetSystemContext(singleTemplate);
            long singleAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int singleCopyChanges = 0;
            for (int iteration = 0; iteration < templateCount * rounds; iteration++)
            {
                if (!ReferenceEquals(singleCopy, singleOperation.GetSystemContext(singleTemplate)))
                {
                    singleCopyChanges++;
                }
            }
            long singleAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - singleAllocatedBefore;

            TestContext.Out.WriteLine(
                $"Managers={templateCount}; rounds={rounds}; additionalCopies={additionalCopies}; " +
                $"allocatedBytes={allocatedBytes}; bytesPerLookup=" +
                $"{(double)allocatedBytes / (templateCount * rounds):F2}");
            TestContext.Out.WriteLine(
                $"Managers=1; lookups={templateCount * rounds}; copyChanges={singleCopyChanges}; " +
                $"allocatedBytes={singleAllocatedBytes}; bytesPerLookup=" +
                $"{(double)singleAllocatedBytes / (templateCount * rounds):F2}");
        }
#endif

        /// <summary>
        /// Creates an operation whose retired template can be collected while the operation remains live.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            OperationContext Operation,
            WeakReference RetiredTemplate,
            WeakReference RetiredCopy) CreateReplacementReferences()
        {
            ServerSystemContext firstTemplate = CreateTemplate(systemHandle: "first");
            ServerSystemContext retiredTemplate = CreateTemplate(systemHandle: "retired");
            OperationContext operation = CreateOperation();
            operation.GetSystemContext(firstTemplate);
            ServerSystemContext retiredCopy = operation.GetSystemContext(retiredTemplate);
            ServerSystemContext replacement = CreateTemplate(systemHandle: "replacement");
            ServerSystemContext replacementCopy = operation.GetSystemContext(replacement);

            Assert.That(replacementCopy, Is.Not.SameAs(retiredCopy));
            Assert.That(replacementCopy.SystemHandle, Is.EqualTo("replacement"));
            return (operation, new WeakReference(retiredTemplate), new WeakReference(retiredCopy));
        }

        /// <summary>
        /// Creates an operation and weak references to its first and cached context copies.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            OperationContext Operation,
            WeakReference FirstTemplate,
            WeakReference FirstCopy,
            WeakReference CachedTemplate,
            WeakReference CachedCopy) CreateOperationReferences()
        {
            OperationContext operation = CreateOperation();
            ServerSystemContext firstTemplate = CreateTemplate(systemHandle: "first");
            ServerSystemContext cachedTemplate = CreateTemplate(systemHandle: "cached");
            ServerSystemContext firstCopy = operation.GetSystemContext(firstTemplate);
            ServerSystemContext cachedCopy = operation.GetSystemContext(cachedTemplate);

            return (
                operation,
                new WeakReference(firstTemplate),
                new WeakReference(firstCopy),
                new WeakReference(cachedTemplate),
                new WeakReference(cachedCopy));
        }

        /// <summary>
        /// Collects unreachable test objects without keeping the test frame alive.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static ServerSystemContext CreateTemplate(object? systemHandle = null)
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            return new ServerSystemContext(server.Object) { SystemHandle = systemHandle };
        }

        private static OperationContext CreateOperation()
        {
            return new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Read,
                RequestLifetime.None);
        }
    }
}
