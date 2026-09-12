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
- Node.js **20.19+** (или 22 LTS) и npm 10+
- Java JDK 17+ (`JAVA_HOME`, подойдёт JBR из Android Studio)
- Android SDK (`ANDROID_HOME`) с `platform-tools` и `build-tools`
- USB debugging и одно авторизованное устройство (`adb devices` → `device`)

### Запуск

```powershell
powershell -File scripts/test-android-e2e.ps1
```

Несколько устройств:

```powershell
powershell -File scripts/test-android-e2e.ps1 -DeviceId <adb-id>
```

`-NoBuild` пропускает сборку, `-KeepE2EData` не вызывает `pm clear` для E2E-пакета.

Логи и E2E APK: `artifacts/e2e/android/` (`appium.log`, `apk/`, trx).

### Типичные ошибки

- **adb unauthorized** — разблокировать телефон и принять отладку по USB.
- **ANDROID_HOME / SDK** — установить platform-tools и build-tools, задать `ANDROID_HOME`.
- **Appium port 4723 busy** — остановить чужой Appium вручную. Скрипт не убивает чужие Node-процессы.
- **Node.js too old** — Appium 3 не стартует на Node 18; нужна 20.19+.

## English

The E2E build uses `io.github.vakineti.aptechka.e2e` so it can sit next to the
production app. Production data must never be cleared. The runner only runs
`pm clear` against the E2E package, and only after APK metadata confirms that id.

Prerequisites: .NET 9, Node.js 20.19+, npm 10+, JDK 17+, Android SDK, one authorized
device.

```powershell
powershell -File scripts/test-android-e2e.ps1
powershell -File scripts/test-android-e2e.ps1 -DeviceId <adb-id>
```

Logs land in `artifacts/e2e/android/`. Typical failures: unauthorized adb, missing
SDK, Node older than 20.19, or port 4723 already taken by another Appium server.
