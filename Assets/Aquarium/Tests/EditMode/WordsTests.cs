using System;
using NUnit.Framework;

namespace Aquarium.Life.Tests
{
    public class WordsTests
    {
        [TestCase(1, "рыбка")]
        [TestCase(2, "рыбки")]
        [TestCase(4, "рыбки")]
        [TestCase(5, "рыбок")]
        [TestCase(11, "рыбок")]
        [TestCase(12, "рыбок")]
        [TestCase(14, "рыбок")]
        [TestCase(21, "рыбка")]
        [TestCase(22, "рыбки")]
        [TestCase(25, "рыбок")]
        [TestCase(111, "рыбок")]
        [TestCase(101, "рыбка")]
        [TestCase(0, "рыбок")]
        public void PluralFollowsRussianRules(int n, string expected)
        {
            Assert.AreEqual($"{n} {expected}", Words.FishWord(n));
        }

        [Test]
        public void FryWord() => Assert.AreEqual("3 малька", Words.FryWord(3));

        [Test]
        public void DurationMinutes() => Assert.AreEqual("12 мин", Words.Duration(TimeSpan.FromMinutes(12)));

        [Test]
        public void DurationNeverZero() => Assert.AreEqual("1 мин", Words.Duration(TimeSpan.FromSeconds(5)));

        [Test]
        public void DurationHoursAndMinutes() => Assert.AreEqual("5 часов 20 мин", Words.Duration(TimeSpan.FromMinutes(5 * 60 + 20)));

        [Test]
        public void DurationWholeHours() => Assert.AreEqual("2 часа", Words.Duration(TimeSpan.FromHours(2)));

        [Test]
        public void DurationShortDaysKeepHours() => Assert.AreEqual("1 день 5 часов", Words.Duration(TimeSpan.FromHours(29)));

        [Test]
        public void DurationLongDaysDropHours() => Assert.AreEqual("4 дня", Words.Duration(TimeSpan.FromHours(4 * 24 + 7)));
    }
}
