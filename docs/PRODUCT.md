# Продуктовый контракт / Product contract

## Русский

### 1. Назначение

Aptechka отвечает на три бытовых вопроса:

1. Есть ли дома нужная позиция и в каком она состоянии?
2. Что из имеющегося ранее было отмечено для конкретной проблемы?
3. Что закончилось, просрочилось или скоро потребует внимания?

Приложение не ставит диагноз, не назначает лечение и не подтверждает
совместимость лекарств. Оно показывает домашний инвентарь и внесённые
пользователем связи и заметки.

### 2. Пользователь и платформы

- один владелец домашней аптечки;
- Windows и Android;
- русский язык — первый интерфейсный язык;
- приложение полностью работает без сети, кроме синхронизации GitHub;
- один приватный GitHub-репозиторий данных на одну аптечку.

### 3. Основные сценарии

#### 3.1. Поиск позиции

Пользователь вводит название, алиас или действующее вещество. Результат
показывает карточку, фотографию, описание, вычисляемое наличие и упаковки.

Если совпадений нет, приложение предлагает создать новую позицию. Пустая
позиция без упаковок допустима: она считается отсутствующей.

#### 3.2. Учёт упаковок

У одной позиции может быть несколько упаковок с разными сроками годности,
датами вскрытия и состоянием запаса. Количество таблеток не учитывается.

Состояние запаса упаковки выбирается вручную:

- `available` — в наличии;
- `low` — скоро закончится;
- `depleted` — закончилось.

Просроченность вычисляется отдельно и всегда делает упаковку непригодной.

#### 3.3. Поиск по проблеме

Пользователь выбирает бытовую проблему, например «головная боль» или «рана».
Приложение показывает связанные с ней позиции и отдельно отмечает их наличие.

Формулировка интерфейса: «Отмечено для этой проблемы», а не «Рекомендуется
принять». Связи создаются и редактируются пользователем.

#### 3.4. Обязательный запас

Для позиции можно включить `keepInStock`. Если у неё не осталось ни одной
пригодной незаконченной упаковки, приложение:

1. показывает одну вычисляемую позицию в списке покупок;
2. один раз показывает локальное уведомление;
3. не создаёт дубликаты при следующих проверках и синхронизациях;
4. автоматически убирает системную причину покупки после появления
   пригодной упаковки.

Причина отсутствия не влияет на правило: упаковка могла закончиться,
просрочиться или быть удалена.

#### 3.5. Сроки годности

Для каждой упаковки срок на этикетке необязателен и может быть введён с точностью
до дня или месяца. Месяц нормализуется к его последнему дню. Если указаны дата вскрытия
и срок хранения после вскрытия, приложение вычисляет дополнительный срок.
Эффективный срок — более ранний из двух известных сроков.

По умолчанию приложение проверяет приближение срока за 90, 30 и 7 дней. Эти
пороги являются локальной настройкой устройства.

#### 3.6. Фотографии

Позиция может иметь обложку и дополнительные фотографии. Изображения
сжимаются в WebP до отправки в репозиторий. Исходные фотографии телефона
не изменяются.

#### 3.7. Совместимость и экспорт

Приложение экспортирует в Markdown названия, категории, формы и действующие
вещества. Пользователь может передать экспорт внешнему инструменту, а затем
внести результат как ручную заметку с источником и датой проверки.

Заметка не становится автоматическим медицинским вердиктом. В интерфейсе
всегда видны её источник и дата.

### 4. Основные экраны

- главная: поиск, требующие внимания позиции, ближайшие сроки;
- каталог: все позиции и фильтры по наличию и категории;
- карточка позиции: описание, фото, упаковки, проблемы и заметки;
- редактор позиции и упаковки;
- проблемы: обратный поиск;
- покупки: активные и недавно закрытые позиции;
- синхронизация и настройки.

### 5. Вычисляемое наличие позиции

В расчёте участвуют только неудалённые и непросроченные упаковки:

1. Если есть хотя бы одна упаковка `available`, позиция имеет состояние
   `available`.
2. Иначе, если есть хотя бы одна упаковка `low`, позиция имеет состояние
   `low`.
3. Иначе позиция имеет состояние `missing`.

Наличие не сохраняется в файле позиции: оно всегда вычисляется заново.

### 6. Границы текущего продукта

Не входят в текущий план:

- Telegram-бот;
- штрихкоды и OCR;
- подсчёт таблеток или миллилитров;
- места хранения;
- несколько пользователей и роли;
- собственный сервер;
- автоматический подбор лечения;
- автоматическая оценка совместимости;
- дозировки и напоминания о приёме лекарств.

### 7. Критерии готовности P0

- для каждой сущности определены поля, идентификаторы и инварианты;
- разделены хранимые и вычисляемые состояния;
- описаны действия при окончании и истечении срока;
- определены границы между приложением, локальными файлами и GitHub;
- описаны конфликты, удаления и хранение учётных данных;
- P1 может начать сквозной прототип без нового архитектурного решения.

## English

### 1. Purpose

Aptechka answers three household questions: whether an item is available, which
owned items were tagged for a problem, and what has expired or needs attention.
It does not diagnose, prescribe treatment, or validate drug interactions.

### 2. User and platforms

- one medicine-cabinet owner;
- Windows and Android;
- Russian is the first UI language;
- all features work offline except GitHub synchronization;
- one private GitHub data repository per cabinet.

### 3. Core behavior

Search covers item names, aliases, and active ingredients. An item may represent
a medicine or a medical supply and may exist without packages. Each package has
an optional labeled expiration date, optional opening date and after-opening
shelf life, and a manual stock state: `available`, `low`, or `depleted`.

Problem search displays user-defined associations as “Tagged for this problem,”
never as treatment advice.

When an item marked `keepInStock` becomes `missing`, the app shows exactly one
derived restock entry and one local notification. The automatic reason disappears
when a usable package becomes available again. The cause of absence is irrelevant.

Expiration reminders use local device thresholds of 90, 30, and 7 days by
default. Photos are compressed to WebP before synchronization.

Interaction analysis happens outside the app. The app exports Markdown and stores
the returned result only as a manual note with its source and review date.

### 4. Main screens

- home: search, attention items, and upcoming expirations;
- catalog: all items with availability and category filters;
- item details: description, photos, packages, problems, and notes;
- item and package editors;
- problems: reverse lookup;
- restock list;
- synchronization and settings.

### 5. Derived availability

Only non-deleted, non-expired packages participate. Any `available` package makes
the item `available`; otherwise any `low` package makes it `low`; otherwise it is
`missing`. Item availability is never persisted.

### 6. Current exclusions

The current plan excludes a Telegram bot, barcode/OCR support, unit counting,
storage locations, multi-user roles, a custom server, automated treatment or
interaction advice, dosage tracking, and medication schedules.

### 7. P0 acceptance

P0 is complete when entities and invariants, derived states, restock behavior,
application/GitHub boundaries, conflict and deletion handling, and credential
storage are documented well enough to start the P1 end-to-end prototype without
another architectural decision.
