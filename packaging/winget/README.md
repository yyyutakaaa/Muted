# winget

These manifests are the first submission of Muted to the
[Windows Package Manager community repository](https://github.com/microsoft/winget-pkgs).
winget only accepts a new package through a pull request on that repository, so the
first version goes in by hand:

```powershell
winget install Microsoft.WingetCreate
wingetcreate submit --token <github-token> .\packaging\winget\manifests\y\yyyutakaaa\Muted\0.6.0
```

The token needs the `public_repo` scope. Once that pull request is merged, every stable
release does the rest itself: add the same token as the `WINGET_TOKEN` repository
secret and the release workflow runs `wingetcreate update` for each new version.

Before submitting, check the manifests locally:

```powershell
winget validate .\packaging\winget\manifests\y\yyyutakaaa\Muted\0.6.0
winget install --manifest .\packaging\winget\manifests\y\yyyutakaaa\Muted\0.6.0
```
