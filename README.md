# Aptechka

## Русский

Aptechka — локальная домашняя аптечка для Windows и Android. Приложение хранит
лекарства и медицинские расходники, помогает искать их по названию или бытовой
проблеме, отслеживает сроки годности и напоминает пополнить обязательный запас.

Данные принадлежат пользователю, доступны без сети и синхронизируются через
отдельный приватный GitHub-репозиторий. Репозиторий исходного кода и репозиторий
личных данных принципиально разделены.

### Зафиксированный объём

- позиции аптечки: лекарства и медицинские расходники;
- отдельные упаковки с разными сроками и состоянием запаса;
- поиск по названию, алиасам, действующему веществу и проблеме;
- фотографии позиций;
- отметка «всегда иметь дома»;
- список покупок и локальные напоминания;
- ручные заметки о совместимости;
- Markdown-экспорт для внешнего анализа;
- офлайн-работа и синхронизация Windows ↔ GitHub ↔ Android.

Не входят в текущий объём: Telegram-бот, штрихкоды/OCR, подсчёт таблеток,
места хранения, автоматическая диагностика и автоматические медицинские советы.

### Документы P0

- [Продуктовый контракт](docs/PRODUCT.md)
- [Архитектура](docs/ARCHITECTURE.md)
- [Модель данных](docs/DATA_MODEL.md)
- [Контракт синхронизации](docs/SYNC.md)

## English

Aptechka is a local-first home medicine cabinet for Windows and Android. It
stores medicines and medical supplies, supports search by name or household
problem, tracks expiration dates, and reminds the user to restock essential
items.

The user owns the data. It remains available offline and is synchronized through
a separate private GitHub repository. The source repository and the personal
data repository are intentionally isolated.

### Confirmed scope

- medicines and medical supplies;
- separate packages with independent expiration dates and stock states;
- search by name, alias, active ingredient, and problem;
- item photos;
- a personal “keep in stock” flag;
- restock list and local notifications;
- manually entered interaction notes;
- Markdown export for external analysis;
- offline operation and Windows ↔ GitHub ↔ Android synchronization.

Out of scope for now: a Telegram bot, barcode/OCR support, pill counting,
storage locations, automated diagnosis, and automated medical advice.

### P0 documents

- [Product contract](docs/PRODUCT.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Data model](docs/DATA_MODEL.md)
- [Synchronization contract](docs/SYNC.md)
