# Home Tracker

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

Each component is a plain .NET library with no dependency on the app, so it can be referenced by any .NET project.

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build HomeTracker.slnx
dotnet test HomeTracker.slnx
```

The Android app additionally needs the MAUI Android workload (`dotnet workload install maui-android`) and a JDK 17.

## Contributing rules

- Dependencies must be MIT, Apache-2.0, BSD or similar permissive licenses. No GPL code. Record every dependency in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Never commit secrets (signing keys, passwords).
- Date and expiry logic must have unit tests.
