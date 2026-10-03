# DW2 Mod Launcher

[English](README.md) | **日本語**

『Distant Worlds 2』向けの、コミュニティ主導のMODランチャーです。

Steam Workshopから導入したMODと、ゲームフォルダーに導入したMODを、ひとつのランチャーでまとめて管理できます。
このプロジェクトは趣味として開発されており、コミュニティによる貢献、改善、フォーク、継続的な開発を歓迎します。

> 本プロジェクトは非公式のコミュニティプロジェクトであり、CodeForce、Slitherine、Matrix Gamesとは関係がなく、承認も受けていません。

- [DW2 Mod Launcher](#dw2-mod-launcher)
  - [概要](#概要)
  - [主な機能](#主な機能)
  - [必要環境](#必要環境)
  - [ビルド方法](#ビルド方法)
  - [初期設定](#初期設定)
  - [Linux](#linux)
  - [共同開発について](#共同開発について)
  - [ライセンス](#ライセンス)
  - [免責事項](#免責事項)

---

## 概要

**DW2 Mod Launcher** は、『Distant Worlds 2』のMODを管理するための非公式ランチャーです。

Steam Workshopから導入したMODと、ゲーム本体のMODフォルダーに導入したMODをまとめて確認・管理できます。

このプロジェクトは趣味として開発されています。
機能追加、改善、バグ修正、フォーク、別バージョンの作成など、コミュニティによる自由な参加を歓迎します。

## 主な機能

- ゲーム本体MODフォルダーのMOD検索・一覧表示
- MODの有効／無効管理
- MODの重複インストール検出
- 有効なMOD同士のファイル競合チェック
- Steam Workshop MODの更新確認
- MOD情報・説明文の表示
- MOD付属のREADME／マニュアルの検出と直接表示
- MODに付属するBAT／EXEツールの検出
- MODフォルダーを直接開く機能
- MODが提供する `settings.schema.json` に基づく、スキーマ駆動のフォームによるMOD設定の確認・変更
- MODごとの起動オプション、ゲーム用の環境変数（Windows）に対応。Steamの起動オプションをワンクリックでインポート可能
- 同梱のローダーDLLによるコードMODの読み込み（MODの設定をDLLへ直接渡せます）
- ローカルMODのSteam Workshopへの公開（公開済みMODの更新にも対応）。新しいアイテムのWorkshop IDは `mod.json` に保存されます
- 日本語／English UI切り替え

## 必要環境

- Windows、またはLinux（ゲームはProtonで動作します。[Linux](#linux)を参照）
- Distant Worlds 2
- Steam版を推奨
- ビルドには [.NET 10 SDK](https://dotnet.microsoft.com/download) が必要です（必要なSDKはこれだけです）

ランチャー本体は.NET 10を対象とし、自己完結型で配布されるため、プレイヤー側で.NETをインストールする必要はありません。
ゲームに注入されるローダーDLL（`DW2ModLauncher.Loader`）は、Distant Worlds 2の内部で動作し、ゲームが現在.NET 8を必要とするため、
.NET 8を対象としています。.NET 10 SDKは.NET 8向けのビルドにも完全に対応しているため、.NET 8 SDKを別途インストールする必要はありません。

## ビルド方法

どのOSでも（Python 3と.NET 10 SDKが必要）、`scripts/build.py` は、コードの整形（自動修正）と単体テストを行ったうえで、
使用中のOS向けのランチャー（Avalonia製）をビルドします。`scripts/run.py` は同じ処理の後にランチャーを起動します。

```text
python3 scripts/run.py                # build + run (python scripts\run.py on Windows)
python3 scripts/run.py --no-validate  # skip the format fix and tests for a faster loop
python3 scripts/build.py              # build only
```

Windowsではバッチファイルも使用できます。リポジトリをダウンロードまたはCloneした後、

```text
build.cmd
```

を実行してください。

ビルド後、そのままランチャーを起動する場合は、

```text
run.cmd
```

を使用できます。

どちらのスクリプトも [`DW2ModLauncher.sln`](DW2ModLauncher.sln) に対して `dotnet build` を実行します。プロジェクトは
`DW2ModLauncher.Core`（MOD検索、Steam／Workshop検出、JSON処理などUIに依存しないロジック）、
`DW2ModLauncher.Avalonia`（ランチャーのUI）、`DW2ModLauncher.Tests`（Coreロジックの単体テスト）に
分割されています。詳細は [AGENTS.md](AGENTS.md) を参照してください。ビルドされた実行ファイルは
`src\DW2ModLauncher.Avalonia\bin\Release\net10.0\DW2ModLauncher.exe` です。

`build.cmd`（およびそれを呼び出す `run.cmd`）は、既定ではビルドの前にコードの整形（自動修正）と単体テストの
実行も行います。これはCIで実行されるチェックと同じものです。`--no-validate` を付けるとこの2つをスキップして
そのままビルドだけを行うため、ローカルでの編集・ビルドのサイクルを速くできます。

```text
build.cmd --no-validate
run.cmd --no-validate
```

**Pull Requestを送る前に、`--no-validate` を付けずに `build.cmd` を実行してください。** CI（GitHub Actions）は
フォーク元からのPRも含め、すべてのPRに対して同じフォーマットチェック・ビルド・単体テストを実行します。
ローカルで通らないものはCIでも通らないため、事前に確認しておくことで無駄な待ち時間を避けられます。

## 初期設定

初回起動時に必要に応じて以下の場所を指定してください。

- Distant Worlds 2 のゲームフォルダー
- Steam Workshop のDW2 MODフォルダー
- 管理対象とするローカルMODフォルダー

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

## Linux

Distant Worlds 2にはLinuxネイティブ版がないため、LinuxではこれまでどおりSteamのProtonでゲームを動かします。
ランチャー自体はLinuxネイティブのアプリです。リリースページから `linux-x64` の `.tar.gz` をダウンロードして
任意の場所に展開し、`./DW2ModLauncher` を実行してください。.NETのインストールは不要です。

- Steamがインストールされ、起動している必要があります。**Play** はSteam経由でゲームを起動する
  （`steam -applaunch 1531540`）ため、Proton、適切なプレフィックス、Steamの起動オプションがそのまま適用されます。
- Steam、ゲーム、Workshopフォルダーは、一般的な場所（`~/.steam/steam`、`~/.local/share/Steam`、
  FlatpakやSnapでのインストール、その他のSteamライブラリフォルダー）から自動的に検出されます。
  別の場所にある場合は設定タブで指定してください。
- Steam Workshopへの公開は、起動中でログイン済みのLinux版Steamクライアントと直接やり取りします。
- ランチャーの設定（`launcher_settings.json`）、プロファイル、ログは `~/.config/DW2ModLauncher`（Windows では `%AppData%\DW2ModLauncher`）に保存されます。

Linuxでソースからビルドして実行する場合（.NET 10 SDKのみ必要）：

```text
dotnet run --project src/DW2ModLauncher.Avalonia
```

## 共同開発について

このプロジェクトはコミュニティによる開発を歓迎しています。

以下のような貢献を自由に行えます。

- バグ修正
- UI改善
- 新機能
- コード整理
- ドキュメント改善
- 翻訳
- フォーク
- 独自バージョンの開発

Pull Request / Merge Request、Issue、提案なども歓迎します。

PRを送る前に、`--no-validate` を付けずに `build.cmd` を実行してください（[ビルド方法](#ビルド方法)を参照）。
CIと同じ内容（フォーマット・ビルド・テスト）をローカルで事前に確認できます。

開発者が将来このプロジェクトのメンテナンスを継続することを保証するものではありません。
その場合も、MIT Licenseの範囲内でコミュニティが自由に開発を継続できます。

## ライセンス

このプロジェクトは **MIT License** で公開されています。

詳細は [`LICENSE`](LICENSE) を参照してください。

**Steamworksネイティブライブラリ。** Workshopへの公開には、ValveのSteam APIネイティブライブラリ
（Windowsでは `steam_api64.dll`、Linuxでは `libsteam_api.so`）が必要です。これらはMIT Licenseの対象外です。
公式Steamworks SDKから変更を加えずに取得したValveの再配布可能バイナリであり、ランチャーがユーザー自身の
Steamクライアントと通信できるように同梱されています。引き続きSteamworks SDKの利用規約が適用されます。

---

## 免責事項

『Distant Worlds 2』および関連する名称・アセットは、それぞれの権利者に帰属します。
本ランチャーは非公式のファン／コミュニティプロジェクトです。
