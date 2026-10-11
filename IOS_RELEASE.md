# iOS Release Guide — Empire Rush

Status of the iOS target for this build. Read this before attempting an App Store build.

## 1. Verified local toolchain

| Component | Version | How it was checked |
|---|---|---|
| Unity | `6000.6.5f1` (arm64) | `/Applications/Unity/Hub/Editor/6000.6.5f1-arm64` |
| Xcode | **26.3** (build `17C529`) | `xcodebuild -version` |
| iPhoneOS SDK | **26.2** (`iphoneos26.2`) | `xcodebuild -showsdks` |
| iOS Simulator SDK | 26.2 (`iphonesimulator26.2`) | `xcodebuild -showsdks` |
| macOS | 24.6.0 (arm64) | host |

Apple's October 2026 App Store rule requires submissions to be built with **Xcode 26 or newer and an
iOS 26-or-newer SDK**. The installed Xcode 26.3 / iOS SDK 26.2 satisfies that requirement.

## 2. Blocker: the iOS Build Support module is not installed

`/Applications/Unity/Hub/Editor/6000.6.5f1-arm64/PlaybackEngines/` contains only:

```
MacStandaloneSupport
WebGLSupport
```

There is **no `iOSSupport` directory**, so Unity cannot produce an Xcode project on this machine.
No device build was attempted and none is claimed.

To unblock:

1. Unity Hub → **Installs** → `6000.6.5f1` → **Add modules** → **iOS Build Support**.
   The Hub advertises roughly **832 MB download / ~2.6 GB installed** for this module (plus the
   Apple silicon IL2CPP toolchain it pulls in).
2. Verify afterwards: `ls /Applications/Unity/Hub/Editor/6000.6.5f1-arm64/PlaybackEngines/iOSSupport`
   must exist.
3. Re-run the batch-mode compile to confirm nothing else is missing (see `README.md`).

## 3. iOS settings already applied by the project

`EmpireRush.Editor.ProjectBootstrap.Run` writes all of these, so they survive a fresh clone as soon
as the bootstrap is re-run:

| Setting | Value | API used |
|---|---|---|
| Company / product | `EmpireRush` / `EmpireRush` | `PlayerSettings.companyName`, `productName` |
| Bundle identifier | `com.empirerush.kingdom` | `PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, …)` |
| Bundle version | `0.1.0` | `PlayerSettings.bundleVersion` |
| Scripting backend | IL2CPP | `SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP)` |
| Minimum iOS | 15.0 | `PlayerSettings.iOS.targetOSVersionString` |
| Orientation | Portrait only (autorotate off) | `defaultInterfaceOrientation`, `allowedAutorotateToPortrait` |
| Full screen | required | `PlayerSettings.iOS.requiresFullScreen` |
| Status bar | hidden | `PlayerSettings.statusBarHidden` |
| Active input handling | Both (`2`) | `ProjectSettings.asset: activeInputHandler` |
| Render pipeline | `EmpireRush_URP.asset` assigned to `GraphicsSettings.defaultRenderPipeline` | bootstrap |

Notes for maintainers: in Unity 6 there is **no `PlayerSettings.iOSRequiresFullScreen` or
`PlayerSettings.iOSStatusBarHidden`** — only the `PlayerSettings.iOS.*` forms above exist.
Screen orientation ordinal `0` in `ProjectSettings.asset` is `UIOrientation.Portrait`.

## 4. Producing the Xcode project (once the module is installed)

Interactive:

```
File > Build Settings > iOS > Switch Platform > Build
```

Headless (after the module is installed):

```bash
U=/Applications/Unity/Hub/Editor/6000.6.5f1-arm64/Unity.app/Contents/MacOS/Unity
gtimeout 1800 "$U" -projectPath "$PWD" -batchmode -quit -nographics \
  -buildTarget iOS -executeMethod EmpireRush.Editor.ProjectBootstrap.Run \
  -logFile /tmp/er_ios_bootstrap.log
```

A dedicated `-executeMethod` iOS player build entry point is **not** part of this milestone; the
Xcode project is produced from the Build Settings dialog. Then:

1. Open `Build/iOS/Unity-iPhone.xcodeproj` in Xcode 26.3.
2. Signing & Capabilities → set the Team; keep automatic signing.
3. Select the `Unity-iPhone` scheme, a physical device, and Run to install.
4. TestFlight: Product → Archive → Distribute App → App Store Connect.

## 5. App Store preparation checklist

- [ ] Bundle identifier registered in App Store Connect (`com.empirerush.kingdom`).
- [ ] 1024×1024 app icon (not yet authored).
- [ ] Screenshots for the required iPhone size classes.
- [ ] Age rating questionnaire (mild fantasy violence).
- [ ] Privacy nutrition labels — no tracking SDKs are present in this build;
      review again after AdMob/IAP are integrated (`MONETIZATION_SETUP.md`).
- [ ] Export compliance: the app uses only standard HTTPS; confirm the exemption.
- [ ] `app-ads.txt` hosted on the developer domain before AdMob goes live.
- [ ] In-app purchase products created in App Store Connect and matched to the identifiers in
      `MONETIZATION_SETUP.md`.
- [ ] AdMob app-readiness review completed (AdMob requires a published/approved app before real ads
      serve; test ad units only until then).

## 6. Honestly unverified

These cannot be asserted from this build:

- Any device or simulator build (blocked on the missing module above).
- On-device frame rate, memory, thermal behaviour, and IL2CPP/AOT build time.
- Touch feel (tap forgiveness, pinch-vs-pan disambiguation) on real hardware.
- Safe-area / notch layout — there is no UI canvas in this milestone to test.
- App Store review outcome, TestFlight processing, and AdMob/IAP store behaviour.
