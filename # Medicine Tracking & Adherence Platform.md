# Home Expiry Tracker & Medicine Platform

> Status: Product requirements v4 – **zero cost, free/open-source, no own server, remote family alerts, Samsung-first**. Ready for implementation.
> Last updated: 2026-11-11

## Objective

**Main motto: never consume an expired or spoiled product at home.**

People keep food, sweets, chocolates, medicines and other items at home and forget to throw them away when they expire. Some items (homemade or loose sweets, leftovers) have **no printed label**, but should still not be eaten after a few days. Expired medicines are the most dangerous case.

The app reminds the household before items expire, with as little effort as possible.

Second goal: **build everything as reusable components**, so future apps (e.g. a product-quality checker that scans a label/barcode and checks FSSAI details) can be built from the same parts without rewriting code.

Target users:

- Households / families (shared home list)
- Elderly people and users who are not comfortable with technology
- Users who may not understand English (supported after MVP; architecture ready)

The user should do the minimum work. The system should do the maximum work.

---

# Confirmed Decisions

| Topic | Decision |
|---|---|
| Main focus (MVP) | Expiry tracking for **any** product: medicines, packaged food, sweets, chocolates, homemade/loose items, cosmetics, etc. |
| Medicine dose reminders | **Module 2, in the plan** (Release 4), built on the same components, with missed-dose alerts to family (see "Module 2") |
| **Remote family alerts** | **Missed-dose and expiry-nearing alerts reach the child or parent anywhere**, not only on home Wi-Fi. Works both ways (parent → child, child → parent). See "Remote Alerts". |
| **Cost** | **Zero.** No paid services, accounts, servers or stores. |
| **Open source** | Our code is **public on GitHub under the MIT License**. Third-party libraries must be permissive (MIT / Apache-2.0 / BSD) or free-to-use closed tools (Google ML Kit). No GPL libraries. See "Legal & Safety". |
| **Legal approach** | Safest route: only free services used within their terms; no unofficial WhatsApp automation; no medical advice; privacy notice; data encrypted end-to-end when it leaves the phone |
| **Target phones** | Samsung Galaxy S23 FE, S24 FE, S25 Ultra, A15 5G (One UI 6–8, Android 14–16). Other Android 8.0+ phones supported. See "Samsung Notes". |
| Platforms | **Android + Windows.** Code stays iOS-ready; iOS built only if a Mac becomes available. |
| Client stack | .NET MAUI (.NET 10) |
| Backend server | **None in MVP.** Everything runs on the devices. |
| Login | **No login.** Each device gets a local identity; a Home is joined by scanning a QR code. |
| Family sharing | **Home Wi-Fi sync** (full list, peer-to-peer) **+ encrypted internet relay** for changes and alerts when away from home. One-time QR pairing. |
| Notifications | Local notifications for own reminders; remote alerts via free open-source relay (ntfy) with **SMS fallback** from the phone's own SIM for missed doses |
| Distribution & updates | APK (Android) and installer (Windows) on **GitHub Releases**; app checks GitHub for new versions and offers to update |
| Barcode / text reading | Android: Google ML Kit (free, on-device). Windows: ZXing.Net (open source) for barcodes, Windows built-in OCR (free OS API) for text. |
| Product names | Open Food Facts (free, open data) + household's own scan history |
| Component reuse | Shared libraries in one repository (monorepo) |
| "Opened" tracking | In MVP |
| Default reminder | 1–2 days before expiry (1 day for short-life items; 2 days for others); changeable per item and category |
| Storage location | Not in MVP |
| Market | India first |
| Languages | English in MVP; Indian languages later |
| Testing | Unit tests for date/expiry logic only (safety-critical); manual testing on real phones before each release; broader testing later |

---

# Problem Statement

1. **Expired items are forgotten at home** – bought, stored, forgotten, eaten after expiry.
2. **Many items have no expiry label** – loose/homemade sweets, leftovers, bakery items, cut fruit. Need a custom "good for N days".
3. **Items go bad after opening** – milk, ketchup, jam, eye drops (often 28 days after opening), syrups.
4. **Expired medicines are dangerous** – kept "just in case", taken later.
5. **Labels are hard to read** – tiny print, many formats (`EXP 05/2027`, `USE BY 12.03.26`, `Best before 6 months from manufacture`, `MFD: 01/2026`).

---

# Assumptions Challenged

| Assumption | Challenge | Resolution |
|---|---|---|
| Every item has a printed date | Sweets, leftovers don't | Category presets ("Sweets – 3 days") + custom days |
| Printed date = real expiry | Opening changes it | "Opened" tap with after-opening shelf life |
| Typing the date is fine | Small print, elderly users | Camera reads the date; fallback big month/year picker |
| Barcode gives the expiry | Retail barcodes identify the product, not the batch | Barcode → product name; date from camera reading or manual |
| "Best before 6 months" is a date | It's relative to manufacture date | Parser calculates MFD + N months |
| One person manages the home | Anyone buys/opens things | Shared household list via Wi-Fi sync |
| Sharing needs a server + login | Costs money (SMS, hosting) | Peer-to-peer sync on home Wi-Fi, QR pairing, no login |
| A single reminder is enough | People see it and forget | Expired items stay at the top of the home screen until handled |

---

# Success Criteria

- Add an item with a printed date: **≤ 3 taps + one camera shot**, under 15 seconds.
- Add a loose item (sweets): **≤ 3 taps**.
- Mark Used / Thrown: **1 tap** from the reminder.
- No one has to calculate a date.
- Home screen understandable without training: **red = throw, orange = use soon, green = fine** (always with text).
- Total running cost: **₹0**.

---

# Design Principles

1. **Minimize typing** – camera, presets, chips, large buttons.
2. **Optimize for elderly users** – large text (respect system font size), large touch targets, high contrast.
3. **Multilingual-ready** – no hard-coded strings.
4. **Max three taps** for common actions.
5. **Offline-first, server-free** – everything works on the device; sync is a bonus.
6. **Component-first** – every capability is a reusable component with a clean interface.
7. **No safety advice beyond dates** – the app reminds; it never says "it's still safe".
8. **Zero cost** – no dependency may require payment, a credit card or a paid account.

---

# Users & Roles

| Role | Description |
|---|---|
| **Household member** | Adds items, marks opened/used/thrown, receives reminders |
| **Home creator** | Creates the Home on their device and shows the pairing QR code to others |

No accounts. A device can belong to more than one Home (e.g. own home + parents' home); one Home is shown at a time with a switcher.

---

# Item Lifecycle

```
Added ──► Fresh ──► Use soon ──► Expired ──► Thrown away
            │          │            │
            └── Opened (recalculates expiry) ─┘
            └──────────── Used up / Finished ──────────►  (closed)
```

**Effective expiry** = earliest of:
- Printed expiry date
- Manufacture date + "best before N months"
- Added date + custom shelf life (loose items)
- Opened date + after-opening shelf life

Month-only dates (`05/2027`) expire at the **end of that month**.

Statuses: **Fresh** (green) · **Use soon** (orange, inside reminder window) · **Expired** (red) · **Closed** (used up / thrown, kept in history).

---

# MVP User Journeys

## Journey A – First-time setup

1. Install the APK (from GitHub Releases link shared on WhatsApp).
2. "Create a Home" (default name "My Home") **or** "Join a Home" → scan the QR code shown on a family member's phone.
3. Allow notifications. Lands on the home screen with one big **+ Add item** button.

## Journey B – Add a packaged item (scan)

1. **+ Add item** → camera opens.
2. Scan barcode (optional): name/category filled from household history or Open Food Facts. Unknown → type/pick once; remembered next time.
3. Point at the expiry date → "Expires: May 2027 – correct?" → **Yes**. Handles MFD + "best before N months". Can't read → big month/year picker.
4. Optional quantity (default 1) and photo.
5. Save → reminder scheduled.

## Journey C – Add a loose / homemade item

1. **+ Add item** → **"No date on it"**.
2. Preset: Sweets (3 days) · Milk sweets (2 days) · Leftovers (1 day) · Bakery (3 days) · Cut fruit (1 day) · Custom.
3. Optional name and photo. Save.

Presets are editable per household.

## Journey D – Mark as opened

Tap item → **Opened today** → category's after-opening rule applies (Milk 2 days, Ketchup 30 days, Eye drops 28 days) or asks with chips. Expiry and reminder recalculated.

## Journey E – Reminder

1. 1–2 days before: **"Kaju katli – use by tomorrow"** with **Used up** / **Thrown** / **Remind tomorrow**.
2. Expired and unhandled → red, pinned at top of home screen.
3. Handled on one device → closed on all devices after the next sync.
4. Several due together → one grouped notification.

## Journey F – Home screen

Expired (red) → Use soon (orange) → Fresh (green), sorted by date. Filter chips: All · Food · Medicines · Other. Search.

## Journey G – Used up / Thrown / Edit

One-tap close, edit any field, Undo for 10 seconds.

## Journey H – Wi-Fi sync

1. Phones in the same Home find each other automatically on the same Wi-Fi.
2. Sync happens when the app opens, when an item changes, and periodically in the background while on Wi-Fi.
3. Home screen shows "Synced with Amma's phone 5 min ago".
4. Away from home → changes and alerts still travel through the encrypted internet relay (see "Remote Alerts"); full sync happens when back on home Wi-Fi.

## Journey I – App update

App checks GitHub Releases (at most once a day, only on Wi-Fi) → "New version available" → **Update** downloads and opens the installer. Android will ask once to allow installing from this app.

---

# Wi-Fi Sync Design (no server)

- **Pairing:** the Home creator's device shows a QR code containing the Home ID and a random secret key. Joining devices scan it once. The key never leaves the devices.
- **Discovery:** devices announce themselves on the local network (mDNS / UDP broadcast) with the Home ID only.
- **Transport:** direct device-to-device connection on the local network, encrypted with the Home's secret key.
- **Data model:** every change is an event (item added, opened, closed, edited) with device ID + timestamp. Devices exchange missing events; all devices end up with the same list.
- **Conflicts:** same field edited on two devices → latest timestamp wins; "closed" (used/thrown) always wins over edits so an item doesn't come back to life.
- **Limitations (accepted):**
  - Full sync only while devices are on the same Wi-Fi; away from home, changes travel through the relay (messages kept ~12 h).
  - Some routers block devices from talking to each other ("client/AP isolation"). Fallback: **share the list as a file via WhatsApp** and import it.
  - Mobile hotspot from one phone also works as a "same network".
- **Backup:** export/import the full list as a file (same format as the WhatsApp fallback).

---

# Remote Alerts (child ↔ parent, anywhere, free)

**Goal:** a son/daughter in another city gets "Appa missed his 9 PM tablet" or "Amma's eye drops expire tomorrow", and a parent can equally get alerts about the child's home. No paid server, no SMS gateway.

## Who gets what

| Alert | Sent from | Received by | When |
|---|---|---|---|
| **Missed dose** (Module 2) | Patient's phone | All family members who opted in for that person | Dose not marked TAKEN within 60 min |
| **Expiry nearing** | Any device in the Home | All family members who opted in for that Home | 1–2 days before (same as local reminder) |
| **Expired, not handled** | Any device in the Home | Opted-in family members | Day after expiry, once |
| **Low medicine stock** (Module 2) | Patient's phone | Opted-in family members | Refill threshold reached |
| **Phone silent for 24 h** | Receiver's own app | The receiver | No check-in received from a family phone for 24 h ("Appa's phone hasn't checked in – reminders may be blocked") |

Each family member chooses per Home / per person: **All alerts · Only missed doses · None**. Alerts are not repeated for every re-remind, only once per event.

## Channel 1 – Internet relay via ntfy (primary)

- **ntfy** is an open-source (Apache-2.0 / GPL-2.0) push notification service. Its public server **ntfy.sh is free**, needs no account and no card. It can also be self-hosted for free later; the relay address is a setting.
- At QR pairing, the Home gets a **random secret topic name** (unguessable) and the Home secret key.
- The sender's app **encrypts** the alert/event with the Home key (AES-256-GCM) and publishes it to the topic. The ntfy server only sees unreadable data; medicine names never leave the phone in plain text.
- Receiving phones run our app's background check (Android WorkManager, every ~15 minutes) to fetch new messages, decrypt them and show a normal notification. No second app needs to be installed.
- The same channel carries **sync events** (item added / opened / closed), so the list stays roughly up to date when family members are away from home Wi-Fi. Full sync still happens on home Wi-Fi.
- Each device also sends a small encrypted **check-in** a few times a day, which powers the "phone silent for 24 h" alert.
- **Delay:** usually under 15–30 minutes. Acceptable for expiry; for missed doses the SMS fallback covers urgency.
- **Limits:** ntfy.sh keeps messages ~12 hours and has per-device daily limits; a household's volume is far below them. If ntfy.sh is ever unavailable or changes its terms, switch the relay address to a self-hosted ntfy (free on any always-on PC/Raspberry Pi) without an app change.

## Channel 2 – SMS from the phone's own SIM (fallback, opt-in)

- For **missed doses only** (urgent, must work without internet).
- The patient's phone sends a plain SMS to the chosen family member's number using its own SIM. Most Indian mobile plans include 100 free SMS/day; the app sends at most a few per day and shows the expected count before the user enables it.
- Personal phone-to-phone SMS, with explicit consent, is not bulk/commercial SMS, so no DLT registration is needed.
- SMS text includes the medicine name: "Medicine reminder: Appa has not confirmed his 9 PM dose of Amlodipine 5mg. – Home Tracker". The opt-in screen tells the user that SMS is not encrypted (the mobile network can see it). A setting can hide the medicine name.
- The receiver doesn't need the app for this channel.
- Disabled by default; turned on during family setup with a clear explanation.

## Not used (and why)

- **WhatsApp automation** – unofficial WhatsApp bots break WhatsApp's terms; the official Business API costs money.
- **Firebase Cloud Messaging** – free, but sending requires a server holding secret credentials; putting those in the app would be insecure.
- **Email via the user's Gmail** – would require storing the user's password/app password; unsafe.

---

# Edge Cases

- **Date formats:** `MM/YY`, `MM/YYYY`, `DD/MM/YY`, `DD.MM.YYYY`, `MON YYYY`, `EXP`, `USE BY`, `BB`, `BEST BEFORE`, `MFD/MFG/PKD` + "N months".
- **Ambiguous camera reading:** always confirm with the user; never save silently.
- **Packed date only, no shelf life:** ask, with category default.
- **Several units with different dates:** separate items.
- **Phone restarted / app killed:** notifications rescheduled on boot and app start.
- **Battery savers:** see "Samsung Notes"; guided "keep reminders working" screen for other brands.
- **Receiver offline for a day:** relay keeps messages ~12 h; on reconnect the app also pulls full state on home Wi-Fi. Missed-dose SMS fallback doesn't depend on internet.
- **Patient phone switched off:** no alerts can be sent from it; receiver gets the "phone silent for 24 h" alert instead.
- **Alert storm:** at most one remote alert per event; grouped ("3 items expire tomorrow").
- **Two devices edit offline:** event merge as above.
- **Device lost:** other paired devices still have the full list; new device re-joins via QR.
- **Remove a device from Home:** creator rotates the Home key; removed device stops syncing.
- **Time zones:** dates are calendar dates; reminders at a chosen time (default 9:00 AM local).
- **Windows without camera:** manual add + "pick a photo" for text reading.

---

# Release Plan

| Release | Contents |
|---|---|
| **0. Setup** | Install free tooling (MAUI workload, Android SDK, Microsoft OpenJDK); repo and component projects; MIT license; dependency license check |
| **1. Android – single phone** | Manual add, presets, opened, home screen, used/thrown, local reminders, Samsung "keep reminders working" screen, export/import file, **in-app update check** (so you can ship updates from the start) |
| **2. Camera** | Barcode scan (ML Kit), expiry date reading (ML Kit text recognition + DateExtraction), Open Food Facts lookup |
| **2.1 Photos, widget, in-app update** (v0.3.0) | One product photo kept per item (shown on the item and the home list) and read for barcode, product name (largest text) and dates; "EX" / dotted month-year dates; home-screen widget (top 3 items, Used button, Add); updates downloaded in the app, checked against GitHub's SHA-256, then installed by Android's installer |
| **3. Family** (v0.4.0) | Done: share a home, invite by QR photo or WhatsApp text, several homes per phone with a switcher, **encrypted ntfy relay** sync (every minute while open, ~15 min in background, full resend after 12 h away), each phone's daily reminder covers every home (named when there are several), check-ins and "phone hasn't checked in for a day" alert. Deferred: direct Wi-Fi sync (the relay works at home too; Wi-Fi sync can't be tested on emulators), photo sharing, removing a phone by changing the home key |
| **4. Medicines (Module 2)** | Dose schedules, TAKEN/LATER/SKIP, exact alarms, dose stock, **missed-dose remote alerts** (ntfy + optional SMS fallback) |
| **5. Windows** | Windows build; ZXing.Net + Windows OCR implementations |

---

# Postponed

| Feature | Phase | Reason |
|---|---|---|
| Indian language UI + voice | Later | Translation work |
| Instant (< 1 min) remote alerts | Later | Needs a persistent connection or a push server; 15–30 min relay delay + SMS fallback accepted for now |
| iOS | When a Mac is available | Apple requires a Mac to build |
| Storage locations | Later | Declined for MVP |
| Weekly summary, shopping list, waste insights | Later | Nice-to-have |
| Play Store listing | Later | $25 fee |
| **FSSAI product checker app** | Separate app | Built from the same components |

---

# Component Architecture (Reuse Plan)

Each component lives in its own project, exposes an **interface** with platform implementations behind it, has **no dependency on this app's screens or database**, has unit tests where it holds logic, and a small sample page.

## Layer 1 – Pure logic (no UI, any .NET app)

| Component | Purpose | Reused by |
|---|---|---|
| **DateExtraction** | Label text → dates (`EXP 05/27`, `MFD 01/2026 BEST BEFORE 12 MONTHS`) | Expiry app, Medicine module, FSSAI checker |
| **ShelfLife** | Category presets, custom days, after-opening rules | Expiry app, Medicine module |
| **ExpiryEngine** | Effective expiry + status | Expiry app, Medicine module |
| **ProductCatalog** | Product model + `IProductInfoProvider` (local history, Open Food Facts, later FSSAI) | Expiry app, FSSAI checker |
| **Households** | Home, devices, pairing secrets | Any multi-device app |
| **Sync** | Event log, merge, conflict rules (transport-independent) | Any app |
| **Alerts** | Alert rules (who gets what, de-duplication, opt-in preferences), message formats | Expiry app, Medicine module, any family app |
| **Crypto** | Home key, AES-256-GCM encrypt/decrypt of events and alerts | Sync, relay, any app |
| **Localization** | Strings, formats | Any app |

## Layer 2 – Device components (interfaces + platform implementations)

| Component | Purpose | Android | Windows |
|---|---|---|---|
| **Scanner** | Camera + `IBarcodeScanner` | ML Kit barcode | ZXing.Net (webcam / photo) |
| **TextRecognition** | `ITextRecognizer`: image → text | ML Kit text recognition | Windows.Media.Ocr |
| **LabelCapture** | Scanner + OCR + DateExtraction in one "scan, read, confirm" flow | ✓ | ✓ |
| **Reminders** | `IReminderScheduler`: local notifications with actions, reschedule on boot | ✓ | ✓ |
| **LocalNetworkSync** | Discovery + encrypted transport for Sync | ✓ | ✓ |
| **RelayTransport** | `IRelayTransport`: publish/fetch encrypted messages via ntfy (public or self-hosted) | ✓ | ✓ |
| **SmsAlert** | Send opt-in fallback SMS from the device SIM | ✓ | – |
| **BackgroundWork** | Periodic background checks (WorkManager on Android) | ✓ | ✓ |
| **QrPairing** | Show / scan pairing QR | ✓ | ✓ |
| **AppUpdater** | Check GitHub Releases, download, launch installer | ✓ | ✓ |
| **ImageStore** | Save/compress photos | ✓ | ✓ |

## Layer 3 – UI components (MAUI controls)

BigButton, ExpiryBadge, ItemCard, DatePresetPicker, ConfirmDateSheet, ScanPage, EmptyState; elderly-friendly theme (large text, high contrast).

## Layer 4 – Apps

- **Expiry Tracker** (this MVP)
- **Medicine module** (later)
- **FSSAI Product Checker** (future)

## How the future FSSAI checker reuses this

1. **LabelCapture** scans barcode + reads label text.
2. A small new parser extracts the 14-digit **FSSAI licence number**, ingredients and nutrition.
3. **ProductCatalog** gets new providers: FSSAI licence verification + Open Food Facts.
4. New **ProductAssessment** component scores the product.
5. UI reuses ItemCard, badges, ScanPage.

> To verify before building: as far as I know, FSSAI has no public barcode-lookup API; its FoSCoS portal verifies **licence numbers**. The checker would read the licence number from the label and use Open Food Facts for ingredients/nutrition.

## Repository layout

```
/src
  /Components
    Core.DateExtraction
    Core.ShelfLife
    Core.ExpiryEngine
    Core.ProductCatalog
    Core.Households
    Core.Sync
    Core.Alerts
    Core.Crypto
    Core.Localization
    Device.Scanner
    Device.TextRecognition
    Device.LabelCapture
    Device.Reminders
    Device.LocalNetworkSync
    Device.RelayTransport
    Device.SmsAlert
    Device.BackgroundWork
    Device.QrPairing
    Device.AppUpdater
    Device.ImageStore
    UI.Controls
  /Apps
    ExpiryTracker.App          (MAUI: Android, Windows; iOS-ready)
/samples
/tests
LICENSE                        (MIT)
THIRD-PARTY-NOTICES.md
```

---

# Technical Direction

- **Client:** .NET MAUI (.NET 10), MVVM (CommunityToolkit.Mvvm), SQLite (sqlite-net) for local storage.
- **No backend server** in MVP.
- **All tools free:** .NET SDK + MAUI (MIT), Android SDK (free), Microsoft Build of OpenJDK (free), VS Code / CLI, Git, GitHub (free public repo + Releases).
- **Why MAUI:** Android + Windows from one C# codebase; reusable C# component libraries; iOS possible later from the same code.

---

# Samsung Notes (S23 FE, S24 FE, S25 Ultra, A15 5G)

All four run One UI 6–8 (Android 14–16) and have Google Play services, so ML Kit works on all of them.

**Installing the APK**
- Samsung **Auto Blocker** (Settings → Security and privacy → Auto Blocker) blocks apps from outside the Galaxy Store / Play Store. The install guide tells the user to switch it off, install, then switch it back on. Updates need the same step.
- Android asks once to "allow installing unknown apps" for the browser/file app used.

**Keeping reminders working** (guided screen with buttons that open the exact settings page)
- Battery: Settings → Apps → [our app] → Battery → **Unrestricted**.
- Settings → Battery → Background usage limits → add the app to **Never sleeping apps**; make sure "Put unused apps to sleep" doesn't include it.
- Notifications: allowed, category "Reminders" set to **Alert** with sound.
- Android 14+: **Alarms & reminders** permission (exact alarms) – needed for medicine doses in Module 2; requested with an explanation.
- Android 14+: **full-screen notifications** permission for dose alarms on the lock screen (Module 2).
- After each step the app shows a green tick; a **test reminder** confirms it works.

**Device differences**
- A15 5G is the low-end target (less RAM, slower camera): design and test camera scanning on it first.
- S25 Ultra/S24 FE: large screens – layouts must scale, but no tablet-specific UI needed.
- Samsung's large font / "Easy mode" settings must not break layouts.

---

# Legal & Safety

*Not legal advice – this is the safest-route plan; get a professional opinion before any commercial use.*

**License**
- Our code: **MIT License**, public GitHub repository. MIT allows use alongside closed-source libraries such as Google ML Kit, so no exceptions are needed.
- Don't copy in GPL code or add GPL libraries (that would force the whole app to become GPL). Dependencies must be MIT / Apache-2.0 / BSD or similar; a license list (`THIRD-PARTY-NOTICES.md`) is kept and checked at each release, with the attributions those licenses require.
- Google ML Kit is used under Google's ML Kit terms (free, on-device).
- Never commit secrets (APK signing key, passwords) to the public repository.

**Third-party services**
- **Open Food Facts:** data under the Open Database License; the app shows "Product data from Open Food Facts" attribution and does not redistribute their database.
- **ntfy.sh:** used within its free-tier limits and terms; content end-to-end encrypted; relay address configurable so a self-hosted ntfy can replace it.
- **SMS:** only from the user's own SIM, to numbers the user entered, after explicit opt-in; personal, not commercial.
- **No unofficial WhatsApp automation.** Sharing via WhatsApp only uses Android's normal "Share" sheet (the user taps Send).

**Privacy (India DPDP Act, 2023)**
- Data stays on the household's devices. What leaves the phone: encrypted relay messages (unreadable to the server), barcode numbers sent to Open Food Facts, and opt-in SMS.
- Clear in-app privacy notice and consent at setup; consent from the person whose medicine data is shared with family (e.g. the parent agrees to alerts going to the child).
- "Delete all my data" button; leaving a Home deletes that Home's data from the device.

**Health safety**
- The app is a **reminder tool only**: no dosage advice, no diagnosis, no "still safe to eat/take" judgements. This keeps it outside medical-device software rules, as far as I know. Disclaimer shown at setup.
- Expiry reading by camera is always **confirmed by the user**; the app never silently trusts OCR.
- Date and expiry logic has unit tests (safety-critical).

**Distribution**
- GitHub Releases (free). APKs are signed with one release key, which is kept safe and backed up; losing it means users must uninstall and reinstall for updates.

---

# Module 2 – Medicine Adherence (later)

Release 4, built on the same components.

- Dose reminders with **TAKEN / LATER / SKIP**, grouped doses, undo; exact alarms and full-screen alert (Samsung permissions above).
- Missed-dose rule: re-remind at +10 and +30 min; mark Missed at +60 min; **remote alert to opted-in family** via ntfy relay, plus optional SMS fallback.
- Dose stock: strips × tablets per strip, auto decrement, refill alerts (also sent remotely).
- Schedules: fixed times daily, alternate days, weekdays, weekly; as-needed (SOS).
- Child can see the parent's dose status (taken / missed today) from relay events, and vice versa.
- Medicines are added by whoever is at the phone; remote editing from the child's phone arrives via relay events.

---

# Non-Functional Requirements

- **Offline-first, server-free.**
- **Privacy:** see "Legal & Safety". All sync and relay data encrypted end-to-end.
- **On-device scanning/OCR.**
- **Accessibility:** large text, TalkBack / Narrator, no colour-only meaning.
- **Low-end phones:** Android 8.0+ (API 26), 2–3 GB RAM.

---

# Open Questions

1. **Medicines reminder window:** longer default (e.g. 30 days) or same 1–2 days?
2. **After expiry:** remind daily until handled, or only pin on the home screen?
