# Vaydeer text macro loader

A **C# command-line tool** for the nine-key Vaydeer keypad (`0483:5752`). Define
key numbers, names, text, and optional Enter presses in JSON, then write them to
**layer 1**. The macros are stored on the keypad; the CLI can exit after loading.
For shortcuts and function keys, use a `steps` array mixing text, key presses,
and pauses. Existing text-only JSON files continue to work.

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
the .NET 10 runtime. Terminal presentation uses **Spectre.Console** (pinned to
0.57.0); NuGet restores it and its dependencies during the first build. No Python
or custom driver is needed. Keep the generated DLLs beside `bin\vaydeer.exe`.
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
| `name` | Yes | Nonblank label of at most **16 UTF-16 units**; no control or supplementary characters. Longer names are rejected before writing because stock firmware discards them. |
| `text` | Either `text` or `steps` | String to type. Empty text is allowed. |
| `steps` | Either `text` or `steps` | Ordered array of text, key-press, and pause actions; see below. |
| `enter` | No; text form only | Boolean; `true` appends a real Enter press. Defaults to `false`. |
| `delayMs` | No | Integer from 0 to 65535: pause after a complete character is released. Defaults to 5 ms. |
| `enterDelayMs` | No; text form only | Integer from 0 to 65535: pause before the optional final Enter. Defaults to `delayMs`, or 5 ms when both are omitted. |

Each key must have **exactly one of `text` or `steps`**. Different keys in the same
file may use different forms. Key names and numbers follow the same rules for both.

Only listed keys change logically. Unlisted keys and other layers are preserved.
Loading selects layer 1. Empty text with `enter: false` unassigns a key;
empty text with `enter: true` creates an Enter-only macro. If the text already ends
in a newline, `enter: true` adds another Enter.

### Typing speed and Enter timing

For applications that need slower input, set timing separately for each key:

```json
[
  {
    "key": 1,
    "name": "Build",
    "text": "make",
    "enter": true,
    "delayMs": 20,
    "enterDelayMs": 100
  }
]
```

This requests a **20 ms pause between characters** and a **100 ms pause before
the final Enter**. The Enter pause replaces the last character's ordinary gap;
it is not added to it. Modifier setup/release and key-hold events remain at 5 ms,
so total time between character key-downs is longer than `delayMs`. The character
gap follows modifier releases as well, so Shift is not held during that pause.

Omitting both timing fields preserves the previous bytes: 5 ms per press/release
event (approximately 10 ms per ordinary character), with a 50 ms settling interval
after the final release. The final settling interval stays 50 ms regardless of
the configured gap; `delayMs` does not add a pause after the macro finishes.
The keypad and host determine actual observed timing.

`delayMs` also applies between embedded Tab/Enter characters and the next
character. `enterDelayMs` applies only to the extra Enter requested by `enter: true`;
it has no effect when `enter` is false. An Enter-only macro has no preceding text
event on which to store a pause, so `enterDelayMs` does not delay its first press.
Zero is allowed for gaps; key holds remain 5 ms. Fractions, negative values,
values above 65535, strings, booleans, and explicit `null` values are rejected.

### Mixed actions: Ctrl shortcuts, function keys, and pauses

Use `steps` when a macro must do more than type a string:

```json
[
  {
    "key": 1,
    "name": "Search and next",
    "delayMs": 20,
    "steps": [
      { "press": "Ctrl+F" },
      { "text": "search term" },
      { "press": "Enter" },
      { "delayMs": 100 },
      { "press": "F3" }
    ]
  },
  {
    "key": 2,
    "name": "Save as",
    "steps": [ { "press": "Ctrl+Shift+S" } ]
  }
]
```

The first macro presses Ctrl+F, types the text, presses Enter, waits 100 ms, then
presses F3. [steps.example.json](steps.example.json) contains a complete editable example. After
editing it and exiting the factory software from the system tray:

```powershell
.\bin\vaydeer.exe validate .\steps.example.json
.\bin\vaydeer.exe load .\steps.example.json
```

Each step must contain **exactly one** of these fields:

| Step | Behavior |
| --- | --- |
| `{ "text": "Hello!" }` | Types literal text with the same layout, character, and newline rules as the simple format. |
| `{ "press": "Ctrl+Shift+S" }` | Presses the modifiers in the written order, taps the final key, then releases modifiers in reverse order. |
| `{ "delayMs": 100 }` | Sets the pause after the preceding action to 100 ms, replacing its ordinary gap. |

All keys and modifiers are released before the next step. Modifiers do not carry
over to later text steps. Each macro runs once per keypad press; there are no
hold/release, looping, or repeating step types.

The key-level `delayMs` applies after each character and each complete `press`
action; it defaults to 5 ms. An explicit pause step overrides the previous action's
gap rather than adding to it. Pauses must be integers from 0 to 65535 ms. Leading
pauses and consecutive pauses are rejected; a pause needs a preceding nonempty
text or press action. An empty text step does not provide an action to pause after.
Trailing pauses are supported and replace the usual final 50 ms settling interval.
Without a trailing pause, the final settling interval remains 50 ms.

Do not include `enter` (even `false`) or `enterDelayMs` on a `steps` key. Add an
explicit `{ "press": "Enter" }` step; to wait before it, place a pause after the
preceding action. Empty text steps are allowed; a macro containing only empty
text steps unassigns the key. Each array may contain 1–512 steps, subject to the
shared 512 key-event limit across the entire macro. Pauses consume no extra key
events. Ctrl+F uses four events; Ctrl+Shift+S uses six.

#### Supported `press` names

Names are case-insensitive. Whitespace around `+` is allowed. Modifiers must come
first, followed by exactly one base key. Duplicate modifiers (including aliases)
and modifier-only presses are rejected.

| Category | Supported names |
| --- | --- |
| Modifiers | `Ctrl` (`Control`), `Alt`, `Shift`, `Win` (`Windows`). |
| Letters and digits | `A`–`Z`, `0`–`9`. |
| Function keys | `F1`–`F12`. Stock firmware does not support `F13`–`F24`; `Fn` is not supported. |
| Editing | `Enter` (`Return`), `Tab`, `Space`, `Escape` (`Esc`), `Backspace`, `Delete` (`Del`), `Insert` (`Ins`). |
| Navigation | `Home`, `End`, `PageUp`, `PageDown`, `Left`, `Up`, `Right`, `Down`. |
| Punctuation keys | `Equals`, `Minus`, `Comma`, `Period`, `Slash`, `Backslash`, `Semicolon`, `Quote`, `LeftBracket`, `RightBracket`, `Backtick`. |

Examples: `Ctrl+C`, `Ctrl+V`, `Alt+F4`, `Shift+Tab`, `Win+D`, `Ctrl+Alt+Delete`,
and `Ctrl+Shift+Equals`. The punctuation names describe physical Windows virtual
keys; their resulting symbols depend on the target keyboard layout. Use
`Ctrl+Shift+Equals` for Ctrl+Plus on a US layout; a literal `+` is the separator,
not an accepted base-key name.

`press: "A"` taps the A key without automatically adding Shift; use
`press: "Shift+A"` or `text: "A"` when uppercase output is intended. Text steps
are layout-mapped; press steps explicitly specify key combinations. For Ctrl
characters such as Ctrl+C, use a `press` step, not `\u0003` inside text. What a
shortcut does depends on the receiving application (for example, Ctrl+C may copy
or interrupt). The loader stores these key events; it does not execute them on load.

Text, including text steps, uses the **current Windows keyboard layout**. Use the same layout in the
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

Spectre.Console displays a key table with a **Macro text preview**, character
counts, Enter settings, and timing. The preview shows the beginning of each
command, truncates long text with `…`, and adapts to the terminal width. Enter
presses (including the optional final Enter) appear as `\n`, Tab as `\t`, and
literal backslashes as `\\`. The complete macro is still programmed; truncation
only affects the display. For `steps`, the preview shows actions separated by
arrows, such as `Ctrl+F → "search term" → Enter → wait 100 ms → F3`; the Enter
column says `Steps`, and the character count counts only text-step characters.
An interactive terminal shows a spinner for reading, backup, writing,
verification, and activation, followed by a green success panel and the backup
path. Load errors appear in a red panel on stderr. Redirected output uses stage
lines instead of animation; it remains human-readable rather than JSON.

Validates all input and checks that the factory software has exited. Saves a
timestamped backup, merges the listed keys with the existing first layer, and
stages the **complete layer** when changes are needed. The firmware requires this
even when changing one key. Verifies all key records and activates layer 1.

If programming fails, the CLI attempts to restore the old layer-1 assignments,
reports the result, and returns an error. Keep the printed backup path. The CLI
cannot replace a layer containing unsupported action types or multi-phase macros;
use the factory software for those configurations.

On failure, the final error panel includes the read-back mismatch details, the
recovery outcome, and the backup path. Recovery runs before the live display is
closed and reported; a progress-rendering failure cannot interrupt restoration.
If a label is too long, shorten `name` to 16 characters or fewer; this does not
limit the length of the emitted `text`.

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
keyboard layout. Representable character gaps and pre-Enter gaps are included as
`delayMs` and `enterDelayMs`; default or redundant fields are omitted. Only gaps
observable in stored events can be recovered (a single-character macro has no
inter-character gap to infer).

Supported shortcuts and execute-once mixed macros are exported using `steps`
when the simpler text format cannot represent them. Dumping may normalize text
into individual `press` steps and pauses into explicit delay steps; it does not
recover the original grouping or JSON formatting. For stored macro sequences,
the recompiled event bytes must match before the dump labels them as loadable.

Actions, custom key-hold timings, overlapping held-key sequences, or other
structures that neither format can represent include `type`, `reason`,
`headerHex`, and `dataHex` instead. These raw entries deliberately fail `load` validation.
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
process detection, shortcut modifier ordering, F1–F12, step validation, pause
semantics, timing limits and dump/reload preservation, terminal rendering,
packet checksums, multipart transfers, backups, verification,
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

The separate integration-check executable supports three options. **Exit the factory
software first. Both options write the physical keypad; they are not offline tests.**
These developer checks use protocol APIs directly and also require the factory
software to be fully exited.

```powershell
dotnet run --project tests\Vaydeer.HardwareChecks -- --write-unchanged
dotnet run --project tests\Vaydeer.HardwareChecks -- --round-trip
dotnet run --project tests\Vaydeer.HardwareChecks -- --label-limit
```

| Option | Behavior |
| --- | --- |
| `--write-unchanged` | Back up, rewrite layer 1 unchanged twice, and verify all keys, layer names, and device state. |
| `--round-trip` | Back up, temporarily load a text-only test macro on key 1, then restore and verify the original layer and active layer. |
| `--label-limit` | Back up, probe key 3 with labels of 16–19 characters, then restore and verify layer 1. This confirmed the firmware's 16-character name limit. |

The `--` separates `dotnet run` arguments from the check executable's option.
Exactly one of these options is required. The unchanged-write and round-trip checks
require an existing simple, execute-once macro on layer 1. These checks are intended
for development. Hardware testing
has covered firmware 1.1.5 and bootloader 0.2.2. Physical output in your target
application still needs a key-press test. No firmware flashing is implemented.

Protocol references: [community HID notes](https://github.com/alex-savin/go-vaydeer-ninepad-hid/blob/main/VAYDEER_HID_PROTOCOL.md)
and [vendor behavior research](https://github.com/philipp-fischer/vaydeer-macro-keyboard/blob/main/DEVNOTES.md).
