// Russian words that change with numbers (ported from the web version's words.js).
using System;

namespace Aquarium.Life
{
    public static class Words
    {
        /// <summary>Plural(5, "день", "дня", "дней") -> "дней".</summary>
        public static string Plural(long n, string one, string few, string many)
        {
            long a = Math.Abs(n) % 100, b = a % 10;
            return a > 10 && a < 20 ? many : b > 1 && b < 5 ? few : b == 1 ? one : many;
        }

        public static string Count(long n, string one, string few, string many) => $"{n} {Plural(n, one, few, many)}";

        public static string FryWord(long n) => Count(n, "малёк", "малька", "мальков");
        public static string FishWord(long n) => Count(n, "рыбка", "рыбки", "рыбок");

        /// <summary>"3 дня", "1 день 5 часов", "5 часов 20 мин", "12 мин".</summary>
        public static string Duration(TimeSpan span)
        {
            long m = Math.Max(1, (long)Math.Floor(span.TotalMinutes + 0.5)), hr = m / 60, d = hr / 24;
            if (d >= 1)
                return Count(d, "день", "дня", "дней") + (d < 3 && hr % 24 != 0 ? " " + Count(hr % 24, "час", "часа", "часов") : "");
            if (hr >= 1)
                return Count(hr, "час", "часа", "часов") + (m % 60 != 0 ? $" {m % 60} мин" : "");
            return $"{m} мин";
        }
    }
}
