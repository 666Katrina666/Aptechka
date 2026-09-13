# Android E2E / Android E2E

## Русский

Отдельный пакет `io.github.vakineti.aptechka.e2e` («Аптечка E2E») ставится рядом с
настоящим приложением `io.github.vakineti.aptechka`. Android даёт ему свой sandbox,
поэтому canary-тесты не читают и не чистят данные Рины.

**Нельзя** выполнять `pm clear`, `uninstall` или другие destructive-команды для
`io.github.vakineti.aptechka`. Скрипт очищает только E2E-пакет и только после
проверки, что APK действительно имеет id `io.github.vakineti.aptechka.e2e`.

### Требования

- .NET 9 SDK
- Node.js `^20.19.0 || ^22.12.0 || >=24.0.0` и npm `>=10`
- Java JDK 17+ (`JAVA_HOME`, подойдёт JBR из Android Studio)
- Android SDK (`ANDROID_HOME`) с `platform-tools` и `build-tools`
- USB debugging и одно авторизованное устройство (`adb devices` → `device`)
- Appium 3.7.0 и UiAutomator2 8.1.1 из `tools/appium` (`package-lock.json` / `npm ci`).
  Глобальный Appium и ручная регистрация драйвера (`appium driver install`) не нужны.

### Запуск

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-android-e2e.ps1
```

Несколько устройств:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-android-e2e.ps1 -DeviceId <adb-id>
```

`-NoBuild` пропускает сборку, `-KeepE2EData` не вызывает `pm clear` для E2E-пакета.

Каждый E2E-класс поднимает свою Appium-сессию (`noReset=false`, без `fullReset`)
и работает с чистым `io.github.vakineti.aptechka.e2e`. Классы не делят данные
и не зависят от порядка выполнения.

Логи и E2E APK: `artifacts/e2e/android/` (`appium.log`, `apk/`, trx).

### Типичные ошибки

- **adb unauthorized** — разблокировать телефон и принять отладку по USB.
- **ANDROID_HOME / SDK** — установить platform-tools и build-tools, задать `ANDROID_HOME`.
- **Appium port 4723 busy** — остановить чужой Appium вручную. Скрипт не убивает чужие процессы и не трогает устройство.
- **Node.js / npm** — Appium 3 принимает только `^20.19.0 || ^22.12.0 || >=24.0.0` и npm `>=10`. Node 18 не подходит.
- **doctor / emulator** — обязательные проверки ANDROID_HOME, JAVA_HOME, `java` и `adb` должны пройти. Отсутствующий `emulator.exe` для физического устройства не блокирует runner.

## English

The E2E build uses `io.github.vakineti.aptechka.e2e` so it can sit next to the
production app. Production data must never be cleared. The runner only runs
`pm clear` against the E2E package, and only after APK metadata confirms that id.

Prerequisites: .NET 9, Node.js `^20.19.0 || ^22.12.0 || >=24.0.0`, npm `>=10`,
JDK 17+, Android SDK, one authorized device. Appium and UiAutomator2 load from
the project-local npm dependencies in `tools/appium`; a global Appium install
and `appium driver install` are not used.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-android-e2e.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-android-e2e.ps1 -DeviceId <adb-id>
```

Each E2E class starts its own Appium session (`noReset=false`, no `fullReset`)
against a clean `io.github.vakineti.aptechka.e2e`. Classes do not share data
and do not depend on execution order.

Logs land in `artifacts/e2e/android/`. Typical failures: unauthorized adb, missing
SDK, Node/npm outside the Appium 3 engine range, or port 4723 already taken by
another Appium server. Doctor must pass ANDROID_HOME, JAVA_HOME, `java`, and
`adb`; a missing `emulator.exe` is accepted for a physical device.
