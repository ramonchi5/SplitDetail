# SplitDetail — LiveSplit Component

A one-row LiveSplit component that shows timing information for the current or
prior **parent split group**, with full Subsplits support.

---

## Modes

| Mode | Left label | Middle | Right |
|------|-----------|--------|-------|
| **Current Split** | `Current Split` | `PB: 12.34`  `Best: 11.90` | Live group timer `8.52` |
| **Prior Split** | `Prior Split` | `+2.31  \| +5.84` (delta vs PB, delta vs comparison) | Actual group time `42.18` |
| **Prior Subsplit** | `Prior Subsplit` | `+0.44  \| +1.20` | Actual subsplit time `9.76` |

---

## Building

### Requirements

- Visual Studio 2019+ (or MSBuild 15+)
- .NET Framework 4.6.1 SDK
- LiveSplit installed

### Steps

1. Open `SplitDetail.csproj` in Visual Studio.
2. Right-click **References → Add Reference → Browse**.
3. Navigate to your LiveSplit folder and select `LiveSplit.Core.dll`.
4. Build in **Release** mode.
5. Copy `bin\Release\LiveSplit.SplitDetail.dll` into your LiveSplit
   `Components\` folder.
6. Restart LiveSplit, open Layout Editor, click `+`, find **SplitDetail**
   under Comparison.

### Alternative: MSBuild with LiveSplitPath

```bat
msbuild SplitDetail.csproj /p:Configuration=Release /p:LiveSplitPath="C:\LiveSplit\"
```

---

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Mode | Current Split | Which range to display |
| Comparison | Best Segments | Which comparison to show in the middle column |
| Separator | `\|` | Character between the two delta values (Prior modes) |

Comparison choices are read directly from the open run file, so any comparison
LiveSplit knows about (Average Segments, Balanced PB, custom comparisons, etc.)
will appear automatically.

---

## Subsplits Convention

SplitDetail detects groups using the standard naming prefix used by LiveSplit's
Subsplits component:

```
-Room 1      ← child subsplit (starts with "-")
-Room 2      ← child subsplit
Castle       ← parent/header (no prefix) — group spans all three
```

If your run uses a different prefix (e.g. `{-}`), change the constant at the
top of `SplitDetailComponent.cs`:

```csharp
private const string SubsplitPrefix = "-";
```

If your splits have no subsplits at all, every segment is treated as its own
group of size 1 — the component degrades gracefully.

---

## Modifying the Component

### Key functions to know

| Function | File | What it does |
|----------|------|--------------|
| `GetGroupRange(run, index)` | `SplitDetailComponent.cs` | Core group detection — change subsplit logic here |
| `GetCurrentGroupRange(state)` | same | Finds the active group |
| `GetPriorGroupRange(state)` | same | Finds the last completed group |
| `GetPriorSubsplitIndex(state)` | same | Finds the last completed individual segment |
| `GetCompletedRangeTime(...)` | same | Current run time for a completed range |
| `GetActiveRangeTime(...)` | same | Live elapsed time for the active range |
| `GetComparisonRangeTime(...)` | same | Comparison time for any range |
| `CalcCurrentSplit(...)` | same | Mode 1 display logic |
| `CalcPriorSplit(...)` | same | Mode 2 display logic |
| `CalcPriorSubsplit(...)` | same | Mode 3 display logic |
| `DrawRow(...)` | same | All rendering — change column widths, fonts, colors here |
| `AbbreviateComparison(...)` | same | Short labels for comparison names |
| `FormatTime(...)` | same | Time string formatting |
| `FormatDelta(...)` | same | Delta string formatting (with +/− sign) |

### Adding a new mode

1. Add a value to `SplitDetailMode` enum in `SplitDetailComponent.cs`.
2. Add a `CalcXxx(...)` method following the existing pattern.
3. Add a `case` in `CalculateDisplayValues`.
4. Add the display string to `_modeCombo.Items` in `SplitDetailSettings.cs`.

### Changing column widths

Edit the constants near the top of `SplitDetailComponent.cs`:

```csharp
private const float LabelColumnWidth = 90f;   // "Current Split" label
private const float TimerColumnWidth = 80f;   // right-side time
```

The middle area gets whatever is left over.

---

## Known Limitations & Verification Checklist

If something doesn't compile or behaves oddly, check these first:

- **`ILayoutSettings` color names** — `AheadGainingColor` and `BehindLosingColor`
  are correct for recent LiveSplit builds. Older builds may use `AheadColor` /
  `BehindColor`.  Search your `LiveSplit.Core.dll` decompiled source.

- **`layout.Font`** — if your build uses `TextFont` or `TimesFont` instead,
  change it in `DrawRow()`.

- **Subsplit prefix** — open one of your splits in the Splits Editor; if the
  child segment names start with something other than `-`, update `SubsplitPrefix`.

- **`state.CurrentTime[method]`** — this is the standard live-timer value
  used by all official LiveSplit components.  If it's null during a run, check
  whether your timing method (Game Time) has an active connection.

- **`[assembly: ComponentFactory(...)]`** — LiveSplit's plugin loader looks for
  this attribute to discover the factory type.  If the component doesn't appear
  in the list, verify the attribute is present in `SplitDetailFactory.cs` and
  that the DLL is in the `Components\` folder (not a sub-folder).
