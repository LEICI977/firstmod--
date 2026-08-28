# Bundled LangGraph backend

Each release archive contains one self-contained backend for exactly one
platform. The SMAPI mod starts the matching executable automatically and talks
to it over loopback.
Players do not need Python, pip, or a virtual environment.

Build the artifact on the target operating system:

- Windows x64: `scripts/build-langgraph-backend.ps1`
- Intel macOS: `scripts/build-langgraph-backend.sh`
- Apple Silicon macOS: `scripts/build-langgraph-backend.sh`

macOS binaries must be code-signed and notarized before public distribution.

Create a platform-specific archive with:

```powershell
.\scripts\package.ps1 -Configuration Release -BackendPlatform win-x64
```

The same script can package `osx-x64` or `osx-arm64` after that platform's
backend artifact has been built. Do not combine Windows and macOS backends in
one public archive.
