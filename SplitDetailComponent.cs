// ============================================================================
// SplitDetailComponent.cs
// LiveSplit component: SplitDetail
//
// ── Subsplits Convention ─────────────────────────────────────────────────────
//   Segments whose names begin with "-" are children (subsplits).
//   The first segment in a group whose name does NOT begin with "-"
//   is the parent / group header.
//
//   Example:
//     index 0: "-Room 1"   ← child subsplit
//     index 1: "-Room 2"   ← child subsplit
//     index 2: "Castle"    ← PARENT (end of group; group spans [0,2])
//     index 3: "Forest"    ← standalone (group spans [3,3])
//     index 4: "-Area A"   ← child subsplit
//     index 5: "Mountain"  ← PARENT (group spans [4,5])
//
//   If subsplits are not used every segment is its own group (start==end).
//   Change SubsplitPrefix below if your splits use a different convention.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace LiveSplit.UI.Components
{
    public enum SplitDetailMode
    {
        CurrentSplit,
        PriorSplit,
        PriorSubsplit
    }

    internal struct SegmentRange
    {
        public readonly int Start;
        public readonly int End;
        public bool IsValid => Start >= 0 && End >= 0 && Start <= End;
        public SegmentRange(int start, int end) { Start = start; End = end; }
        public static readonly SegmentRange Invalid = new SegmentRange(-1, -1);
    }

    public class SplitDetailComponent : IComponent
    {
        // ── Subsplit prefix ───────────────────────────────────────────────────
        // ⚠ Change if your splits use a different prefix (e.g. "{-}").
        private const string SubsplitPrefix = "-";

        // ── Layout constants ──────────────────────────────────────────────────
        // Minimum left column width (px).  Measured dynamically at draw time
        // but never allowed to fall below this floor.
        private const float MinLabelColumnWidth = 20f;

        // Gap between columns (px).
        private const float ColGap = 3f;

        // Outer left/right padding (px).
        private const float OuterPad = 5f;

        // Right column width (px) — wide enough for "0:00:00.00".
        private const float RightColumnWidth = 78f;

        // Current Split: small font scale factor.
        // Two lines of this height must fit inside the row.
        private const float SmallFontScale = 0.50f;

        // Minimum small font size (pt).
        private const float MinSmallFontPt = 5f;

        // ── Settings ──────────────────────────────────────────────────────────
        private readonly SplitDetailSettings _settings;

        // ── Cached display data (set in CalculateDisplayValues every tick) ────

        // Shared
        private string _labelText = string.Empty;
        private string _rightText = string.Empty;

        // Current Split mode — stacked middle column
        //   Line 1:  "PB"          _cs_pbTime
        //   Line 2:  _cs_cmpLabel  _cs_cmpTime
        private string _cs_pbTime   = string.Empty;
        private string _cs_cmpLabel = string.Empty;
        private string _cs_cmpTime  = string.Empty;

        // Prior Split / Prior Subsplit mode — separator-centred middle column
        //   [_pr_delta1 right-aligned] [ sep ] [_pr_delta2 left-aligned]
        private string _pr_delta1      = string.Empty;
        private Color  _pr_delta1Color = Color.White;
        private string _pr_delta2      = string.Empty;
        private Color  _pr_delta2Color = Color.White;

        // ── Constructor ───────────────────────────────────────────────────────
        public SplitDetailComponent(LiveSplitState state)
        {
            _settings = new SplitDetailSettings(state);
        }

        // ── IComponent identity ───────────────────────────────────────────────
        public string ComponentName => "SplitDetail";

        // ── IComponent sizing ─────────────────────────────────────────────────
        // Height is measured from the layout text font, like TotalTimeloss does,
        // instead of using a fixed 23f value that can clip shadows/outlines.
        private float _rowHeight = 30f;
        private float RowHeight => _rowHeight;

        public float HorizontalWidth => 300f;
        public float MinimumHeight   => RowHeight;
        public float VerticalHeight  => RowHeight;
        public float MinimumWidth    => 120f;
        public float PaddingTop      => 0f;
        public float PaddingBottom   => 0f;
        public float PaddingLeft     => 0f;
        public float PaddingRight    => 0f;

        public IDictionary<string, Action> ContextMenuControls => null;

        // ── IComponent settings ───────────────────────────────────────────────
        public Control GetSettingsControl(LayoutMode mode)
        {
            _settings.RefreshComparisons();
            return _settings;
        }

        public XmlNode GetSettings(XmlDocument document) => _settings.GetSettings(document);
        public void SetSettings(XmlNode settings)        => _settings.SetSettings(settings);

        // ── IComponent update ─────────────────────────────────────────────────
        public void Update(IInvalidator invalidator,
                           LiveSplitState state,
                           float width, float height,
                           LayoutMode mode)
        {
            CalculateDisplayValues(state);
            invalidator?.Invalidate(0, 0, width, height);
        }

        // ── IComponent drawing ────────────────────────────────────────────────
        public void DrawHorizontal(Graphics g, LiveSplitState state,
                                   float height, Region clipRegion)
            => DrawRow(g, state, HorizontalWidth, height);

        public void DrawVertical(Graphics g, LiveSplitState state,
                                 float width, Region clipRegion)
            => DrawRow(g, state, width, RowHeight);

        public void Dispose() { }

        // =====================================================================
        // GROUP / SUBSPLIT DETECTION  — do not modify unless changing subsplit logic
        // =====================================================================

        /// <summary>
        /// Given any segment index, returns the inclusive [Start, End] range
        /// of the parent split GROUP that contains it.
        ///
        /// Step 1 — Walk FORWARD until reaching a segment whose name does NOT
        ///           start with SubsplitPrefix → that is the group parent (end).
        /// Step 2 — Walk BACKWARD from end while the preceding segment's name
        ///           starts with SubsplitPrefix → that is the first child (start).
        ///
        /// If subsplits are not used, every segment is its own group (start==end).
        /// </summary>
        private SegmentRange GetGroupRange(IRun run, int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= run.Count)
                return SegmentRange.Invalid;

            // Step 1: forward to parent (first non-"-" segment at or after index)
            int end = segmentIndex;
            while (end < run.Count - 1 && run[end].Name.StartsWith(SubsplitPrefix))
                end++;

            // Step 2: backward to first child
            int start = end;
            while (start > 0 && run[start - 1].Name.StartsWith(SubsplitPrefix))
                start--;

            return new SegmentRange(start, end);
        }

        /// <summary>
        /// Returns the group range that is currently being run.
        /// Returns Invalid when the run is not in progress.
        /// </summary>
        private SegmentRange GetCurrentGroupRange(LiveSplitState state)
        {
            if (state.CurrentPhase == TimerPhase.NotRunning ||
                state.CurrentPhase == TimerPhase.Ended)
                return SegmentRange.Invalid;

            int idx = state.CurrentSplitIndex;
            if (idx < 0 || idx >= state.Run.Count)
                return SegmentRange.Invalid;

            return GetGroupRange(state.Run, idx);
        }

        /// <summary>
        /// Returns the last fully completed parent split group.
        ///   While running → the group just before the current group.
        ///   After ending  → the group containing the final segment.
        ///   Not started   → Invalid.
        /// </summary>
        private SegmentRange GetPriorGroupRange(LiveSplitState state)
        {
            if (state.CurrentPhase == TimerPhase.NotRunning)
                return SegmentRange.Invalid;

            IRun run = state.Run;

            if (state.CurrentPhase == TimerPhase.Ended)
                return GetGroupRange(run, run.Count - 1);

            SegmentRange currentGroup = GetCurrentGroupRange(state);
            if (!currentGroup.IsValid || currentGroup.Start <= 0)
                return SegmentRange.Invalid; // still in the first group

            return GetGroupRange(run, currentGroup.Start - 1);
        }

        /// <summary>
        /// Returns the index of the last completed individual segment.
        /// Returns -1 when there is no prior segment.
        /// </summary>
        private int GetPriorSubsplitIndex(LiveSplitState state)
        {
            if (state.CurrentPhase == TimerPhase.NotRunning) return -1;
            if (state.CurrentPhase == TimerPhase.Ended)      return state.Run.Count - 1;
            int prev = state.CurrentSplitIndex - 1;
            return prev >= 0 ? prev : -1;
        }

        // =====================================================================
        // TIMING CALCULATIONS  — do not modify unless changing timing logic
        // =====================================================================

        /// <summary>
        /// Current run time for a completed segment range [start, end].
        /// Formula: run[end].SplitTime − run[start-1].SplitTime  (start==0: omit offset)
        /// Returns null if any boundary time is missing.
        /// </summary>
        private TimeSpan? GetCompletedRangeTime(IRun run, int start, int end,
                                                 TimingMethod method)
        {
            TimeSpan? endTime = run[end].SplitTime[method];
            if (endTime == null) return null;
            if (start == 0)     return endTime;

            TimeSpan? startTime = run[start - 1].SplitTime[method];
            if (startTime == null) return null;

            return endTime - startTime;
        }

        /// <summary>
        /// Live elapsed time for the currently active range from start.
        /// Uses state.CurrentTime as the live "end" point.
        /// Returns null if timing data is unavailable.
        /// </summary>
        private TimeSpan? GetActiveRangeTime(IRun run, LiveSplitState state,
                                              int start, int end,
                                              TimingMethod method)
        {
            TimeSpan? currentTime = state.CurrentTime[method];
            if (currentTime == null) return null;
            if (start == 0)         return currentTime;

            TimeSpan? startTime = run[start - 1].SplitTime[method];
            if (startTime == null) return null;

            return currentTime - startTime;
        }

        /// <summary>
        /// Comparison predicted time for a segment range [start, end].
        /// Formula: comparison[end] − comparison[start-1]  (start==0: omit offset)
        /// Works for any comparison name including Best Segments.
        /// Returns null if comparison data is unavailable.
        /// </summary>
        private TimeSpan? GetComparisonRangeTime(IRun run, int start, int end,
                                                  string comparison,
                                                  TimingMethod method)
        {
            TimeSpan? endTime = run[end].Comparisons[comparison][method];
            if (endTime == null) return null;
            if (start == 0)     return endTime;

            TimeSpan? startTime = run[start - 1].Comparisons[comparison][method];
            if (startTime == null) return null;

            return endTime - startTime;
        }

        // =====================================================================
        // DISPLAY VALUE CALCULATION
        // =====================================================================

        private void CalculateDisplayValues(LiveSplitState state)
        {
            // Reset all cached values
            _labelText     = string.Empty;
            _rightText     = Dash;
            _cs_pbTime     = Dash;
            _cs_cmpLabel   = string.Empty;
            _cs_cmpTime    = Dash;
            _pr_delta1     = Dash;
            _pr_delta2     = Dash;
            _pr_delta1Color = _settings.TextColor;
            _pr_delta2Color = _settings.TextColor;

            IRun         run        = state.Run;
            TimingMethod method     = state.CurrentTimingMethod;
            string       comparison = _settings.Comparison;
            string       pbComp    = "Personal Best";

            switch (_settings.Mode)
            {
                case SplitDetailMode.CurrentSplit:
                    CalcCurrentSplit(state, run, method, comparison, pbComp);
                    break;
                case SplitDetailMode.PriorSplit:
                    CalcPriorRange(state, run, method, comparison, pbComp, isPriorSubsplit: false);
                    break;
                case SplitDetailMode.PriorSubsplit:
                    CalcPriorRange(state, run, method, comparison, pbComp, isPriorSubsplit: true);
                    break;
            }
        }

        // ── Mode 1: Current Split ─────────────────────────────────────────────
        //
        //  Left      │ Middle            │ Right
        //  ──────────│───────────────────│────────────
        //  Current   │ PB    1:36.55     │
        //  Split     │ Best  1:21.35     │ 17:15.84
        //
        //  Middle column: two small stacked lines
        //    Line 1 — "PB"          _cs_pbTime    (right-aligned in middle)
        //    Line 2 — _cs_cmpLabel  _cs_cmpTime   (right-aligned in middle)
        //
        private void CalcCurrentSplit(LiveSplitState state, IRun run,
                                       TimingMethod method,
                                       string comparison, string pbComp)
        {
            _labelText = "Current Split";
            _cs_cmpLabel = AbbreviateComparison(comparison);

            bool active = (state.CurrentPhase == TimerPhase.Running ||
                           state.CurrentPhase == TimerPhase.Paused);

            if (!active)
            {
                // Run not started / ended — show dashes
                _cs_pbTime  = Dash;
                _cs_cmpTime = Dash;
                _rightText  = Dash;
                return;
            }

            SegmentRange group = GetCurrentGroupRange(state);
            if (!group.IsValid)
            {
                _cs_pbTime  = Dash;
                _cs_cmpTime = Dash;
                _rightText  = Dash;
                return;
            }

            TimeSpan? pbTime  = GetComparisonRangeTime(run, group.Start, group.End, pbComp,    method);
            TimeSpan? cmpTime = GetComparisonRangeTime(run, group.Start, group.End, comparison, method);

            _cs_pbTime  = FormatTime(pbTime);
            _cs_cmpTime = FormatTime(cmpTime);

            TimeSpan? elapsed = GetActiveRangeTime(run, state, group.Start, group.End, method);
            _rightText = FormatTime(elapsed);
        }

        // ── Mode 2 & 3: Prior Split / Prior Subsplit ──────────────────────────
        //
        //  Left           │ Middle                       │ Right
        //  ───────────────│──────────────────────────────│────────
        //  Prior Split    │ -1:24.03  |  -1:14.33        │ 22.64
        //  Prior Subsplit │ +4:28.89  |  +4:30.61        │ 4:43.00
        //
        //  Middle column:
        //    _pr_delta1  right-aligned to left of separator
        //    separator   centered in middle column
        //    _pr_delta2  left-aligned from right of separator
        //
        private void CalcPriorRange(LiveSplitState state, IRun run,
                                     TimingMethod method,
                                     string comparison, string pbComp,
                                     bool isPriorSubsplit)
        {
            _labelText = isPriorSubsplit ? "Prev Seg." : "Prev Split";

            if (state.CurrentPhase == TimerPhase.NotRunning)
                return; // all fields already set to Dash

            TimeSpan? actual = null, pbTime = null, cmpTime = null;

            if (isPriorSubsplit)
            {
                int idx = GetPriorSubsplitIndex(state);
                if (idx >= 0)
                {
                    actual  = GetCompletedRangeTime(run, idx, idx, method);
                    pbTime  = GetComparisonRangeTime(run, idx, idx, pbComp,    method);
                    cmpTime = GetComparisonRangeTime(run, idx, idx, comparison, method);
                }
            }
            else
            {
                SegmentRange group = GetPriorGroupRange(state);
                if (group.IsValid)
                {
                    actual  = GetCompletedRangeTime(run, group.Start, group.End, method);
                    pbTime  = GetComparisonRangeTime(run, group.Start, group.End, pbComp,    method);
                    cmpTime = GetComparisonRangeTime(run, group.Start, group.End, comparison, method);
                }
            }

            _rightText = FormatTime(actual);

            TimeSpan? deltaPb  = (actual.HasValue && pbTime.HasValue)
                ? actual.Value  - pbTime.Value  : (TimeSpan?)null;
            TimeSpan? deltaCmp = (actual.HasValue && cmpTime.HasValue)
                ? actual.Value - cmpTime.Value : (TimeSpan?)null;

            _pr_delta1      = FormatDelta(deltaPb);
            _pr_delta2      = FormatDelta(deltaCmp);
            _pr_delta1Color = DeltaColor(state, deltaPb);
            _pr_delta2Color = DeltaColor(state, deltaCmp);
        }

        // =====================================================================
        // DRAWING
        // =====================================================================

        /// <summary>
        /// Renders the one-row component.
        ///
        /// Column layout:
        ///
        ///   [OuterPad] [Left: auto-sized to label] [ColGap]
        ///   [Middle: remainder] [ColGap]
        ///   [Right: RightColumnWidth] [OuterPad]
        ///
        /// The left column width is measured from the actual label text each
        /// frame so "Prior Subsplit" never gets truncated.  The middle column
        /// gets all remaining space.
        ///
        /// Current Split mode draws two small stacked lines in the middle.
        /// Prior modes draw: [delta1 right] [separator] [delta2 left].
        /// </summary>
        private void DrawRow(Graphics g, LiveSplitState state, float width, float height)
        {
            var ls = state.LayoutSettings;

            // ── Background ───────────────────────────────────────────────────
            DrawBackground(g, ls, width, height);

            // ── Fonts ────────────────────────────────────────────────────────
            Font mainFont = ls.TextFont ?? SystemFonts.DefaultFont;

            // Match TotalTimeloss-style sizing: MeasureString gives the actual
            // visual height needed by this font, including the extra room that
            // prevents descenders/shadows/outlines from being clipped.
            _rowHeight = Math.Max(30f, g.MeasureString("Ay", mainFont).Height);
            height = Math.Max(height, _rowHeight);

            // ── Colors ───────────────────────────────────────────────────────
            // Use the user-configurable colors from settings.
            Color textColor = _settings.TextColor;
            Color timeColor = _settings.TimeColor;

            // ── Measure label width dynamically ──────────────────────────────
            // Current Split can use its own width.
            // Prior Split and Prior Seg. must reserve the same label width so
            // their separator/delta blocks align across multiple SplitDetail rows.
            string labelMeasureText = _settings.Mode == SplitDetailMode.CurrentSplit
                ? _labelText
                : "Prev Split";

            SizeF labelSz = g.MeasureString(labelMeasureText, mainFont);
            float labelColW = Math.Max(MinLabelColumnWidth, labelSz.Width + 2f);

            // ── Column geometry ───────────────────────────────────────────────
            float xLeft = OuterPad;
            float xMid = xLeft + labelColW + ColGap;

            // Use only the width the right-side time actually needs, up to the max.
            // This gives more room to the delta block when the time is short.
            float rightColW = Math.Min(
                RightColumnWidth,
                Math.Max(22f, g.MeasureString(_rightText, mainFont).Width + 1f));

            float xRight = width - OuterPad - rightColW;
            float midW = Math.Max(0f, xRight - xMid - ColGap);

            // ── Vertical center helpers ───────────────────────────────────────
            // Use MeasureString instead of GetHeight, like TotalTimeloss.
            // This gives enough vertical room for descenders, shadows and outlines.
            float fontH = g.MeasureString("Ay", mainFont).Height;
            float textY = Math.Max(0f, (height - fontH) / 2f);

            // ── StringFormats ─────────────────────────────────────────────────
            var fmtLeft = new StringFormat
            {
                Alignment   = StringAlignment.Near,
                Trimming    = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            var fmtRight = new StringFormat
            {
                Alignment   = StringAlignment.Far,
                Trimming    = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            // ── Left column: mode label ───────────────────────────────────────
            // Uses the user-selected TextColor directly — no dimming.
            DrawTextWithEffects(g, _labelText, mainFont, textColor,
                                new RectangleF(xLeft, textY, labelColW, fontH),
                                fmtLeft, ls);

            // ── Right column: time / timer ────────────────────────────────────
            DrawTextWithEffects(g, _rightText, mainFont, timeColor,
                                new RectangleF(xRight, textY, rightColW, fontH),
                                fmtRight, ls);

            // ── Middle column: mode-dependent ────────────────────────────────
            switch (_settings.Mode)
            {
                case SplitDetailMode.CurrentSplit:
                    DrawCurrentSplitMiddle(g, mainFont, textColor, timeColor,
                                           xMid, midW, height, fmtLeft, fmtRight, ls);
                    break;

                case SplitDetailMode.PriorSplit:
                case SplitDetailMode.PriorSubsplit:
                    DrawPriorMiddle(g, mainFont, textColor,
                                    xMid, midW, textY, fontH, fmtLeft, fmtRight, ls);
                    break;
            }
        }

        // ── Current Split middle column ───────────────────────────────────────
        //
        //   Two stacked small-font lines, each spanning the full middle width:
        //
        //     PB    [right-aligned time]
        //     Best  [right-aligned time]
        //
        //   Both lines are centered together vertically inside the row.
        //
        private void DrawCurrentSplitMiddle(Graphics g, Font mainFont,
                                     Color textColor, Color timeColor,
                                     float xMid, float midW, float height,
                                     StringFormat fmtLeft, StringFormat fmtRight,
                                     LiveSplit.Options.LayoutSettings ls)
        {
            // Scale the font down so two lines fit comfortably in one row.
            float smallPt = Math.Max(MinSmallFontPt, mainFont.Size * SmallFontScale);
            using (var smallFont = new Font(mainFont.FontFamily, smallPt, FontStyle.Regular))
            {
                float lineH = smallFont.GetHeight(g);
                float lineStep = lineH * 0.68f;
                float totalH = lineH + lineStep;
                float y1 = (height - totalH) / 2f;
                float y2 = y1 + lineStep;

                string pbLabel = "PB:";
                string cmpLabel = _cs_cmpLabel + ":";

                // Compact label column. Keep PB/Best labels aligned, but keep the
                // gap between label and time small.
                float labelSubW = Math.Max(
                    g.MeasureString(pbLabel, smallFont).Width,
                    g.MeasureString(cmpLabel, smallFont).Width) + 2f;

                // Compact time column. Both times stay aligned with each other,
                // but they no longer use the entire middle column width.
                float timeSubW = Math.Max(
                    g.MeasureString(_cs_pbTime, smallFont).Width,
                    g.MeasureString(_cs_cmpTime, smallFont).Width) + 4f;

                if (labelSubW + timeSubW > midW)
                    timeSubW = Math.Max(0f, midW - labelSubW);

                // Line 1: "PB:" label (time color) + PB time (time color)
                DrawTextWithEffects(g, "PB:", smallFont, timeColor,
                                    new RectangleF(xMid, y1, labelSubW, lineH),
                                    fmtLeft, ls);

                DrawTextWithEffectsFit(g, _cs_pbTime, smallFont, timeColor,
                       new RectangleF(xMid + labelSubW, y1, timeSubW, lineH),
                       fmtRight, ls, MinSmallFontPt);

                // Line 2: comparison label (time color) + comparison time (time color)
                DrawTextWithEffects(g, cmpLabel, smallFont, timeColor,
                                    new RectangleF(xMid, y2, labelSubW, lineH),
                                    fmtLeft, ls);

                DrawTextWithEffectsFit(g, _cs_cmpTime, smallFont, timeColor,
                       new RectangleF(xMid + labelSubW, y2, timeSubW, lineH),
                       fmtRight, ls, MinSmallFontPt);
            }
        }

        // ── Prior Split / Prior Subsplit middle column ────────────────────────
        //
        //   Three sub-elements drawn at fixed positions:
        //
        //     [delta1 right-aligned] [sep centered] [delta2 left-aligned]
        //
        //   The separator is drawn at the horizontal center of the middle column.
        //   delta1 is right-aligned immediately to the left of the separator.
        //   delta2 is left-aligned immediately to the right of the separator.
        //
        private void DrawPriorMiddle(Graphics g, Font font, Color textColor,
                              float xMid, float midW, float textY, float fontH,
                              StringFormat fmtLeft, StringFormat fmtRight,
                              LiveSplit.Options.LayoutSettings ls)
        {
            // No separator: just a small gap between both deltas.
            const float DeltaGap = 2f;

            string d1Text = _pr_delta1;
            string d2Text = _pr_delta2;

            float available = Math.Max(0f, midW - DeltaGap);

            float d1NaturalW = g.MeasureString(d1Text, font).Width + 1f;
            float d2NaturalW = g.MeasureString(d2Text, font).Width + 1f;

            float d1W;
            float d2W;

            if (d1NaturalW + d2NaturalW <= available)
            {
                // Best case: both deltas fit fully.
                d1W = d1NaturalW;
                d2W = d2NaturalW;
            }
            else
            {
                // Tight case:
                // Prioritize the second delta, because it is the comparison / Best delta.
                d2W = Math.Min(d2NaturalW, available);
                d1W = Math.Max(0f, available - d2W);

                // If the comparison delta itself is too large, it gets all the space
                // and the PB delta disappears.
                if (d2NaturalW > available)
                {
                    d2W = available;
                    d1W = 0f;
                }
            }

            d1Text = ShortenDeltaToFit(g, d1Text, font, d1W);
            d2Text = ShortenDeltaToFit(g, d2Text, font, d2W);

            // Re-measure after shortening, so the block stays compact.
            if (string.IsNullOrEmpty(d1Text))
                d1W = 0f;
            else
                d1W = Math.Min(d1W, g.MeasureString(d1Text, font).Width + 1f);

            if (string.IsNullOrEmpty(d2Text))
                d2W = 0f;
            else
                d2W = Math.Min(d2W, g.MeasureString(d2Text, font).Width + 1f);

            float actualGap = d1W > 0f && d2W > 0f ? DeltaGap : 0f;
            float blockW = d1W + actualGap + d2W;

            // Right-align the whole block inside the middle column.
            float blockX = xMid + midW - blockW;

            float d1X = blockX;
            float d2X = blockX + d1W + actualGap;

            // Never resize the font here. If something still does not fit perfectly,
            // it gets clipped inside its own zone instead of invading other columns.
            if (d1W > 0f)
            {
                DrawTextWithEffectsClipped(g, d1Text, font, _pr_delta1Color,
                                           new RectangleF(d1X, textY, d1W, fontH),
                                           fmtRight, ls);
            }

            if (d2W > 0f)
            {
                DrawTextWithEffectsClipped(g, d2Text, font, _pr_delta2Color,
                                           new RectangleF(d2X, textY, d2W, fontH),
                                           fmtLeft, ls);
            }
        }


        // ── Text drawing with LiveSplit-style shadows/outlines ────────────────
        //
        // DrawString by itself does not respect LiveSplit's text outline/shadow
        // look well enough. This helper mirrors the approach used in TotalTimeloss:
        // it draws shadows and outlines manually using GraphicsPath.
        private static void DrawTextWithEffects(Graphics g, string text, Font font,
                                        Color textColor, RectangleF rect,
                                        StringFormat format,
                                        LiveSplit.Options.LayoutSettings settings)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return;

            bool hasShadow = GetLayoutSetting(settings, "DropShadows", false);
            Color shadowColor = GetLayoutSetting(settings, "ShadowsColor", Color.Black);
            Color outlineColor = GetLayoutSetting(settings, "TextOutlineColor", Color.Transparent);

            // Convert rectangle alignment into an actual x coordinate, then draw
            // using a huge layout rectangle like TotalTimeloss/SimpleLabel style.
            // This avoids clipping shadows/outlines on letters like p/g or symbols.
            SizeF measured = g.MeasureString(text, font);

            float x = rect.X;
            if (format.Alignment == StringAlignment.Far)
                x = rect.Right - measured.Width;
            else if (format.Alignment == StringAlignment.Center)
                x = rect.X + (rect.Width - measured.Width) / 2f;

            float y = rect.Y;

            using (var nearFormat = new StringFormat())
            {
                nearFormat.Alignment = StringAlignment.Near;
                nearFormat.LineAlignment = format.LineAlignment;
                nearFormat.Trimming = StringTrimming.None;
                nearFormat.FormatFlags = StringFormatFlags.NoWrap;

                if (g.TextRenderingHint == TextRenderingHint.AntiAlias && outlineColor.A > 0)
                {
                    float fontSize = GetFontSize(g, font);

                    using (var path = new GraphicsPath())
                    using (var outlinePen = new Pen(outlineColor, GetOutlineSize(fontSize)))
                    using (var textBrush = new SolidBrush(textColor))
                    {
                        outlinePen.LineJoin = LineJoin.Round;

                        if (hasShadow && shadowColor.A > 0)
                        {
                            using (var shadowBrush = new SolidBrush(shadowColor))
                            {
                                path.AddString(text, font.FontFamily, (int)font.Style, fontSize,
                                    new RectangleF(x + 1f, y + 1f, 9999f, 9999f), nearFormat);
                                g.FillPath(shadowBrush, path);
                                path.Reset();

                                path.AddString(text, font.FontFamily, (int)font.Style, fontSize,
                                    new RectangleF(x + 2f, y + 2f, 9999f, 9999f), nearFormat);
                                g.FillPath(shadowBrush, path);
                                path.Reset();
                            }
                        }

                        path.AddString(text, font.FontFamily, (int)font.Style, fontSize,
                            new RectangleF(x, y, 9999f, 9999f), nearFormat);
                        g.DrawPath(outlinePen, path);
                        g.FillPath(textBrush, path);
                    }
                }
                else
                {
                    if (hasShadow && shadowColor.A > 0)
                    {
                        using (var shadowBrush = new SolidBrush(shadowColor))
                        {
                            g.DrawString(text, font, shadowBrush, x + 1f, y + 1f, nearFormat);
                            g.DrawString(text, font, shadowBrush, x + 2f, y + 2f, nearFormat);
                        }
                    }

                    using (var textBrush = new SolidBrush(textColor))
                        g.DrawString(text, font, textBrush, x, y, nearFormat);
                }
            }
        }

        private static void DrawTextWithEffectsClipped(Graphics g, string text, Font font,
                                               Color textColor, RectangleF rect,
                                               StringFormat format,
                                               LiveSplit.Options.LayoutSettings settings)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return;

            Region oldClip = g.Clip;
            try
            {
                using (var clip = new Region(rect))
                {
                    g.Clip = clip;
                    DrawTextWithEffects(g, text, font, textColor, rect, format, settings);
                }
            }
            finally
            {
                g.Clip = oldClip;
            }
        }

        private static void DrawTextWithEffectsFit(Graphics g, string text, Font baseFont,
                                           Color textColor, RectangleF rect,
                                           StringFormat format,
                                           LiveSplit.Options.LayoutSettings settings,
                                           float minFontPt)
        {
            if (string.IsNullOrEmpty(text) || baseFont == null)
                return;

            if (rect.Width <= 1f)
                return;

            Font drawFont = baseFont;
            bool disposeFont = false;

            // Leave a little horizontal safety room for outlines/shadows.
            float availableWidth = Math.Max(1f, rect.Width - 1f);
            float measuredWidth = g.MeasureString(text, drawFont).Width;

            if (measuredWidth > availableWidth && drawFont.Size > minFontPt)
            {
                float scale = availableWidth / measuredWidth;
                float newSize = Math.Max(minFontPt, drawFont.Size * scale);

                drawFont = new Font(baseFont.FontFamily, newSize, baseFont.Style);
                disposeFont = true;
            }

            try
            {
                DrawTextWithEffects(g, text, drawFont, textColor, rect, format, settings);
            }
            finally
            {
                if (disposeFont)
                    drawFont.Dispose();
            }
        }


        private static T GetLayoutSetting<T>(LiveSplit.Options.LayoutSettings settings,
                                             string propertyName, T fallback)
        {
            try
            {
                var prop = settings.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                    return fallback;

                object value = prop.GetValue(settings, null);
                if (value is T)
                    return (T)value;
            }
            catch
            {
                // Fall back silently. LiveSplit versions may differ slightly.
            }

            return fallback;
        }

        private static float GetFontSize(Graphics g, Font font)
        {
            if (font.Unit == GraphicsUnit.Point)
                return font.Size * g.DpiY / 72f;

            return font.Size;
        }

        private static float GetOutlineSize(float fontSize)
        {
            return 2.1f + fontSize * 0.055f;
        }

        // ── Background ────────────────────────────────────────────────────────
        private static void DrawBackground(Graphics g,
                                            LiveSplit.Options.LayoutSettings ls,
                                            float width, float height)
        {
            if (ls.BackgroundColor2 == Color.Transparent)
            {
                using (var br = new SolidBrush(ls.BackgroundColor))
                    g.FillRectangle(br, 0, 0, width, height);
            }
            else
            {
                using (var br = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(0, height),
                    ls.BackgroundColor, ls.BackgroundColor2))
                {
                    g.FillRectangle(br, 0, 0, width, height);
                }
            }
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private const string Dash = "-";

        /// <summary>
        /// Formats a positive TimeSpan as "S.ff", "M:SS.ff", or "H:MM:SS.ff".
        /// Returns Dash for null.
        /// </summary>
        private static string FormatTime(TimeSpan? t)
        {
            if (t == null) return Dash;
            TimeSpan ts = t.Value;
            if (ts.TotalHours >= 1)
                return string.Format("{0}:{1:D2}:{2:D2}.{3:D2}",
                    (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
            if (ts.TotalMinutes >= 1)
                return string.Format("{0}:{1:D2}.{2:D2}",
                    ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
            return string.Format("{0}.{1:D2}",
                ts.Seconds, ts.Milliseconds / 10);
        }

        private static string RemoveDeltaSign(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Do not turn the missing-value dash "-" into an empty string.
            if (text == Dash)
                return text;

            if (text[0] == '+' || text[0] == '−' || text[0] == '-')
                return text.Substring(1);

            return text;
        }

        private static string ShortenDeltaToFit(Graphics g, string text, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (maxWidth <= 1f)
                return string.Empty;

            if (text == Dash)
                return g.MeasureString(text, font).Width <= maxWidth ? text : string.Empty;

            if (g.MeasureString(text, font).Width <= maxWidth)
                return text;

            string sign = string.Empty;
            string body = text;

            if (text[0] == '+' || text[0] == '−' || text[0] == '-')
            {
                sign = text.Substring(0, 1);
                body = text.Substring(1);
            }

            // First compact form: remove decimals, but keep the sign.
            string noDecimals = RemoveDecimalPart(body);
            string candidate = sign + noDecimals;

            if (!string.IsNullOrEmpty(noDecimals) &&
                g.MeasureString(candidate, font).Width <= maxWidth)
                return candidate;

            // Second compact form: progressively shorten the body and add an ellipsis.
            for (int len = body.Length - 1; len >= 1; len--)
            {
                candidate = sign + body.Substring(0, len) + "…";
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;

                candidate = sign + body.Substring(0, len);
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }

            // Last resort: show only the sign, if it fits.
            if (!string.IsNullOrEmpty(sign) &&
                g.MeasureString(sign, font).Width <= maxWidth)
                return sign;

            return string.Empty;
        }

        private static string RemoveDecimalPart(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            int dot = text.IndexOf('.');
            if (dot > 0)
                return text.Substring(0, dot);

            return text;
        }

        /// <summary>
        /// Formats a delta TimeSpan with a leading "+" or "−" sign.
        /// Returns Dash for null.
        /// </summary>
        private static string FormatDelta(TimeSpan? t)
        {
            if (t == null) return Dash;

            TimeSpan ts = t.Value;
            string sign = ts.Ticks >= 0 ? "+" : "−";
            TimeSpan abs = ts.Duration();

            // For large deltas, decimals waste too much horizontal space.
            // Keep decimals only under one minute.
            if (abs.TotalMinutes >= 1)
                return string.Format("{0}{1}:{2:D2}",
                    sign, (int)abs.TotalMinutes, abs.Seconds);

            return string.Format("{0}{1}.{2:D2}",
                sign, abs.Seconds, abs.Milliseconds / 10);
        }

        /// <summary>
        /// Maps a delta to a LiveSplit delta color.
        ///   negative (faster) → AheadGainingTimeColor (green)
        ///   positive (slower) → BehindLosingTimeColor (red)
        ///   null              → settings TextColor
        /// </summary>
        private Color DeltaColor(LiveSplitState state, TimeSpan? delta)
        {
            if (delta == null)         return _settings.TextColor;
            if (delta.Value.Ticks > 0) return state.LayoutSettings.BehindLosingTimeColor;
            return state.LayoutSettings.AheadGainingTimeColor;
        }

        /// <summary>
        /// Short display label for a comparison name.
        /// </summary>
        private static string AbbreviateComparison(string comparison)
        {
            switch (comparison)
            {
                case "Best Segments":    return "Best";
                case "Personal Best":    return "PB";
                case "Average Segments": return "Avg";
                case "Balanced PB":      return "Bal";
                case "Latest Run":       return "Latest";
                default:
                    return comparison.Length > 8
                        ? comparison.Substring(0, 7) + "…"
                        : comparison;
            }
        }
    }
}
