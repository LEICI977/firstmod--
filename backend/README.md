# Bundled LangGraph backend

The release package contains one self-contained backend per platform. The SMAPI
mod starts the matching executable automatically and talks to it over loopback.
Players do not need Python, pip, or a virtual environment.

Build the artifact on the target operating system:

- Windows x64: `scripts/build-langgraph-backend.ps1`
- Intel macOS: `scripts/build-langgraph-backend.sh`
- Apple Silicon macOS: `scripts/build-langgraph-backend.sh`

macOS binaries must be code-signed and notarized before public distribution.
