# Steamworks (vendored)

Matched pair taken from the [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) master
checkout (building against Steamworks SDK 1.65):

- `linux-x64/Steamworks.NET.dll`: managed wrapper (MIT), built from that checkout's `Standalone2.0` project with
  `-c OSX-Linux -p:Platform=x64` (the configuration decides struct packing).
- `linux-x64/libsteam_api.so`: Valve's native library, unmodified, from the same checkout's `Plugins/`.
- `win-x64/Steamworks.NET.dll`: the same wrapper built with `-c Windows -p:Platform=x64`.
- `win-x64/steam_api64.dll`: Valve's native library, unmodified, from the same checkout's `Plugins/`.

NuGet's `Steamworks.NET` 2024.8.0 targets SDK 1.60 and does **not** work with this library
(`EntryPointNotFoundException` at init), so the pair is vendored instead. Always update both files
together, from the same Steamworks.NET revision. The native library is Valve's, not MIT: see README "License".
