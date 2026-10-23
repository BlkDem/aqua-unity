# Эталонные данные веб-версии

`harness.mjs` запускает код живого мира веб-версии (`aqua/src/life`) в Node и пишет данные, по которым тесты проверяют C#-порт:

- `rng.json` — значения hashStr и mulberry32;
- `web-save.json` — сохранение после 30 дней жизни;
- `stats-60d.json` — статистика двух месяцев офлайна по 60 прогонам.

`browser-save.json` снят из настоящей веб-версии в браузере (`?away=200`, затем `localStorage`): в нём есть растения, побеги, любимые места и дружба.

```bash
# из WSL, рядом с репозиторием aqua (путь к его src — в начале скрипта)
node Tools/web-reference/harness.mjs Assets/Aquarium/Tests/EditMode/Fixtures
```
