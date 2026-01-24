# Ul8ziz.FittingApp

A Windows desktop application for hearing aid fitting, built with C# and WPF. The application integrates with the Ezairo Sound Designer SDK to communicate with hearing aid devices through wired programmers such as HI-PRO.

## Overview

Ul8ziz.FittingApp provides a modern, maintainable codebase for hearing aid fitting workflows. The solution follows a layered architecture that separates UI, business logic, device communication, and data persistence, enabling clean integration with the Ezairo Sound Designer SDK.

## Key Capabilities

- WPF-based user interface for fitting workflows
- Integration layer for Ezairo Sound Designer SDK
- Support for wired programmers (HI-PRO, DSP3, CAA, Promira)
- Patient data management and session tracking
- Modular architecture for maintainability and testing

## Technology Stack

- **.NET 7** with Windows Presentation Foundation (WPF)
- **C#** for application logic
- **Ezairo Sound Designer SDK** for device communication
- **SQLite** for local data persistence
- **xUnit** for unit testing

## Repository Structure

```
FattingAPP/
├── src/
│   ├── App/              # WPF UI project
│   ├── Core/             # Domain models and business logic interfaces
│   ├── Device/           # SDK interop layer and device communication
│   ├── Data/             # SQLite persistence layer
│   └── Shared/           # Shared utilities and common types
├── tests/                # Unit tests
├── docs/                 # Documentation
├── scripts/              # Build and setup scripts
└── SoundDesignerSDK/     # Ezairo SDK (read-only, must exist at repo root)
```

## Prerequisites

Before building or running the application, ensure the following are installed:

- **Windows 10/11** (64-bit recommended)
- **Visual Studio 2022** with Desktop development workload (WPF)
- **.NET SDK 7.0**
- **Ezairo Sound Designer SDK** present at `./SoundDesignerSDK/` (read-only folder)

## SDK Runtime Prerequisites

The Ezairo Sound Designer SDK requires additional runtime components. Install the following from the SDK's redistribution folder:

### Required for SDK

- **Microsoft Visual C++ 2022 Redistributable (x86)**
  - Location: `SoundDesignerSDK/redistribution/MS VC++ 2022 Redist (x86)/VC_redist.x86.exe`
  - **Note:** This is required for the SDK to function.

### Required for Wired Programmers (CTK)

If you plan to use wired programmers (other than Noahlink Wireless), install:

- **CTK Runtime**
  - Location: `SoundDesignerSDK/redistribution/CTK/CTKRuntime.msi` (or `CTKRuntime64.msi` for 64-bit)
- **Microsoft Visual C++ 2010 SP1 Redistributable (x86)**
  - Location: `SoundDesignerSDK/redistribution/MS VC++ 2010 Redist (x86)/vcredist_x86.exe`
  - **Note:** Required dependency for CTK.

## Programmer Setup

### HI-PRO Programmer

For HI-PRO programmer support:

1. Install the HI-PRO driver (version 4.02 or later, available from Otometrics®).
2. Add the HI-PRO installation directory to your system PATH:
   ```
   C:\Program Files (x86)\HI-PRO;
   ```

This PATH entry is required for the SDK to locate the HI-PRO communication libraries.

## Setup Steps

### 1. Copy SDK Runtime Files

Copy the required Windows SDK runtime files into the solution's device library folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\copy-sdk-deps.ps1 -Platform x64
```

This script copies the necessary DLLs and configuration files from `SoundDesignerSDK/binaries/win64/` into `src/Device/Libs/`.

### 2. Verify SDK Setup

Verify that the SDK folder structure and copied runtime files are present:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-sdk.ps1 -Platform x64
```

This script checks for required SDK folders and validates that runtime dependencies are correctly copied.

### 3. Build the Solution

Open `Ul8ziz.FittingApp.sln` in Visual Studio 2022 and build the solution. The default platform configuration is x64.

## Documentation

For detailed step-by-step setup instructions, including prerequisites installation and troubleshooting, see [docs/Setup.md](docs/Setup.md).

## Notes

- The `SoundDesignerSDK/` folder is treated as read-only. Do not modify files within this directory.
- SDK runtime files are copied into `src/Device/Libs/` by the setup scripts. These files are tracked in the repository.
- The application targets Windows x64 by default, but scripts support both x64 and x86 platforms via the `-Platform` parameter.