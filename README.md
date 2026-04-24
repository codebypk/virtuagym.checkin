# Virtuagym CheckIn

Desktop and web application for member check-in using **RFID**, **CCID smart cards**, and **QR codes**, built with **WPF**, **Blazor Server**, and **.NET 10**.

The application connects local input devices with the **Virtuagym API** and optionally with **Jablotron** components to support check-ins, check-outs, door/relay control, and a welcome screen for reception or kiosk scenarios. 
It also includes a standalone **AccessPass** module for temporary offline access passes that work independently of Virtuagym.

## Features

- Check-in / check-out through Virtuagym
- Support for RFID readers, CCID readers, and QR code cameras
- Welcome screen for kiosk or reception use
- Local member cache for faster lookups and offline/fallback scenarios
- Duplicate scan protection
- Configurable device mappings per entrance / reader
- Optional relay and Jablotron integration
- Multilingual support through language files
- Separate test project for core logic and services

### AccessPass – Temporary Offline Access

The **AccessPass** project provides a self-contained feature for issuing temporary, limited-use access passes that are stored locally as JSON files — no Virtuagym account required.

- Create passes with a configurable number of uses and validity period
- Identify pass holders via card ID or encrypted QR code
- AES-256-CBC + HMAC-SHA256 encrypted QR tokens (`QrTokenCryptoService`)
- Automatic check-in / check-out toggle with remaining-use tracking
- Optional avatar photo per pass holder
- Local persistence under `Resources/access_pass/` (JSON data + avatars)

## Requirements

- Windows 10 or Windows 11
- .NET 10
- At least one of the following devices:
  - HID-compatible RFID reader
  - CCID smart card reader
  - Camera for QR code scanning
- Valid Virtuagym credentials / API keys (not required for AccessPass-only usage)
- Optional: Jablotron credentials for gate / cloud features

## License

This project is licensed under the **MIT License**.
