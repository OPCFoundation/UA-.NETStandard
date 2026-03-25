using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NumericRangeTests
    {
        private static IEnumerable<TestCaseData> SubRangesCombinations
        {
            get
            {
                NumericRange[] range1 = [new(1, 3)];
                NumericRange[] range2 = [new(2, 4)];
                NumericRange[] empty = [];
                NumericRange[] nullValue = null;

                yield return new TestCaseData(range1, range2).Returns(false);
                yield return new TestCaseData(empty, range1).Returns(false);
                yield return new TestCaseData(range1, empty).Returns(false);
                yield return new TestCaseData(nullValue, range1).Returns(false);
                yield return new TestCaseData(range1, nullValue).Returns(false);

                yield return new TestCaseData(range1, range1).Returns(true);
                yield return new TestCaseData(nullValue, nullValue).Returns(true);
            }
        }

        [TestCaseSource(nameof(SubRangesCombinations))]
        public bool Equals_ForSubRanges(NumericRange[] subRanges1, NumericRange[] subRanges2)
        {
            var range1 = new NumericRange(1, 5)
            {
                SubRanges = subRanges1
            };

            var range2 = new NumericRange(1, 5)
            {
                SubRanges = subRanges2
            };

            return range1.Equals(range2);
        }

        /// <remarks>
        /// NOTE: Could theoretically fail for hash collisions
        /// </remarks>
        [TestCaseSource(nameof(SubRangesCombinations))]
        public bool GetHashCode_ForSubRanges(NumericRange[] subRanges1, NumericRange[] subRanges2)
        {
            var range1 = new NumericRange(1, 5)
            {
                SubRanges = subRanges1
            };

            var range2 = new NumericRange(1, 5)
            {
                SubRanges = subRanges2
            };

            return range1.GetHashCode() == range2.GetHashCode();
        }
    }
}
