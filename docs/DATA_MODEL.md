# Модель данных / Data model

## Русский

### 1. Общие соглашения

- JSON использует `camelCase` и UTF-8 без BOM.
- Идентификаторы создаются на устройстве как ULID и не меняются.
- Технические моменты времени хранятся в UTC ISO 8601, например
  `2026-08-31T18:20:00Z`.
- Календарные даты хранятся как `YYYY-MM-DD` без часового пояса.
- Неизвестное значение представлено `null`, а не пустой строкой.
- Enum-значения сохраняются строчными `snake_case`.
- Вычисляемые значения не записываются в синхронизируемые JSON.
- Идентификаторы уникальны внутри типа сущности.

Каждая изменяемая сущность имеет метаданные:

```json
{
  "id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
  "revision": 1,
  "createdAt": "2026-08-31T18:20:00Z",
  "updatedAt": "2026-08-31T18:20:00Z",
  "deletedAt": null
}
```

`revision` увеличивается при каждом логическом изменении. После слияния двух
версий она становится на единицу больше максимальной родительской ревизии.
Запись с `deletedAt != null` является tombstone и не показывается в обычном UI.

### 2. Манифест набора данных

Путь: `aptechka.json`.

```json
{
  "schemaVersion": 1,
  "datasetId": "01ARZ3NDEKTSV4RRFFQ69G5FAZ",
  "createdAt": "2026-08-31T18:20:00Z"
}
```

`datasetId` защищает от случайного подключения к чужой аптечке.
`schemaVersion` определяет миграцию файлов при обновлении приложения.

### 3. `InventoryItem` — позиция аптечки

Путь: `items/{id}.json`.

| Поле | Тип | Правило |
|---|---|---|
| `name` | string | Обязательно, после trim не пусто |
| `aliases` | string[] | Уникальны без учёта регистра |
| `category` | enum | `medicine` или `medical_supply` |
| `activeIngredients` | string[] | Обычно пусто для расходников |
| `form` | string? | Например «таблетки», «мазь», «вата» |
| `strength` | string? | Например «200 мг»; пользовательский текст |
| `description` | string? | Пользовательское краткое описание |
| `keepInStock` | bool | Личная политика обязательного запаса |
| `coverPhotoId` | ULID? | Ссылка на неудалённое `Photo` этой позиции |

Пример лекарства:

```json
{
  "id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
  "revision": 3,
  "createdAt": "2026-08-31T18:20:00Z",
  "updatedAt": "2026-09-02T08:00:00Z",
  "deletedAt": null,
  "name": "Ибупрофен",
  "aliases": ["Нурофен"],
  "category": "medicine",
  "activeIngredients": ["ибупрофен"],
  "form": "таблетки",
  "strength": "200 мг",
  "description": null,
  "keepInStock": true,
  "coverPhotoId": null
}
```

Пример расходника:

```json
{
  "id": "01ARZ3NDEKTSV4RRFFQ69G5FAW",
  "revision": 1,
  "createdAt": "2026-08-31T18:20:00Z",
  "updatedAt": "2026-08-31T18:20:00Z",
  "deletedAt": null,
  "name": "Вата",
  "aliases": [],
  "category": "medical_supply",
  "activeIngredients": [],
  "form": null,
  "strength": null,
  "description": null,
  "keepInStock": false,
  "coverPhotoId": null
}
```

### 4. `Package` — упаковка

Путь: `packages/{id}.json`.

| Поле | Тип | Правило |
|---|---|---|
| `itemId` | ULID | Существующая неудалённая позиция |
| `label` | string? | Необязательное различающее имя |
| `expirationDate` | date? | Последний календарный день пригодности |
| `expirationPrecision` | enum? | `day` или `month`; null вместе с отсутствующим сроком |
| `openedDate` | date? | Дата первого вскрытия |
| `shelfLifeAfterOpeningDays` | int? | Положительное число дней |
| `stockState` | enum | `available`, `low`, `depleted` |
| `note` | string? | Пользовательская заметка об упаковке |

```json
{
  "id": "01ARZ3NDEKTSV4RRFFQ69G5FAX",
  "revision": 2,
  "createdAt": "2026-08-31T18:25:00Z",
  "updatedAt": "2026-09-10T11:00:00Z",
  "deletedAt": null,
  "itemId": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
  "label": null,
  "expirationDate": "2027-04-30",
  "expirationPrecision": "month",
  "openedDate": null,
  "shelfLifeAfterOpeningDays": null,
  "stockState": "available",
  "note": null
}
```

Если пользователь вводит только месяц и год, `expirationDate` сохраняет последний
день месяца, а `expirationPrecision == month` позволяет UI не притворяться, будто
на упаковке был напечатан точный день.

Если заданы `openedDate` и `shelfLifeAfterOpeningDays`, срок после вскрытия равен
`openedDate + shelfLifeAfterOpeningDays`. Эффективный срок — минимум из срока на
этикетке и срока после вскрытия. Упаковка пригодна до конца эффективной даты и
просрочена, когда локальная календарная дата устройства стала позже неё.

### 5. Вычисляемые состояния

`PackageUsability`:

- `usable` — не удалена, не закончилась и не просрочена;
- `depleted` — `stockState == depleted`;
- `expired` — текущая дата позже эффективного срока.

Если упаковка одновременно закончилась и просрочилась, для интерфейса причина
`depleted` имеет приоритет, но в расчёте обе причины делают её непригодной.

`ItemAvailability`:

- `available` — есть пригодная упаковка со `stockState == available`;
- `low` — пригодных `available` нет, но есть пригодная `low`;
- `missing` — пригодных упаковок нет.

Состояния вычисляются через чистый доменный сервис и не сохраняются.

### 6. `Problem` — бытовая проблема

Путь: `problems/{id}.json`.

```json
{
  "id": "01ARZ3NDEKTSV4RRFFQ69G5FAY",
  "revision": 1,
  "createdAt": "2026-08-31T18:30:00Z",
  "updatedAt": "2026-08-31T18:30:00Z",
  "deletedAt": null,
  "name": "Головная боль",
  "aliases": ["болит голова"],
  "itemIds": ["01ARZ3NDEKTSV4RRFFQ69G5FAV"],
  "note": null
}
```

`itemIds` — пользовательские связи, а не медицинские рекомендации. Удалённые
или отсутствующие позиции не удаляются из связи автоматически, но UI показывает
их актуальное состояние.

### 7. `ShoppingItem` — ручная отметка покупки

Путь: `shopping/{itemId}.json`. Поле `id` равно `itemId`, поэтому на одну позицию
существует ровно одна синхронизируемая ручная отметка.

```json
{
  "id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
  "itemId": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
  "revision": 1,
  "createdAt": "2026-09-20T08:00:00Z",
  "updatedAt": "2026-09-20T08:00:00Z",
  "deletedAt": null,
  "isRequested": true,
  "note": null
}
```

Видимая строка списка покупок является вычисляемым `ShoppingEntry`. Она
показывается, если `ShoppingItem.isRequested == true` или выполняется
`keepInStock && availability == missing`. У строки может быть две причины:
`manual` и `keep_in_stock_missing`; они не дублируют строку. Автоматическая
причина исчезает после пополнения запаса, ручная — только по действию пользователя.

### 8. `Photo` — фотография

Метаданные: `photos/{id}.json`. Изображение:
`media/items/{itemId}/{id}.webp`.

Поля: `itemId`, `fileName`, `caption`, `contentSha256` и общие метаданные.
`contentSha256` проверяет целостность передачи. Обложка задаётся через
`InventoryItem.coverPhotoId`, поэтому у позиции не может быть двух обложек.

### 9. `InteractionNote` — ручная заметка о совместимости

Путь: `interaction-notes/{id}.json`.

Поля:

- `itemIds` — минимум две позиции категории `medicine`;
- `summary` — внесённый пользователем текст;
- `sourceKind` — `ai`, `doctor`, `pharmacist`, `official_instructions`,
  `manual` или `other`;
- `sourceReference` — необязательная ссылка или описание источника;
- `reviewedOn` — дата, к которой относится проверка.

Такая заметка отображается вместе с источником и датой и не изменяет поиск по
проблеме или наличие.

### 10. Локальные несинхронизируемые данные

В системном каталоге приложения, но не в репозитории данных, хранятся:

- `deviceId` и отображаемое имя устройства;
- GitHub owner/repository/branch;
- SHA последнего синхронизированного коммита и базовые хеши файлов;
- пороги напоминаний о сроке;
- `NotificationLedger` для подавления повторов;
- временные файлы и перестраиваемый поисковый индекс.

GitHub-токен хранится отдельно в Secure Storage и никогда не сериализуется в
обычный JSON.

## English

### 1. Conventions

JSON uses camelCase and UTF-8. Device-generated ULIDs are stable identifiers.
Technical timestamps use UTC ISO 8601; calendar dates use `YYYY-MM-DD`.
Unknown values are `null`, enums use lower snake case, and derived values are
never synchronized.

Mutable entities contain `id`, `revision`, `createdAt`, `updatedAt`, and
`deletedAt`. A non-null `deletedAt` is a tombstone. The data manifest at
`aptechka.json` contains `schemaVersion`, `datasetId`, and `createdAt`.

### 2. Entities

- `InventoryItem`: medicine or medical supply; contains name, aliases, category,
  active ingredients, form, description, `keepInStock`, and `coverPhotoId`;
- `Package`: item reference, optional labeled and after-opening expiration,
  stock state, and note;
- `Problem`: a user-defined household problem and associated item IDs;
- `ShoppingItem`: one synchronized manual buy flag per item, keyed by `itemId`;
- `Photo`: WebP metadata and content hash;
- `InteractionNote`: manually entered text, participating medicines, source,
  source reference, and review date.

### 3. Derived rules

The effective expiration is the earlier known value of the labeled expiration
and opening date plus after-opening shelf-life days. A package is usable through
the end of that calendar date.

An item is `available` when any usable package is `available`; otherwise it is
`low` when any usable package is `low`; otherwise it is `missing`. These values
are computed by pure domain logic.

A derived shopping entry is visible when the manual flag is set or while
`keepInStock` is true and availability is `missing`. Automatic and manual reasons
share one visible row. Replenishment clears only the derived automatic reason.

### 4. Device-local state

Device identity, GitHub repository coordinates, last synchronized commit, base
file hashes, reminder thresholds, notification ledger, temporary files, and the
search index remain outside the synchronized repository. The GitHub credential
is stored separately in platform Secure Storage.
