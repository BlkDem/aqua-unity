// The glass interface (the web version's ui.js) in UI Toolkit, built in code: the dock, the
// story feed, a hint, the hover label, the card of a creature (with renaming), the roster
// with the history and what is happening now, the settings, "while you were away", rings
// around the selected creature and where the glass was knocked, and the immersive modes'
// letterbox, fade and exit button. Styles: UI/Aquarium.uss.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Aquarium.Fish;
using Aquarium.Life;
using Aquarium.World;
using UnityEngine;
using UnityEngine.UIElements;
using FishT = Aquarium.Fish.Fish;

namespace Aquarium.App
{
    public sealed class AquariumUI
    {
        static readonly CultureInfo Ru = new CultureInfo("ru-RU");

        readonly AquariumShell shell;
        readonly VisualElement root, ui, feed, hintBox, card, label, ringSel, barTop, barBottom, fadeEl, loader, dockWrap;
        readonly Label hintText, fps;
        readonly Button exitBtn;
        readonly Dictionary<string, VisualElement> panels = new Dictionary<string, VisualElement>();
        Button dayBtn, fullBtn;
        object cardSubject;
        IVisualElementScheduledItem hintTimer, exitTimer;
        readonly List<(VisualElement el, float t, Vector2 at)> taps = new List<(VisualElement, float, Vector2)>();
        int frame;
        public bool showStories = true;

        AquariumApp App => shell.app;
        Ecosystem Eco => shell.app.Eco;

        static VisualElement E(string cls = null, VisualElement parent = null)
        {
            var e = new VisualElement();
            if (cls != null) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        static Label L(string text, string cls = null, VisualElement parent = null)
        {
            var l = new Label(text);
            if (cls != null) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        static Button B(string text, string cls, Action click, VisualElement parent = null)
        {
            var b = new Button(click) { text = text };
            if (cls != null) foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        static Button IconButton(string icon, string cls, Action click, VisualElement parent)
        {
            var b = B("", cls, click, parent);
            b.Add(new Icon(icon));
            return b;
        }

        /// <summary>Add a class on the next frame, so the transition to it plays.</summary>
        static void Later(VisualElement e, Action a, long ms = 30) => e.schedule.Execute(a).StartingIn(ms);

        public AquariumUI(AquariumShell shell, VisualElement root)
        {
            this.shell = shell;
            this.root = root;
            root.AddToClassList("screen");
            root.pickingMode = PickingMode.Ignore;

            // the immersive modes' bars and fade sit under everything else
            barTop = E("bar top", root);
            barBottom = E("bar bottom", root);
            fadeEl = E("fade", root);
            foreach (var e in new[] { barTop, barBottom, fadeEl }) e.pickingMode = PickingMode.Ignore;

            ringSel = E("ring", root);
            ringSel.pickingMode = PickingMode.Ignore;

            ui = E("ui screen", root);
            ui.pickingMode = PickingMode.Ignore;

            feed = E("stories", ui);
            feed.pickingMode = PickingMode.Ignore;

            // dock
            dockWrap = E("dock-wrap chrome", ui);
            dockWrap.pickingMode = PickingMode.Ignore;
            var dock = E("dock", dockWrap);
            DockButton(dock, "feed", "Кормить", "feed", b =>
            {
                shell.Feed();
                b.AddToClassList("pulse");
                Later(b, () => b.RemoveFromClassList("pulse"), 600);
            });
            DockButton(dock, "observe", "Наблюдать", "watch", _ => shell.SetMode(CameraMode.Observe));
            DockButton(dock, "cine", "Кино", "cine", _ => shell.SetMode(CameraMode.Cinematic));
            DockButton(dock, "cam", "Камера", "cam", _ => Hint($"Камера: {shell.director.CycleView()}"));
            DockButton(dock, "fish", "Обитатели", "fish", _ => TogglePanel("roster"));
            dayBtn = DockButton(dock, "day", "Авто", "sun", _ => shell.CycleDayMode());
            DockButton(dock, "settings", "Настройки", "gear", _ => TogglePanel("settings"));
            if (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer)
                fullBtn = DockButton(dock, "full", "Экран", "full", _ => shell.ToggleFullScreen());

            hintBox = E("hint chrome", ui);
            hintBox.pickingMode = PickingMode.Ignore;
            hintText = L("", null, hintBox);

            card = E("card hidden chrome", ui);
            card.style.display = DisplayStyle.None;

            panels["roster"] = BuildRoster();
            panels["settings"] = BuildSettings();

            label = E("hover-label", root);
            label.pickingMode = PickingMode.Ignore;

            exitBtn = B("Выйти", "exit-btn", () => shell.SetMode(CameraMode.Free), root);
            exitBtn.Insert(0, new Icon("close") { style = { width = 12, height = 12, marginRight = 6 } });
            exitBtn.style.flexDirection = FlexDirection.Row;

            fps = L("", "fps chrome", root);
            fps.pickingMode = PickingMode.Ignore;

            loader = E("loader", root);
            L("Живой аквариум", "loader-title", loader);
            L("маленький подводный мир просыпается…", "loader-sub", loader);
        }

        Button DockButton(VisualElement dock, string id, string text, string icon, Action<Button> click)
        {
            Button b = null;
            b = new Button(() => click(b));
            b.AddToClassList("dock-btn");
            b.AddToClassList(id);
            b.Add(new Icon(icon));
            L(text, "lbl", b);
            dock.Add(b);
            return b;
        }

        static void SetLabel(Button b, string text) => b.Q<Label>(className: "lbl").text = text;

        // ---------------------------------------------------------------- state from outside
        public void Started(AwaySummary away)
        {
            Later(loader, () => loader.AddToClassList("gone"), 300);
            Later(loader, () => loader.RemoveFromHierarchy(), 2000);
            if (away != null) Later(root, () => ShowAway(away), 1200);
            else Later(root, () => Hint("Кликните по стеклу, наведите на рыбку или нажмите «Кормить»"), 2100);
        }

        public void SetDayLabel(string text) => SetLabel(dayBtn, text);
        public void SetFullScreen(bool on)
        {
            if (fullBtn == null) return;
            fullBtn.Q<Icon>().Set(on ? "unfull" : "full");
            SetLabel(fullBtn, on ? "Окно" : "Экран");
        }

        public void SetImmersive(bool on)
        {
            ui.EnableInClassList("immersive", on);
            ui.pickingMode = PickingMode.Ignore;
            foreach (var p in panels.Values) Close(p);
            if (on) HideCard();
            exitBtn.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The pointer moved during an immersive mode: show the exit button for a moment.</summary>
        public void PokeExit()
        {
            exitBtn.AddToClassList("show");
            exitTimer?.Pause();
            exitTimer = exitBtn.schedule.Execute(() => exitBtn.RemoveFromClassList("show")).StartingIn(2200);
        }

        public void SetIdle(bool idle) => root.EnableInClassList("idle", idle);
        public void SetNarrow(bool narrow) => root.EnableInClassList("narrow", narrow);

        /// <summary>Whether something modal-ish is open (the chrome does not hide then).</summary>
        public bool Busy => panels.Values.Any(p => p.ClassListContains("open")) || root.Q(className: "away") != null;

        public void Hint(string text)
        {
            hintText.text = text;
            hintBox.AddToClassList("show");
            hintTimer?.Pause();
            hintTimer = hintBox.schedule.Execute(() => hintBox.RemoveFromClassList("show")).StartingIn(2200);
        }

        public void ShowFps(bool on) => fps.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        public void SetFps(string text) => fps.text = text;

        // ---------------------------------------------------------------- stories
        static string Clean(string s) => s.Replace("✨", "").Trim();

        public void Story(Story s)
        {
            if (!showStories) return;
            var el = E("story" + (s.rare ? " rare" : ""));
            E("dot", el);
            L(Clean(s.text), null, el);
            if (s.focus != null)
            {
                el.AddToClassList("clickable");
                el.RegisterCallback<ClickEvent>(_ => shell.Select(s.focus));
            }
            else el.pickingMode = PickingMode.Ignore;
            feed.Insert(0, el);
            Later(el, () => el.AddToClassList("in"));
            while (feed.childCount > 4) feed.RemoveAt(feed.childCount - 1);
            el.schedule.Execute(() =>
            {
                el.RemoveFromClassList("in");
                el.AddToClassList("out");
                Later(el, el.RemoveFromHierarchy, 900);
            }).StartingIn(s.rare ? 9000 : 6500);
            if (IsOpen("roster") && rosterTab == "log") RenderLog();
        }

        // ---------------------------------------------------------------- panels
        bool IsOpen(string id) => panels[id].ClassListContains("open");

        static void Open(VisualElement p)
        {
            p.style.display = DisplayStyle.Flex;
            Later(p, () => p.AddToClassList("open"));
        }

        static void Close(VisualElement p)
        {
            if (!p.ClassListContains("open")) return;
            p.RemoveFromClassList("open");
            Later(p, () => { if (!p.ClassListContains("open")) p.style.display = DisplayStyle.None; }, 320);
        }

        public void TogglePanel(string id)
        {
            foreach (var (k, p) in panels.Select(kv => (kv.Key, kv.Value)))
            {
                if (k == id && !p.ClassListContains("open")) Open(p);
                else Close(p);
            }
            if (id == "roster") ShowRosterTab(rosterTab);
        }

        VisualElement Panel(string title, string cls)
        {
            var p = E("panel chrome " + cls, ui);
            var head = E("panel-head", p);
            L(title, null, head);
            IconButton("close", "x", () => Close(p), head);
            return p;
        }

        (VisualElement bar, Dictionary<string, Button> buttons) Tabs(VisualElement parent, (string id, string title)[] tabs, Action<string> pick)
        {
            var bar = E("tabs", parent);
            var buttons = new Dictionary<string, Button>();
            foreach (var (id, title) in tabs) buttons[id] = B(title, null, () => pick(id), bar);
            return (bar, buttons);
        }

        // roster: residents, history, now
        string rosterTab = "list";
        Dictionary<string, Button> rosterTabs;
        ScrollView rosterBody, histBody, logBody;
        Label secrets;

        VisualElement BuildRoster()
        {
            var p = Panel("Обитатели", "roster");
            rosterTabs = Tabs(p, new[] { ("list", "Жители"), ("hist", "История"), ("log", "Сейчас") }, ShowRosterTab).buttons;
            rosterBody = new ScrollView { mode = ScrollViewMode.Vertical };
            histBody = new ScrollView { mode = ScrollViewMode.Vertical };
            logBody = new ScrollView { mode = ScrollViewMode.Vertical };
            foreach (var b in new[] { rosterBody, histBody, logBody }) { b.AddToClassList("body"); p.Add(b); }
            secrets = L("", "secrets", p);
            ShowRosterTab("list");
            return p;
        }

        public void ShowRosterTab(string t)
        {
            rosterTab = t;
            foreach (var kv in rosterTabs) kv.Value.EnableInClassList("on", kv.Key == t);
            rosterBody.style.display = t == "list" ? DisplayStyle.Flex : DisplayStyle.None;
            histBody.style.display = t == "hist" ? DisplayStyle.Flex : DisplayStyle.None;
            logBody.style.display = t == "log" ? DisplayStyle.Flex : DisplayStyle.None;
            if (App == null || App.Eco == null) return;
            if (t == "list") RenderRoster();
            if (t == "hist") RenderHistory();
            if (t == "log") RenderLog();
        }

        void RenderRoster()
        {
            rosterBody.Clear();
            var groups = new List<(string title, FishSpecies sp, List<object> list)>();
            void Put(string title, FishSpecies sp, object o)
            {
                int i = groups.FindIndex(g => g.title == title);
                if (i < 0) { groups.Add((title, sp, new List<object>())); i = groups.Count - 1; }
                groups[i].list.Add(o);
            }
            foreach (var f in Eco.fish) if (!(f.hidden && f.sp.Rare)) Put(f.sp.Title, f.sp, f);
            foreach (var c in Eco.critters) Put(c.title, null, c);
            foreach (var (title, sp, list) in groups)
            {
                var g = E("group", rosterBody);
                var gt = E("gtitle", g);
                L(title.ToUpper(Ru), null, gt);
                L(list.Count.ToString(), "count", gt);
                if (sp != null && !sp.Rare && list.Count < (sp.Life.Life.Breed?.Cap ?? sp.Life.Count))
                {
                    // the user may bring in another fish of this kind
                    var spId = sp.id;
                    var add = B("+", "add", () =>
                    {
                        var f = Eco.AddFish(spId);
                        if (f == null) return;
                        RenderRoster();
                        Hint($"{f.sp.Title} {f.name} теперь живёт в аквариуме");
                    }, gt);
                    add.tooltip = "Добавить ещё одну";
                }
                IEnumerable<object> sorted = sp != null ? list.Cast<FishT>().OrderByDescending(f => (int)f.stage).Cast<object>() : list;
                foreach (var o in sorted)
                {
                    var row = E("row", g);
                    var nm = E("nm-box", row);
                    if (o is FishT f)
                    {
                        L(f.name, "nm", nm);
                        if (f.stage != Stage.Adult) L(Lifecycle.StageTitle(f.stage), "badge " + Lifecycle.StageKey(f.stage), nm);
                        if (f.rec.Gene.Rare != null) nm.Add(new Icon("star"));
                        L(f.StatusText(), "st", row);
                    }
                    else if (o is Critter c)
                    {
                        L(c.name, "nm", nm);
                        L(c.Status, "st", row);
                    }
                    var target = o;
                    row.RegisterCallback<ClickEvent>(_ => shell.Select(target));
                }
            }
            secrets.text = $"Найдено секретов: {App.World.State.Secrets.Count} из 6";
        }

        /// <summary>The aquarium's history, newest first, grouped by day.</summary>
        void RenderHistory()
        {
            histBody.Clear();
            var hist = App.World.State.History;
            if (hist.Count == 0) { L("История только начинается…", "empty", histBody); return; }
            string lastDay = "";
            for (int i = hist.Count - 1; i >= 0; i--)
            {
                var e = hist[i];
                var d = DateTimeOffset.FromUnixTimeMilliseconds((long)e.T).LocalDateTime;
                string day = d.ToString("d MMMM", Ru);
                if (day != lastDay) { L(day, "hday", histBody); lastDay = day; }
                var row = E($"hist {e.Kind}", histBody);
                L(d.ToString("HH:mm", Ru), "tm", row);
                L(Clean(e.Text), "tx", row);
            }
        }

        void RenderLog()
        {
            logBody.Clear();
            if (Eco.storyLog.Count == 0) { L("Пока ничего не произошло… понаблюдайте немного", "empty", logBody); return; }
            foreach (var s in Eco.storyLog)
            {
                var row = E("log" + (s.rare ? " rare" : ""), logBody);
                L($"{(int)(s.t / 60)}:{(int)(s.t % 60):00}", "tm", row);
                L(Clean(s.text), "tx", row);
            }
        }

        // settings: the aquarium, the picture, the sound
        Dictionary<string, Button> setTabs;
        readonly Dictionary<string, VisualElement> setBodies = new Dictionary<string, VisualElement>();

        VisualElement BuildSettings()
        {
            var p = Panel("Настройки", "settings");
            setTabs = Tabs(p, new[] { ("life", "Аквариум"), ("view", "Изображение"), ("sound", "Звук") }, ShowSettingsTab).buttons;
            foreach (var id in new[] { "life", "view", "sound" })
            {
                var sv = new ScrollView { mode = ScrollViewMode.Vertical };
                sv.AddToClassList("body");
                p.Add(sv);
                setBodies[id] = sv;
            }
            ShowSettingsTab(Prefs.Get("tab", "life"));
            return p;
        }

        void ShowSettingsTab(string t)
        {
            foreach (var kv in setTabs) kv.Value.EnableInClassList("on", kv.Key == t);
            foreach (var kv in setBodies) kv.Value.style.display = kv.Key == t ? DisplayStyle.Flex : DisplayStyle.None;
            Prefs.Set("tab", t);
        }

        /// <summary>Fill the settings once the aquarium is running (they show its values).</summary>
        public void FillSettings()
        {
            var F = App.World.State.Feeder;
            var life = setBodies["life"];
            life.Clear();
            L("АВТОКОРМУШКА", "sect", life);
            Check(life, "Кормить по расписанию", F.On, v =>
            {
                F.On = v;
                Eco.Save();
                Hint(v ? "Автокормушка включена" : "Автокормушка выключена — рыбки не пропадут, просто кормите сами, когда захочется");
            });
            Choice(life, "Кормлений в день", new[] { ("2", "2 раза"), ("3", "3 раза"), ("4", "4 раза") }, F.PerDay.ToString(), v => { F.PerDay = int.Parse(v); Eco.Save(); });
            Choice(life, "Корм", new[] { ("flakes", "Хлопья"), ("pellets", "Гранулы — для донных"), ("worms", "Мотыль — лакомство") }, F.Food, v => { F.Food = v; Eco.Save(); });
            L("ВРЕМЯ И СОБЫТИЯ", "sect", life);
            Range(life, "Длина суток", 2, 30, shell.day.cycleMinutes, v => $"{v:0} мин", v => shell.SetPref("cycle", Mathf.Round(v)), true);
            Check(life, "Показывать истории", showStories, v => shell.SetPref("stories", v ? 1 : 0));
            L("РЕЗЕРВНАЯ КОПИЯ", "sect", life);
            L("Весь аквариум — рыбки, их семьи, растения и история — в одном файле. Его можно сохранить на память или перенести на другое устройство (в том числе в веб-версию и обратно).", "tip", life);
            var bk = E("backup", life);
            B("Сохранить в файл", "wide", () => Hint($"Копия сохранена: {Path.GetFileName(shell.SaveBackup())}"), bk);
            B("Загрузить из файла", "wide", ShowBackups, bk);
            B("Открыть папку с копиями", "wide", () => Application.OpenURL("file:///" + Backup.Dir.Replace('\\', '/')), life);
            L("Кликните по стеклу, чтобы постучать. Наведите на рыбку, чтобы узнать имя. В аквариуме спрятано несколько секретов…", "tip", life);

            var view = setBodies["view"];
            view.Clear();
            qualityChoice = Choice(view, "Качество графики", QualitySettings.names.Select((n, i) => (i.ToString(), n)).ToArray(), shell.quality.Level.ToString(),
                v => { shell.SetPref("quality", int.Parse(v)); Hint($"Качество: {QualitySettings.names[int.Parse(v)]}"); });
            Check(view, "Авто-разрешение: держать 30+ FPS", shell.quality.auto, v => shell.SetPref("autores", v ? 1 : 0));
            Check(view, "Глубина резкости (в режимах «Наблюдать» и «Кино»)", shell.director.dofOn, v => shell.SetPref("dof", v ? 1 : 0));
            Range(view, "Свечение", 0, 1.5f, Prefs.Get("bloom", 0.38f), v => $"{Mathf.RoundToInt(v * 100)}%", v => shell.SetPref("bloom", v));
            Range(view, "Яркость", 0.6f, 1.6f, shell.day.exposureMul, v => $"{Mathf.RoundToInt(v * 100)}%", v => shell.SetPref("exposure", v));
            Check(view, "Показывать FPS и видеокарту", Prefs.Get("fps", 1f) > 0.5f, v => shell.SetPref("fps", v ? 1 : 0));
            L("ВИДЕОКАРТА", "sect", view);
            L($"Сейчас рисует: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} МБ)", "tip", view);
            L("Приложение само выбирает мощную видеокарту, если она есть. Если ноутбук всё же запускает его на встроенной: Параметры Windows → Система → Дисплей → Графика → «Живой аквариум» → «Высокая производительность».", "tip", view);

            var sound = setBodies["sound"];
            sound.Clear();
            var a = shell.audio;
            Range(sound, "Громкость", 0, 1, a.master, v => $"{Mathf.RoundToInt(v * 100)}%", v => shell.SetPref("master", v));
            Range(sound, "Музыка", 0, 1, a.music, v => $"{Mathf.RoundToInt(v * 100)}%", v => shell.SetPref("music", v));
            Range(sound, "Звуки воды", 0, 1, a.fx, v => $"{Mathf.RoundToInt(v * 100)}%", v => shell.SetPref("fx", v));
        }

        static void Check(VisualElement parent, string text, bool on, Action<bool> changed)
        {
            var row = E("check" + (on ? " on" : ""), parent);
            E("box", row);
            L(text, null, row);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                bool v = !row.ClassListContains("on");
                row.EnableInClassList("on", v);
                changed(v);
            });
        }

        VisualElement qualityChoice;

        /// <summary>The preset changed by itself (lowered to keep it smooth): show it in the settings.</summary>
        public void QualityChanged(int level)
        {
            if (qualityChoice == null) return;
            int i = 0;
            foreach (var b in qualityChoice.Query<Button>().ToList()) b.EnableInClassList("on", i++ == level);
        }

        static VisualElement Choice(VisualElement parent, string title, (string value, string text)[] options, string current, Action<string> picked)
        {
            var box = E("setting", parent);
            var lab = E("lab", box);
            L(title, null, lab);
            var row = E("choice", box);
            var buttons = new List<(Button b, string v)>();
            foreach (var (value, text) in options)
            {
                var v = value;
                Button b = null;
                b = B(text, value == current ? "on" : null, () =>
                {
                    foreach (var (o, ov) in buttons) o.EnableInClassList("on", ov == v);
                    picked(v);
                }, row);
                buttons.Add((b, v));
            }
            return row;
        }

        static void Range(VisualElement parent, string title, float min, float max, float value, Func<float, string> fmt, Action<float> changed, bool whole = false)
        {
            var box = E("setting", parent);
            var lab = E("lab", box);
            L(title, null, lab);
            var val = L(fmt(value), "val", lab);
            var s = new Slider(min, max) { value = value };
            s.RegisterValueChangedCallback(e =>
            {
                float v = whole ? Mathf.Round(e.newValue) : e.newValue;
                val.text = fmt(v);
                changed(v);
            });
            box.Add(s);
        }

        // ---------------------------------------------------------------- dialogs
        VisualElement Dialog(string title, string sub)
        {
            var el = E("away", ui);
            L(title, "away-title", el);
            if (sub != null) L(sub, "away-sub", el);
            Later(el, () => el.AddToClassList("in"));
            return el;
        }

        static void CloseDialog(VisualElement el)
        {
            el.RemoveFromClassList("in");
            Later(el, el.RemoveFromHierarchy, 500);
        }

        /// <summary>"While you were away…": the pleasant changes since the last visit.</summary>
        public void ShowAway(AwaySummary a)
        {
            var items = new List<string>();
            int kids = a.Births.Sum(e => e.Kids?.Count ?? 0);
            if (kids > 0)
            {
                var bySp = a.Births.GroupBy(e => Species.Get(e.F.Sp).Title).Select(g => $"{g.Key.ToLower(Ru)} ×{g.Sum(e => e.Kids.Count)}");
                items.Add($"Появились мальки: {kids} — {string.Join(", ", bySp)}");
                var rare = a.Births.SelectMany(e => e.Kids).FirstOrDefault(k => k.Gene.Rare != null);
                if (rare != null) items.Add($"Среди них {rare.Name} — {Lifecycle.Rare[rare.Gene.Rare].Title}!");
            }
            if (a.Pairs.Count == 1) items.Add($"{a.Pairs[0].F.Name} и {a.Pairs[0].M.Name} стали парой");
            else if (a.Pairs.Count > 1) items.Add($"Новых пар: {a.Pairs.Count} — например, {a.Pairs[0].F.Name} и {a.Pairs[0].M.Name}");
            if (a.GrewJuvenile > 0) items.Add($"Подросли мальки: {a.GrewJuvenile}");
            if (a.GrewAdult > 0) items.Add($"Стали взрослыми: {a.GrewAdult}");
            foreach (var s in a.Schools) items.Add($"Стая «{s.Species.Title}» выросла: было {s.From}, теперь {s.To}");
            if (a.Plants > 0) items.Add($"Растения разрослись: молодых кустов — {a.Plants}");
            else if (a.Away > 86400000) items.Add("Растения заметно подросли");
            if (items.Count == 0) items.Add("Рыбки спокойно жили своей жизнью — и, кажется, ждали вас");

            var el = Dialog("Пока вас не было…", $"прошло {Words.Duration(TimeSpan.FromMilliseconds(a.Away))}");
            var list = E("away-list", el);
            foreach (var t in items)
            {
                var it = E("away-item", list);
                E("dot", it);
                L(t, null, it);
            }
            var actions = E("away-actions", el);
            B("Смотреть", null, () => CloseDialog(el), actions);
            if (kids > 0)
                B("Показать малышей", null, () =>
                {
                    CloseDialog(el);
                    var fry = Eco.fish.Where(f => f.stage == Stage.Fry && !f.hidden).ToList();
                    if (fry.Count > 0) shell.Select(Rnd.Pick(fry));
                }, actions);
        }

        /// <summary>Saved copies of the aquarium, newest first; picking one replaces the aquarium (after asking).</summary>
        void ShowBackups()
        {
            var files = Backup.List();
            var el = Dialog("Загрузить копию", files.Count == 0 ? $"Копий пока нет. Сохраните аквариум или положите файл из веб-версии в папку {Backup.Dir}" : "Нынешний аквариум будет заменён выбранной копией");
            var list = new ScrollView();
            list.AddToClassList("away-list");
            list.style.maxHeight = 260;
            el.Add(list);
            foreach (var (path, fish, when) in files)
            {
                var row = E("file-row", list);
                L(Path.GetFileNameWithoutExtension(path), "bold", row);
                L($"{Words.FishWord(fish)} · {when.ToString("d MMM HH:mm", Ru)}", "sub", row);
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    CloseDialog(el);
                    Confirm($"Заменить нынешний аквариум копией «{Path.GetFileNameWithoutExtension(path)}» ({Words.FishWord(fish)})? Нынешний пропадёт, если его не сохранить.",
                        () => { if (!shell.LoadBackup(path)) Hint("Не получилось загрузить копию"); });
                });
            }
            var actions = E("away-actions", el);
            B("Закрыть", null, () => CloseDialog(el), actions);
        }

        void Confirm(string text, Action yes)
        {
            var el = Dialog("Точно?", null);
            var it = E("away-item", E("away-list", el));
            L(text, null, it);
            var actions = E("away-actions", el);
            B("Заменить", null, () => { CloseDialog(el); yes(); }, actions);
            B("Отмена", null, () => CloseDialog(el), actions);
        }

        // ---------------------------------------------------------------- card
        Label cName, cStatus, cAge, cFav, cFamily, cFriends, cMemory;
        Button cFollow;

        public void ShowCard(object c)
        {
            cardSubject = c;
            card.style.display = DisplayStyle.Flex;
            Later(card, () => card.RemoveFromClassList("hidden"));
            RenderCard(true);
        }

        public void HideCard()
        {
            cardSubject = null;
            card.AddToClassList("hidden");
            Later(card, () => { if (cardSubject == null) card.style.display = DisplayStyle.None; }, 360);
        }

        public void FollowChanged() { if (cFollow != null) cFollow.text = shell.director.mode == CameraMode.Follow ? "Не следить" : "Следить"; }

        void RenderCard(bool full)
        {
            var c = cardSubject;
            if (c == null) return;
            var f = c as FishT;
            if (full)
            {
                card.Clear();
                IconButton("close", "x", () => shell.Select(null), card);
                var nr = E("name-row", card);
                cName = L(f?.name ?? ((Critter)c).name, "name", nr);
                cName.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) EditName(nr); });
                IconButton("edit", "edit", () => EditName(nr), nr).tooltip = "Переименовать";
                var sr = E("species-row", card);
                L(f != null ? f.sp.Title : ((Critter)c).title, "species", sr);
                if (f != null && f.sp.Life.Life.Breed != null) sr.Add(new Icon(f.sex == "f" ? "female" : "male"));
                if (f?.rec.Gene.Rare != null)
                {
                    var rt = E("rare-tag", card);
                    rt.Add(new Icon("star"));
                    L(Lifecycle.Rare[f.rec.Gene.Rare].Title, null, rt);
                }
                if (f != null)
                {
                    L(f.Character, "trait", card);
                    cAge = Kv("Возраст");
                    cFav = Kv("Любимое место");
                }
                cStatus = Kv("Сейчас");
                if (f != null)
                {
                    cFamily = Kv("Семья", true);
                    cFriends = Kv("Друзья", true);
                    cMemory = Kv("Помнит", true);
                }
                var actions = E("actions", card);
                cFollow = B("", null, () =>
                {
                    if (shell.director.mode == CameraMode.Follow) shell.director.Unfollow();
                    else shell.director.Follow(c);
                    FollowChanged();
                }, actions);
                FollowChanged();
            }
            if (f == null) { cStatus.text = ((Critter)c).Status; return; }
            cStatus.text = f.StatusText();
            cFav.text = f.fav?.name ?? "—";
            var w = App.World;
            cAge.text = $"{Lifecycle.StageTitle(f.stage)} · {Words.Duration(TimeSpan.FromMilliseconds(Math.Max(0, w.Now() - f.rec.Born)))}";
            var fam = new List<string>();
            if (f.mate != null) fam.Add($"пара — {f.mate.name}");
            if (f.rec.ParentNames != null) fam.Add($"родители — {string.Join(" и ", f.rec.ParentNames)}");
            int kids = w.Fish.Count(r => r.Parents != null && r.Parents.Contains(f.uid));
            if (kids > 0) fam.Add($"мальков — {kids}");
            cFamily.text = fam.Count > 0 ? string.Join(" · ", fam) : "пока сам по себе";
            var friends = f.mem.friends.Where(kv => kv.Value > 0.12f).OrderByDescending(kv => kv.Value).Take(3)
                .Select(kv => Eco.byId.TryGetValue(kv.Key, out var o) ? o.name : null).Where(n => n != null).ToList();
            cFriends.text = friends.Count > 0 ? string.Join(", ", friends) : "пока нет";
            cMemory.text = f.mem.last is var (who, kind, _) ? $"{kind}: {who}" : f.mem.foodSpot.HasValue ? "где дают корм" : "—";
        }

        Label Kv(string key, bool small = false)
        {
            var row = E("kv" + (small ? " small" : ""), card);
            L(key, "k", row);
            return L("", "v", row);
        }

        void EditName(VisualElement nameRow)
        {
            var c = cardSubject;
            var old = c is FishT f ? f.name : ((Critter)c).name;
            var input = new TextField { value = old, maxLength = 18 };
            input.AddToClassList("name-input");
            nameRow.Clear();
            nameRow.Add(input);
            bool done = false;
            void Finish(bool keep)
            {
                if (done) return;
                done = true;
                var v = input.value.Trim();
                if (keep && v.Length > 0 && v != old)
                {
                    if (c is FishT ff) Eco.Rename(ff, v); else ((Critter)c).name = v;
                    Hint($"Теперь это {v}");
                }
                RenderCard(true);
            }
            input.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Finish(true);
                if (e.keyCode == KeyCode.Escape) Finish(false);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            input.RegisterCallback<FocusOutEvent>(_ => Finish(true));
            Later(input, () => { input.Focus(); input.SelectAll(); });
        }

        /// <summary>A text field has the keyboard (the hotkeys are off then).</summary>
        public bool Typing => root.panel?.focusController?.focusedElement is TextField || root.panel?.focusController?.focusedElement?.GetType().Name.Contains("TextInput") == true;

        // ---------------------------------------------------------------- pointer feedback
        public void SetHover(object o, Vector2 panelPos)
        {
            if (o == null) { label.RemoveFromClassList("show"); return; }
            label.Clear();
            string name = o is FishT f ? f.name : ((Critter)o).name, sub = o is FishT g ? g.sp.Title : ((Critter)o).title;
            L(name, "bold", label);
            L(sub, "sub", label);
            label.style.left = panelPos.x + 16;
            label.style.top = panelPos.y - 10;
            label.AddToClassList("show");
        }

        /// <summary>The ring around the selected creature (centre and radius in panel pixels; radius 0 hides it).</summary>
        public void SetRing(Vector2 c, float r, float alpha)
        {
            ringSel.style.display = r > 0 && alpha > 0.01f ? DisplayStyle.Flex : DisplayStyle.None;
            if (r <= 0) return;
            ringSel.style.left = c.x - r;
            ringSel.style.top = c.y - r;
            ringSel.style.width = r * 2;
            ringSel.style.height = r * 2;
            var radius = new StyleLength(r);
            ringSel.style.borderTopLeftRadius = radius; ringSel.style.borderTopRightRadius = radius;
            ringSel.style.borderBottomLeftRadius = radius; ringSel.style.borderBottomRightRadius = radius;
            ringSel.style.opacity = alpha;
        }

        /// <summary>A ripple where the glass was knocked.</summary>
        public void TapFlash(Vector2 at)
        {
            var el = E("ring", root);
            el.pickingMode = PickingMode.Ignore;
            taps.Add((el, 0, at));
        }

        public void SetLetterbox(float bars, float fade)
        {
            float h = root.layout.height * 0.115f * bars;
            barTop.style.height = h;
            barBottom.style.height = h;
            fadeEl.style.opacity = fade;
        }

        public void Update(float dt)
        {
            for (int i = taps.Count - 1; i >= 0; i--)
            {
                var (el, t, at) = taps[i];
                t += dt;
                float r = 4 + t * 70;
                el.style.left = at.x - r; el.style.top = at.y - r; el.style.width = r * 2; el.style.height = r * 2;
                var rr = new StyleLength(r);
                el.style.borderTopLeftRadius = rr; el.style.borderTopRightRadius = rr; el.style.borderBottomLeftRadius = rr; el.style.borderBottomRightRadius = rr;
                el.style.opacity = 0.8f * (1 - t / 0.6f);
                if (t > 0.6f) { el.RemoveFromHierarchy(); taps.RemoveAt(i); }
                else taps[i] = (el, t, at);
            }
            if (++frame % 15 != 0) return;   // a few refreshes a second are plenty
            if (cardSubject != null)
            {
                if (cardSubject is FishT f && !Eco.fish.Contains(f)) shell.Select(null);
                else RenderCard(false);
            }
            if (IsOpen("roster") && rosterTab == "list" && frame % 120 == 0) RenderRoster();
            if (IsOpen("roster") && rosterTab == "hist" && frame % 300 == 0) RenderHistory();
        }
    }
}
