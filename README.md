# SplitDetail — LiveSplit Component

SplitDetail is a compact, subsplit-aware LiveSplit component that shows timing details for the current split group, the previous split group, or the previous segment/subsplit.

It is designed for runners who use Subsplits and want cleaner timing information than LiveSplit’s default Detailed Timer / Previous Segment combination can provide.

---

## What it does

SplitDetail can be added multiple times to a layout. Each instance can use a different mode.

| Mode | What it shows |
|---|---|
| **Current Split** | The live elapsed time of the current parent split group, plus comparison times such as `PB:` and `Best:`. |
| **Prev Split** | The last completed parent split group: actual time on the right, comparison deltas in the middle. |
| **Prev Seg.** | The last completed individual segment/subsplit: actual time on the right, comparison deltas in the middle. |

When a prior-mode component detects that the **current active** split or segment is losing time, it can temporarily switch to a live display:

| Normal label | Live label | Meaning |
|---|---|---|
| `Prev Split` | `Live Split` | Shows live deltas for the current active split group. |
| `Prev Seg.` | `Live Seg.` | Shows live deltas for the current active segment/subsplit. |

After the runner splits, the component returns to the normal previous-completed display.

---

## Example layouts

A common setup is to add SplitDetail three times:

```text
Current Split    PB:   1:46.67        1:20.99
                 Best: 1:36.97
Prev Seg.        -16.49  -16.41       1.04
Prev Split       +3m     +3:11        4:48.35
```

You can also use only one instance, for example as a more configurable replacement for Previous Segment.

---

## Features

- Subsplit-aware parent split group detection.
- `Current Split`, `Prev Split`, and `Prev Seg.` modes.
- Temporary `Live Split` / `Live Seg.` display when the active split or segment is losing time.
- One-comparison or two-comparison display.
- Fully selectable comparisons, including custom LiveSplit comparisons.
- Priority delta setting for tight layouts.
- Optional separator between deltas.
- Configurable internal column spacing.
- Configurable accuracy: seconds, tenths, hundredths, milliseconds.
- Compact shortening behavior for large deltas.
- Gold delta coloring for new bests.
- LiveSplit-style text shadows and outlines.
- Dynamic Layout Editor names, such as `SplitDetail - Current Split`.
- Appears under `Information` in the Layout Editor component picker.

---

## Settings

### Mode & Labels

| Setting | Default | Description |
|---|---:|---|
| **Mode** | `Current Split` | Chooses what this instance displays. |
| **Label - Current** | `Current Split` | Label used for Current Split mode. |
| **Label - Split** | `Split` | Suffix used to build `Prev Split` and `Live Split`. |
| **Label - Segment** | `Seg.` | Suffix used to build `Prev Seg.` and `Live Seg.`. |

SplitDetail automatically builds the previous/live labels:

```text
Prev + Split suffix  → Prev Split
Live + Split suffix  → Live Split
Prev + Segment suffix → Prev Seg.
Live + Segment suffix → Live Seg.
```

If you prefer wider labels, change the suffixes. For example, `Segment` gives `Prev Segment` and `Live Segment`.

### Comparisons

| Setting | Default | Description |
|---|---:|---|
| **Comparison 1** | `Personal Best` | First comparison used for Current Split values and prior/live deltas. |
| **Comparison 2** | `Best Segments` | Second comparison used when showing two comparisons. |
| **Show comparisons** | `2 (both)` | Choose one or two comparison values/deltas. |
| **Priority delta** | `Comparison 2` | Which delta is preserved first when horizontal space is tight. |

Comparison choices are read from the active run, so custom comparisons can be used.

### Layout

| Setting | Default | Description |
|---|---:|---|
| **Separator** | empty | Optional separator between deltas. Empty means no separator. |
| **Column spacing (px)** | `3` | Internal horizontal spacing between labels, deltas, separator, and time. This does not change outer padding or vertical height. |

Examples:

```text
Separator empty: +1.21 +1.11
Separator |:     +1.21 | +1.11
Separator ·:     +1.21 · +1.11
```

### Accuracy

| Setting | Description |
|---|---|
| **Seconds** | No decimals. |
| **Tenths** | One decimal. |
| **Hundredths** | Two decimals. |
| **Milliseconds** | Three decimals. |

When space is tight, SplitDetail shortens values while keeping them readable. For example:

```text
+57.530 → +57.53 → +57.5 → +57
+3:11   → +3m
+1:02:30 → +1h
```

It avoids unreadable forms such as `+3:` or a lone `+`.

### Colors

| Setting | Description |
|---|---|
| **Text Color** | Label color. |
| **Time Color** | Right-side time and Current Split comparison value color. |

Delta colors follow LiveSplit’s ahead/behind colors. Gold deltas use the layout’s gold/best-segment color where available.

---

## Subsplits convention

SplitDetail detects subsplit groups using the same naming convention as LiveSplit’s Subsplits component:

```text
-Room 1      ← child subsplit
-Room 2      ← child subsplit
Castle       ← parent split group/header
```

A child subsplit starts with `-`. The parent/header does not.

If your splits use a different prefix, change this constant in `SplitDetailComponent.cs`:

```csharp
private const string SubsplitPrefix = "-";
```

If a run does not use subsplits, every segment is treated as its own group.

---

## Installation

1. Build the project in **Release** mode.
2. Copy the compiled DLL:

```text
bin\Release\LiveSplit.SplitDetail.dll
```

into your LiveSplit `Components` folder.

3. Restart LiveSplit.
4. Open **Layout Editor**.
5. Click `+ → Information → SplitDetail`.

---

## Building from source

### Requirements

- Visual Studio 2022 recommended.
- .NET Framework 4.8.1 Developer Pack.
- LiveSplit installed, or the required LiveSplit DLL references available in the project’s `packages` folder.

### Steps

1. Open `SplitDetail.csproj` in Visual Studio.
2. Make sure the references resolve:
   - `LiveSplit.Core.dll`
   - `UpdateManager.dll`
3. Build in **Release** mode.
4. Copy `LiveSplit.SplitDetail.dll` to LiveSplit’s `Components` folder.

If references do not resolve automatically, add them manually through:

```text
References → Add Reference → Browse
```

and select the DLLs from your LiveSplit installation or local `packages` folder.

---

## Development notes

Important areas in `SplitDetailComponent.cs`:

| Function | Purpose |
|---|---|
| `GetGroupRange(...)` | Detects parent split groups from subsplit names. |
| `GetCurrentGroupRange(...)` | Finds the active parent split group. |
| `GetPriorGroupRange(...)` | Finds the last completed parent split group. |
| `GetPriorSubsplitIndex(...)` | Finds the last completed individual segment. |
| `GetCompletedRangeTime(...)` | Gets actual time for a completed range. |
| `GetActiveRangeTime(...)` | Gets live elapsed time for an active range. |
| `GetComparisonRangeTime(...)` | Gets comparison time for any range. |
| `CalcCurrentSplit(...)` | Display logic for Current Split mode. |
| `CalcPriorRange(...)` | Display logic for Prev/Live Split and Prev/Live Seg. |
| `DrawRow(...)` | Main row layout and drawing entry point. |
| `DrawTextWithEffects(...)` | LiveSplit-style shadows/outlines without clipping. |
| `DrawTextWithEffectsClipped(...)` | Draws text with effects while preventing column overlap. |
| `ShortenDeltaToFit(...)` | Compact delta text shortening for tight layouts. |

The text drawing helpers are intentionally custom. Plain `Graphics.DrawString` can clip LiveSplit-style shadows/outlines on letters like `p`, `g`, and `y`.

---

## Recommended testing checklist

Before publishing a new build, test:

- Current Split mode.
- Prev Split mode.
- Prev Seg. mode.
- Live Split switching when losing time.
- Live Seg. switching when losing time.
- One-comparison mode.
- Two-comparison mode.
- Priority Delta = Comparison 1.
- Priority Delta = Comparison 2.
- Separators empty, `|`, `/`, and `·`.
- Accuracy: seconds, tenths, hundredths, milliseconds.
- Runs with no subsplits.
- Runs with multiple subsplits per parent group.
- Game Time and Real Time, if applicable.
- Missing comparison values / skipped splits.

---

## License

MIT License.
