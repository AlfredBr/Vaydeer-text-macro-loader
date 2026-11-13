# Vaydeer text macro loader

A **C# command-line tool** for the nine-key Vaydeer keypad (`0483:5752`). Define
key numbers, names, text, and optional Enter presses in JSON, then write them to
**layer 1**. The macros are stored on the keypad; the CLI can exit after loading.

## Important: exit the factory software from the system tray

> **Closing the factory software's window is not enough. Fully exit it from its
> system-tray icon before loading macros, and keep it exited while using them.**

1. Find the Vaydeer icon in the Windows system tray beside the clock. Open the
   hidden-icons arrow if necessary.
2. Right-click the Vaydeer icon and choose **Exit** or **Quit**.
3. If the CLI still reports a conflict, check Task Manager for remaining
   `Vaydeer keyboard`, `Vaydeer.Keyboard.Win`, or `Vaydeer.Keyboard.Win1` processes.
   Exit the factory application and its helpers completely, then retry.

The factory application's **Software** text mode pastes strings from its cached
profile when it receives a keypad event. It can therefore emit **old text even
after the keypad has been successfully programmed with new text**. Its background
helpers may also manage layers. A successful hardware read-back verifies stored
settings; it cannot prove what another running application will type or paste.

`load` and `restore` detect these factory processes and refuse to write while they
are running. `info`, `dump`, and `backup` only read the device; `validate` and
`self-test` do not access it. Keeping the factory software installed is fine.
Exit it again after using it for advanced configuration.

## Requirements and quick start

Use Windows with the **.NET 10 SDK** to build. Running the built executable requires
the .NET 10 runtime. No Python, external NuGet packages, or custom driver is needed.
Connect only one Vaydeer keypad.

From PowerShell in the repository directory:

```powershell
.\Build.ps1
Copy-Item .\keys.example.json .\keys.json
# Edit keys.json before loading it.
.\bin\vaydeer.exe validate .\keys.json
.\bin\vaydeer.exe load .\keys.json
```

Copy the example only when creating a new configuration; keep your existing
`keys.json` if you already have one. Test the programmed keys in a text editor
with the factory software fully exited.

## JSON configuration

The file contains an array of one to nine key definitions:

```json
[
  { "key": 1, "name": "Build", "text": "make", "enter": true },
  { "key": 2, "name": "Greeting", "text": "Hello!" },
  { "key": 3, "name": "Two lines", "text": "first\nsecond", "enter": false }
]
```

| Field | Required | Meaning |
| --- | --- | --- |
| `key` | Yes | Unique integer from 1 to 9, left to right and top to bottom. |
| `name` | Yes | Nonblank label of at most 24 UTF-16 units; no control or supplementary characters. |
| `text` | Yes | String to type. Empty text is allowed. |
| `enter` | No | Boolean; `true` appends a real Enter press. Defaults to `false`. |

Only listed keys change logically. Unlisted keys and other layers are preserved.
Loading selects layer 1. Empty text with `enter: false` unassigns a key;
empty text with `enter: true` creates an Enter-only macro. If the text already ends
in a newline, `enter: true` adds another Enter.

Macros use the **current Windows keyboard layout**. Use the same layout in the
receiving application, with Caps Lock off. Uppercase and punctuation include
modifier presses. `\t` sends Tab; `\n`, `\r`, or `\r\n` sends Enter, with a CRLF pair
treated as one Enter. Emoji, dead-key composition, and unsupported keys are
rejected before writing. This is not arbitrary Unicode text injection.

Each macro is limited to 512 press/release events: up to 256 plain letters, fewer
with modifiers or a final Enter. Duplicate key numbers, duplicate or unknown JSON
properties, missing required fields, and files over 1 MiB are rejected.

## Command reference

Run commands as `.\bin\vaydeer.exe <command> [arguments]`. Names are case-sensitive.
Paths containing spaces must be quoted. There are no nested subcommands or layer
selection options. Brackets below mean an optional argument; do not type them.

| Command | Purpose | Keypad access |
| --- | --- | --- |
| `help`, `--help`, `-h`, or no arguments | Print usage. | None |
| `validate <keys.json>` | Validate JSON and compile every macro. | None |
| `load <keys.json>` | Back up, program, verify, and activate layer 1. | Read/write |
| `info` | Show device, firmware, active layer, and stored-layer count. | Read only |
| `dump [file.json]` | Export all nine layer-1 keys as readable JSON. | Read only |
| `backup [file.json]` | Save a raw snapshot of all stored layers. | Read only |
| `restore <snapshot.json>` | Restore layer-1 assignments from a CLI snapshot. | Read/write |
| `self-test` | Run offline checks with an in-memory device. | None |

### `help`, `--help`, `-h`, and no arguments

```powershell
.\bin\vaydeer.exe
.\bin\vaydeer.exe help
.\bin\vaydeer.exe --help
.\bin\vaydeer.exe -h
```

All four forms print the command summary and return success.

### `validate <keys.json>`

```powershell
.\bin\vaydeer.exe validate .\keys.json
```

Checks the entire configuration, including keyboard-layout mapping and macro
limits. Prints a per-key summary. It does not open USB, create a backup, or change
settings. Validation can run while the keypad is disconnected.

### `load <keys.json>`

```powershell
.\bin\vaydeer.exe load .\keys.json
```

Validates all input and checks that the factory software has exited. Saves a
timestamped backup, merges the listed keys with the existing first layer, and
stages the **complete layer** when changes are needed. The firmware requires this
even when changing one key. Verifies all key records and activates layer 1.

If programming fails, the CLI attempts to restore the old layer-1 assignments,
reports the result, and returns an error. Keep the printed backup path. The CLI
cannot replace a layer containing unsupported action types or multi-phase macros;
use the factory software for those configurations.

### `info`

```powershell
.\bin\vaydeer.exe info
```

Displays the connected keypad model, firmware version, active layer number, and
number of stored layers. Does not select a layer or change settings.

### `dump [file.json]`

```powershell
.\bin\vaydeer.exe dump
.\bin\vaydeer.exe dump .\current-keys.json
.\bin\vaydeer.exe dump > .\redirected-keys.json
```

Reads all nine keys on **layer 1**, regardless of which layer is active. Without a
filename, stdout contains only JSON; status messages and conversion notes go to
stderr. The filename form writes UTF-8 and **refuses to overwrite an existing
file**. Shell redirection follows your shell's overwrite rules instead.

Simple text macros, supported native Unicode text, printable key assignments,
and unassigned keys use the loadable `key`, `name`, `text`, and `enter` format.
One final Enter becomes `"enter": true`; internal Enter and Tab remain `\n` and
`\t`. Unnamed keys receive labels such as `Key 3`. Decoding uses the current
keyboard layout. Custom delays are not retained in this text format.

Actions that cannot be represented as text include `type`, `reason`, `headerHex`,
and `dataHex` instead. These raw entries deliberately fail `load` validation.
Edit or remove them before loading a dump; unlisted keys keep their existing
assignments. Use `backup` and `restore` when you need raw assignments and timing.

### `backup [file.json]`

```powershell
.\bin\vaydeer.exe backup
.\bin\vaydeer.exe backup .\my-keypad-backup.json
```

Saves raw device information, layer names, and readable key records for **all
stored layers**. Without a filename, creates a unique `cli-*.json` file under
`backups/` relative to the working directory. An explicit filename also refuses
to overwrite an existing file. Prints the absolute backup path.

Snapshots contain stored text and commands; keep them private. They do not include
the factory app's local database or cached Software-mode strings. Multi-phase
macro phases are not fully captured by this reader. A backup is a raw snapshot,
not the editable text array accepted by `load`.

### `restore <snapshot.json>`

```powershell
.\bin\vaydeer.exe restore .\backups\cli-EXAMPLE.json
```

Replace `cli-EXAMPLE.json` with an actual backup filename. Accepts a **C# CLI
snapshot**, validates its layer-1 records, checks for factory-software conflicts,
and backs up the current device before restoring. Restores all nine layer-1
assignments, verifies them, and selects layer 1.

Does not restore other layers, layer names, the old active-layer selection, or
factory-app settings. Unsupported action types and multi-phase macros are
rejected. Earlier Python snapshots, including `backups/initial.json`, use a
different format and cannot be loaded by this command.

### `self-test`

```powershell
.\bin\vaydeer.exe self-test
```

Runs offline checks for JSON validation, text encoding and decoding, factory
process detection, packet checksums, multipart transfers, backups, verification,
and rollback. Uses temporary files and a simulated device; no real USB device is
opened. The simulated failure messages in this test are intentional; the final
`PASS` line indicates success.

### Output and exit codes

| Code | Meaning |
| --- | --- |
| `0` | Command completed successfully. A dump containing raw entries is still successful. |
| `1` | Validation, factory-software conflict, file, device, or operation error. Read stderr. |
| `2` | Unknown command or incorrect argument count. Usage is printed to stderr. |

In PowerShell, check `$LASTEXITCODE` immediately after a CLI command. Apart from
`dump` without a filename, command output includes human-readable summaries and
is not a JSON API. Existing-file protection applies to direct dump/backup outputs
and automatically generated backups, not to shell redirection.

## Troubleshooting old or unexpected output

1. **Fully exit the factory software from the system tray.** Do not just close
   its window. Keep it exited while pressing the macro keys.
2. Run `dump` and compare the stored text with your JSON. Old text on screen with
   correct dumped values can be caused by the factory app's cached Software-mode
   bindings.
3. If needed, unplug and reconnect the keypad after exiting the factory app.
4. Check the receiving application's keyboard layout and turn Caps Lock off.
   Test in a plain text editor before using Enter-enabled command macros.

## Build and developer hardware checks

`Build.ps1` builds `src/Vaydeer.Cli/Vaydeer.Cli.csproj` in Release mode into `bin/`.
It takes no project-specific arguments. Source is under `src/Vaydeer.Cli/`;
obsolete prototypes and caches are archived under `.archive/`.

The separate integration-check executable supports two options. **Exit the factory
software first. Both options write the physical keypad; they are not offline tests.**
These developer checks use protocol APIs directly and do not run the normal
`load`/`restore` process guard.

```powershell
dotnet run --project tests\Vaydeer.HardwareChecks -- --write-unchanged
dotnet run --project tests\Vaydeer.HardwareChecks -- --round-trip
```

| Option | Behavior |
| --- | --- |
| `--write-unchanged` | Back up, rewrite layer 1 unchanged twice, and verify all keys, layer names, and device state. |
| `--round-trip` | Back up, temporarily load a text-only test macro on key 1, then restore and verify the original layer and active layer. |

The `--` separates `dotnet run` arguments from the check executable's option.
Exactly one of these options is required. The check requires an existing simple,
execute-once macro on layer 1 and is intended for development. Hardware testing
has covered firmware 1.1.5 and bootloader 0.2.2. Physical output in your target
application still needs a key-press test. No firmware flashing is implemented.

Protocol references: [community HID notes](https://github.com/alex-savin/go-vaydeer-ninepad-hid/blob/main/VAYDEER_HID_PROTOCOL.md)
and [vendor behavior research](https://github.com/philipp-fischer/vaydeer-macro-keyboard/blob/main/DEVNOTES.md).
