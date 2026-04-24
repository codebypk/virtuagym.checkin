# Virtuagym.CheckIn.Web – Installationsanleitung

## Inhaltsverzeichnis

1. [Voraussetzungen](#voraussetzungen)
2. [Projekt kompilieren und veröffentlichen](#projekt-kompilieren-und-veröffentlichen)
3. [Konfiguration (appsettings.json)](#konfiguration-appsettingsjson)
4. [Background Services – Wichtige Hinweise](#background-services--wichtige-hinweise)
5. [Anwendung starten](#anwendung-starten)
6. [Betrieb als Windows-Dienst (optional)](#betrieb-als-windows-dienst-optional)
7. [Troubleshooting](#troubleshooting)

---

## Voraussetzungen

| Komponente | Mindestversion |
|---|---|
| .NET Runtime | **10.0** (Windows) |
| Betriebssystem | Windows 10/11 oder Windows Server 2019+ (`net10.0-windows`) |
| Hardware (optional) | USB-QR-Scanner, HID-Kartenleser, CCID-Smartcard-Leser |

> **Hinweis:** Das Projekt nutzt `net10.0-windows` als Target-Framework. Die Anwendung kann daher **nur auf Windows** betrieben werden.

---

## Projekt kompilieren und veröffentlichen

```powershell
# Aus dem Solution-Root:
dotnet publish Virtuagym.CheckIn.Web\Virtuagym.CheckIn.Web.csproj -c Release -o .\publish\web
```

Im Ordner `.\publish\web` befindet sich anschließend die lauffähige Anwendung.

---

## Konfiguration (appsettings.json)

1. Die Datei `appsettings.json.template` als Vorlage verwenden:

   ```powershell
   copy publish\web\appsettings.json.template publish\web\appsettings.json
   ```

2. Folgende Werte in `appsettings.json` anpassen:

| Einstellung | Beschreibung |
|---|---|
| `VirtuagymApiKey` | API-Key des Virtuagym-Kontos |
| `VirtuagymClubSecret` | Club-Secret für die API-Authentifizierung |
| `MemberCheckinApiV0Key` | API-Key für die Member-Checkin-API v0 |
| `MemberCheckinApiV0Username` | Benutzername für die Member-Checkin-API v0 |
| `MemberCheckinApiV0Password` | Passwort für die Member-Checkin-API v0 |
| `JablotronApiUsername` | Jablotron-Benutzername (falls Zutrittskontrolle genutzt wird) |
| `JablotronApiPassword` | Jablotron-Passwort |
| `AppLanguage` | Sprache der Anwendung (`de` oder `en`) |
| `CheckinClientMappings` | JSON-Array der Checkin-Client-Zuordnungen |

3. **Weitere wichtige Einstellungen:**

   - `MemberCacheEnabled` / `CacheSyncEnabled` – Steuern den Member-Cache und die automatische Synchronisation.
   - `CacheSyncMode` – `"Interval"` (alle X Minuten) oder `"Daily"` (zu einer festen Uhrzeit via `CacheSyncDailyTime`).
   - `CacheSyncIntervalMinutes` – Sync-Intervall in Minuten (Standard: 480).
   - `LogRetentionDays` – Anzahl der Tage, bevor alte Log-Dateien gelöscht werden.
   - `WelcomeScreenEnabled` – Aktiviert den Welcome-Screen im Browser.

---

## Background Services – Wichtige Hinweise

Die Blazor-Server-Anwendung registriert zwei **Hosted Services** (`IHostedService`), die beim Start der Anwendung automatisch gestartet werden und **unabhängig von aktiven Browser-Verbindungen** im Hintergrund laufen:

### 1. `BackgroundTaskService`

Verantwortlich für:

- **Log-Cleanup** – Löscht alte Log-Dateien basierend auf `LogRetentionDays`.
- **Member-Cache-Initialisierung** – Lädt den Member-Cache beim Start und bereinigt transiente Daten.
- **Cache-Sync-Scheduler** – Synchronisiert den Cache regelmäßig mit der Virtuagym-API (Intervall- oder Tages-Modus).
- **Auto-Checkout-Scheduler** – Führt automatische Checkouts nach Ablauf der konfigurierten Zeit durch.

> **Beachten:** Ist `MemberCacheEnabled` auf `false` gesetzt, wird kein Cache geladen und die Sync-Funktion ist inaktiv. Alle Abfragen gehen dann direkt an die Virtuagym-API (höhere Latenz, API-Rate-Limits beachten).

### 2. `HardwareScannerService`

Verantwortlich für:

- **QR-Code-Scanner** – Initialisiert angeschlossene USB-Kameras für QR-Code-Erkennung (via OpenCvSharp).
- **HID-Kartenleser** – Liest RFID-Karten über HID-kompatible Lesegeräte.
- **CCID-Smartcard-Leser** – Liest Smartcards über CCID-kompatible Lesegeräte.

> **Beachten:**
> - Die Hardware-Geräte müssen am **Server** (nicht am Client-Browser) angeschlossen sein.
> - Der Anwendungs-Benutzer benötigt Berechtigungen für den Zugriff auf USB-Geräte.
> - Bei Betrieb als Windows-Dienst: Der Dienst läuft standardmäßig unter `Local System` – sicherstellen, dass USB-Geräte in diesem Kontext erreichbar sind, oder den Dienst unter einem Benutzerkonto laufen lassen.

### Wichtig für den Betrieb

- Beide Services werden als **Singletons** registriert und über `AddHostedService` gestartet.
- Der **Blazor-Server** muss dauerhaft laufen – wird der Prozess beendet, stoppen auch alle Background Services.
- Für den produktiven Einsatz wird empfohlen, die Anwendung als **Windows-Dienst** zu betreiben (siehe unten).

---

## Anwendung starten

### Direkt starten (Entwicklung / Test)

```powershell
cd publish\web
dotnet Virtuagym.CheckIn.Web.dll
```

Die Anwendung ist dann erreichbar unter:
- **HTTPS:** `https://localhost:65363`
- **HTTP:** `http://localhost:65364`

Die URLs können über `Properties\launchSettings.json` oder die Umgebungsvariable `ASPNETCORE_URLS` angepasst werden:

```powershell
$env:ASPNETCORE_URLS = "https://0.0.0.0:443;http://0.0.0.0:80"
dotnet Virtuagym.CheckIn.Web.dll
```

### Umgebung setzen

```powershell
# Produktion (Standard, kein detailliertes Error-Handling im Browser):
$env:ASPNETCORE_ENVIRONMENT = "Production"

# Entwicklung (detaillierte Fehlerseiten):
$env:ASPNETCORE_ENVIRONMENT = "Development"
```

---

## Betrieb als Windows-Dienst (optional)

Für den dauerhaften produktiven Betrieb empfiehlt sich die Installation als Windows-Dienst:

```powershell
# Dienst erstellen
sc.exe create "VirtuagymCheckInWeb" binPath="C:\path\to\publish\web\Virtuagym.CheckIn.Web.exe" start=auto

# Dienst starten
sc.exe start "VirtuagymCheckInWeb"
```

> **Hinweis:** Damit die `IHostedService`-Background-Tasks (Cache-Sync, Hardware-Scanner) korrekt laufen, muss der Dienst dauerhaft aktiv sein. Bei einem Neustart des Servers sollte der Dienst automatisch starten (`start=auto`).

Um die Anwendung nativ als Windows-Dienst zu betreiben, kann alternativ das NuGet-Paket `Microsoft.Extensions.Hosting.WindowsServices` eingebunden und `builder.Host.UseWindowsService()` in `Program.cs` ergänzt werden.

---

## Troubleshooting

| Problem | Lösung |
|---|---|
| Anwendung startet nicht | .NET 10 Runtime installiert? `dotnet --info` prüfen. |
| API-Fehler beim Check-in | `appsettings.json` prüfen – API-Keys und URLs korrekt? |
| Hardware wird nicht erkannt | USB-Geräte am Server angeschlossen? Berechtigungen prüfen. |
| Cache-Sync läuft nicht | `MemberCacheEnabled` und `CacheSyncEnabled` auf `true`? Logs prüfen. |
| Port bereits belegt | `ASPNETCORE_URLS` auf einen freien Port ändern. |
| Alte Logs werden nicht gelöscht | `LogRetentionDays` in `appsettings.json` prüfen. |
