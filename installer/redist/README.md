# WebView2 Evergreen Bootstrapper (installer redistributable)

This folder holds the official Microsoft Edge WebView2 **Evergreen Bootstrapper**
used by `SalrynSetup.iss` so clients never install WebView2 manually.

## File

| File | Role |
|------|------|
| `MicrosoftEdgeWebview2Setup.exe` | Evergreen Bootstrapper (small; downloads runtime during install) |

## Official source

- Microsoft Learn: [Distribute your app and the WebView2 Runtime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- Download redirect (Evergreen Bootstrapper): `https://go.microsoft.com/fwlink/p/?LinkId=2124703`

Silent install flags (official):

```text
MicrosoftEdgeWebview2Setup.exe /silent /install
```

## Hash (as of last download on this branch)

| Property | Value |
|----------|-------|
| Size | 1690320 bytes |
| SHA-256 | `23a55fbff920c0f99887848cfc25125f8f915df35638e01beb8f8fa9b5a0bc51` |

If missing, `tools/installer/Build-SalrynBetaRc1Installer.ps1` downloads it again from the official Microsoft link and records the SHA-256 in the build log.

## Client impact

- Beta installer may require **internet access** during setup so the bootstrapper can fetch the runtime.
- Offline standalone WebView2 bundling is deferred unless a later lane adds `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`.
