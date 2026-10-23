// What the living world needs to know about each species (the web version's species.js, life
// part only; looks and swimming live with the fish rendering). Ages are in real days.
using System.Collections.Generic;

namespace Aquarium.Life
{
    public sealed class Breeding
    {
        /// <summary>Days between broods of a pair.</summary>
        public double Every;
        /// <summary>Fry per brood, from..to.</summary>
        public double BroodMin, BroodMax;
        /// <summary>Population cap for the species.</summary>
        public int Cap;
        /// <summary>Livebearer (fry are born, no nest).</summary>
        public bool Live;
        /// <summary>Parents guard the nest and the fry.</summary>
        public bool Care;
    }

    public sealed class LifeSpan
    {
        /// <summary>Age when a fry becomes a juvenile, and when a juvenile grows up.</summary>
        public double Juvenile, Adult;
        /// <summary>A comfortable life span.</summary>
        public double Span;
        /// <summary>Null for species that do not breed in the aquarium.</summary>
        public Breeding Breed;
    }

    public sealed class SpeciesInfo
    {
        public string Id, Title;
        /// <summary>How many live in a new aquarium.</summary>
        public int Count;
        /// <summary>Number of colour variants (0: one look).</summary>
        public int Variants;
        /// <summary>How strongly they keep together, 0..1 (above 0.6: a real school).</summary>
        public double School;
        public bool Rare;
        /// <summary>Personality defaults, in the web version's key order (seeded founders depend on it).</summary>
        public (string key, double value)[] Traits;
        public LifeSpan Life;
    }

    public static class Species
    {
        static (string, double)[] T(double activity, double bravery, double curiosity, double playfulness,
            double shyness, double aggression, double gluttony, double territoriality) => new (string, double)[]
        {
            ("activity", activity), ("bravery", bravery), ("curiosity", curiosity), ("playfulness", playfulness),
            ("shyness", shyness), ("aggression", aggression), ("gluttony", gluttony), ("territoriality", territoriality),
        };

        static Breeding B(double every, double min, double max, int cap, bool live = false, bool care = false) =>
            new Breeding { Every = every, BroodMin = min, BroodMax = max, Cap = cap, Live = live, Care = care };

        static LifeSpan L(double juvenile, double adult, double span, Breeding breed) =>
            new LifeSpan { Juvenile = juvenile, Adult = adult, Span = span, Breed = breed };

        /// <summary>All species in the web version's order (a new aquarium is populated in this order).</summary>
        public static readonly IReadOnlyList<SpeciesInfo> All = new[]
        {
            new SpeciesInfo { Id = "neon", Title = "Неон", Count = 16, School = 1.0,
                Traits = T(0.8, 0.3, 0.4, 0.6, 0.6, 0.05, 0.6, 0.0), Life = L(1, 3, 90, B(6, 3, 7, 22)) },
            new SpeciesInfo { Id = "guppy", Title = "Гуппи", Count = 7, Variants = 7, School = 0.45,
                Traits = T(0.75, 0.5, 0.6, 0.8, 0.3, 0.1, 0.7, 0.0), Life = L(0.6, 2, 60, B(4, 2, 5, 12, live: true)) },
            new SpeciesInfo { Id = "goldfish", Title = "Золотая рыбка", Count = 2, Variants = 2, School = 0,
                Traits = T(0.5, 0.8, 0.6, 0.5, 0.1, 0.15, 1.0, 0.0), Life = L(2, 8, 180, B(12, 2, 4, 4)) },
            new SpeciesInfo { Id = "angelfish", Title = "Скалярия", Count = 3, School = 0.15,
                Traits = T(0.35, 0.6, 0.5, 0.2, 0.3, 0.3, 0.6, 0.3), Life = L(2, 7, 150, B(10, 2, 4, 4, care: true)) },
            new SpeciesInfo { Id = "clown", Title = "Рыба-клоун", Count = 2, School = 0,
                Traits = T(0.6, 0.7, 0.5, 0.5, 0.2, 0.45, 0.6, 0.7), Life = L(2, 7, 150, B(10, 2, 3, 4, care: true)) },
            new SpeciesInfo { Id = "cory", Title = "Сомик-коридорас", Count = 5, School = 0.35,
                Traits = T(0.6, 0.4, 0.8, 0.4, 0.4, 0, 0.8, 0), Life = L(1.5, 5, 120, B(7, 2, 5, 8)) },
            new SpeciesInfo { Id = "cichlid", Title = "Цихлида", Count = 2, Variants = 2, School = 0,
                Traits = T(0.6, 0.9, 0.5, 0.2, 0.05, 0.85, 0.8, 1.0), Life = L(1.5, 6, 120, B(9, 2, 4, 3, care: true)) },
            new SpeciesInfo { Id = "discus", Title = "Дискус", Count = 2, Variants = 2, School = 0.2,
                Traits = T(0.3, 0.4, 0.4, 0.2, 0.6, 0.1, 0.5, 0.2), Life = L(3, 10, 200, B(14, 2, 3, 4, care: true)) },
            new SpeciesInfo { Id = "barb", Title = "Суматранский барбус", Count = 7, School = 0.85,
                Traits = T(0.95, 0.7, 0.6, 0.9, 0.1, 0.5, 0.8, 0), Life = L(1, 3, 90, B(6, 3, 6, 10)) },
            new SpeciesInfo { Id = "betta", Title = "Петушок", Count = 1, Variants = 1, School = 0,
                Traits = T(0.35, 0.9, 0.6, 0.2, 0.1, 0.7, 0.7, 0.9), Life = L(1, 4, 80, null) },
            new SpeciesInfo { Id = "rainbow", Title = "Радужница", Count = 4, School = 0.8,
                Traits = T(0.9, 0.6, 0.5, 0.7, 0.2, 0.1, 0.6, 0), Life = L(1.5, 5, 120, B(8, 2, 5, 8)) },
            new SpeciesInfo { Id = "moonfish", Title = "Лунная рыбка", Count = 1, School = 0, Rare = true,
                Traits = T(0.4, 0.4, 0.9, 0.6, 0.7, 0, 0.3, 0), Life = L(1, 3, 365, null) },
        };

        static Dictionary<string, SpeciesInfo> byId;

        public static SpeciesInfo Get(string id)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, SpeciesInfo>();
                foreach (var s in All) byId[s.Id] = s;
            }
            return byId[id];
        }

        public static readonly IReadOnlyList<string> Names = new[]
        {
            "Боб", "Шустрик", "Капитан", "Толстяк", "Пират", "Искорка", "Бублик", "Зефир", "Марго", "Сырник", "Кнопка", "Тоша",
            "Лучик", "Пончик", "Граф", "Ириска", "Вихрь", "Мармелад", "Фантик", "Сёма", "Буля", "Звёздочка", "Бисер", "Кекс",
            "Хвостик", "Плюх", "Адмирал", "Профессор", "Ниндзя", "Соня", "Чудик", "Огонёк", "Барон", "Жемчуг", "Каспер", "Рыжик",
            "Тень", "Бусинка", "Шкипер", "Маэстро", "Пузырь", "Персик", "Фунтик", "Изюм", "Комета", "Бриз", "Ватсон", "Дюна",
            "Финик", "Луна", "Мичман", "Кокос", "Сапфир", "Дракоша", "Ёжик", "Тыковка", "Барабан", "Сморчок", "Шерлок", "Юнга",
            "Карамель", "Неон", "Призрак", "Гусар", "Прыг", "Монетка", "Тучка", "Лаки", "Мусс", "Пинг", "Понг", "Батон",
        };
    }
}
