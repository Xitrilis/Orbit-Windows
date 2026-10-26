# Компоненты Orbit 0.7

Orbit — отдельный интерфейс и расширение над Playnite. Это независимый проект; он не является официальным продуктом магазинов или Playnite.

| Компонент | Версия | Исходники и лицензия |
| --- | --- | --- |
| Playnite, Playnite.SDK, включённые зависимости | 10.63 | [JosefNemec/Playnite](https://github.com/JosefNemec/Playnite), MIT для Playnite; полный файл лицензий зависимостей сохранён в `engine/license.txt` |
| Steam, Epic, GOG, Battle.net, Ubisoft, IGDB | 2.44 / 2.29 / 2.23 / 2.24 / 2.9 / 2.13 | [JosefNemec/PlayniteExtensions](https://github.com/JosefNemec/PlayniteExtensions), `Notices/PlayniteExtensions-LICENSE.txt` |
| EA app | 3.2.2 | [Jeshibu/PlayniteExtensions](https://github.com/Jeshibu/PlayniteExtensions), `Notices/EA-LICENSE.txt` |

Основа и интеграции включены в виде опубликованных бинарных выпусков; их исходный код не менялся. Собственный код находится в `Source` и `engine/Extensions/Orbit/ui`. Собственные файлы: `Orbit.exe`, `Orbit.Plugin.dll`, `Orbit.InstallHelper.exe`, интерфейс и значок Orbit.

Архив основы: [10.63.7z](https://github.com/JosefNemec/Playnite/releases/download/10.63/10.63.7z).
SHA-256: `9af9a0db330c97879697beac9f819c16f9f544e712261a83fa4e62a8f9a2c787`.
Версии, URL и SHA-256 исходных пакетов интеграций: `Notices/bundled-addons.json`.

Демонстрационные изображения игр получены из публичных изображений Steam по адресам `https://cdn.akamai.steamstatic.com/steam/apps/<appid>/library_600x900.jpg` и `library_hero.jpg` (с резервным `header.jpg`). AppID: 990080, 1091500, 1174180, 1245620, 1145360, 292030, 1426210, 367520, 2357570, 812140, 275850, 413150. Эти изображения и названия принадлежат соответствующим правообладателям, не входят в лицензию собственного кода и используются в явно обозначенной демонстрационной коллекции. Настоящая библиотека использует метаданные импортированных игр.

Для разработки использовались .NET SDK 8.0.419, reference assemblies .NET Framework 4.6.2 1.0.3 и Playwright 1.62.1. Эти инструменты не включены в пользовательский архив и для запуска не нужны.

Gemini вызывается напрямую через HTTPS REST API с пользовательским ключом; Google SDK и общий ключ не включены. Документация реализации: [generateContent](https://ai.google.dev/api/generate-content), [список моделей](https://ai.google.dev/api/models), [API-ключи](https://ai.google.dev/gemini-api/docs/api-key). Квоты, доступность и условия обработки запросов задаёт Google.

Windows API: [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [CopyFromScreen](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.copyfromscreen), [DPAPI ProtectedData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata). Собственный оверлей — отдельное WPF/CefSharp-окно без внедрения в игровые процессы.

Модуль цен использует включённый `engine/AngleSharp.dll` для чтения публичного HTML FunPay. Лицензия AngleSharp сохранена в `engine/license.txt`. Новых внешних бинарных зависимостей для цен нет. Источники данных — в `PRICE-SOURCES.md`; цены и названия сторонних товаров не являются собственным контентом Orbit.

Калькулятор пополнения читает публичные параметры GAMEMONEY и курс ЦБ. Удалённый JavaScript не исполняется и не включён в дистрибутив. Orbit не аффилирован с сервисами оплаты. Источники и статус интеграции: `WALLET-SOURCES.md`. Новых бинарных зависимостей нет.


## Центр загрузок 0.5

Интерфейс, SVG-график и расчёт показателей написаны в Orbit без новой библиотеки графиков. Steam читается только через локальный steamapps/appmanifest_<AppID>.acf; это не стабильный публичный API загрузок. Данные Steam не изменяются. Состояние установки берётся из [Game.IsInstalled / Game.IsInstalling в Playnite SDK](https://api.playnite.link/docs/api/Playnite.SDK.Models.Game.html). См. DOWNLOADS.md.


## Оформление игр 0.6

Редактор, кадрирование и хранилище реализованы в Orbit. Системный выбор файла использует Windows Forms; нормализация изображений — WPF BitmapDecoder / TransformedBitmap / PngBitmapEncoder из .NET Framework. Новые сторонние библиотеки не добавлены. Локальные картинки не отправляются на сервер; интернет-поиск изображений не выполняется.

## Данные распродаж в 0.7

Новые библиотеки не добавлены. Используются публичные витрины Steam/Epic/GOG, Steamworks и существующие источники FunPay/Plati/ЦБ. Ссылки и проверенные даты приведены в SALES.md. Локальная история — собственные наблюдения Orbit; SteamDB и ITAD не выгружаются.

## Главная 0.8

Новых бинарных зависимостей нет. Обложки и описания Steam — контент правообладателей, загружаемый из каталога. Gemini использует существующий клиент и пользовательский ключ. Источники и состав контекста — HOME.md.
