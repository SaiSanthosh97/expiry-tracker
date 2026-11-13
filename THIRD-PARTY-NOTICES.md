# Third-Party Notices

Dependencies shipped in the app, with their licenses. Checked at every release.

| Dependency | Used by | License |
|---|---|---|
| .NET runtime and libraries | All | MIT |
| .NET MAUI (Microsoft.Maui.Controls) | App | MIT |
| CommunityToolkit.Mvvm | App | MIT |
| QRCoder (family invite QR codes) | App | MIT |
| sqlite-net-pcl (Krueger Systems) | Storage.Sqlite | MIT |
| SQLitePCLRaw.core, SQLitePCLRaw.provider.e_sqlite3 | Storage.Sqlite | Apache-2.0 |
| SourceGear.sqlite3 (native SQLite builds) | Storage.Sqlite | SQLite: public domain |
| Open Sans font | App | SIL Open Font License 1.1 |
| Google ML Kit barcode scanning and text recognition (on-device models) | Device.Vision | Free to use under the [ML Kit Terms of Service](https://developers.google.com/ml-kit/terms); closed source |
| Google Play services client libraries (used by ML Kit) | Device.Vision | [Android Software Development Kit License](https://developer.android.com/studio/terms) |
| .NET bindings for ML Kit and Play services (Xamarin.Google.MLKit.*, Xamarin.GooglePlayServices.*) by Microsoft | Device.Vision | MIT |

## Services

| Service | Used for | Terms |
|---|---|---|
| [ntfy.sh](https://ntfy.sh) (open-source ntfy server, public instance) | Carrying a shared home's changes between family phones. Messages are encrypted on the phone (AES-256-GCM) with a key only the family's phones have; ntfy.sh keeps them for up to 12 hours. No account. The relay address can be changed to a self-hosted ntfy. | Free public service, used within its [terms](https://ntfy.sh/docs/privacy/) and limits (250 messages a day per internet connection) |

## Data

| Source | Used for | License / terms |
|---|---|---|
| [Open Food Facts](https://world.openfoodfacts.org) | Product names and categories when a barcode is scanned (only the barcode number is sent) | Database: [Open Database License](https://opendatacommons.org/licenses/odbl/1-0/); the app shows "Product details from Open Food Facts" when it uses their data and does not copy their database |

ML Kit packages are pinned to `Xamarin.Google.MLKit.BarcodeScanning` 117.3.0.5 and `Xamarin.Google.MLKit.TextRecognition`
116.0.1.5: newer ones need AndroidX libraries newer than the ones .NET MAUI 10 requires, and fail to restore.

Do not add `SQLitePCLRaw.bundle_green` or the 2.x `SQLitePCLRaw.lib.e_sqlite3` packages: 2.1.11 has a known
high-severity vulnerability (GHSA-2m69-gcr7-jv3q), and the 2.x native package contains only desktop builds,
which crash the app on Android. sqlite-net-pcl 1.11 already brings the correct native SQLite via SourceGear.sqlite3.

## Development and test only (not shipped)

| Dependency | License |
|---|---|
| xUnit | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |
