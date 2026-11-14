# Use It Before

The app is called **Use It Before** on the phone. In the code, projects and namespaces keep the earlier working name `HomeTracker`.

Reminds your household before food, sweets, medicines and other items expire, and (later) helps family members keep track of each other's medicines.

- Free and open source (MIT). No accounts, no paid services.
- Android first, then Windows. Built with .NET MAUI.
- Built from reusable components, so other apps (e.g. a product label checker) can use the same parts.

Product requirements: [`# Medicine Tracking & Adherence Platform.md`](%23%20Medicine%20Tracking%20%26%20Adherence%20Platform.md)

> **Not medical advice.** This app only reminds you about dates you or the pack provide. Always follow the label and your doctor or pharmacist.

## Components

| Component | What it does |
|---|---|
| [`Core.DateExtraction`](src/Components/Core.DateExtraction) | Reads expiry, manufacture and "best before N months" from label text (OCR output) |
| [`Core.ShelfLife`](src/Components/Core.ShelfLife) | Shelf-life durations, category presets (sweets, milk, eye drops…), reminder lead time |
| [`Core.ExpiryEngine`](src/Components/Core.ExpiryEngine) | Works out the effective expiry and status (fresh / use soon / expired) |
| [`Core.Items`](src/Components/Core.Items) | Tracked items, actions (opened, used up, thrown, undo, snooze), home-screen and reminder lists, export/import file format |
| [`Storage.Sqlite`](src/Components/Storage.Sqlite) | Stores items in a local SQLite database |
| [`Core.AppUpdater`](src/Components/Core.AppUpdater) | Checks a GitHub repository's latest release for a newer installer and downloads it, verified against GitHub's published SHA-256 |
| [`Device.Reminders`](src/Components/Device.Reminders) | Android daily reminder: schedule, grouped notification with action buttons, restore after restart, battery-saver checks |
| [`Core.LabelCapture`](src/Components/Core.LabelCapture) | Reads a pack photo: product barcode (with check-digit validation), a suggested product name (the largest text) and printed dates, through `IBarcodeReader` / `ITextReader` |
| [`Core.ProductCatalog`](src/Components/Core.ProductCatalog) | Product lookup by barcode from several sources in order (e.g. household history, then Open Food Facts) |
| [`Device.Vision`](src/Components/Device.Vision) | Android implementations of `IBarcodeReader` and `ITextReader` using Google ML Kit, on the phone; `PhotoShrinker` saves small, upright copies of photos |
| [`Core.Households`](src/Components/Core.Households) | Homes shared between phones, invite codes (QR / text), family phones and "hasn't checked in" logic |
| [`Core.Sync`](src/Components/Core.Sync) | End-to-end encryption (AES-256-GCM, `SealedBox`), compressed sync messages, splitting lists into relay-sized messages |
| [`Core.Relay`](src/Components/Core.Relay) | Publish and fetch messages through an [ntfy](https://ntfy.sh) server (public or self-hosted) |
| [`Device.BackgroundWork`](src/Components/Device.BackgroundWork) | Android periodic background work (JobScheduler, about every 15 minutes, survives restarts) |
| [`Device.HomeWidget`](src/Components/Device.HomeWidget) | Android home-screen widget: a header, up to three rows with a coloured status and an action button, and an Add button |

Each component is a library with no dependency on the app, so it can be referenced by other .NET projects. `Core.*` and `Storage.*` are plain .NET; `Device.*` contain platform code.

The app itself is in [`src/Apps/ExpiryTracker.App`](src/Apps/ExpiryTracker.App).

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build HomeTracker.slnx
dotnet test HomeTracker.slnx
```

The Android app additionally needs the MAUI Android workload (`dotnet workload install maui-android`), a JDK 17 and the Android SDK:

```sh
dotnet publish src/Apps/ExpiryTracker.App -f net10.0-android -c Release -p:AndroidPackageFormats=apk \
  -p:AndroidSdkDirectory=<android-sdk> -p:JavaSdkDirectory=<jdk>
```

The APK is written to `src/Apps/ExpiryTracker.App/bin/Release/net10.0-android/publish/`.

## Contributing rules

- Dependencies must be MIT, Apache-2.0, BSD or similar permissive licenses. No GPL code. Record every dependency in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Never commit secrets (signing keys, passwords).
- Date and expiry logic must have unit tests.
