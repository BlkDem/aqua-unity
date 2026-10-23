# Живой аквариум — Unity

Перенос «Живого аквариума» (веб-версия: репозиторий `aqua`, https://aqua.umolab.ru) на Unity. План работ — [PLAN.md](PLAN.md).

## Что нужно

- Unity **6000.6.7f1** (Unity 6.6) с модулями Windows Build Support (IL2CPP) и Web
- Unity Hub с выполненным входом (лицензия Personal)
- Git с Git LFS. Модели, текстуры и звуки лежат в LFS (см. `.gitattributes`). Под WSL у git нет LFS: работать с репозиторием из Windows (PowerShell, Git Bash, VS Code)
- VS Code с расширением Unity (оно подтянет C# и C# Dev Kit) и .NET 8 SDK. В Unity: Edit → Preferences → External Tools → External Script Editor → Visual Studio Code

## Состояние

| Этап | Что есть |
|---|---|
| 0. Подготовка | проект, структура, LFS, CI (ждёт секретов), эталонные снимки |
| 1. Ядро живого мира | `World`, `Lifecycle`, `Species`, `Words` на C#, 41 тест; читает и пишет сохранения веб-версии, статистика двух месяцев жизни совпадает с вебом |
| 2. Сцена, свет, материалы | аквариум собирается генератором из моделей веб-версии; свои шейдеры URP (каустики, колыхание растений, стекло, вода, задник, лучи, взвесь); утро/день/вечер/ночь; картинка сверена с эталоном (`Docs/unity` рядом с `Docs/reference`) |
| 3. Рыбы | 12 видов из моделей веб-версии; шейдер изгиба тела, хвоста и плавников; инстансинг по моделям; поведение из `fish.js` (28 состояний, стаи, территория, игры, корм) и физика плавания; корм и пузыри; запуск мира из сохранения (`AquariumApp`), кормление — `F` |

Перенос аквариума из веб-версии: там в Настройки → Аквариум → «Резервная копия» → «Сохранить в файл», здесь — `World.Import(json)` (кнопка появится вместе с интерфейсом).

## Структура

```
Assets/Aquarium/
  Scenes/            Aquarium.unity — основная сцена
  Scripts/Life/      живой мир: время, рост, пары, мальки, наследование, история, склонения.
                     Чистый C# без UnityEngine (noEngineReferences) — тестируется и переносится отдельно
  Tests/EditMode/    тесты NUnit
Assets/Settings/     URP: профили качества PC/Mobile, Volume
```

Новые слои появляются по мере работ: `World` (аквариум, декор, растения, день и ночь), `Fish`, `Critters`, `FX` (шейдеры, частицы, камера, звук), `UI`. У каждого свой assembly definition; `Life` ни от кого из них не зависит.

## Сцена

Сцена `Assets/Aquarium/Scenes/Aquarium.unity` целиком собирается кодом: **Aquarium → Build Scene** (или `-executeMethod Aquarium.EditorTools.SceneBuilder.BuildAndSave`). Генератор (`Assets/Aquarium/Editor/SceneBuilder.cs`) строит аквариум, песок и воду, расставляет декор и растения по координатам веб-версии, ставит свет, камеру, пост-обработку и смену дня (`DayCycle`), запекает окружение для отражений. Повторный запуск пересобирает сцену заново — правки, которые нужно сохранить, вносятся в генератор.

- Модели — в `Assets/Aquarium/Resources/Models` (из веб-версии, текстуры перекодированы из WebP в PNG: `python3 Tools/glb_webp_to_png.py <src> <dst>`; glTFast не читает WebP).
- Координаты веб-версии переводятся `WebSpace.P(x, y, z)` → `(-x, y, z)`: glTFast отражает модели по X при переходе в левую систему координат Unity.
- Шейдеры — `Assets/Aquarium/Shaders`: `Aquarium/Lit` (PBR glTF + каустики + колыхание), `Glass`, `Water`, `Backdrop`, `Room`, `GodRays`, `Dust`, `EnvSky`.
- Время суток — компонент `DayCycle` на объекте `System` (режим, фаза, длина суток); в редакторе меняется сразу.
- Рыбы — `Assets/Aquarium/Scripts/Fish`: `Fish` (поведение и физика, перенос `fish.js`), `Ecosystem` (стаи, корм, места, истории), `FishRenderer` (инстансинг), `Bubbles`, `FoodItem`. Всё считается в координатах веб-версии и переводится в Unity только при отрисовке. Модели рыб для инстансинга готовит **Aquarium → Build Fish Models** (вызывается и генератором сцены).
- В Play-режиме аквариум живёт: мир берётся из сохранения в `Application.persistentDataPath/world` (формат веб-версии), `F` — покормить. Аргументы `-timescale 600` и `-away 48` — как `?timescale` и `?away` в вебе.
- **Aquarium → Render Snapshots** (или `Snapshot.BuildAndRender`) снимает те же ракурсы и времена суток, что эталон веб-версии, в `Docs/unity`.

## Эталон

`Docs/reference/` — снимки веб-версии (утро, день, вечер, ночь; общий план и крупный), по ним сверяем картинку в Unity на этапе 2. Сняты в браузере с программным рендером, поэтому сглаживание и шум чуть грубее, чем на видеокарте; цвет, свет и композиция — как есть.

## Тесты

В редакторе: Window → General → Test Runner → EditMode → Run All.

Из командной строки (Unity Hub должен быть запущен — из него берётся лицензия):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode `
  -projectPath C:\Users\maxim\projects\aqua-unity -runTests -testPlatform EditMode `
  -testResults Logs\editmode.xml -logFile Logs\batch.log
```

Код выхода 0 — все тесты прошли; подробности в `Logs/editmode.xml`.

## CI

`.github/workflows/ci.yml` (GameCI): на каждый пуш — EditMode-тесты (без LFS-моделей, они тестам не нужны); сборка под Windows (артефакт `Aquarium-Windows`) — на теги `v*` и по кнопке Run workflow. Так экономится LFS-трафик: у бесплатного GitHub это 1 ГБ в месяц, а модели весят ~180 МБ. Нужны секреты репозитория — учётная запись Unity, на которой активирована лицензия Personal:

```powershell
gh secret set UNITY_EMAIL -R BlkDem/aqua-unity
gh secret set UNITY_PASSWORD -R BlkDem/aqua-unity
```

(каждая команда спросит значение; оно не попадает ни в историю, ни в логи). GameCI входит под этой учётной записью и возвращает место лицензии после прогона. Учётная запись с включённой двухфакторной аутентификацией так не войдёт — для CI тогда нужна отдельная учётная запись Unity без 2FA.

## Соглашения

- Логика живого мира — только в `Scripts/Life` и только на C#, без Unity API; формат сохранения совпадает с веб-версией (мир v2), чтобы аквариум можно было перенести.
- Строки интерфейса — по-русски; склонения через `Words`.
- Коммиты — небольшими шагами, по этапам плана.
