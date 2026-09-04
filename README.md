# DW2 Mod Launcher BETA

A community-oriented MOD launcher for **Distant Worlds 2**.

Steam Workshop MODs and MODs installed in the game folder can be managed from one launcher.  
The project is developed as a hobby project, and contributions, improvements, forks, and continued development by the community are welcome.

> This is an unofficial community project and is not affiliated with or endorsed by CodeForce, Slitherine, or Matrix Games.

---
## English

### About

**DW2 Mod Launcher BETA** is an unofficial MOD launcher for **Distant Worlds 2**.

It provides a single interface for managing MODs installed through Steam Workshop and MODs installed in the game's local MOD folders.

This is a hobby project. Community contributions, improvements, bug fixes, forks, alternate versions, and continued development are all welcome.

### Main Features

- Scan and display MODs installed in the game MOD folder
- Enable / disable MODs
- Detect duplicate MOD installations
- Check file conflicts between enabled MODs
- Check Steam Workshop MOD update status
- Display MOD information and descriptions
- Detect and open included README/manual files
- Detect included BAT/EXE tools
- Open MOD folders directly
- View and edit supported INI settings
- Support per-MOD launch arguments
- Japanese / English UI switching

### Requirements

- Windows
- Distant Worlds 2
- Steam version recommended
- [.NET 8 SDK](https://dotnet.microsoft.com/download) for building

### Building

Download or clone the repository, then run:

```text
BUILD_BETA.cmd
```

To build and immediately launch the application, run:

```text
BUILD_AND_RUN_BETA.cmd
```

Both scripts call `dotnet build` on [`DW2ModLauncher.sln`](DW2ModLauncher.sln). The project is split into
`DW2ModLauncher.Core` (mod scanning, Steam/Workshop lookups, INI/JSON helpers — no UI dependency),
`DW2ModLauncher.App` (the WinForms launcher), and `DW2ModLauncher.Tests` (unit tests for the Core logic);
see [AGENTS.md](AGENTS.md) for details. The built executable is
`src\DW2ModLauncher.App\bin\Release\net8.0-windows\DW2ModLauncherBeta.exe`.

### Initial Setup

On first launch, configure the paths as needed:

- Distant Worlds 2 game folder
- Steam Workshop folder for DW2
- Local/managed MOD folder

`launcher_settings.example.json` is provided as an example configuration file.

Typical Steam Workshop path:

```text
Steam\steamapps\workshop\content\1531540
```

Typical game path:

```text
Steam\steamapps\common\Distant Worlds 2
```

The actual drive and Steam Library location may be different on your system.

### Contributing

Community development is welcome.

You are free to contribute:

- Bug fixes
- UI improvements
- New features
- Code cleanup
- Documentation
- Translations
- Forks
- Alternate versions

Pull Requests / Merge Requests, Issues, and suggestions are welcome.

There is no guarantee that the original developer will maintain this project indefinitely.  
If maintenance stops, the community is welcome to continue development under the terms of the MIT License.

### License

This project is released under the **MIT License**.

See [`LICENSE`](LICENSE) for details.

---

## Disclaimer

Distant Worlds 2 and related names and assets belong to their respective owners.  
This launcher is an unofficial fan/community project.

## 日本語

### 概要

**DW2 Mod Launcher BETA** は、『Distant Worlds 2』のMODを管理するための非公式ランチャーです。

Steam Workshopから導入したMODと、ゲーム本体のMODフォルダーに導入したMODをまとめて確認・管理できます。

このプロジェクトは趣味として開発されています。  
機能追加、改善、バグ修正、フォーク、別バージョンの作成など、コミュニティによる自由な参加を歓迎します。

### 主な機能
- ゲーム本体MODフォルダーのMOD検索・一覧表示
- MODの有効／無効管理
- MODの重複インストール検出
- 有効なMOD同士の競合チェック
- Steam Workshop MODの更新確認
- MOD情報・説明文の表示
- MOD付属のREADME／マニュアルの検出と直接表示
- MODに付属するBAT／EXEツールの検出
- MODフォルダーを直接開く機能
- INI設定の確認・変更
- MODごとの起動オプション対応
- 日本語／English UI切り替え

### 必要環境

- Windows
- Distant Worlds 2
- Steam版を推奨
- ビルドには [.NET 8 SDK](https://dotnet.microsoft.com/download) が必要です

### ビルド方法

リポジトリをダウンロードまたはCloneした後、

```text
BUILD_BETA.cmd
```

を実行してください。

ビルド後、そのままランチャーを起動する場合は、

```text
BUILD_AND_RUN_BETA.cmd
```

を使用できます。

内部では [`DW2ModLauncher.sln`](DW2ModLauncher.sln) に対して `dotnet build` を実行します。プロジェクトは
`DW2ModLauncher.Core`（MOD検索、Steam／Workshop検出、INI／JSON処理などUIに依存しないロジック）、
`DW2ModLauncher.App`（WinForms製ランチャー本体）、`DW2ModLauncher.Tests`（Coreロジックの単体テスト）に
分割されています。詳細は [AGENTS.md](AGENTS.md) を参照してください。ビルドされた実行ファイルは
`src\DW2ModLauncher.App\bin\Release\net8.0-windows\DW2ModLauncherBeta.exe` です。

### 初期設定

初回起動時に必要に応じて以下の場所を指定してください。

- Distant Worlds 2 のゲームフォルダー
- Steam Workshop のDW2 MODフォルダー
- 管理対象とするMODフォルダー

`launcher_settings.example.json` は設定ファイルのサンプルです。

一般的なSteam Workshopの場所：

```text
Steam\steamapps\workshop\content\1531540
```

ゲームフォルダーの例：

```text
Steam\steamapps\common\Distant Worlds 2
```

環境によってドライブやSteamライブラリの場所は異なります。

### 共同開発について

このプロジェクトはコミュニティによる開発を歓迎しています。

- バグ修正
- UI改善
- 新機能
- コード整理
- ドキュメント改善
- 翻訳
- フォーク
- 独自バージョンの開発

Pull Request / Merge Request、Issue、提案なども歓迎します。

開発者が将来このプロジェクトのメンテナンスを継続することを保証するものではありません。  
その場合も、MIT Licenseの範囲内でコミュニティが自由に開発を継続できます。

### ライセンス

このプロジェクトは **MIT License** で公開されています。

詳細は [`LICENSE`](LICENSE) を参照してください。

---

