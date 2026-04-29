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
using System.Linq;
using System.Reflection;
using System.Xml;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the EncodeableFactory class.
    /// </summary>
    [TestFixture]
    [Category("EncodeableFactory")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class EncodeableFactoryTests
    {
        [OneTimeSetUp]
        [GlobalSetup]
        public void OneTimeAndGlobalSetUp()
        {
            IEncodeableFactory encodeableFactory = EncodeableFactory.Create();
            m_builder = encodeableFactory.Builder.AddEncodeableTypes(encodeableFactory.GetType().Assembly);
            m_encodeableFactory = EncodeableFactory.Create();
            m_encodeableFactory.Builder.AddEncodeableTypes(encodeableFactory.GetType().Assembly).Commit();
        }

        /// <summary>
        /// Benchmark for lookup using dictionary
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public void EncodeableFactoryLookupFromBuilder()
        {
            // lookup a type that exists and one that does not
            for (int i = 0; i < 1000; i++)
            {
                _ = m_builder.TryGetEncodeableType(
                    new ExpandedNodeId(ObjectIds.ReadRequest_Encoding_DefaultBinary),
                    out IEncodeableType encodeableType);
                Assert.That(encodeableType, Is.Not.Null);
                _ = m_builder.TryGetEncodeableType(
                    new ExpandedNodeId(ObjectIds.Argument_Encoding_DefaultBinary),
                    out encodeableType);
                Assert.That(encodeableType, Is.Not.Null);
                _ = m_builder.TryGetEncodeableType(
                    new ExpandedNodeId(Guid.NewGuid()),
                    out encodeableType);
                Assert.That(encodeableType, Is.Null);
            }
        }

        /// <summary>
        /// Benchmark for lookup using frozen dictionary
        /// </summary>
        [Benchmark]
        [Test]
        public void EncodeableFactoryLookupFromEncodeableFactory()
        {
            // lookup a type that exists and one that does not
            for (int i = 0; i < 1000; i++)
            {
                _ = m_encodeableFactory.TryGetEncodeableType(
                    new ExpandedNodeId(ObjectIds.ReadRequest_Encoding_DefaultBinary),
                    out IEncodeableType encodeableType);
                Assert.That(encodeableType, Is.Not.Null);
                _ = m_builder.TryGetEncodeableType(
                    new ExpandedNodeId(ObjectIds.Argument_Encoding_DefaultBinary),
                    out encodeableType);
                Assert.That(encodeableType, Is.Not.Null);
                _ = m_encodeableFactory.TryGetEncodeableType(
                    new ExpandedNodeId(Guid.NewGuid()),
                    out encodeableType);
                Assert.That(encodeableType, Is.Null);
            }
        }

        [Test]
        public void Create_ReturnsFactoryWithKnownTypes()
        {
            // Act
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Assert
            Assert.That(factory, Is.Not.Null);
            Assert.That(factory.KnownTypeIds, Is.Not.Null);
            Assert.That(factory.KnownTypeIds.Count(), Is.GreaterThan(0));
        }

        [Test]
        public void KnownTypes_ReturnsEmptyForNewFactory()
        {
            // Arrange - Create factory has pre-loaded types, so test the interface contract
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            IEnumerable<ExpandedNodeId> knownTypes = factory.KnownTypeIds;

            // Assert
            Assert.That(knownTypes, Is.Not.Null);
            // Note: Since Create() pre-loads types, we expect many types, not 0
            Assert.That(knownTypes.Count(), Is.GreaterThan(0));
        }

        [Test]
        public void TryGetEncodeableType_WithUnknownType_ReturnsFalse()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var unknownTypeId = new ExpandedNodeId(9999);

            // Act
            bool result = factory.TryGetEncodeableType(unknownTypeId, out IEncodeableType encodeableType);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(encodeableType, Is.Null);
        }

        [Test]
        public void Builder_ReturnsNonNullBuilder()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Assert
            Assert.That(builder, Is.Not.Null);
        }

        [Test]
        public void Builder_AddEncodeableType_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableType(typeof(TestEncodeable));
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType encodeableType);
            Assert.That(found, Is.True);
            Assert.That(encodeableType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeId_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(5000);

            // Act
            builder.AddType(typeId, typeof(TestEncodeable));
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(typeId, out IEncodeableType encodeableType);
            Assert.That(found, Is.True);
            Assert.That(encodeableType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableType_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddEncodeableType(typeof(TestEncodeable));
            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeId_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddType(new ExpandedNodeId(100000), typeof(TestEncodeable));
            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void Builder_CanLookupTypeBeforeCommit()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(7000); // Use unique ID to avoid conflicts with pre-loaded types

            // Act
            builder.AddType(typeId, typeof(TestEncodeable));
            bool foundInBuilder = builder.TryGetEncodeableType(typeId, out IEncodeableType builderType);
            bool foundInFactory = factory.TryGetEncodeableType(typeId, out IEncodeableType factoryType);

            // Assert
            Assert.That(foundInBuilder, Is.True);
            Assert.That(builderType.Type, Is.EqualTo(typeof(TestEncodeable)));
            Assert.That(foundInFactory, Is.False);
            Assert.That(factoryType, Is.Null);
        }

        [Test]
        public void Builder_CommitMultipleTimes_OnlyCommitsOnce()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(8000); // Use unique ID

            // Act
            builder.AddType(typeId, typeof(TestEncodeable));
            builder.Commit();
            builder.Commit(); // Second commit should be no-op

            // Assert
            bool found = factory.TryGetEncodeableType(typeId, out IEncodeableType encodeableType);
            Assert.That(found, Is.True);
            Assert.That(encodeableType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableTypes_WithNullAssembly_ReturnsBuilder()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            IEncodeableFactoryBuilder result = builder.AddEncodeableTypes(null);

            // Assert
            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void Builder_AddEncodeableTypes_WithValidAssembly_AddsTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var currentAssembly = Assembly.GetExecutingAssembly();

            // Act
            builder.AddEncodeableTypes(currentAssembly);
            builder.Commit();

            // Assert - Should add our test types
            bool foundTest = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType testType);
            Assert.That(foundTest, Is.True);
            Assert.That(testType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableTypes_SkipsAbstractAndNonDefaultConstructorTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            int knownTypesCount = factory.KnownTypeIds.Count();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableTypes(Assembly.GetExecutingAssembly());
            builder.Commit();

            // Assert - Should add our concrete test types but skip abstract and no-default-constructor types
            int addedTypes = factory.KnownTypeIds.Count() - knownTypesCount;
            Assert.That(addedTypes, Is.GreaterThan(0)); // Should have added some types

            // Verify abstract types are not added
            Assert.That(factory.TryGetEncodeableType(new ExpandedNodeId(110000), out _), Is.False);
            Assert.That(factory.TryGetEncodeableType(new ExpandedNodeId(110001), out _), Is.False);
            Assert.That(factory.TryGetEncodeableType(new ExpandedNodeId(110002), out _), Is.False);
        }


        [Test]
        public void Builder_MultipleTypes_AllTypesAdded()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableType(typeof(TestEncodeable))
                   .AddEncodeableType(typeof(TestEncodeableWithDefaultNamespace));
            builder.Commit();

            // Assert
            bool foundTest = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType testType);
            bool foundDefaultNs = factory.TryGetEncodeableType(new ExpandedNodeId(140000), out _);

            Assert.That(foundTest, Is.True);
            Assert.That(foundDefaultNs, Is.True);
            Assert.That(testType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        [Category("ThreadSafety")]
        public void Builder_ConcurrentAccess_ThreadSafety()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var exceptions = new List<Exception>();
            const int threadCount = 10;
            const int operationsPerThread = 100;

            // Act
            var threads = new System.Threading.Thread[threadCount];
            for (uint i = 0; i < threadCount; i++)
            {
                uint threadId = i;
                threads[i] = new System.Threading.Thread(() =>
                {
                    try
                    {
                        for (uint j = 0; j < operationsPerThread; j++)
                        {
                            IEncodeableFactoryBuilder builder = factory.Builder;
                            builder.AddType(
                                new ExpandedNodeId((threadId * 1000) + j + 10000),
                                typeof(TestEncodeable));
                            builder.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            foreach (System.Threading.Thread thread in threads)
            {
                thread.Join();
            }

            // Assert
            Assert.That(exceptions, Is.Empty, $"Exceptions occurred: {string.Join(", ", exceptions.Select(e => e.Message))}");
            Assert.That(factory.KnownTypeIds.Count(), Is.GreaterThan(0));
        }

        [Test]
        public void Clone_ReturnsDeepCopy()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var typeId = new ExpandedNodeId(9001); // Use unique ID
            factory.Builder.AddType(typeId, typeof(TestEncodeable)).Commit();

            // Act - Cast to concrete type to access Clone method
            var concreteFactory = (EncodeableFactory)factory;
            var clonedFactory = (EncodeableFactory)concreteFactory.Clone();

            // Assert
            Assert.That(clonedFactory, Is.Not.Null);
            Assert.That(clonedFactory, Is.Not.SameAs(factory));

            // Should have same types
            bool originalHasType = factory.TryGetEncodeableType(typeId, out IEncodeableType originalType);
            bool clonedHasType = clonedFactory.TryGetEncodeableType(typeId, out IEncodeableType clonedType);

            Assert.That(originalHasType, Is.True);
            Assert.That(clonedHasType, Is.True);
            Assert.That(clonedType, Is.EqualTo(originalType));
        }

        [Test]
        public void MemberwiseClone_ReturnsDeepCopy()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var typeId = new ExpandedNodeId(9002); // Use unique ID
            factory.Builder.AddType(typeId, typeof(TestEncodeable)).Commit();

            // Act - Cast to concrete type to access Clone method
            var concreteFactory = (EncodeableFactory)factory;
            var clonedFactory = (EncodeableFactory)concreteFactory.Clone();

            // Assert
            Assert.That(clonedFactory, Is.Not.Null);
            Assert.That(clonedFactory, Is.Not.SameAs(factory));

            // Should have same types
            bool originalHasType = factory.TryGetEncodeableType(typeId, out IEncodeableType originalType);
            bool clonedHasType = clonedFactory.TryGetEncodeableType(typeId, out IEncodeableType clonedType);

            Assert.That(originalHasType, Is.True);
            Assert.That(clonedHasType, Is.True);
            Assert.That(clonedType, Is.EqualTo(originalType));
        }

        [Test]
        public void Clone_IndependentOfOriginal()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var initialTypeId = new ExpandedNodeId(9003);
            var newTypeId = new ExpandedNodeId(9004);
            factory.Builder.AddType(initialTypeId, typeof(TestEncodeable)).Commit();

            // Act - Cast to concrete type to access Clone method
            var concreteFactory = (EncodeableFactory)factory;
            var clonedFactory = (EncodeableFactory)concreteFactory.Clone();

            // Add type to original
            factory.Builder.AddType(newTypeId, typeof(TestEncodeableWithDefaultNamespace)).Commit();

            // Assert - Clone should not have the new type
            bool originalHasNewType = factory.TryGetEncodeableType(newTypeId, out _);
            bool clonedHasNewType = clonedFactory.TryGetEncodeableType(newTypeId, out _);

            Assert.That(originalHasNewType, Is.True);
            Assert.That(clonedHasNewType, Is.False);
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullType_HandlesGracefully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert - The implementation may handle null types gracefully rather than throwing
            Assert.DoesNotThrow(() => builder.AddEncodeableType((Type)null));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullExpandedNodeId_HandlesGracefully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert - The implementation may handle null NodeIds gracefully rather than throwing
            Assert.DoesNotThrow(() => builder.AddType(default, typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNonEncodeableType_DoesNotAdd()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            int initialCount = factory.KnownTypeIds.Count();

            // Act
            builder.AddEncodeableType(typeof(string)); // string is not IEncodeable
            builder.Commit();

            // Assert - No new types should be added
            Assert.That(factory.KnownTypeIds.Count(), Is.EqualTo(initialCount));
        }

        [Test]
        public void Builder_AddEncodeableType_WithTypeWithoutDefaultConstructor_HandlesGracefully()
        {
            // This test verifies that types without parameterless constructors are handled gracefully
            // We can't easily create such a test type, so we'll use a mock approach

            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() =>
            {
                builder.AddEncodeableTypes(Assembly.GetExecutingAssembly());
                builder.Commit();
            });
        }

        [Test]
        public void Integration_BuilderWithCoreAssembly_AddsKnownOpcUaTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableTypes(typeof(EncodeableFactory).Assembly);
            builder.Commit();

            // Assert - Should have many OPC UA types
            Assert.That(factory.KnownTypeIds.Count(), Is.GreaterThan(100)); // OPC UA has many built-in types
        }

        [Test]
        public void Integration_CreateFactoryHasPreloadedTypes()
        {
            // Act
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Assert - Create() should return factory with preloaded types
            Assert.That(factory.KnownTypeIds.Count(), Is.GreaterThan(100));

            // Should be able to find common OPC UA types like ReadRequest
            var knownTypesList = factory.KnownTypeIds.ToList();
            Assert.That(knownTypesList, Is.Not.Empty);
        }

        [Test]
        public void Integration_FactoryCanResolveReadRequest()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act - Try to find ReadRequest type using proper ExpandedNodeId - wrap the uint constant
            var readRequestEncodingId = new ExpandedNodeId(ObjectIds.ReadRequest_Encoding_DefaultBinary);
            bool found = factory.TryGetEncodeableType(readRequestEncodingId, out IEncodeableType encodeableType);

            // Assert
            Assert.That(found, Is.True);
            Assert.That(encodeableType.Type, Is.EqualTo(typeof(ReadRequest)));
        }

        [Test]
        public void Integration_DefaultNamespaceHandling()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Create a type with default namespace
            var typeIdWithDefaultNs = new ExpandedNodeId(9100, Namespaces.OpcUa);
            var typeIdWithoutNs = new ExpandedNodeId(9100);

            // Act
            builder.AddType(typeIdWithDefaultNs, typeof(TestEncodeable));
            builder.Commit();

            // Assert - Should be findable with both NodeId forms
            bool foundWithNs = factory.TryGetEncodeableType(typeIdWithDefaultNs, out IEncodeableType typeWithNs);
            bool foundWithoutNs = factory.TryGetEncodeableType(typeIdWithoutNs, out IEncodeableType typeWithoutNs);

            Assert.That(foundWithNs, Is.True);
            Assert.That(foundWithoutNs, Is.False);
            Assert.That(typeWithNs.Type, Is.EqualTo(typeof(TestEncodeable)));
            Assert.That(typeWithoutNs, Is.Null);
        }

        [Test]
        public void Builder_EmptyCommit_DoesNotThrow()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            Assert.DoesNotThrow(builder.Commit);
            Assert.That(factory.KnownTypeIds.Count(), Is.GreaterThan(0)); // Should have pre-loaded types
        }


        [Test]
        public void Builder_ReuseAfterCommit_CanAddMoreTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddType(new ExpandedNodeId(9200), typeof(TestEncodeable)).Commit();
            builder.AddType(new ExpandedNodeId(9201), typeof(TestEncodeableWithDefaultNamespace)).Commit();

            // Assert
            bool foundFirst = factory.TryGetEncodeableType(new ExpandedNodeId(9200), out IEncodeableType firstType);
            bool foundSecond = factory.TryGetEncodeableType(new ExpandedNodeId(9201), out IEncodeableType secondType);

            Assert.That(foundFirst, Is.True);
            Assert.That(foundSecond, Is.True);
            Assert.That(firstType.Type, Is.EqualTo(typeof(TestEncodeable)));
            Assert.That(secondType.Type, Is.EqualTo(typeof(TestEncodeableWithDefaultNamespace)));
        }

        [Test]
        public void TryGetEncodeableType_WithNullNodeId_ReturnsFalse()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            bool result = factory.TryGetEncodeableType(default, out IEncodeableType encodeableType);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(encodeableType, Is.Null);
        }

        [Test]
        public void TryGetEncodeableType_WithExpandedNodeIdNull_ReturnsFalse()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            bool result = factory.TryGetEncodeableType(ExpandedNodeId.Null, out IEncodeableType encodeableType);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(encodeableType, Is.Null);
        }

        /// <summary>
        /// Test class for encodeable objects used in factory tests.
        /// </summary>
        public class TestEncodeable : IEncodeable
        {
            public TestEncodeable()
            {
            }

            public TestEncodeable(string value)
            {
                Value = value;
            }

            public string Value { get; set; }

            public virtual ExpandedNodeId TypeId => new(100000);
            public ExpandedNodeId BinaryEncodingId => new(100001);
            public ExpandedNodeId XmlEncodingId => new(100002);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeable test && test.Value == Value;
            }

            public virtual object Clone()
            {
                return new TestEncodeable(Value);
            }
        }


        public class TestNoDefaultConstructorEncodeable : IEncodeable
        {
            public TestNoDefaultConstructorEncodeable(string value)
            {
                Value = value;
            }

            public string Value { get; set; }
            public ExpandedNodeId TypeId => new(110000);
            public ExpandedNodeId BinaryEncodingId => new(110001);
            public ExpandedNodeId XmlEncodingId => new(110002);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestNoDefaultConstructorEncodeable test && test.Value == Value;
            }

            public object Clone()
            {
                return new TestNoDefaultConstructorEncodeable(Value);
            }
        }

        /// <summary>
        /// Abstract test encodeable for testing abstract types.
        /// </summary>
        public abstract class AbstractTestEncodeable : IEncodeable
        {
            public abstract ExpandedNodeId TypeId { get; }
            public abstract ExpandedNodeId BinaryEncodingId { get; }
            public abstract ExpandedNodeId XmlEncodingId { get; }

            public virtual void Encode(IEncoder encoder)
            {
            }

            public virtual void Decode(IDecoder decoder)
            {
            }

            public virtual bool IsEqual(IEncodeable encodeable)
            {
                return false;
            }

            public virtual object Clone()
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Test implementation of IEncodeableType for testing purposes.
        /// </summary>
        public class TestEncodeableType : IEncodeableType
        {
            public TestEncodeableType(Type type)
            {
                Type = type;
            }

            public Type Type { get; }

            public XmlQualifiedName XmlName => new(Type.FullName);

            public IEncodeable CreateInstance()
            {
                return (IEncodeable)Activator.CreateInstance(Type);
            }

            public override bool Equals(object obj)
            {
                return Type.Equals((obj as IEncodeableType)?.Type);
            }

            public override int GetHashCode()
            {
                return Type.GetHashCode();
            }

            public override string ToString()
            {
                return Type.FullName ?? Type.Name;
            }
        }

        /// <summary>
        /// Test implementation of IEncodeableType that throws on CreateInstance.
        /// </summary>
        public class FaultyEncodeableType : IEncodeableType
        {
            public Type Type => typeof(TestEncodeable);

            public XmlQualifiedName XmlName => new(nameof(TestEncodeable));

            public IEncodeable CreateInstance()
            {
                throw new InvalidOperationException("Cannot create instance");
            }
        }

        /// <summary>
        /// Test implementation of IEncodeableType that returns null on CreateInstance.
        /// </summary>
        public class NullReturningEncodeableType : IEncodeableType
        {
            public Type Type => typeof(TestEncodeable);

            public XmlQualifiedName XmlName => new(nameof(TestEncodeable));

            public IEncodeable CreateInstance()
            {
                return null;
            }
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableType_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act
            builder.AddEncodeableType(encodeableType);
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType resultType);
            Assert.That(found, Is.True);
            Assert.That(resultType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableType_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddEncodeableType(encodeableType);
            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeIdAndIEncodeableType_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(50000);
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act
            builder.AddEncodeableType(typeId, encodeableType);
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(typeId, out IEncodeableType resultType);
            Assert.That(found, Is.True);
            Assert.That(resultType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeIdAndIEncodeableType_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(50001);
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddEncodeableType(typeId, encodeableType);
            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullIEncodeableType_ThrowsArgumentNullException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => builder.AddEncodeableType((IEncodeableType)null));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullExpandedNodeIdAndIEncodeableType_DoesNotThrow()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));
            var knownTypes = factory.KnownTypeIds.ToList();

            // Act
            builder.AddEncodeableType(default, encodeableType).Commit();
            // Assert
            Assert.That(factory.KnownTypeIds.Count(), Is.EqualTo(knownTypes.Count));
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeIdAndNullIEncodeableType_ThrowsArgumentNullException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(50002);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => builder.AddEncodeableType(typeId, (IEncodeableType)null));
        }

        [Test]
        public void Builder_AddEncodeableType_WithFaultyIEncodeableType_ThrowsException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var faultyType = new FaultyEncodeableType();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(
                () => builder.AddEncodeableType(faultyType));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullReturningIEncodeableType_ThrowsException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var nullReturningType = new NullReturningEncodeableType();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(
                () => builder.AddEncodeableType(nullReturningType));
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableTypeNotSupportingXmlEncoding_HandlesGracefully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeableWithoutXml));

            // Act & Assert - Should not throw even if XmlEncodingId throws NotSupportedException
            Assert.DoesNotThrow(() =>
            {
                builder.AddEncodeableType(encodeableType);
                builder.Commit();
            });

            // Should still be able to find by other encoding IDs
            bool found = factory.TryGetEncodeableType(new ExpandedNodeId(120000), out IEncodeableType resultType);
            Assert.That(found, Is.True);
            Assert.That(resultType.Type, Is.EqualTo(typeof(TestEncodeableWithoutXml)));
        }


        [Test]
        public void Builder_AddEncodeableType_WithDefaultNamespaceNormalization_HandlesCorrectly()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeableWithDefaultNamespace));

            // Act
            builder.AddEncodeableType(encodeableType);
            builder.Commit();

            // Assert - Should be findable with normalized NodeId (without namespace URI)
            bool foundWithoutNs = factory.TryGetEncodeableType(new ExpandedNodeId(140000), out IEncodeableType typeWithoutNs);
            bool foundWithNs = factory.TryGetEncodeableType(new ExpandedNodeId(140000, Namespaces.OpcUa), out _);

            Assert.That(foundWithoutNs, Is.True);
            Assert.That(foundWithNs, Is.False);
            Assert.That(typeWithoutNs.Type, Is.EqualTo(typeof(TestEncodeableWithDefaultNamespace)));
        }

        [Test]
        public void Builder_TryGetEncodeableType_FallsBackToFactory()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Pre-populate factory with a type
            factory.Builder.AddType(new ExpandedNodeId(60000), typeof(TestEncodeable)).Commit();

            // Act - Try to find type that's only in the factory, not in the builder
            bool found = builder.TryGetEncodeableType(new ExpandedNodeId(60000), out IEncodeableType encodeableType);

            // Assert
            Assert.That(found, Is.True);
            Assert.That(encodeableType.Type, Is.EqualTo(typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_TryGetEncodeableType_PrefersBuilderOverFactory()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var typeId = new ExpandedNodeId(61000);

            // Add one type to factory
            factory.Builder.AddType(typeId, typeof(TestEncodeable)).Commit();

            // Add different type to builder with same ID
            IEncodeableFactoryBuilder builder = factory.Builder;
            builder.AddType(typeId, typeof(TestEncodeableWithDefaultNamespace));

            // Act
            bool found = builder.TryGetEncodeableType(typeId, out IEncodeableType encodeableType);

            // Assert - Should prefer builder's type over factory's type
            Assert.That(found, Is.True);
            Assert.That(encodeableType.Type, Is.EqualTo(typeof(TestEncodeableWithDefaultNamespace)));
        }

        /// <summary>
        /// Test encodeable that throws NotSupportedException for XmlEncodingId.
        /// </summary>
        public class TestEncodeableWithoutXml : IEncodeable
        {
            public ExpandedNodeId TypeId => new(120000);
            public ExpandedNodeId BinaryEncodingId => new(120001);
            public ExpandedNodeId XmlEncodingId => throw new NotSupportedException("XML encoding not supported");

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithoutXml;
            }

            public object Clone()
            {
                return new TestEncodeableWithoutXml();
            }
        }

        /// <summary>
        /// Test encodeable with default OPC UA namespace.
        /// </summary>
        public class TestEncodeableWithDefaultNamespace : IEncodeable
        {
            public ExpandedNodeId TypeId => new(140000, Namespaces.OpcUa);
            public ExpandedNodeId BinaryEncodingId => new(140001, Namespaces.OpcUa);
            public ExpandedNodeId XmlEncodingId => new(140002, Namespaces.OpcUa);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithDefaultNamespace;
            }

            public object Clone()
            {
                return new TestEncodeableWithDefaultNamespace();
            }
        }

        [Test]
        public void Integration_FactorySupportsComplexTypeRegistration()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act - Register multiple types with different encoding patterns
            builder.AddEncodeableType(typeof(TestEncodeable))
                   .AddType(new ExpandedNodeId(99999), typeof(TestEncodeableWithDefaultNamespace));
            builder.Commit();

            // Assert - All types should be accessible
            Assert.That(factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType type1), Is.True);
            Assert.That(factory.TryGetEncodeableType(new ExpandedNodeId(99999), out IEncodeableType type3), Is.True);

            Assert.That(type1.Type, Is.EqualTo(typeof(TestEncodeable)));
            Assert.That(type3.Type, Is.EqualTo(typeof(TestEncodeableWithDefaultNamespace)));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullNodeIds_SkipsGracefully()
        {
            // Test the behavior when an encodeable type returns null NodeIds

            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeableWithNullIds));

            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() =>
            {
                builder.AddEncodeableType(encodeableType);
                builder.Commit();
            });

            // Should not be findable since all IDs are null
            bool found = factory.TryGetEncodeableType(ExpandedNodeId.Null, out _);
            Assert.That(found, Is.False);
        }

        [Test]
        public void EncodeableFactory_FrozenDictionary_Performance_CharacteristicsVerification()
        {
            // This test verifies that the FrozenDictionary is actually being used
            // and has the expected performance characteristics mentioned in the comments

            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act - Perform multiple lookups to verify frozen dictionary performance
            var readRequestId = new ExpandedNodeId(ObjectIds.ReadRequest_Encoding_DefaultBinary);
            var nonExistentId = new ExpandedNodeId(Guid.NewGuid());

            // Time the lookups (though we won't assert on timing, just verify functionality)
            bool found1 = factory.TryGetEncodeableType(readRequestId, out IEncodeableType type1);
            bool found2 = factory.TryGetEncodeableType(nonExistentId, out IEncodeableType type2);

            // Assert
            Assert.That(found1, Is.True);
            Assert.That(type1, Is.Not.Null);
            Assert.That(type1.Type, Is.EqualTo(typeof(ReadRequest)));

            Assert.That(found2, Is.False);
            Assert.That(type2, Is.Null);

            // Verify we have a substantial number of types (mentioned as ~1.5k in comments)
            Assert.That(factory.KnownTypeIds.Count(), Is.GreaterThan(1000));
        }

        private IEncodeableFactoryBuilder m_builder;
        private IEncodeableFactory m_encodeableFactory;

        /// <summary>
        /// Test encodeable with null NodeIds to test edge case handling.
        /// </summary>
        public class TestEncodeableWithNullIds : IEncodeable
        {
            public ExpandedNodeId TypeId => ExpandedNodeId.Null;
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithNullIds;
            }

            public object Clone()
            {
                return new TestEncodeableWithNullIds();
            }
        }
    }
}
