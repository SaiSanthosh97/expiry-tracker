# Third-Party Notices

Dependencies shipped in the app, with their licenses. Checked at every release.

| Dependency | Used by | License |
|---|---|---|
| .NET runtime and libraries | All | MIT |
| .NET MAUI (Microsoft.Maui.Controls) | App | MIT |
| CommunityToolkit.Mvvm | App | MIT |
| sqlite-net-pcl (Krueger Systems) | Storage.Sqlite | MIT |
| SQLitePCLRaw.core, SQLitePCLRaw.provider.e_sqlite3 | Storage.Sqlite | Apache-2.0 |
| SourceGear.sqlite3 (native SQLite builds) | Storage.Sqlite | SQLite: public domain |
| Open Sans font | App | SIL Open Font License 1.1 |

Do not add `SQLitePCLRaw.bundle_green` or the 2.x `SQLitePCLRaw.lib.e_sqlite3` packages: 2.1.11 has a known
high-severity vulnerability (GHSA-2m69-gcr7-jv3q), and the 2.x native package contains only desktop builds,
which crash the app on Android. sqlite-net-pcl 1.11 already brings the correct native SQLite via SourceGear.sqlite3.

## Development and test only (not shipped)

| Dependency | License |
|---|---|
| xUnit | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |
