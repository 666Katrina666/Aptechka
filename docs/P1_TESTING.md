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

Открой страницу создания fine-grained personal access token:

<https://github.com/settings/personal-access-tokens/new>

Выбери:

- Resource owner: `666Katrina666`;
- Repository access: `Only select repositories` → `aptechka-data`;
- Repository permissions → Contents: `Read and write`;
- разумный срок действия токена.

Другие разрешения приложению не нужны. Скопируй токен сразу после создания:
GitHub больше не покажет его полностью.

### 2. Установить и проверить Windows

Из корня репозитория запусти установочный скрипт:

```powershell
.\scripts\install-windows.ps1
```

Он собирает Release-версию, копирует её в
`%LocalAppData%\Programs\Aptechka` и создаёт ярлык «Аптечка» на рабочем столе.
Для повседневного запуска используй ярлык, а не файл из `bin\Debug`.

В приложении:

1. Убедись, что указаны `666Katrina666`, `aptechka-data`, `main`.
2. Вставь токен и сначала синхронизируй пустое приложение. Оно получит уже
   созданный манифест аптечки.
3. Заполни тестовую или настоящую позицию.
4. Нажми «Сохранить локально», затем «Синхронизировать».
5. Проверь, что статус сообщает об отправке данных.
6. Открой приватный `aptechka-data`: должны существовать `aptechka.json` и
   `items/{ulid}.json`.

После первой синхронизации поле токена можно оставлять пустым: приложение берёт
его из Windows Secure Storage.

### 3. Установить Android APK

Подключи телефон с включённой USB-отладкой и подтверди доверие компьютеру.
Проверь подключение:

```powershell
$aptechkaAndroidSdk = '<path-to-android-sdk>'
& "$aptechkaAndroidSdk\platform-tools\adb.exe" devices
```

Затем установи debug APK:

```powershell
$aptechkaApk = Resolve-Path `
  'src\Aptechka.App\bin\Debug\net9.0-android\io.github.vakineti.aptechka-Signed.apk'
& "$aptechkaAndroidSdk\platform-tools\adb.exe" install -r $aptechkaApk
```

Если USB-отладку использовать не хочется, APK можно передать на телефон вручную
и разрешить установку из выбранного файлового менеджера.

После установки «Аптечка» появится в списке приложений Android. Чтобы закрепить
её на домашнем экране, зажми значок и перетащи его на нужное место либо выбери
«Добавить на главный экран» — точное название зависит от лаунчера телефона.

### 4. Проверить Android → Windows

1. На чистом Android не заполняй карточку.
2. Вставь тот же токен — Secure Storage у телефона отдельный.
3. Нажми «Синхронизировать»: карточка с Windows должна появиться.
4. Измени описание или дозировку.
5. Нажми «Сохранить локально», затем «Синхронизировать».
6. На Windows снова нажми «Синхронизировать».
7. Проверь, что изменение с телефона появилось на ПК.

Если оба устройства изменили данные после последней синхронизации, P1 покажет
конфликт и ничего не перезапишет. Ручное разрешение конфликтов относится к P4.

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

Run `scripts/install-windows.ps1` to publish the Release build into
`%LocalAppData%\Programs\Aptechka` and create the desktop shortcut. On a fresh
Windows installation, enter the token and synchronize once to import the
existing manifest. Then save an item locally and synchronize it. Install the
signed debug APK, open a clean Android installation, enter the token, and
synchronize without creating an item first. Edit the pulled item on Android,
save and synchronize, then synchronize Windows again. Android exposes the app
in its launcher after installation; pin it to the home screen using the
launcher menu.

P1 never overwrites concurrent edits: it stops with a conflict. Report the
platform, visible status text, failing step, and whether a data commit appeared.
Never share the token or Authorization headers.
