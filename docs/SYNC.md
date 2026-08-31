# Контракт GitHub-синхронизации / GitHub synchronization contract

## Русский

### 1. Цель и граница

Синхронизация обеспечивает eventual consistency между Windows и Android через
один приватный GitHub-репозиторий данных. Приложение остаётся полностью полезным
офлайн: локальные изменения сохраняются сразу и ждут следующей синхронизации.

GitHub не является сервером приложения и не выполняет доменные правила. Он
хранит версии файлов и предоставляет атомарное продвижение ветки.

### 2. Репозитории

Исходный код и данные никогда не смешиваются. Пользователь подключает отдельный
приватный репозиторий данных с веткой `main`.

```text
aptechka.json
items/{itemId}.json
packages/{packageId}.json
problems/{problemId}.json
shopping/{itemId}.json
photos/{photoId}.json
interaction-notes/{noteId}.json
media/items/{itemId}/{photoId}.webp
```

Локальные настройки, токен, журнал уведомлений, поисковый индекс и временные
файлы в этот репозиторий не попадают.

### 3. Подключение GitHub

В P1 пользователь указывает owner, repository и ветку и предоставляет
ограниченный учётный токен с доступом только к выбранному приватному
репозиторию. Токен сохраняется через платформенный Secure Storage.

Архитектура не разрешает:

- записывать токен в конфигурационный JSON;
- выводить токен или HTTP Authorization в лог;
- добавлять токен в URL remote;
- включать токен в экспорт или отчёт об ошибке.

Позднее способ авторизации можно заменить на OAuth, не меняя `ISyncService` и
формат данных.

### 4. Локальное состояние синхронизации

Каждое устройство хранит вне репозитория:

- стабильный `deviceId` и имя устройства;
- `datasetId` подключённой аптечки;
- SHA последнего успешно синхронизированного коммита;
- локальную копию базового снимка для трёхстороннего слияния;
- флаг локальных несинхронизированных изменений;
- последнее время и результат синхронизации.

Базовый снимок обновляется только после полностью успешного push или после
успешного принятия неизменённого удалённого состояния.

### 5. Когда выполняется синхронизация

В P1 синхронизация запускается явной кнопкой. Это делает сквозной прототип
детерминированным и наблюдаемым.

В P4 добавляются:

- попытка синхронизации при входе приложения на передний план;
- повтор после восстановления сети;
- понятный индикатор `synced`, `local_changes`, `syncing`, `conflict`, `error`.

Android не гарантирует точный фоновой запуск, поэтому продукт не обещает
постоянную фоновую синхронизацию. Локальные напоминания не зависят от GitHub.

### 6. Алгоритм синхронизации

Обозначения:

- `B` — базовый снимок последней успешной синхронизации;
- `L` — текущее локальное состояние;
- `R` — состояние текущего remote head.

Порядок:

1. Получить SHA remote head и проверить `datasetId`.
2. Построить изменения `B → L` и `B → R` по путям и содержимому.
3. Для каждого пути выполнить трёхстороннее слияние.
4. Если остались конфликты, не изменять remote и показать их пользователю.
5. Атомарно записать объединённый снимок локально.
6. Создать Git tree и commit с родителем, равным прочитанному remote head.
7. Продвинуть `main` только fast-forward операцией с ожидаемым старым SHA.
8. Если remote успел измениться, повторить цикл с новым head до трёх раз.
9. После успеха заменить `B` объединённым снимком и сохранить новый commit SHA.
10. Пересчитать наличие, покупки и локальные уведомления.

Если объединённое состояние совпадает с remote, новый пустой commit не создаётся.

### 7. Правила слияния

#### Разные пути

Изменения разных файлов объединяются автоматически.

#### Один JSON изменён с одной стороны

Если одна сторона равна базе, принимается изменённая сторона.

#### Один JSON изменён с двух сторон

Скалярное поле:

- одинаковые новые значения принимаются;
- если одно значение равно базе, принимается другое;
- два разных изменения одного поля создают конфликт.

Технические `revision` и `updatedAt` не считаются пользовательскими полями и
сами не создают конфликт. При слиянии `createdAt` выбирается более ранний,
`updatedAt` устанавливается заново, а `revision` пересчитывается.

Массивы `aliases`, `activeIngredients` и `itemIds` рассматриваются как множества.
Независимые добавления и удаления объединяются. Одновременное удаление и
изменение одного логического элемента создаёт конфликт.

После автоматического слияния `revision` становится
`max(local.revision, remote.revision) + 1`, а `updatedAt` получает текущее UTC.

#### Удаление против изменения

Tombstone на одной стороне и изменение сущности на другой всегда создают
явный конфликт. Удаление не выигрывает молча, и изменение не воскрешает запись
молча.

#### Медиа

Новый `Photo` получает новый ULID, поэтому независимые фотографии имеют разные
пути. Разное содержимое одного WebP-пути считается повреждением или конфликтом
и никогда не разрешается через last-write-wins.

### 8. Удаление

Удаление сущности записывает `deletedAt`, увеличивает `revision` и сохраняет
остальные поля. Tombstone остаётся в репозитории без автоматической очистки:
для домашнего объёма цена ничтожна, а защита от воскрешения важнее.

После успешной синхронизации tombstone фотографии соответствующий WebP можно
удалить отдельным коммитом. Отсутствующий медиафайл не отменяет tombstone.

Восстановление очищает `deletedAt`, увеличивает `revision` и считается обычным
изменением, которое может конфликтовать с более новым remote.

### 9. Первое подключение

- Пустой remote и пустое приложение: создать `aptechka.json` и первый commit.
- Непустой remote и пустое приложение: проверить манифест и принять remote.
- Пустой remote и локальные данные: отправить локальный снимок как первый commit.
- Две непустые стороны без общей базы: автоматическое слияние запрещено;
  пользователь выбирает импорт remote, замену remote или новый репозиторий.
- Разные `datasetId`: синхронизация запрещена до явного переподключения.

### 10. Коммиты и ошибки

Коммиты данных используют формат:

```text
data(sync): update from <device-name>
```

Force push запрещён. Ошибка сети не откатывает локальные изменения. Ошибки
авторизации, отсутствующий репозиторий, несовместимая схема, конфликт и сетевой
сбой показываются как разные состояния с конкретным следующим действием.

Git-история является резервной историей, но не заменяет проверку целостности.
После загрузки проверяются JSON-схема, ссылки между сущностями и SHA-256 медиа.

### 11. Граница P1 и P4

P1 доказывает безопасный happy path:

1. создать позицию на Windows;
2. отправить её в GitHub;
3. получить её на Android;
4. изменить на Android и вернуть на Windows;
5. подтвердить, что токен не попал в файлы и логи;
6. при обнаруженном конфликте остановиться без потери данных.

P4 добавляет автоматическое полевое слияние, экран ручного разрешения,
tombstone-сценарии, повтор при гонке remote head, работу с крупными медиа и
полную матрицу ошибок.

## English

### 1. Boundary

Synchronization provides eventual consistency between Windows and Android via
one private GitHub data repository. Local changes are durable immediately and
normal application usage never waits for the network. GitHub stores file history
and provides atomic branch updates; it does not execute domain rules.

### 2. Authentication and local state

P1 accepts a repository-scoped credential and stores it only in platform Secure
Storage. Credentials never enter JSON, URLs, logs, exports, or error reports.

Each device locally stores device identity, dataset identity, the last successful
commit SHA, a base snapshot, dirty state, and the last synchronization result.
The base advances only after a completely successful synchronization.

### 3. Execution

P1 uses an explicit sync action. P4 adds foreground and reconnect attempts plus
clear sync status. Continuous Android background execution is not promised, and
local reminders never depend on GitHub.

Synchronization performs a three-way merge of base (`B`), local (`L`), and
remote (`R`), writes the merged snapshot atomically, and advances `main` only as
a fast-forward from the observed remote SHA. A changed remote head causes up to
three retries. Empty commits are not created.

### 4. Conflicts and deletion

Different paths and one-sided changes merge automatically. Two different edits
to the same scalar field conflict. Set-like arrays merge independent changes.
Deletion versus modification always conflicts. Media content at the same path is
never resolved by last-write-wins.

Deletion preserves a tombstone indefinitely. Restoration is a new revision.
Different dataset IDs and two non-empty sides without a shared base require an
explicit user decision.

### 5. Phase boundary

P1 proves a Windows → GitHub → Android → GitHub → Windows happy path, protects
credentials, and stops safely on any conflict. P4 adds field-level merging,
manual conflict resolution, tombstone scenarios, remote-head race retries, media
handling, and the complete error matrix.
