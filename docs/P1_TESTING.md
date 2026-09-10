# Проверка P1 / P1 verification

## Русский

### Что проверяет P1

P1 содержит одну редактируемую позицию аптечки и доказывает путь:

```text
Windows → private GitHub repository → Android → private GitHub repository → Windows
```

Обычные данные сохраняются локально сразу. GitHub-токен хранится отдельно в
системном Secure Storage каждого устройства.

### 1. Создать ограниченный GitHub-токен

Без правильных разрешений GitHub отвечает **404 Not Found** (как будто репозитория
нет). Старый токен без прав нужно **заменить новым**.

#### 1.1. Создать новый токен с правами

1. Открой: <https://github.com/settings/personal-access-tokens/new>
2. Заполни:
   - **Token name**: например `aptechka-rina`
   - **Expiration**: любой разумный срок (30–90 дней)
   - **Resource owner**: `666Katrina666`
   - **Repository access**: `Only select repositories` → выбери **`aptechka-data`**
3. В блоке **Repository permissions** найди **Contents** и поставь
   **`Read and write`** (не оставляй `No access`).
4. Остальные permissions можно не трогать.
5. Нажми **Generate token**.
6. Сразу скопируй токен (`github_pat_...`) в блокнот — GitHub больше его
   не покажет.

Проверка: в списке токенов
<https://github.com/settings/personal-access-tokens>
у нового токена должно быть видно `aptechka-data` и Contents: Read and write.

Старый токен без прав можно удалить там же (кнопка Delete / Revoke).

#### 1.2. Заменить токен в приложении

Приложение само перезапишет старый токен, если вставить новый в поле и
синхронизировать. Делать это нужно **на каждом устройстве** отдельно
(Windows и Android хранят токены порознь).

**Windows:**

1. Открой ярлык **«Аптечка»**.
2. Проверь поля: владелец `666Katrina666`, репозиторий `aptechka-data`, ветка `main`.
3. В поле **GitHub token** вставь **новый** токен (даже если снизу написано
   «Токен сохранён…» — вставь поверх).
4. Нажми **«Синхронизировать»**.
5. Поле очистится, статус должен стать успешным (не 404).

**Android:**

1. Открой **«Аптечка»** на телефоне.
2. Те же поля владельца / репозитория / ветки.
3. Вставь **тот же новый** токен.
4. Нажми **«Синхронизировать»**.

Не присылай токен в чат. Если снова будет 404 — напиши только текст статуса
и подтверди, что у токена точно Contents = Read and write на `aptechka-data`.

### 2. Установить и проверить Windows

#### 2.1. Если ярлык «Аптечка» уже есть на рабочем столе

Просто открой его двойным щелчком и переходи к шагу **2.3**.
Повторно запускать скрипт не обязательно.

#### 2.2. Как запустить установочный скрипт (если ярлыка ещё нет)

1. Открой папку репозитория `Aptechka` в Проводнике
   (обычно это `C:\UnityProjects\Aptechka`).
2. В адресной строке Проводника набери `powershell` и нажми Enter.
   Откроется окно PowerShell уже в нужной папке.
3. Вставь команду и нажми Enter:

```powershell
.\scripts\install-windows.ps1
```

4. Дождись конца сборки. В конце должны появиться строки примерно такие:
   - `Installed: ...\Programs\Aptechka\Aptechka.App.exe`
   - `Shortcut: ...\Аптечка.lnk`
5. На рабочем столе появится ярлык **«Аптечка»**. Дальше запускай только его.

Если PowerShell пишет, что выполнение скриптов запрещено, запусти установщик
однократно с обходом политики только для этого процесса:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-windows.ps1
```

#### 2.3. Что сделать в приложении на Windows

1. Открой ярлык **«Аптечка»**.
2. Проверь поля репозитория:
   - владелец: `666Katrina666`
   - репозиторий: `aptechka-data`
   - ветка: `main`
3. Вставь **токен**, который ты скопировала на шаге 1.
4. Карточку пока **не заполняй**. Сразу нажми **«Синхронизировать»**.
   Приложение подтянет уже существующий манифест из GitHub.
5. Теперь заполни тестовую позицию (название, дозировка и т.п.).
6. Нажми **«Сохранить локально»**, затем снова **«Синхронизировать»**.
7. Внизу экрана должен быть статус об успешной отправке.
8. Открой приватный репозиторий
   <https://github.com/666Katrina666/aptechka-data>
   и проверь, что есть файлы:
   - `aptechka.json`
   - `items/...json` (имя файла длинное, это нормально)

После первой успешной синхронизации поле токена можно оставлять пустым:
приложение запомнило его в Windows Secure Storage.

### 3. Установить Android APK на телефон

Самый простой способ — **без USB-отладки**: передать файл APK на телефон и
установить его как обычную программу.

> Важно: нужен **новый** APK (~100 МБ). Старый маленький (~14 МБ) после
> запуска сразу закрывался — в нём не было встроенных библиотек .NET.
> Для обычного обновления текущей сборки не удаляй приложение: см. шаг 3.5.

#### 3.1. Где лежит файл установки

На компьютере открой файл:

```text
C:\UnityProjects\Aptechka\src\Aptechka.App\bin\Debug\net9.0-android\io.github.vakineti.aptechka-Signed.apk
```

Это и есть установщик Android. Имя длинное — ориентируйся на окончание
`-Signed.apk`. Размер около **100 МБ** — это нормально.

Если файла нет или он слишком маленький, попроси пересобрать Android.

#### 3.2. Как передать APK на телефон

Выбери любой удобный способ:

1. **Telegram / WhatsApp себе**
   Отправь себе этот `.apk` файлом → открой сообщение на телефоне → скачай.
2. **Google Диск / Яндекс.Диск**
   Загрузи APK с компьютера → открой диск на телефоне → скачай файл.
3. **USB-кабель как флешка**
   Подключи телефон → скопируй APK в папку `Download` → отключи кабель.

#### 3.3. Как установить APK на Android

1. На телефоне открой скачанный файл `...-Signed.apk`
   (через «Файлы», «Загрузки», Telegram и т.п.).
2. Android спросит разрешение на установку из этого источника
   (файловый менеджер / Telegram / браузер). Нажми **Разрешить**.
3. Нажми **Установить**, затем **Открыть**.
4. В списке приложений появится **«Аптечка»**.

Чтобы закрепить на домашнем экране: зажми значок → «На главный экран»
(формулировка зависит от телефона).

> Это debug-сборка для проверки. Android может показать предупреждение
> «неизвестное приложение» — для нашего теста это ожидаемо.

#### 3.4. Установка через USB (необязательно, для разработчика)

Нужны включённая **USB-отладка** и Android SDK на ПК.

```powershell
$aptechkaAndroidSdk = 'C:\Program Files (x86)\Android\android-sdk'
& "$aptechkaAndroidSdk\platform-tools\adb.exe" devices
```

В списке должно появиться устройство. Затем:

```powershell
$aptechkaApk = Resolve-Path `
  'src\Aptechka.App\bin\Debug\net9.0-android\io.github.vakineti.aptechka-Signed.apk'
& "$aptechkaAndroidSdk\platform-tools\adb.exe" install -r $aptechkaApk
```

Если `adb devices` пустой — используй ручную установку из шагов 3.1–3.3.

### 3.5. Как обновить уже установленную «Аптечку»

Не удаляй приложение перед обновлением: удаление сотрёт локальные данные
аптечки и токен в Secure Storage, после чего синхронизацию придётся
настраивать заново.

`ApplicationId` остаётся `io.github.vakineti.aptechka`. Каждый следующий APK
должен быть подписан **тем же ключом**, а `ApplicationVersion` должна быть
**больше**, чем у установленной копии (сейчас 2 для версии 1.1.0).

#### Через USB (`adb install -r`)

```powershell
$aptechkaAndroidSdk = 'C:\Program Files (x86)\Android\android-sdk'
$aptechkaApk = Resolve-Path `
  'src\Aptechka.App\bin\Debug\net9.0-android\io.github.vakineti.aptechka-Signed.apk'
& "$aptechkaAndroidSdk\platform-tools\adb.exe" install -r $aptechkaApk
```

Флаг `-r` ставит APK поверх текущей установки. Не используй `adb uninstall`
и `pm clear`.

#### Вручную на телефоне

1. Передай новый `-Signed.apk` на телефон (Telegram, диск или USB).
2. Открой файл.
3. Если «Аптечка» уже установлена, Android предложит **«Обновить»** —
   выбери это, а не удаление.
4. Открой приложение и проверь, что каталог и сохранённый токен на месте.

Старый маленький APK (~14 МБ) без embedded assemblies после запуска сразу
закрывался. Его нужно было заменить полностью. Обычные обновления текущей
сборки (~100 МБ) ставятся поверх без удаления.

### 4. Проверить Android → Windows

1. Открой «Аптечку» на телефоне.
2. На **чистом** Android карточку пока **не заполняй**.
3. Вставь **тот же токен** (у телефона своё хранилище, ПК его не передаёт).
4. Нажми **«Синхронизировать»** — должна появиться карточка, которую ты
   сохранила на Windows.
5. Измени на телефоне описание или дозировку.
6. Нажми **«Сохранить локально»**, затем **«Синхронизировать»**.
7. Вернись на Windows и нажми **«Синхронизировать»**.
8. Проверь, что изменение с телефона появилось на ПК.

Если оба устройства изменили данные после последней синхронизации, P1 покажет
конфликт и ничего не перезапишет. Ручное разрешение конфликтов — в P4.

### 5. Что прислать при ошибке

- платформа: Windows или Android;
- точный текст статуса внизу экрана;
- на каком шаге произошла ошибка;
- появился ли commit в `aptechka-data`.

Токен, содержимое Secure Storage и заголовки Authorization присылать нельзя.

## English

P1 verifies one editable cabinet item across Windows, a private GitHub data
repository, and Android. Create a fine-grained token restricted to
`666Katrina666/aptechka-data` with repository Contents set to Read and write.
Each device stores its own copy in platform Secure Storage.

If the desktop shortcut «Аптечка» already exists, open it. Otherwise open
PowerShell in the repo root and run `.\scripts\install-windows.ps1`, then use
the created shortcut. Enter the token, synchronize once before filling the
card, then save locally and synchronize again. Confirm `aptechka.json` and
`items/*.json` appear in `aptechka-data`.

For Android, send
`src\Aptechka.App\bin\Debug\net9.0-android\io.github.vakineti.aptechka-Signed.apk`
to the phone (Telegram, Drive, or USB file copy), allow install from that
source, and open «Аптечка». USB/`adb` install is optional. To update an
already installed build, use `adb install -r` or open the new APK and choose
Update; do not uninstall, or local data and the saved token will be lost.
Each new APK needs a higher `ApplicationVersion` and the same signing key.

P1 never overwrites concurrent edits: it stops with a conflict. Report the
platform, visible status text, failing step, and whether a data commit appeared.
Never share the token or Authorization headers.
