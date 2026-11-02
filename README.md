<div align="center">
  <img src="assets/orbit-mark.svg" width="88" height="88" alt="Orbit" />

  # Orbit

  **Игры из разных магазинов — в одной библиотеке.**

  [![Version](https://img.shields.io/badge/version-0.19-5468ff)](https://github.com/Xitrilis/Orbit-Windows/commits/main)
  [![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-20232a)](https://github.com/Xitrilis/Orbit-Windows)
  [![License](https://img.shields.io/badge/license-MIT-20232a)](LICENSE)

  [Возможности](#возможности) · [Запуск](#запуск) · [Разработка](#разработка) · [Документация](#документация)
</div>

Orbit — независимый игровой лаунчер для Windows на базе [Playnite](https://github.com/JosefNemec/Playnite). Он объединяет игры из Steam, Epic Games, GOG, EA app, Ubisoft Connect и Battle.net, помогает найти установленную копию и сравнить предложения магазинов. Интерфейс Orbit и его функции находятся в этом репозитории; исходный код Playnite не изменён.

> **Статус проекта:** версия `0.19` в ветке `main` активно дорабатывается. Последний опубликованный [готовый выпуск](https://github.com/Xitrilis/Orbit-Windows/releases) — `v0.11.0`. Файлов одного Git-клона недостаточно для запуска: крупные компоненты Playnite не хранятся в репозитории.

## Возможности

- **Общая библиотека:** импорт игр, поиск, фильтры, избранное, недавние запуски и выбор конкретной копии для запуска.
- **Магазин:** поиск изданий и сравнение цен Steam, Epic Games, GOG и EA. Исходная валюта сохраняется, рядом показан справочный пересчёт в рубли и тенге. Для Ubisoft цена другой региональной витрины явно помечается как справочная; Battle.net пока без автоматических цен.
- **Игры и загрузки:** очередь «Поиграть позже», заметки, оформление карточек и состояние загрузок Steam.
- **Minecraft:** поиск сборок Prism/CurseForge, импорт модпаков через Prism и локальные моды. Horizon и Radmin доступны как дополнительные интеграции.
- **Помощник Orbit AI:** вопросы по игре и снимку экрана через Gemini с **собственным** API-ключом пользователя. Отправка снимка требует явного выбора.

Orbit не меняет регион аккаунта и не выполняет покупку. Цена публичной витрины может отличаться от итоговой цены на вашем аккаунте; проверяйте её перед оплатой. Подробности об источниках — в [PRICE-SOURCES.md](PRICE-SOURCES.md).

## Запуск

1. Скачайте **полный архив** на странице [Releases](https://github.com/Xitrilis/Orbit-Windows/releases). На данный момент там опубликована версия `v0.11.0`; исходники `main` новее.
2. Распакуйте архив целиком в отдельную папку с правом записи. Не запускайте `Orbit.exe` внутри ZIP.
3. Запустите `Orbit.exe`, откройте **Подключения** и настройте нужные магазины. Для полной библиотеки включите импорт неустановленных игр в их настройках.
4. В разделе **Все игры** обновите библиотеку. Первый импорт и загрузка обложек могут занять несколько минут.

Для запуска готового архива не нужны Node.js или .NET SDK. Внешние клиенты, например Steam и Prism, устанавливаются отдельно. Если Orbit не стартует, запустите `Orbit-Diagnostics.cmd` и посмотрите [инструкцию по диагностике](SUPPORT.md).

## Разработка

Проект рассчитан на Windows. Для сборки нужны .NET 8 SDK и **полный runtime Playnite 10.63** с зависимостями и интеграциями, размещённый в `engine/`; его контрольные суммы и происхождение описаны в [THIRD-PARTY.md](THIRD-PARTY.md) и [Notices](Notices). Собственный код компилируется для .NET Framework 4.6.2.

```powershell
./Source/Build.ps1
py -c "import sys; sys.path.insert(0, 'Source'); import Package; Package.verify()"
```

Полный архив создаёт `py Source/Package.py` после успешной сборки. Для проверок интерфейса нужны Node.js и Playwright; команды и границы этих проверок описаны в [Source/Tests/README.md](Source/Tests/README.md). Готовые файлы Orbit: `Orbit.exe` и `engine/Extensions/Orbit/`; интерфейс — в `engine/Extensions/Orbit/ui/`.

## Документация

| Тема | Где читать |
| --- | --- |
| Подробная инструкция на русском | [README-RU.md](README-RU.md) |
| Источники и ограничения цен | [PRICE-SOURCES.md](PRICE-SOURCES.md) |
| Отчёты, диагностика и восстановление | [SUPPORT.md](SUPPORT.md), [STARTUP-FIX.md](STARTUP-FIX.md) |
| Minecraft, Horizon, Radmin | [MINECRAFT.md](MINECRAFT.md), [HORIZON-START-RU.md](HORIZON-START-RU.md), [RADMIN-START-RU.md](RADMIN-START-RU.md) |
| Проверки текущей сборки и история изменений | [VALIDATION.md](VALIDATION.md), [AUDIT-0.19.md](AUDIT-0.19.md) |
| Лицензии сторонних компонентов | [THIRD-PARTY.md](THIRD-PARTY.md) |

Данные библиотек, настройки и ключ Gemini хранятся локально и исключены из Git. Перед отправкой архива с рабочей папкой проверьте его содержимое: внутри могут быть сессии магазинов. Как сообщить об ошибке или предложить изменение, описано в [CONTRIBUTING.md](CONTRIBUTING.md).

Собственный код Orbit распространяется по [MIT License](LICENSE). Playnite, расширения магазинов и изображения игр сохраняют свои лицензии и права. Orbit не связан с Playnite или перечисленными магазинами официально.
