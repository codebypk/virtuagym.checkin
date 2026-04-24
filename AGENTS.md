# Agents Instructions

## Workflow
- Schlage Änderungen immer zuerst vor und warte auf meine Bestätigung, bevor du Code generierst oder Dateien änderst.
- Erkläre den Plan auf Deutsch.
- Beachte die Mehrsprachigkeit (Deutsch/Englisch) Übersetzungen.

## Projektkontext
- Solution: Virtuagym.CheckIn
- Sprache: C# / .NET 10
- Kommunikation und Kommentare: Englisch
- UI-Projekte: Virtuagym.CheckIn.WPF (WPF Desktop) und Virtuagym.CheckIn.Web (Blazor Server WebApp)
- Virtuagym.CheckIn.Web nutzt ausschließlich **Blazor Server (Interactive Server Rendering)** – alle Logik wird serverseitig ausgeführt. Es wird kein WebAssembly (WASM) verwendet. Zugriff auf lokale Dateien und Systemressourcen erfolgt direkt auf dem Server.

## Dual-Projekt-Regel
- Alle Anpassungen (UI, Services, Logik, Konfiguration, Bug-Fixes), die in der WPF-Anwendung durchgeführt werden, müssen auch in der Web-Anwendung durchgeführt werden (und umgekehrt).
- Neue Features müssen immer in beiden Projekten implementiert werden.
- Gemeinsame Logik gehört in das Core-Projekt, nicht in die UI-Projekte.

## Code-Stil
- File-Scoped Namespaces
- Primary Constructors wo sinnvoll
- XML-Dokumentation auf Englisch
- Nullable Reference Types aktiviert
- Methoden, Variablen, Beschreibungen, Fehlermeldungen in Englisch
 
