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
//     index 2: "Castle"    ← PARENT (group spans [0,2])
//     index 3: "Forest"    ← standalone (group spans [3,3])
//     index 4: "-Area A"   ← child subsplit
//     index 5: "Mountain"  ← PARENT (group spans [4,5])
//
//   If subsplits are not used every segment is its own group (start==end).
//   Change SubsplitPrefix below if your splits use a different convention.
//
// ── Rendering approach ────────────────────────────────────────────────────────
//   All text goes through DrawTextWithEffects / DrawTextWithEffectsClipped.
//   These mirror TotalTimeloss's GraphicsPath approach to respect LiveSplit
//   shadow/outline settings without clipping descenders (p, g, y, |, etc.).
//   Do NOT replace these calls with plain g.DrawString.
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
using LiveSplit.TimeFormatters;
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
        private const float MinLabelColumnWidth = 20f;
        private const float OuterPad            = 5f;
        private const float RightColumnWidth    = 78f;
        private const float SmallFontScale      = 0.50f;
        private const float MinSmallFontPt      = 5f;

        // ── Settings ──────────────────────────────────────────────────────────
        private readonly SplitDetailSettings _settings;

        // ── Cached display data ────────────────────────────────────────────────

        // Shared
        private string _labelText      = string.Empty;
        private string _rightText      = string.Empty;
        private Color  _rightTextColor = Color.White;  // may become gold

        // Current Split mode — two small stacked comparison lines
        private string _cs_cmp1Label = string.Empty;
        private string _cs_cmp1Time  = string.Empty;
        private string _cs_cmp2Label = string.Empty;
        private string _cs_cmp2Time  = string.Empty;

        // Prior modes — compact delta block
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
        // ComponentName is shown in the Layout Editor component list.
        // We include the active mode label so multiple instances are easy to tell apart:
        //   "SplitDetail - Current Split"
        //   "SplitDetail - Prev Split"
        //   "SplitDetail - Prev Seg."
        // (or whatever custom labels the user has chosen in Settings)
        public string ComponentName
        {
            get
            {
                string label;
                switch (_settings.Mode)
                {
                    case SplitDetailMode.PriorSplit:
                        label = _settings.LabelPrevSplit;
                        break;
                    case SplitDetailMode.PriorSubsplit:
                        label = _settings.LabelPrevSeg;
                        break;
                    default: // CurrentSplit
                        label = _settings.LabelCurrentSplit;
                        break;
                }
                return "SplitDetail - " + label;
            }
        }

        // ── IComponent sizing ─────────────────────────────────────────────────
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

        // ── IComponent update/draw ────────────────────────────────────────────
        public void Update(IInvalidator invalidator, LiveSplitState state,
                           float width, float height, LayoutMode mode)
        {
            CalculateDisplayValues(state);
            invalidator?.Invalidate(0, 0, width, height);
        }

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

            int end = segmentIndex;
            while (end < run.Count - 1 && run[end].Name.StartsWith(SubsplitPrefix))
                end++;

            int start = end;
            while (start > 0 && run[start - 1].Name.StartsWith(SubsplitPrefix))
                start--;

            return new SegmentRange(start, end);
        }

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

        private SegmentRange GetPriorGroupRange(LiveSplitState state)
        {
            if (state.CurrentPhase == TimerPhase.NotRunning)
                return SegmentRange.Invalid;

            IRun run = state.Run;

            if (state.CurrentPhase == TimerPhase.Ended)
                return GetGroupRange(run, run.Count - 1);

            SegmentRange currentGroup = GetCurrentGroupRange(state);
            if (!currentGroup.IsValid || currentGroup.Start <= 0)
                return SegmentRange.Invalid;

            return GetGroupRange(run, currentGroup.Start - 1);
        }

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
        // GOLD DETECTION
        // =====================================================================

        /// <summary>
        /// Returns true if 'actual' is faster than the sum of best segment times
        /// for the given range — i.e. the runner just set a new best for this group.
        /// Uses BestSegmentTime per-segment and sums them, matching how LiveSplit
        /// tracks golds at the segment level.
        /// </summary>
        private static bool IsNewBest(IRun run, int start, int end,
                                       TimeSpan? actual, TimingMethod method)
        {
            if (!actual.HasValue) return false;

            TimeSpan bestSum = TimeSpan.Zero;
            for (int i = start; i <= end; i++)
            {
                TimeSpan? best = run[i].BestSegmentTime[method];
                if (!best.HasValue) return false;   // no reference → can't confirm gold
                bestSum += best.Value;
            }

            return actual.Value < bestSum;
        }

        /// <summary>
        /// Returns the gold/best-segment color from layout settings.
        /// Tries several property names via reflection for version compatibility.
        /// ⚠ VERIFY: "GoldColor" may be named differently in some LiveSplit forks.
        /// </summary>
        private static Color GetGoldColor(LiveSplitState state)
        {
            var ls = state.LayoutSettings;
            Color c = GetLayoutSetting(ls, "GoldColor", Color.Transparent);
            if (c == Color.Transparent)
                c = GetLayoutSetting(ls, "BestSegmentColor", Color.Transparent);
            if (c == Color.Transparent)
                c = Color.FromArgb(255, 215, 0);  // standard gold fallback
            return c;
        }

        // =====================================================================
        // DISPLAY VALUE CALCULATION
        // =====================================================================

        private void CalculateDisplayValues(LiveSplitState state)
        {
            _labelText      = string.Empty;
            _rightText      = Dash;
            _rightTextColor = _settings.TimeColor;
            _cs_cmp1Label   = string.Empty;
            _cs_cmp1Time    = Dash;
            _cs_cmp2Label   = string.Empty;
            _cs_cmp2Time    = Dash;
            _pr_delta1      = Dash;
            _pr_delta2      = Dash;
            _pr_delta1Color = _settings.TextColor;
            _pr_delta2Color = _settings.TextColor;

            IRun         run  = state.Run;
            TimingMethod meth = state.CurrentTimingMethod;
            string       cmp1 = _settings.Comparison1;
            string       cmp2 = _settings.Comparison2;

            switch (_settings.Mode)
            {
                case SplitDetailMode.CurrentSplit:
                    CalcCurrentSplit(state, run, meth, cmp1, cmp2);
                    break;
                case SplitDetailMode.PriorSplit:
                    CalcPriorRange(state, run, meth, cmp1, cmp2, isPriorSubsplit: false);
                    break;
                case SplitDetailMode.PriorSubsplit:
                    CalcPriorRange(state, run, meth, cmp1, cmp2, isPriorSubsplit: true);
                    break;
            }
        }

        // ── Mode 1: Current Split ─────────────────────────────────────────────
        //
        //  Left           │ Middle                  │ Right
        //  ───────────────│─────────────────────────│────────────
        //  Current Split  │ PB:    1:36.55          │
        //                 │ Best:  1:21.35          │ 17:15.84
        //
        private void CalcCurrentSplit(LiveSplitState state, IRun run,
                                       TimingMethod method, string cmp1, string cmp2)
        {
            _labelText    = _settings.LabelCurrentSplit;
            _cs_cmp1Label = AbbreviateComparison(cmp1);
            _cs_cmp2Label = AbbreviateComparison(cmp2);

            bool active = (state.CurrentPhase == TimerPhase.Running ||
                           state.CurrentPhase == TimerPhase.Paused);
            if (!active) return;

            SegmentRange group = GetCurrentGroupRange(state);
            if (!group.IsValid) return;

            TimeSpan? t1 = GetComparisonRangeTime(run, group.Start, group.End, cmp1, method);
            TimeSpan? t2 = GetComparisonRangeTime(run, group.Start, group.End, cmp2, method);

            _cs_cmp1Time = FormatTime(t1, _settings.Accuracy);
            _cs_cmp2Time = FormatTime(t2, _settings.Accuracy);

            TimeSpan? elapsed = GetActiveRangeTime(run, state, group.Start, group.End, method);
            _rightText = FormatTime(elapsed, _settings.Accuracy);
            // Live timer is never gold (run is still in progress)
        }

        // ── Mode 2 & 3: Prev Split / Prev Seg. ───────────────────────────────
        //
        //  Left         │ Middle                │ Right
        //  ─────────────│───────────────────────│────────
        //  Prev Split   │ -1:24  -1:14          │ 22.64
        //  Prev Seg.    │ +4:28  +4:30          │ 4:43.00
        //
        private void CalcPriorRange(LiveSplitState state, IRun run,
                                     TimingMethod method, string cmp1, string cmp2,
                                     bool isPriorSubsplit)
        {
            _labelText = isPriorSubsplit ? _settings.LabelPrevSeg : _settings.LabelPrevSplit;

            if (state.CurrentPhase == TimerPhase.NotRunning)
                return;

            TimeSpan? actual = null, cmp1Time = null, cmp2Time = null;
            int rangeStart = 0, rangeEnd = 0;

            if (isPriorSubsplit)
            {
                int idx = GetPriorSubsplitIndex(state);
                if (idx >= 0)
                {
                    rangeStart = rangeEnd = idx;
                    actual   = GetCompletedRangeTime(run, idx, idx, method);
                    cmp1Time = GetComparisonRangeTime(run, idx, idx, cmp1, method);
                    cmp2Time = GetComparisonRangeTime(run, idx, idx, cmp2, method);
                }
            }
            else
            {
                SegmentRange group = GetPriorGroupRange(state);
                if (group.IsValid)
                {
                    rangeStart = group.Start;
                    rangeEnd   = group.End;
                    actual   = GetCompletedRangeTime(run, group.Start, group.End, method);
                    cmp1Time = GetComparisonRangeTime(run, group.Start, group.End, cmp1, method);
                    cmp2Time = GetComparisonRangeTime(run, group.Start, group.End, cmp2, method);
                }
            }

            // Right side: actual time — always TimeColor, never gold.
            _rightText      = FormatTime(actual, _settings.Accuracy);
            _rightTextColor = _settings.TimeColor;

            TimeSpan? delta1 = (actual.HasValue && cmp1Time.HasValue)
                ? actual.Value - cmp1Time.Value : (TimeSpan?)null;
            TimeSpan? delta2 = (actual.HasValue && cmp2Time.HasValue)
                ? actual.Value - cmp2Time.Value : (TimeSpan?)null;

            _pr_delta1 = FormatDelta(delta1, _settings.Accuracy);
            _pr_delta2 = FormatDelta(delta2, _settings.Accuracy);

            // Gold: if actual time is a new best for this range, both delta colors
            // become gold instead of the usual ahead/behind colors — matching the
            // visual behavior of LiveSplit's Previous Segment component.
            bool gold = IsNewBest(run, rangeStart, rangeEnd, actual, method);
            if (gold)
            {
                Color goldColor   = GetGoldColor(state);
                _pr_delta1Color   = goldColor;
                _pr_delta2Color   = goldColor;
            }
            else
            {
                _pr_delta1Color = DeltaColor(state, delta1);
                _pr_delta2Color = DeltaColor(state, delta2);
            }

            // If only one comparison, suppress delta2
            if (_settings.ComparisonCount == 1)
            {
                _pr_delta2      = string.Empty;
                _pr_delta2Color = _settings.TextColor;
            }
        }

        // =====================================================================
        // DRAWING
        // =====================================================================

        private void DrawRow(Graphics g, LiveSplitState state, float width, float height)
        {
            var ls = state.LayoutSettings;

            DrawBackground(g, ls, width, height);

            Font mainFont = ls.TextFont ?? SystemFonts.DefaultFont;

            _rowHeight = Math.Max(30f, g.MeasureString("Ay", mainFont).Height);
            height = Math.Max(height, _rowHeight);

            Color textColor = _settings.TextColor;
            Color timeColor = _settings.TimeColor;

            float colGap = Math.Max(0f, _settings.ColumnSpacing);

            // ── Label column width — dynamic, matching widest possible label ──
            // Prior modes: reserve the wider of PrevSplit/PrevSeg labels so that
            // their delta blocks align when both are in the layout simultaneously.
            string labelMeasureText;
            if (_settings.Mode == SplitDetailMode.CurrentSplit)
                labelMeasureText = _labelText;
            else
                labelMeasureText = _settings.LabelPrevSplit.Length >= _settings.LabelPrevSeg.Length
                    ? _settings.LabelPrevSplit : _settings.LabelPrevSeg;

            SizeF labelSz  = g.MeasureString(labelMeasureText, mainFont);
            float labelColW = Math.Max(MinLabelColumnWidth, labelSz.Width + 2f);

            // ── Column geometry ───────────────────────────────────────────────
            float xLeft  = OuterPad;
            float xMid   = xLeft + labelColW + colGap;

            float rightColW = Math.Min(
                RightColumnWidth,
                Math.Max(22f, g.MeasureString(_rightText, mainFont).Width + 1f));

            float xRight = width - OuterPad - rightColW;
            float midW   = Math.Max(0f, xRight - xMid - colGap);

            float fontH = g.MeasureString("Ay", mainFont).Height;
            float textY = Math.Max(0f, (height - fontH) / 2f);

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

            // ── Left: mode label ──────────────────────────────────────────────
            DrawTextWithEffects(g, _labelText, mainFont, textColor,
                                new RectangleF(xLeft, textY, labelColW, fontH),
                                fmtLeft, ls);

            // ── Right: time / timer ───────────────────────────────────────────
            DrawTextWithEffects(g, _rightText, mainFont, _rightTextColor,
                                new RectangleF(xRight, textY, rightColW, fontH),
                                fmtRight, ls);

            // ── Middle: mode-dependent ────────────────────────────────────────
            switch (_settings.Mode)
            {
                case SplitDetailMode.CurrentSplit:
                    DrawCurrentSplitMiddle(g, mainFont, textColor, timeColor,
                                           xMid, midW, height, fmtLeft, fmtRight, ls);
                    break;
                case SplitDetailMode.PriorSplit:
                case SplitDetailMode.PriorSubsplit:
                    DrawPriorMiddle(g, mainFont, textColor,
                                    xMid, midW, textY, fontH, fmtLeft, fmtRight, ls, colGap);
                    break;
            }
        }

        // ── Current Split: stacked small comparison lines ─────────────────────
        //
        //   PB:    1:36.55       ← line 1 (cmp1)
        //   Best:  1:21.35       ← line 2 (cmp2, only if ComparisonCount == 2)
        //
        private void DrawCurrentSplitMiddle(Graphics g, Font mainFont,
                                     Color textColor, Color timeColor,
                                     float xMid, float midW, float height,
                                     StringFormat fmtLeft, StringFormat fmtRight,
                                     LiveSplit.Options.LayoutSettings ls)
        {
            float smallPt = Math.Max(MinSmallFontPt, mainFont.Size * SmallFontScale);
            using (var smallFont = new Font(mainFont.FontFamily, smallPt, FontStyle.Regular))
            {
                bool twoLines = (_settings.ComparisonCount == 2);

                float lineH    = smallFont.GetHeight(g);
                float lineStep = lineH * 0.68f;
                float totalH   = twoLines ? (lineH + lineStep) : lineH;
                float y1       = (height - totalH) / 2f;
                float y2       = y1 + lineStep;

                string lbl1 = _cs_cmp1Label + ":";
                string lbl2 = twoLines ? _cs_cmp2Label + ":" : string.Empty;

                float labelSubW = g.MeasureString(lbl1, smallFont).Width;
                if (twoLines)
                    labelSubW = Math.Max(labelSubW, g.MeasureString(lbl2, smallFont).Width);
                labelSubW += 2f;

                float timeSubW = g.MeasureString(_cs_cmp1Time, smallFont).Width;
                if (twoLines)
                    timeSubW = Math.Max(timeSubW, g.MeasureString(_cs_cmp2Time, smallFont).Width);
                timeSubW += 4f;

                if (labelSubW + timeSubW > midW)
                    timeSubW = Math.Max(0f, midW - labelSubW);

                // Line 1: cmp1 label + cmp1 time
                DrawTextWithEffects(g, lbl1, smallFont, timeColor,
                                    new RectangleF(xMid, y1, labelSubW, lineH), fmtLeft, ls);
                DrawTextWithEffectsFit(g, _cs_cmp1Time, smallFont, timeColor,
                                       new RectangleF(xMid + labelSubW, y1, timeSubW, lineH),
                                       fmtRight, ls, MinSmallFontPt);

                // Line 2: cmp2 label + cmp2 time (only if two comparisons)
                if (twoLines)
                {
                    DrawTextWithEffects(g, lbl2, smallFont, timeColor,
                                        new RectangleF(xMid, y2, labelSubW, lineH), fmtLeft, ls);
                    DrawTextWithEffectsFit(g, _cs_cmp2Time, smallFont, timeColor,
                                           new RectangleF(xMid + labelSubW, y2, timeSubW, lineH),
                                           fmtRight, ls, MinSmallFontPt);
                }
            }
        }

        // ── Prior modes: compact delta block ──────────────────────────────────
        //
        //   Without separator:  [delta1]  [delta2]
        //   With separator:     [delta1] [sep] [delta2]
        //
        //   The whole block is right-aligned inside the middle column.
        //   Priority delta gets space first; the other is shortened if needed.
        //   Font size is NEVER changed here.
        //
        private void DrawPriorMiddle(Graphics g, Font font, Color textColor,
                              float xMid, float midW, float textY, float fontH,
                              StringFormat fmtLeft, StringFormat fmtRight,
                              LiveSplit.Options.LayoutSettings ls,
                              float colGap)
        {
            string sep    = _settings.Separator;          // may be empty
            bool   hasSep = !string.IsNullOrEmpty(sep);
            float  spacing = Math.Max(0f, _settings.ColumnSpacing); // user-configurable gap

            // Measure separator if present
            float sepW = hasSep ? g.MeasureString(sep, font).Width + 1f : 0f;

            // Gap between the two deltas (or on each side of separator)
            float deltaGap = hasSep ? Math.Max(1f, spacing) : Math.Max(2f, spacing);

            bool   onlyOne  = (_settings.ComparisonCount == 1 || string.IsNullOrEmpty(_pr_delta2));
            bool   prio2    = (_settings.PriorityDelta == 2);   // true = prioritize delta2

            string d1Text = _pr_delta1;
            string d2Text = onlyOne ? string.Empty : _pr_delta2;

            // Total space: middle width minus separator (and gaps around it)
            float gapTotal = hasSep && !onlyOne ? (sepW + deltaGap * 2f) : (onlyOne ? 0f : deltaGap);
            float available = Math.Max(0f, midW - gapTotal);

            float d1NaturalW = string.IsNullOrEmpty(d1Text)
                ? 0f : g.MeasureString(d1Text, font).Width + 1f;
            float d2NaturalW = string.IsNullOrEmpty(d2Text)
                ? 0f : g.MeasureString(d2Text, font).Width + 1f;

            float d1W, d2W;

            if (onlyOne)
            {
                // Only delta1 (or whichever is non-empty)
                d1W = Math.Min(d1NaturalW, midW);
                d2W = 0f;
            }
            else if (d1NaturalW + d2NaturalW <= available)
            {
                // Best case: both fit
                d1W = d1NaturalW;
                d2W = d2NaturalW;
            }
            else
            {
                // Tight: prioritize the preferred delta
                if (prio2)
                {
                    d2W = Math.Min(d2NaturalW, available);
                    d1W = Math.Max(0f, available - d2W);
                    if (d2NaturalW > available) { d2W = available; d1W = 0f; }
                }
                else
                {
                    d1W = Math.Min(d1NaturalW, available);
                    d2W = Math.Max(0f, available - d1W);
                    if (d1NaturalW > available) { d1W = available; d2W = 0f; }
                }
            }

            // Shorten delta text to fit their allocated widths
            d1Text = ShortenDeltaToFit(g, d1Text, font, d1W);
            d2Text = ShortenDeltaToFit(g, d2Text, font, d2W);

            // Re-measure after shortening for a tight block
            if (string.IsNullOrEmpty(d1Text)) d1W = 0f;
            else d1W = Math.Min(d1W, g.MeasureString(d1Text, font).Width + 1f);

            if (string.IsNullOrEmpty(d2Text)) d2W = 0f;
            else d2W = Math.Min(d2W, g.MeasureString(d2Text, font).Width + 1f);

            // Build the block: [d1] [gap/sep/gap] [d2]
            bool  drawSep     = hasSep && !onlyOne && (d1W > 0f || d2W > 0f);
            float innerGap    = drawSep ? (sepW + deltaGap * 2f) : (d1W > 0f && d2W > 0f ? deltaGap : 0f);
            float blockW      = d1W + innerGap + d2W;

            // Left-anchor the block immediately after the label column.
            // Free space appears between the delta block and the right-side time,
            // not between the label and the deltas.  This keeps the deltas visually
            // stable even as the right-side time width changes during the run.
            float blockX = xMid;

            float d1X   = blockX;
            float sepX  = blockX + d1W + (drawSep ? deltaGap : 0f);
            float d2X   = drawSep ? (sepX + sepW + deltaGap) : (blockX + d1W + deltaGap);

            if (d1W > 0f)
                DrawTextWithEffectsClipped(g, d1Text, font, _pr_delta1Color,
                                           new RectangleF(d1X, textY, d1W, fontH),
                                           fmtRight, ls);

            if (drawSep)
                DrawTextWithEffects(g, sep, font, _pr_delta1Color,
                                    new RectangleF(sepX, textY, sepW, fontH),
                                    fmtLeft, ls);

            if (d2W > 0f)
                DrawTextWithEffectsClipped(g, d2Text, font, _pr_delta2Color,
                                           new RectangleF(d2X, textY, d2W, fontH),
                                           fmtLeft, ls);
        }

        // =====================================================================
        // TEXT RENDERING — do not replace with plain g.DrawString
        // =====================================================================
        //
        // DrawTextWithEffects reads DropShadows / ShadowsColor / TextOutlineColor
        // from layout settings via reflection (for version compatibility) and draws
        // using GraphicsPath.AddString into a huge rectangle to avoid clipping
        // descenders, outlines, or shadows on letters like p/g/y or symbols.
        //
        // DrawTextWithEffectsClipped: sets a clip region for the given rectangle,
        // then calls DrawTextWithEffects.  Use when text must not overflow a column.
        //
        // DrawTextWithEffectsFit: used only in the small PB/Best block, where slight
        // font scaling is acceptable if the label + time block is too wide.

        private static void DrawTextWithEffects(Graphics g, string text, Font font,
                                        Color textColor, RectangleF rect,
                                        StringFormat format,
                                        LiveSplit.Options.LayoutSettings settings)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return;

            bool  hasShadow  = GetLayoutSetting(settings, "DropShadows",      false);
            Color shadowColor = GetLayoutSetting(settings, "ShadowsColor",     Color.Black);
            Color outlineColor= GetLayoutSetting(settings, "TextOutlineColor", Color.Transparent);

            SizeF measured = g.MeasureString(text, font);

            float x = rect.X;
            if (format.Alignment == StringAlignment.Far)
                x = rect.Right - measured.Width;
            else if (format.Alignment == StringAlignment.Center)
                x = rect.X + (rect.Width - measured.Width) / 2f;

            float y = rect.Y;

            using (var nearFormat = new StringFormat())
            {
                nearFormat.Alignment     = StringAlignment.Near;
                nearFormat.LineAlignment = format.LineAlignment;
                nearFormat.Trimming      = StringTrimming.None;
                nearFormat.FormatFlags   = StringFormatFlags.NoWrap;

                if (g.TextRenderingHint == TextRenderingHint.AntiAlias && outlineColor.A > 0)
                {
                    float fontSize = GetFontSize(g, font);

                    using (var path        = new GraphicsPath())
                    using (var outlinePen  = new Pen(outlineColor, GetOutlineSize(fontSize)))
                    using (var textBrush   = new SolidBrush(textColor))
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
            if (string.IsNullOrEmpty(text) || font == null) return;

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
            if (string.IsNullOrEmpty(text) || baseFont == null) return;
            if (rect.Width <= 1f) return;

            Font  drawFont    = baseFont;
            bool  disposeFont = false;
            float availW      = Math.Max(1f, rect.Width - 1f);
            float measuredW   = g.MeasureString(text, drawFont).Width;

            if (measuredW > availW && drawFont.Size > minFontPt)
            {
                float scale   = availW / measuredW;
                float newSize = Math.Max(minFontPt, drawFont.Size * scale);
                drawFont      = new Font(baseFont.FontFamily, newSize, baseFont.Style);
                disposeFont   = true;
            }

            try
            {
                DrawTextWithEffects(g, text, drawFont, textColor, rect, format, settings);
            }
            finally
            {
                if (disposeFont) drawFont.Dispose();
            }
        }

        // ── Reflection helper (version-safe property access) ──────────────────
        private static T GetLayoutSetting<T>(LiveSplit.Options.LayoutSettings settings,
                                             string propertyName, T fallback)
        {
            try
            {
                var prop = settings.GetType().GetProperty(
                    propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) return fallback;
                object value = prop.GetValue(settings, null);
                if (value is T) return (T)value;
            }
            catch { }
            return fallback;
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
                    g.FillRectangle(br, 0, 0, width, height);
            }
        }

        private static float GetFontSize(Graphics g, Font font)
        {
            return font.Unit == GraphicsUnit.Point
                ? font.Size * g.DpiY / 72f
                : font.Size;
        }

        private static float GetOutlineSize(float fontSize)
            => 2.1f + fontSize * 0.055f;

        // =====================================================================
        // HELPERS
        // =====================================================================

        private const string Dash = "-";

        /// <summary>
        /// Formats a positive TimeSpan respecting the chosen accuracy.
        /// Returns Dash for null.
        /// </summary>
        private static string FormatTime(TimeSpan? t, TimeAccuracy accuracy)
        {
            if (t == null) return Dash;
            TimeSpan ts = t.Value;

            string decimals;
            switch (accuracy)
            {
                case TimeAccuracy.Milliseconds:
                    decimals = string.Format(".{0:D3}", ts.Milliseconds);
                    break;
                case TimeAccuracy.Tenths:
                    decimals = string.Format(".{0:D1}", ts.Milliseconds / 100);
                    break;
                case TimeAccuracy.Seconds:
                    decimals = string.Empty;
                    break;
                default: // Hundredths
                    decimals = string.Format(".{0:D2}", ts.Milliseconds / 10);
                    break;
            }

            if (ts.TotalHours >= 1)
                return string.Format("{0}:{1:D2}:{2:D2}{3}",
                    (int)ts.TotalHours, ts.Minutes, ts.Seconds, decimals);
            if (ts.TotalMinutes >= 1)
                return string.Format("{0}:{1:D2}{2}", ts.Minutes, ts.Seconds, decimals);
            return string.Format("{0}{1}", ts.Seconds, decimals);
        }

        /// <summary>
        /// Formats a delta TimeSpan with a leading "+" or "−" sign,
        /// respecting the chosen accuracy.
        ///
        /// Decimals are dropped when the delta is >= 1 minute, to keep
        /// large deltas compact.  This behavior is intentional and useful
        /// for wide splits where +12:34.56 would be cluttered.
        ///
        /// Returns Dash for null.
        /// </summary>
        private static string FormatDelta(TimeSpan? t, TimeAccuracy accuracy)
        {
            if (t == null) return Dash;

            TimeSpan ts   = t.Value;
            string   sign = ts.Ticks >= 0 ? "+" : "−";
            TimeSpan abs  = ts.Duration();

            // For large deltas, decimals waste too much space — drop them.
            string decimals;
            if (abs.TotalMinutes >= 1)
            {
                decimals = string.Empty; // always drop decimals at >=1 min
            }
            else
            {
                switch (accuracy)
                {
                    case TimeAccuracy.Milliseconds:
                        decimals = string.Format(".{0:D3}", abs.Milliseconds);
                        break;
                    case TimeAccuracy.Tenths:
                        decimals = string.Format(".{0:D1}", abs.Milliseconds / 100);
                        break;
                    case TimeAccuracy.Seconds:
                        decimals = string.Empty;
                        break;
                    default: // Hundredths
                        decimals = string.Format(".{0:D2}", abs.Milliseconds / 10);
                        break;
                }
            }

            if (abs.TotalHours >= 1)
                return string.Format("{0}{1}:{2:D2}:{3:D2}",
                    sign, (int)abs.TotalHours, abs.Minutes, abs.Seconds);
            if (abs.TotalMinutes >= 1)
                return string.Format("{0}{1}:{2:D2}",
                    sign, (int)abs.TotalMinutes, abs.Seconds);
            return string.Format("{0}{1}{2}", sign, abs.Seconds, decimals);
        }

        private static string RemoveDeltaSign(string text)
        {
            if (string.IsNullOrEmpty(text) || text == Dash) return text;
            if (text[0] == '+' || text[0] == '−' || text[0] == '-')
                return text.Substring(1);
            return text;
        }

        /// <summary>
        /// Shortens a delta string to fit within maxWidth pixels.
        ///
        /// Shortening strategy (in order):
        ///   1. Return as-is if it fits.
        ///   2. Strip decimals (e.g. "+1:24.56" → "+1:24").
        ///   3. For ≥1 min: use compact minute notation "+1m", "+12m".
        ///   4. For ≥1 hour: use compact hour notation "+1h".
        ///   5. Truncate with ellipsis.
        ///   6. Return empty string.
        ///
        /// Never leaves dangling punctuation like "+1:" or "−2:".
        /// </summary>
        private static string ShortenDeltaToFit(Graphics g, string text, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (maxWidth <= 1f)             return string.Empty;
            if (text == Dash)
                return g.MeasureString(text, font).Width <= maxWidth ? text : string.Empty;

            if (g.MeasureString(text, font).Width <= maxWidth)
                return text;

            // Extract sign and numeric body
            string sign = string.Empty;
            string body = text;
            if (text[0] == '+' || text[0] == '−' || text[0] == '-')
            {
                sign = text.Substring(0, 1);
                body = text.Substring(1);
            }

            // Step 2: strip decimals
            string noDecimals = RemoveDecimalPart(body);
            string candidate  = sign + noDecimals;
            if (!string.IsNullOrEmpty(noDecimals) &&
                g.MeasureString(candidate, font).Width <= maxWidth)
                return candidate;

            // Step 3: compact minute/hour notation.
            // Parse the body to find total minutes / hours.
            // Body formats: "S", "S.ff", "M:SS", "M:SS.ff", "H:MM:SS", ...
            int totalMinutes = 0;
            int totalHours   = 0;
            ParseDeltaBody(body, out totalMinutes, out totalHours);

            if (totalHours >= 1)
            {
                candidate = sign + totalHours + "h";
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }
            else if (totalMinutes >= 1)
            {
                candidate = sign + totalMinutes + "m";
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }

            // Step 4: progressive ellipsis truncation
            for (int len = body.Length - 1; len >= 1; len--)
            {
                candidate = sign + body.Substring(0, len) + "…";
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;

                candidate = sign + body.Substring(0, len);
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }

            // Step 5: just the sign
            if (!string.IsNullOrEmpty(sign) &&
                g.MeasureString(sign, font).Width <= maxWidth)
                return sign;

            return string.Empty;
        }

        private static string RemoveDecimalPart(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int dot = text.IndexOf('.');
            if (dot > 0) return text.Substring(0, dot);
            return text;
        }

        /// <summary>
        /// Parses a delta body string (without sign) to extract total minutes/hours.
        /// Handles formats: "S", "S.ff", "M:SS", "M:SS.ff", "H:MM:SS", etc.
        /// </summary>
        private static void ParseDeltaBody(string body, out int totalMinutes, out int totalHours)
        {
            totalMinutes = 0;
            totalHours   = 0;
            try
            {
                // Strip decimals first
                string clean = RemoveDecimalPart(body);
                string[] parts = clean.Split(':');
                if (parts.Length == 1)
                {
                    // seconds only
                }
                else if (parts.Length == 2)
                {
                    // M:SS
                    int m = int.Parse(parts[0]);
                    totalMinutes = m;
                }
                else if (parts.Length >= 3)
                {
                    // H:MM:SS
                    int h = int.Parse(parts[0]);
                    int m = int.Parse(parts[1]);
                    totalHours   = h;
                    totalMinutes = h * 60 + m;
                }
            }
            catch { }
        }

        private Color DeltaColor(LiveSplitState state, TimeSpan? delta)
        {
            if (delta == null)         return _settings.TextColor;
            if (delta.Value.Ticks > 0) return state.LayoutSettings.BehindLosingTimeColor;
            return state.LayoutSettings.AheadGainingTimeColor;
        }

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
