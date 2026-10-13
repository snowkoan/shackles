# Optional local WESP client

Shackles builds and publishes without WESP. To use the WESP workspace in a local
build, obtain an authorized x64 client from the WESP preview 1.0.0 package and
place it here with this exact filename:

```text
local-dependencies/wesp/espclient.dll
```

The DLL is ignored by Git. Build and publish output include it beside
`Shackles.exe` only when this local file is present. Rebuild after adding it.
This directory is not searched at runtime.

At runtime Shackles checks `SHACKLES_WESP_CLIENT_DLL` first, then
`espclient.dll` beside the app, then `%SystemRoot%\System32\espclient.dll`.
An explicit override takes precedence even if its path is missing or unusable.
For an already running app, supply the DLL beside the app and use **Refresh
support** on the WESP page. Missing-client guidance appears before elevation;
starting a blocking session still requires administrator access and a matching
WESP driver.

A local SDK copy does not establish public redistribution rights. Follow the
SDK's terms before distributing it. See the
[WESP setup and compatibility guide](../../docs/WESP-BLOCKING.md) for driver
setup, upgrade precautions, and the pinned native DLL version.
