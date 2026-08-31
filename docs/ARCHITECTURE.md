# Архитектура / Architecture

## Русский

### 1. Рекомендуемое решение

Один клиент на .NET MAUI и C# для Windows и Android. Приложение локальное:
интерфейс и доменные сценарии работают с файлами на устройстве, а GitHub
используется только как удалённый транспорт и история версий.

Исходный код и личные данные находятся в разных репозиториях:

```text
Aptechka          — исходный код приложения
aptechka-data     — приватные пользовательские JSON и WebP
```

Это позволяет развивать или публиковать код, не рискуя раскрыть содержимое
домашней аптечки.

### 2. Слои

```text
Aptechka.App
    ↓
Aptechka.Application
    ↓
Aptechka.Domain
    ↑
Aptechka.Infrastructure
```

#### `Aptechka.Domain`

Чистые C#-типы и правила:

- `InventoryItem`, `Package`, `Problem`, `ShoppingItem`, `InteractionNote`, `Photo`;
- вычисление срока и наличия;
- правило обязательного запаса;
- доменные ошибки и инварианты.

Слой не зависит от MAUI, файловой системы, JSON, GitHub и системного времени.

#### `Aptechka.Application`

Прикладные сценарии и порты инфраструктуры:

- поиск и получение карточки;
- сохранение позиции и упаковки;
- пересчёт состояния аптечки;
- формирование покупок и уведомлений;
- синхронизация;
- экспорт.

Основные интерфейсы:

```csharp
public interface IInventoryRepository;
public interface IPhotoStore;
public interface ISyncService;
public interface INotificationService;
public interface IExportService;
public interface IClock;
```

#### `Aptechka.Infrastructure`

Реализации портов:

- атомарное чтение и запись JSON;
- хранение и сжатие WebP;
- GitHub API и трёхстороннее слияние;
- Android/Windows Secure Storage;
- локальные уведомления;
- Markdown-экспорт.

#### `Aptechka.App`

MAUI-страницы, ViewModel, навигация и композиция зависимостей. ViewModel
вызывает прикладные сценарии и не читает JSON или GitHub напрямую.

### 3. Структура solution в P1

```text
src/
  Aptechka.App/
  Aptechka.Application/
  Aptechka.Domain/
  Aptechka.Infrastructure/
tests/
  Aptechka.Domain.Tests/
  Aptechka.Application.Tests/
```

Разделение на проекты оправдано границами платформы и тестирования. Внутри
каждого проекта структура остаётся простой; MediatR, EventBus и универсальный
generic repository в P1 не добавляются.

### 4. Поток данных

```text
MAUI View
  → ViewModel
  → Application use case
  → Domain rules
  → IInventoryRepository
  → local JSON files

User sync
  → ISyncService
  → GitHub remote snapshot
  → three-way merge
  → local JSON files
  → domain reevaluation
  → restock/notification rules
```

Локальные файлы являются рабочим источником истины устройства. Удалённая ветка
GitHub является общей точкой обмена между устройствами. UI никогда не ждёт сеть
для обычного чтения и редактирования.

### 5. Хранение и индекс поиска

Основные данные хранятся как отдельные JSON-файлы. Один файл на сущность
уменьшает поверхность Git-конфликтов. Запись выполняется через временный файл
и атомарную замену, чтобы авария не оставила обрезанный JSON.

При запуске приложение строит индекс в памяти по нормализованным:

- названию;
- алиасам;
- действующим веществам;
- названиям проблем.

SQLite не используется как синхронизируемый источник истины. Его можно добавить
позже только как полностью перестраиваемый локальный кеш, если объём данных
действительно потребует этого.

### 6. Правила домена

- все изменения проходят через Application use case;
- вычисляемые поля не записываются в JSON;
- время передаётся через `IClock` и тестируется детерминированно;
- срок годности хранится как календарная дата, а технические отметки — UTC;
- `keepInStock` является личной политикой пользователя, а не медицинской
  характеристикой позиции;
- заметки о совместимости не участвуют в автоматических рекомендациях;
- удаление создаёт tombstone с `deletedAt`, а не стирает JSON немедленно.

### 7. Уведомления

Проверка правил выполняется:

- после локального изменения упаковки;
- после завершённой синхронизации;
- при ежедневной фоновой проверке;
- при запуске, если фоновая проверка была пропущена системой.

Синхронизируемый `ShoppingItem` хранит только ручную отметку «купить».
Автоматическая причина `keep_in_stock_missing` вычисляется из домена, поэтому
оба устройства не создают конфликтующие записи при наступлении срока. Локальный
`NotificationLedger` не даёт Android или Windows показывать одно уведомление повторно.

### 8. Безопасность и приватность

- GitHub-репозиторий данных должен быть приватным;
- токен хранится только в системном Secure Storage;
- токен не пишется в логи, JSON, экспорт и резервные копии;
- экспорт создаётся только по явному действию пользователя;
- диагностические логи не содержат описания лекарств и заметок;
- приложение не требует собственного сервера.

### 9. Компромиссы

.NET MAUI даёт общий C#-код и единый UI для нужных платформ, но платформенные
уведомления и работа с фотографиями всё равно потребуют тонких адаптеров.

JSON удобен для Git и ручного восстановления, но требует явных миграций схемы.
Для домашней аптечки объём мал, поэтому этот компромисс предпочтительнее
бинарной базы и сложного сервера синхронизации.

GitHub API привязывает первую реализацию синхронизации к GitHub. Интерфейс
`ISyncService` сохраняет возможность позднее добавить другой Git-провайдер,
не меняя домен и UI.

## English

### 1. Recommended design

Use one .NET MAUI and C# client for Windows and Android. The application is
local-first: UI and domain use cases operate on device files, while GitHub is
only the remote transport and version history. Source code and private cabinet
data live in separate repositories.

### 2. Layers

- `Aptechka.Domain`: pure entities, expiration and availability rules, restock
  policy, invariants, and domain errors;
- `Aptechka.Application`: use cases and infrastructure ports;
- `Aptechka.Infrastructure`: JSON, WebP, GitHub, Secure Storage, notifications,
  and Markdown export;
- `Aptechka.App`: MAUI views, ViewModels, navigation, and dependency composition.

Dependencies point toward Domain. ViewModels never access JSON or GitHub
directly. P1 deliberately avoids MediatR, an EventBus, and a generic repository.

### 3. Data flow

Normal reads and edits always use local files. A user-triggered synchronization
fetches the GitHub snapshot, performs a three-way merge, writes the merged local
snapshot, reevaluates domain rules, and then updates restock and notification
state. Network availability never blocks normal catalog usage.

### 4. Storage

Each entity has its own JSON file. Writes use a temporary file and atomic replace.
An in-memory index covers normalized names, aliases, active ingredients, and
problem names. SQLite may only be introduced as a disposable cache, never as the
Git-synchronized source of truth.

### 5. Domain and notification rules

Derived fields are not persisted. Calendar expiration uses date-only values;
technical timestamps use UTC through an injected `IClock`. Deletion creates a
tombstone. `keepInStock` is a personal policy, not medical metadata.

Rules are evaluated after local package changes, after synchronization, during
the daily background check, and on startup when the OS skipped background work.
`ShoppingItem` stores only the synchronized manual “buy” flag. The automatic
`keep_in_stock_missing` reason is derived, which prevents both devices from
creating competing records when an expiration date passes. A device-local
`NotificationLedger` suppresses duplicate platform notifications.

### 6. Security and trade-offs

The data repository is private and the GitHub credential stays in platform
Secure Storage. Exports are explicit and diagnostic logs exclude cabinet content.

MAUI provides shared C# and UI but still needs platform adapters for photos and
notifications. JSON requires schema migrations but is mergeable and recoverable.
GitHub API creates provider coupling in the first implementation; `ISyncService`
keeps that coupling outside the domain and UI.
