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
//   These use GraphicsPath rendering to respect LiveSplit shadow/outline
//   settings without clipping descenders (p, g, y, |, etc.).
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
        CurrentSegment,
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

    internal static class SplitDetailLayoutLinks
    {
        private const int MaxGroup = 4;
        private const int ReportLifetimeMs = 1000;

        private static readonly object Sync = new object();
        private static readonly List<WeakReference> Settings = new List<WeakReference>();
        private static readonly Dictionary<int, float> GroupSpacing = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> GroupValueTimeGap = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> GroupLabelRightOffset = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> GroupLabelValueGap = new Dictionary<int, float>();
        private static readonly Dictionary<int, Dictionary<int, MiddleEndReport>> GroupReports =
            new Dictionary<int, Dictionary<int, MiddleEndReport>>();
        private static readonly Dictionary<int, Dictionary<int, MiddleEndReport>> GroupLabelLeftReports =
            new Dictionary<int, Dictionary<int, MiddleEndReport>>();

        private struct MiddleEndReport
        {
            public readonly float Value;
            public readonly int Tick;

            public MiddleEndReport(float value, int tick)
            {
                Value = value;
                Tick = tick;
            }
        }

        public static void Register(SplitDetailSettings settings)
        {
            if (settings == null)
                return;

            lock (Sync)
            {
                CleanupSettingsLocked();

                for (int i = 0; i < Settings.Count; i++)
                {
                    if (ReferenceEquals(Settings[i].Target, settings))
                        return;
                }

                Settings.Add(new WeakReference(settings));
            }
        }

        public static float ResolveColumnSpacing(int group, float localSpacing)
        {
            group = ClampGroup(group);
            if (group == 0)
                return Math.Max(0f, localSpacing);

            lock (Sync)
            {
                float spacing;
                if (GroupSpacing.TryGetValue(group, out spacing))
                    return Math.Max(0f, spacing);
            }

            return Math.Max(0f, localSpacing);
        }

        public static void PublishSpacing(SplitDetailSettings source, int group, float spacing)
        {
            PublishLinkedSetting(GroupSpacing, source, group, spacing,
                                 (settings, value) => settings.ApplyLinkedColumnSpacing(value));
        }

        public static void PublishMiddleValueTimeGap(SplitDetailSettings source, int group, float gap)
        {
            PublishLinkedSetting(GroupValueTimeGap, source, group, gap,
                                 (settings, value) => settings.ApplyLinkedMiddleValueTimeGap(value));
        }

        public static void PublishMiddleLabelRightOffset(SplitDetailSettings source, int group, float offset)
        {
            PublishLinkedSetting(GroupLabelRightOffset, source, group, offset,
                                 (settings, value) => settings.ApplyLinkedMiddleLabelRightOffset(value));
        }

        public static void PublishMiddleLabelValueGap(SplitDetailSettings source, int group, float gap)
        {
            PublishLinkedSetting(GroupLabelValueGap, source, group, gap,
                                 (settings, value) => settings.ApplyLinkedMiddleLabelValueGap(value));
        }

        public static void PublishBoldFonts(SplitDetailSettings source, int group,
                                            bool left, bool middleLabel,
                                            bool middleValue, bool right,
                                            bool enableLinkedRecipients)
        {
            group = ClampGroup(group);
            if (source == null || group == 0)
                return;

            List<SplitDetailSettings> linkedSettings = new List<SplitDetailSettings>();

            lock (Sync)
            {
                CleanupSettingsLocked();

                for (int i = 0; i < Settings.Count; i++)
                {
                    SplitDetailSettings settings = Settings[i].Target as SplitDetailSettings;
                    if (settings == null ||
                        ReferenceEquals(settings, source) ||
                        settings.MiddleColumnLinkGroup != group ||
                        (!enableLinkedRecipients && !settings.LinkBoldFonts))
                    {
                        continue;
                    }

                    linkedSettings.Add(settings);
                }
            }

            for (int i = 0; i < linkedSettings.Count; i++)
                linkedSettings[i].ApplyLinkedBoldFonts(
                    left, middleLabel, middleValue, right, enableLinkedRecipients);
        }

        private static void PublishLinkedSetting(
            Dictionary<int, float> values,
            SplitDetailSettings source,
            int group,
            float value,
            Action<SplitDetailSettings, float> apply)
        {
            group = ClampGroup(group);
            if (source == null || group == 0 || apply == null)
                return;

            value = Math.Max(0f, value);
            List<SplitDetailSettings> linkedSettings = new List<SplitDetailSettings>();

            lock (Sync)
            {
                values[group] = value;
                CleanupSettingsLocked();

                for (int i = 0; i < Settings.Count; i++)
                {
                    SplitDetailSettings settings = Settings[i].Target as SplitDetailSettings;
                    if (settings == null ||
                        ReferenceEquals(settings, source) ||
                        settings.MiddleColumnLinkGroup != group)
                    {
                        continue;
                    }

                    linkedSettings.Add(settings);
                }
            }

            for (int i = 0; i < linkedSettings.Count; i++)
                apply(linkedSettings[i], value);
        }

        public static float ResolveMiddleEnd(int group, int instanceId, float localMidEnd)
        {
            return ResolveLinkedMinimum(GroupReports, group, instanceId, localMidEnd);
        }

        public static float ResolveLabelLeft(int group, int instanceId, float localLabelLeft)
        {
            return ResolveLinkedMinimum(GroupLabelLeftReports, group, instanceId, localLabelLeft);
        }

        private static float ResolveLinkedMinimum(
            Dictionary<int, Dictionary<int, MiddleEndReport>> reportGroups,
            int group,
            int instanceId,
            float localValue)
        {
            group = ClampGroup(group);
            if (group == 0 || instanceId <= 0)
                return localValue;

            lock (Sync)
            {
                Dictionary<int, MiddleEndReport> reports;
                if (!reportGroups.TryGetValue(group, out reports))
                {
                    reports = new Dictionary<int, MiddleEndReport>();
                    reportGroups[group] = reports;
                }

                int now = Environment.TickCount;
                reports[instanceId] = new MiddleEndReport(localValue, now);

                float linkedValue = localValue;
                List<int> stale = null;

                foreach (KeyValuePair<int, MiddleEndReport> report in reports)
                {
                    if (TickAge(now, report.Value.Tick) > ReportLifetimeMs)
                    {
                        if (stale == null)
                            stale = new List<int>();
                        stale.Add(report.Key);
                        continue;
                    }

                    linkedValue = Math.Min(linkedValue, report.Value.Value);
                }

                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                        reports.Remove(stale[i]);
                }

                return linkedValue;
            }
        }

        private static int ClampGroup(int group)
        {
            if (group < 0) return 0;
            if (group > MaxGroup) return MaxGroup;
            return group;
        }

        private static int TickAge(int now, int then)
        {
            unchecked
            {
                return now - then;
            }
        }

        private static void CleanupSettingsLocked()
        {
            for (int i = Settings.Count - 1; i >= 0; i--)
            {
                if (!Settings[i].IsAlive)
                    Settings.RemoveAt(i);
            }
        }
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
        private const float MinMiddleColumnWidth= 28f;
        private const float LabelComparisonPad  = 1f;
        private const float MiddleTextGap       = 3f;
        private const float MiddleRightSafeGap  = 5f;
        private const float SmallFontScale      = 0.50f;
        private const float MinSmallFontPt      = 5f;

        // ── Settings ──────────────────────────────────────────────────────────
        private static int _nextLayoutLinkId;
        private readonly SplitDetailSettings _settings;
        private readonly int _layoutLinkId;

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
        private Color  _backgroundDeltaColor = Color.Transparent;
        private readonly LiveSplitState _state;
        private int _highlightScrollOffset;

        private struct MiddleLayout
        {
            public float ValueRight;
            public float LabelX;
            public float LabelW;
            public float LeftBound;
            public float LabelValueGap;

            public MiddleLayout(float valueRight, float labelX, float labelW,
                                float leftBound)
                : this(valueRight, labelX, labelW, leftBound, MiddleTextGap)
            {
            }

            public MiddleLayout(float valueRight, float labelX, float labelW,
                                float leftBound, float labelValueGap)
            {
                ValueRight = valueRight;
                LabelX = labelX;
                LabelW = labelW;
                LeftBound = leftBound;
                LabelValueGap = Math.Max(0f, labelValueGap);
            }
        }

        // ── Constructor ───────────────────────────────────────────────────────
        public SplitDetailComponent(LiveSplitState state)
        {
            _state = state;
            _layoutLinkId = System.Threading.Interlocked.Increment(ref _nextLayoutLinkId);
            _settings = new SplitDetailSettings(state);
            if (_state != null)
            {
                _state.OnStart += state_OnResetHighlightScroll;
                _state.OnSplit += state_OnResetHighlightScroll;
                _state.OnUndoSplit += state_OnResetHighlightScroll;
                _state.OnSkipSplit += state_OnResetHighlightScroll;
                _state.OnReset += state_OnResetHighlightScroll;
                _state.OnScrollUp += state_OnScrollUp;
                _state.OnScrollDown += state_OnScrollDown;
            }
        }

        // ── IComponent identity ───────────────────────────────────────────────
        // ComponentName is shown in the Layout Editor component list.
        // We include the active mode label so multiple instances are easy to tell apart:
        //   "Split Detail - Current Split"
        //   "Split Detail - Current Seg."
        //   "Split Detail - Prev Split"
        //   "Split Detail - Prev Seg."
        // (or whatever custom labels the user has chosen in Settings)
        // NOTE: ComponentName reflects the configured mode, not temporary live state.
        public string ComponentName
        {
            get
            {
                return "Split Detail - " + _settings.ComponentLabel;
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

        public void Dispose()
        {
            if (_state != null)
            {
                _state.OnStart -= state_OnResetHighlightScroll;
                _state.OnSplit -= state_OnResetHighlightScroll;
                _state.OnUndoSplit -= state_OnResetHighlightScroll;
                _state.OnSkipSplit -= state_OnResetHighlightScroll;
                _state.OnReset -= state_OnResetHighlightScroll;
                _state.OnScrollUp -= state_OnScrollUp;
                _state.OnScrollDown -= state_OnScrollDown;
            }
        }

        private void state_OnResetHighlightScroll(object sender, EventArgs e)
        {
            _highlightScrollOffset = 0;
        }

        private void state_OnResetHighlightScroll(object sender, TimerPhase e)
        {
            _highlightScrollOffset = 0;
        }

        private void state_OnScrollUp(object sender, EventArgs e)
        {
            if (_state == null) return;
            _highlightScrollOffset--;
            ClampHighlightScrollOffset();
        }

        private void state_OnScrollDown(object sender, EventArgs e)
        {
            if (_state == null) return;
            _highlightScrollOffset++;
            ClampHighlightScrollOffset();
        }

        private void ClampHighlightScrollOffset()
        {
            IRun run = _state?.Run;
            if (run == null || run.Count == 0)
            {
                _highlightScrollOffset = 0;
                return;
            }

            int baseIndex = GetBaseHighlightSplitIndex(_state);
            _highlightScrollOffset = Math.Min(
                Math.Max(_highlightScrollOffset, -baseIndex),
                run.Count - baseIndex - 1);
        }

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

        private bool UsesHighlightedSplit(LiveSplitState state)
        {
            return state != null &&
                   (state.CurrentPhase == TimerPhase.Ended ||
                    _highlightScrollOffset != 0);
        }

        private int GetBaseHighlightSplitIndex(LiveSplitState state)
        {
            if (state?.Run == null || state.Run.Count == 0) return -1;
            return Math.Min(Math.Max(state.CurrentSplitIndex, 0), state.Run.Count - 1);
        }

        private int GetHighlightedSplitIndex(LiveSplitState state)
        {
            if (state?.Run == null || state.Run.Count == 0) return -1;

            ClampHighlightScrollOffset();
            return GetBaseHighlightSplitIndex(state) + _highlightScrollOffset;
        }

        private SegmentRange GetPriorGroupRange(LiveSplitState state)
        {
            if (state.CurrentPhase == TimerPhase.NotRunning &&
                !UsesHighlightedSplit(state))
                return SegmentRange.Invalid;

            IRun run = state.Run;

            if (UsesHighlightedSplit(state))
                return GetGroupRange(run, GetHighlightedSplitIndex(state));

            SegmentRange currentGroup = GetCurrentGroupRange(state);
            if (!currentGroup.IsValid || currentGroup.Start <= 0)
                return SegmentRange.Invalid;

            return GetGroupRange(run, currentGroup.Start - 1);
        }

        private int GetPriorSubsplitIndex(LiveSplitState state, TimingMethod method)
        {
            if (state.CurrentPhase == TimerPhase.NotRunning &&
                !UsesHighlightedSplit(state))
            {
                return -1;
            }

            if (UsesHighlightedSplit(state))
                return GetHighlightedSplitIndex(state);

            int prev = state.CurrentSplitIndex - 1;
            if (prev < 0) return -1;

            return GetPriorSubsplitIndexAfterShortFilter(state.Run, prev, method);
        }

        private int GetPriorSubsplitIndexAfterShortFilter(IRun run, int startIndex,
                                                           TimingMethod method)
        {
            if (!_settings.IgnoreShortSubsplits ||
                _settings.IgnoreShortSubsplitSeconds <= 0d ||
                run == null)
            {
                return startIndex;
            }

            TimeSpan threshold = TimeSpan.FromSeconds(_settings.IgnoreShortSubsplitSeconds);
            for (int idx = startIndex; idx >= 0; idx--)
            {
                if (!IsChildSubsplit(run, idx))
                    return idx;

                TimeSpan? actual = GetCompletedRangeTime(run, idx, idx, method);
                if (!actual.HasValue || actual.Value >= threshold)
                    return idx;
            }

            return -1;
        }

        private static bool IsChildSubsplit(IRun run, int index)
        {
            return run != null &&
                   index >= 0 &&
                   index < run.Count &&
                   run[index].Name.StartsWith(SubsplitPrefix);
        }

        private void ApplyItemNameLabel(IRun run, int segmentIndex)
        {
            if (!_settings.UseItemName) return;
            if (run == null || segmentIndex < 0 || segmentIndex >= run.Count) return;
            _labelText = FormatItemNameForDisplay(run[segmentIndex].Name);
        }

        private string FormatItemNameForDisplay(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            string displayName;
            if (_settings.Mode == SplitDetailMode.CurrentSegment ||
                _settings.Mode == SplitDetailMode.PriorSubsplit)
            {
                displayName = ExtractSegmentName(name);
            }
            else
            {
                displayName = ExtractBraceName(name);
            }

            return _settings.AlwaysRemoveLeadingNumbers
                ? RemoveLeadingNumberParts(displayName)
                : displayName;
        }

        private static string ExtractSegmentName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            string segmentName = RemoveLeadingBracePrefix(name);
            segmentName = RemoveLeadingSubsplitPrefix(segmentName);

            return string.IsNullOrEmpty(segmentName) ? ExtractBraceName(name) : segmentName;
        }

        private static string RemoveLeadingBracePrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            if (name[0] != '{') return name;

            int close = name.IndexOf('}');
            if (close <= 0 || close >= name.Length - 1) return name;

            return name.Substring(close + 1).TrimStart();
        }

        private static string RemoveLeadingSubsplitPrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            if (name[0] == '-')
                return name.Substring(1).TrimStart();
            return name;
        }

        private static string ExtractBraceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            if (name[0] != '{') return name;

            int close = name.IndexOf('}');
            if (close <= 1) return name;

            return name.Substring(1, close - 1);
        }

        private static string RemoveLeadingNumberParts(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            string result = name.TrimStart();
            bool removedAny = false;

            while (!string.IsNullOrEmpty(result))
            {
                int tokenEnd = FirstWhitespaceIndex(result);
                if (tokenEnd <= 0)
                    break;

                string token = result.Substring(0, tokenEnd);
                if (!ContainsDigit(token))
                    break;

                result = result.Substring(tokenEnd).TrimStart();
                removedAny = true;
            }

            if (removedAny)
                result = TrimLeadingNameSeparators(result);

            return string.IsNullOrEmpty(result) ? name.Trim() : result;
        }

        private static int FirstWhitespaceIndex(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i;
            }
            return -1;
        }

        private static bool ContainsDigit(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                    return true;
            }
            return false;
        }

        private static string TrimLeadingNameSeparators(string text)
        {
            string result = text.TrimStart();
            while (result.Length > 0 &&
                   (result[0] == '-' || result[0] == ':' || result[0] == '/' ||
                    result[0] == '\\' || result[0] == '|' || result[0] == '.'))
            {
                result = result.Substring(1).TrimStart();
            }
            return result;
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

        /// <summary>
        /// Gets the active elapsed time for the current segment (not yet split).
        /// </summary>
        private TimeSpan? GetActiveSegmentTime(IRun run, LiveSplitState state,
                                                int segmentIndex, TimingMethod method)
        {
            TimeSpan? currentTime = state.CurrentTime[method];
            if (currentTime == null) return null;
            if (segmentIndex == 0) return currentTime;

            TimeSpan? prevSplitTime = run[segmentIndex - 1].SplitTime[method];
            if (prevSplitTime == null) return null;

            return currentTime - prevSplitTime;
        }

        private TimeSpan? GetComparisonRangeTime(IRun run, int start, int end,
                                                  string comparison,
                                                  TimingMethod method)
        {
            if (!HasComparison(run, comparison) ||
                start < 0 || end < start || end >= run.Count)
                return null;

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
        /// Tries several property names via reflection for version compatibility,
        /// then falls back to a standard gold color.
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
        // LIVE MODE DETECTION  — determine when to show current (live) vs prior
        // =====================================================================

        /// <summary>
        /// Determines if the current active split group is losing time compared to
        /// the selected comparison(s).
        /// Returns true if the priority delta is positive (losing time).
        /// 
        /// Use ComparisonCount to decide which comparison to check:
        /// - if ComparisonCount == 1: use Comparison 1 (ignore PriorityDelta)
        /// - if ComparisonCount == 2: use the priority comparison
        /// </summary>
        private bool IsCurrentSplitLosingTime(LiveSplitState state, IRun run,
                                               TimingMethod method, string cmp1, string cmp2)
        {
            SegmentRange group = GetCurrentGroupRange(state);
            if (!group.IsValid) return false;

            TimeSpan? activeTime = GetActiveRangeTime(run, state, group.Start, group.End, method);
            if (!activeTime.HasValue) return false;

            // Determine which comparison to use for live detection
            string comparisonToCheck;
            if (_settings.ComparisonCount == 1)
            {
                // Only one comparison shown: use Comparison 1
                comparisonToCheck = cmp1;
            }
            else
            {
                // Two comparisons shown: use priority delta setting
                comparisonToCheck = (_settings.PriorityDelta == 1) ? cmp1 : cmp2;
            }

            TimeSpan? comparisonTime = GetComparisonRangeTime(run, group.Start, group.End,
                                                               comparisonToCheck, method);
            if (!comparisonTime.HasValue) return false;

            TimeSpan delta = activeTime.Value - comparisonTime.Value;
            return delta.Ticks > 0;  // Positive delta = losing time
        }

        /// <summary>
        /// Determines if the current active segment is losing time compared to
        /// the selected comparison(s).
        /// Returns true if the priority delta is positive (losing time).
        /// 
        /// Use ComparisonCount to decide which comparison to check:
        /// - if ComparisonCount == 1: use Comparison 1 (ignore PriorityDelta)
        /// - if ComparisonCount == 2: use the priority comparison
        /// </summary>
        private bool IsCurrentSegmentLosingTime(LiveSplitState state, IRun run,
                                                 TimingMethod method, string cmp1, string cmp2)
        {
            int currentIdx = state.CurrentSplitIndex;
            if (currentIdx < 0 || currentIdx >= run.Count) return false;

            // Get the active elapsed time for the current segment
            TimeSpan? activeTime = GetActiveSegmentTime(run, state, currentIdx, method);
            if (!activeTime.HasValue) return false;

            // Determine which comparison to use for live detection
            string comparisonToCheck;
            if (_settings.ComparisonCount == 1)
            {
                // Only one comparison shown: use Comparison 1
                comparisonToCheck = cmp1;
            }
            else
            {
                // Two comparisons shown: use priority delta setting
                comparisonToCheck = (_settings.PriorityDelta == 1) ? cmp1 : cmp2;
            }

            // Get the segment-only comparison time (not cumulative)
            TimeSpan? comparisonTime = GetComparisonRangeTime(run, currentIdx, currentIdx,
                                                               comparisonToCheck, method);
            if (!comparisonTime.HasValue) return false;

            TimeSpan delta = activeTime.Value - comparisonTime.Value;
            return delta.Ticks > 0;  // Positive delta = losing time
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
            _backgroundDeltaColor = Color.Transparent;

            IRun         run  = state.Run;
            TimingMethod meth = state.CurrentTimingMethod;
            string       cmp1 = ResolveComparisonChoice(state, run, _settings.Comparison1, "Personal Best");
            string       cmp2 = ResolveComparisonChoice(state, run, _settings.Comparison2, "Best Segments");

            switch (_settings.Mode)
            {
                case SplitDetailMode.CurrentSplit:
                    CalcCurrentSplit(state, run, meth, cmp1, cmp2, isCurrentSubsplit: false);
                    break;
                case SplitDetailMode.CurrentSegment:
                    CalcCurrentSplit(state, run, meth, cmp1, cmp2, isCurrentSubsplit: true);
                    break;
                case SplitDetailMode.PriorSplit:
                    CalcPriorRange(state, run, meth, cmp1, cmp2, isPriorSubsplit: false);
                    break;
                case SplitDetailMode.PriorSubsplit:
                    CalcPriorRange(state, run, meth, cmp1, cmp2, isPriorSubsplit: true);
                    break;
            }
        }

        // ── Current Split / Current Seg. ──────────────────────────────────────
        //
        //  Left           │ Middle                  │ Right
        //  ───────────────│─────────────────────────│────────────
        //  Current Split  │ PB:    1:36.55          │
        //                 │ Best:  1:21.35          │ 17:15.84
        //
        private void SetBackgroundDeltaColor(LiveSplitState state,
                                             IRun run,
                                             int start,
                                             int end,
                                             TimingMethod method,
                                             TimeSpan? actual,
                                             string cmp1,
                                             string cmp2)
        {
            string comparisonName = BackgroundComparisonName(state, run, cmp1);
            if (string.IsNullOrEmpty(comparisonName))
            {
                _backgroundDeltaColor = Color.Transparent;
                return;
            }

            TimeSpan? comparison = GetComparisonRangeTime(run, start, end, comparisonName, method);
            if (!actual.HasValue || !comparison.HasValue)
            {
                _backgroundDeltaColor = Color.Transparent;
                return;
            }

            TimeSpan delta = actual.Value - comparison.Value;
            int comparisonIndex = ComparisonSlot(comparisonName, cmp1, cmp2);
            _backgroundDeltaColor = DeltaColor(state, delta, comparisonIndex);
        }

        private void SetPriorBackgroundDeltaColor(LiveSplitState state,
                                                  IRun run,
                                                  int start,
                                                  int end,
                                                  TimingMethod method,
                                                  TimeSpan? actual,
                                                  string cmp1,
                                                  string cmp2,
                                                  TimeSpan? delta1,
                                                  TimeSpan? delta2,
                                                  bool gold)
        {
            string comparisonName = BackgroundComparisonName(state, run, cmp1);
            if (string.IsNullOrEmpty(comparisonName) || !actual.HasValue)
            {
                _backgroundDeltaColor = Color.Transparent;
                return;
            }

            int comparisonIndex = ComparisonSlot(comparisonName, cmp1, cmp2);
            TimeSpan? backgroundDelta;
            Color backgroundColor;

            if (comparisonIndex == 1)
            {
                backgroundDelta = delta1;
                backgroundColor = DeltaColor(state, backgroundDelta, comparisonIndex);
            }
            else if (comparisonIndex == 2)
            {
                backgroundDelta = delta2;
                backgroundColor = DeltaColor(state, backgroundDelta, comparisonIndex);
            }
            else
            {
                TimeSpan? comparison = GetComparisonRangeTime(run, start, end, comparisonName, method);
                backgroundDelta = comparison.HasValue
                    ? actual.Value - comparison.Value
                    : (TimeSpan?)null;
                backgroundColor = DeltaColor(state, backgroundDelta, comparisonIndex);
            }

            if (!backgroundDelta.HasValue)
            {
                _backgroundDeltaColor = Color.Transparent;
                return;
            }

            _backgroundDeltaColor = gold ? GetGoldColor(state) : backgroundColor;
        }

        private static string BackgroundComparisonName(LiveSplitState state, IRun run, string fallback)
        {
            if (state != null && HasComparison(run, state.CurrentComparison))
                return state.CurrentComparison;

            return HasComparison(run, fallback) ? fallback : string.Empty;
        }

        private static string HighlightComparisonName(LiveSplitState state, IRun run, string fallback)
        {
            if (state != null && HasComparison(run, state.CurrentComparison))
                return state.CurrentComparison;

            return HasComparison(run, fallback) ? fallback : FirstComparisonName(run);
        }

        private static string ResolveComparisonChoice(LiveSplitState state, IRun run,
                                                      string comparison, string fallback)
        {
            string resolved = comparison;
            if (string.Equals(comparison, SplitDetailSettings.CurrentComparisonChoice,
                              StringComparison.Ordinal))
            {
                resolved = state != null ? state.CurrentComparison : null;
            }

            if (HasComparison(run, resolved))
                return resolved;
            if (HasComparison(run, fallback))
                return fallback;
            return FirstComparisonName(run);
        }

        private static bool HasComparison(IRun run, string comparison)
        {
            if (run == null || string.IsNullOrEmpty(comparison))
                return false;

            try
            {
                foreach (string runComparison in run.Comparisons)
                {
                    if (string.Equals(runComparison, comparison, StringComparison.Ordinal))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static string FirstComparisonName(IRun run)
        {
            if (run == null)
                return string.Empty;

            try
            {
                foreach (string comparison in run.Comparisons)
                    return comparison;
            }
            catch { }

            return string.Empty;
        }

        private static int ComparisonSlot(string comparison, string cmp1, string cmp2)
        {
            if (string.Equals(comparison, cmp1, StringComparison.Ordinal))
                return 1;
            if (string.Equals(comparison, cmp2, StringComparison.Ordinal))
                return 2;
            return 0;
        }

        private void CalcCurrentSplit(LiveSplitState state, IRun run,
                                       TimingMethod method, string cmp1, string cmp2,
                                       bool isCurrentSubsplit)
        {
            _labelText    = _settings.LabelForDisplay(live: false);
            _cs_cmp1Label = AbbreviateComparison(cmp1);
            _cs_cmp2Label = AbbreviateComparison(cmp2);

            bool live = (state.CurrentPhase == TimerPhase.Running ||
                         state.CurrentPhase == TimerPhase.Paused);
            bool highlight = UsesHighlightedSplit(state);
            if (!live && !highlight) return;

            bool comparisonHighlight = highlight &&
                                       state.CurrentPhase == TimerPhase.NotRunning;
            bool completedHighlight = highlight && !comparisonHighlight;
            string highlightComparison = comparisonHighlight
                ? HighlightComparisonName(state, run, cmp1)
                : string.Empty;

            int rangeStart;
            int rangeEnd;
            TimeSpan? elapsed;

            if (isCurrentSubsplit)
            {
                int idx = highlight ? GetHighlightedSplitIndex(state) : state.CurrentSplitIndex;
                if (idx < 0 || idx >= run.Count) return;

                rangeStart = idx;
                rangeEnd = idx;
                if (comparisonHighlight)
                    elapsed = GetComparisonRangeTime(run, idx, idx, highlightComparison, method);
                else if (completedHighlight)
                    elapsed = GetCompletedRangeTime(run, idx, idx, method);
                else
                    elapsed = GetActiveSegmentTime(run, state, idx, method);
                ApplyItemNameLabel(run, idx);
            }
            else
            {
                SegmentRange group = highlight
                    ? GetGroupRange(run, GetHighlightedSplitIndex(state))
                    : GetCurrentGroupRange(state);
                if (!group.IsValid) return;

                rangeStart = group.Start;
                rangeEnd = group.End;
                if (comparisonHighlight)
                    elapsed = GetComparisonRangeTime(run, group.Start, group.End,
                                                     highlightComparison, method);
                else if (completedHighlight)
                    elapsed = GetCompletedRangeTime(run, group.Start, group.End, method);
                else
                    elapsed = GetActiveRangeTime(run, state, group.Start, group.End, method);
                ApplyItemNameLabel(run, group.End);
            }

            if (completedHighlight && !elapsed.HasValue)
                return;

            TimeSpan? t1 = GetComparisonRangeTime(run, rangeStart, rangeEnd, cmp1, method);
            TimeSpan? t2 = GetComparisonRangeTime(run, rangeStart, rangeEnd, cmp2, method);

            _cs_cmp1Time = FormatTime(t1, _settings.Accuracy);
            _cs_cmp2Time = FormatTime(t2, _settings.Accuracy);
            SetBackgroundDeltaColor(state, run, rangeStart, rangeEnd, method,
                                    elapsed, cmp1, cmp2);

            _rightText = FormatTime(elapsed, _settings.Accuracy);
            // Live/review timer is never gold.
        }

        // ── Previous / Live Split and Segment modes ──────────────────────────
        //
        //  Left         │ Middle                │ Right
        //  ─────────────│───────────────────────│────────
        //  Prev Split   │ -1:24  -1:14          │ 22.64     (or live split data)
        //  Prev Seg.    │ +4:28  +4:30          │ 4:43.00   (or live segment data)
        //
        private void CalcPriorRange(LiveSplitState state, IRun run,
                                     TimingMethod method, string cmp1, string cmp2,
                                     bool isPriorSubsplit)
        {
            // Determine if we should display live data or prior data
            bool highlight = UsesHighlightedSplit(state);
            bool isLosingTime = false;
            if (!highlight)
            {
                if (isPriorSubsplit)
                    isLosingTime = IsCurrentSegmentLosingTime(state, run, method, cmp1, cmp2);
                else
                    isLosingTime = IsCurrentSplitLosingTime(state, run, method, cmp1, cmp2);
            }

            _cs_cmp1Label = AbbreviateComparison(cmp1);
            _cs_cmp2Label = AbbreviateComparison(cmp2);

            // If item-name labels are enabled, live overrides should show the
            // active split/segment name directly, never the generated Live label.
            _labelText = (_settings.UseItemName && isLosingTime)
                ? string.Empty
                : _settings.LabelForDisplay(isLosingTime);

            if (state.CurrentPhase == TimerPhase.NotRunning && !highlight)
                return;

            TimeSpan? actual = null, cmp1Time = null, cmp2Time = null;
            int rangeStart = 0, rangeEnd = 0;
            bool hasRange = false;

            if (highlight)
            {
                bool comparisonHighlight = state.CurrentPhase == TimerPhase.NotRunning;
                string highlightComparison = comparisonHighlight
                    ? HighlightComparisonName(state, run, cmp1)
                    : string.Empty;

                if (isPriorSubsplit)
                {
                    int idx = GetHighlightedSplitIndex(state);
                    if (idx >= 0 && idx < run.Count)
                    {
                        rangeStart = rangeEnd = idx;
                        hasRange = true;
                        actual = comparisonHighlight
                            ? GetComparisonRangeTime(run, idx, idx, highlightComparison, method)
                            : GetCompletedRangeTime(run, idx, idx, method);
                    }
                }
                else
                {
                    SegmentRange group = GetGroupRange(run, GetHighlightedSplitIndex(state));
                    if (group.IsValid)
                    {
                        rangeStart = group.Start;
                        rangeEnd = group.End;
                        hasRange = true;
                        actual = comparisonHighlight
                            ? GetComparisonRangeTime(run, group.Start, group.End,
                                                     highlightComparison, method)
                            : GetCompletedRangeTime(run, group.Start, group.End, method);
                    }
                }

                if (hasRange && (comparisonHighlight || actual.HasValue))
                {
                    cmp1Time = GetComparisonRangeTime(run, rangeStart, rangeEnd, cmp1, method);
                    cmp2Time = GetComparisonRangeTime(run, rangeStart, rangeEnd, cmp2, method);
                }
            }
            else if (isLosingTime)
            {
                // Live mode: show the current active item
                if (isPriorSubsplit)
                {
                    int idx = state.CurrentSplitIndex;
                    if (idx >= 0 && idx < run.Count)
                    {
                        rangeStart = rangeEnd = idx;
                        hasRange = true;
                        actual = GetActiveSegmentTime(run, state, idx, method);
                        cmp1Time = GetComparisonRangeTime(run, idx, idx, cmp1, method);
                        cmp2Time = GetComparisonRangeTime(run, idx, idx, cmp2, method);
                    }
                }
                else
                {
                    SegmentRange group = GetCurrentGroupRange(state);
                    if (group.IsValid)
                    {
                        rangeStart = group.Start;
                        rangeEnd = group.End;
                        hasRange = true;
                        actual = GetActiveRangeTime(run, state, group.Start, group.End, method);
                        cmp1Time = GetComparisonRangeTime(run, group.Start, group.End, cmp1, method);
                        cmp2Time = GetComparisonRangeTime(run, group.Start, group.End, cmp2, method);
                    }
                }
            }
            else
            {
                // Prior mode: show the prior completed item
                if (isPriorSubsplit)
                {
                    int idx = GetPriorSubsplitIndex(state, method);
                    if (idx >= 0)
                    {
                        rangeStart = rangeEnd = idx;
                        hasRange = true;
                        actual = GetCompletedRangeTime(run, idx, idx, method);
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
                        rangeEnd = group.End;
                        hasRange = true;
                        actual = GetCompletedRangeTime(run, group.Start, group.End, method);
                        cmp1Time = GetComparisonRangeTime(run, group.Start, group.End, cmp1, method);
                        cmp2Time = GetComparisonRangeTime(run, group.Start, group.End, cmp2, method);
                    }
                }
            }

            if (hasRange)
                ApplyItemNameLabel(run, rangeEnd);

            // Right side: actual time — always TimeColor, never gold.
            if (highlight &&
                state.CurrentPhase != TimerPhase.NotRunning &&
                !actual.HasValue)
            {
                return;
            }

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
            // Note: Live mode never displays gold (still in progress).
            bool gold = !isLosingTime &&
                        state.CurrentPhase != TimerPhase.NotRunning &&
                        IsNewBest(run, rangeStart, rangeEnd, actual, method);
            if (gold)
            {
                Color goldColor   = GetGoldColor(state);
                _pr_delta1Color   = goldColor;
                _pr_delta2Color   = goldColor;
            }
            else
            {
                _pr_delta1Color = DeltaColor(state, delta1, 1);
                _pr_delta2Color = DeltaColor(state, delta2, 2);
            }

            // If only one comparison, suppress delta2
            if (_settings.ComparisonCount == 1)
            {
                _pr_delta2      = string.Empty;
                _pr_delta2Color = _settings.TextColor;
            }

            SetPriorBackgroundDeltaColor(state, run, rangeStart, rangeEnd, method,
                                         actual, cmp1, cmp2, delta1, delta2, gold);
        }

        // =====================================================================
        // DRAWING
        // =====================================================================

        private void DrawRow(Graphics g, LiveSplitState state, float width, float height)
        {
            var ls = state.LayoutSettings;

            DrawBackground(g, width, height);

            Font baseFont = ls.TextFont ?? SystemFonts.DefaultFont;
            using (Font leftFont = CreateColumnFont(baseFont, _settings.LeftColumnBold))
            using (Font middleLabelFont = CreateColumnFont(baseFont, _settings.MiddleLabelBold))
            using (Font middleValueFont = CreateColumnFont(baseFont, _settings.MiddleValueBold))
            using (Font rightFont = CreateColumnFont(baseFont, _settings.RightColumnBold))
            {
                float middleLabelFontH = g.MeasureString("Ay", middleLabelFont).Height;
                float middleValueFontH = g.MeasureString("Ay", middleValueFont).Height;
                float middleFontH = Math.Max(middleLabelFontH, middleValueFontH);
                _rowHeight = Math.Max(30f, Math.Max(
                    g.MeasureString("Ay", leftFont).Height,
                    Math.Max(middleFontH, g.MeasureString("Ay", rightFont).Height)));
                height = Math.Max(height, _rowHeight);

                Color textColor = _settings.TextColor;
                Color timeColor = _settings.TimeColor;

                float valueRightOffset = SplitDetailLayoutLinks.ResolveColumnSpacing(
                    _settings.MiddleColumnLinkGroup,
                    _settings.ColumnSpacing);
                bool fixedNameColumns = _settings.UseItemName && !_settings.AutoFitNameColumns;
                float rightTextNaturalW = Math.Max(0f, g.MeasureString(_rightText, rightFont).Width + 1f);
            // ── Column geometry ───────────────────────────────────────────────
                float xLeft  = OuterPad;
                float xRight;
                float rightColW;
                float rightBorder = Math.Max(xLeft, width - OuterPad);
                float rightValueLeft;
                float labelColW;

            if (fixedNameColumns)
            {
                float innerW = Math.Max(0f, width - OuterPad * 2f);
                float colW = innerW / 3f;

                xRight = xLeft + colW * 2f;
                rightColW = Math.Max(0f, colW);
                float rightValueW = Math.Min(rightColW, rightTextNaturalW);
                rightValueLeft = rightBorder - rightValueW;
            }
            else
            {
                rightColW = Math.Min(
                    RightColumnWidth,
                    Math.Max(22f, rightTextNaturalW));

                xRight = rightBorder - rightColW;
                rightValueLeft = rightBorder - rightTextNaturalW;
            }

                float localValueRight = Math.Min(
                    rightBorder - Math.Max(0f, valueRightOffset),
                    rightValueLeft - Math.Max(0f, _settings.MiddleValueTimeGap));
                localValueRight = Math.Max(xLeft, localValueRight);

                float valueRight = SplitDetailLayoutLinks.ResolveMiddleEnd(
                    _settings.MiddleColumnLinkGroup,
                    _layoutLinkId,
                    localValueRight);
                valueRight = Math.Max(xLeft, Math.Min(valueRight, localValueRight));

                MiddleLayout middleLayout = CalculateMiddleLayout(
                    g, middleLabelFont, middleValueFont,
                    fixedNameColumns, xLeft, rightBorder, valueRight);
                labelColW = Math.Max(0f, middleLayout.LeftBound - xLeft - LabelComparisonPad);

                SplitDetailNameShortening shortening = _settings.UseItemName
                    ? _settings.NameShortening
                    : SplitDetailNameShortening.EndEllipsis;
                string labelDrawText = ShortenLabelToFit(g, _labelText, leftFont, labelColW, shortening);

                float leftFontH = g.MeasureString("Ay", leftFont).Height;
                float rightFontH = g.MeasureString("Ay", rightFont).Height;
                float leftTextY = Math.Max(0f, (height - leftFontH) / 2f);
                float middleTextY = Math.Max(0f, (height - middleFontH) / 2f);
                float rightTextY = Math.Max(0f, (height - rightFontH) / 2f);

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
                if (!string.IsNullOrEmpty(labelDrawText) && labelColW > 0f)
                    DrawTextWithEffectsClipped(g, labelDrawText, leftFont, textColor,
                                               new RectangleF(xLeft, leftTextY, labelColW, leftFontH),
                                               fmtLeft, ls);

            // ── Right: time / timer ───────────────────────────────────────────
                if (fixedNameColumns)
                    DrawTextWithEffectsClipped(g, _rightText, rightFont, _rightTextColor,
                                               new RectangleF(xRight, rightTextY, rightColW, rightFontH),
                                               fmtRight, ls);
                else
                    DrawTextWithEffects(g, _rightText, rightFont, _rightTextColor,
                                        new RectangleF(xRight, rightTextY, rightColW, rightFontH),
                                        fmtRight, ls);

            // ── Middle: mode-dependent ────────────────────────────────────────
                switch (_settings.Mode)
                {
                    case SplitDetailMode.CurrentSplit:
                    case SplitDetailMode.CurrentSegment:
                        DrawCurrentSplitMiddle(g, middleLabelFont, middleValueFont, textColor, timeColor,
                                               middleLayout, height,
                                               fmtLeft, fmtRight, ls);
                        break;
                    case SplitDetailMode.PriorSplit:
                    case SplitDetailMode.PriorSubsplit:
                        if (UsePriorStackedMiddle(fixedNameColumns))
                            DrawPriorMiddleStacked(g, middleLabelFont, middleValueFont,
                                                   middleLayout, height,
                                                   middleTextY, middleFontH,
                                                   fixedNameColumns,
                                                   fmtLeft, fmtRight, ls);
                        else
                            DrawPriorMiddle(g, middleValueFont, textColor,
                                            middleLayout, middleTextY, middleFontH,
                                            fmtLeft, fmtRight, ls);
                        break;
                }
            }
        }

        private static Font CreateColumnFont(Font baseFont, bool bold)
        {
            FontStyle style = bold
                ? (baseFont.Style | FontStyle.Bold)
                : (baseFont.Style & ~FontStyle.Bold);

            try
            {
                return new Font(baseFont, style);
            }
            catch (ArgumentException)
            {
                return (Font)baseFont.Clone();
            }
        }

        private bool UsePriorStackedMiddle(bool fixedNameColumns)
        {
            return fixedNameColumns || _settings.ComparisonCount == 1;
        }

        private MiddleLayout CalculateMiddleLayout(Graphics g, Font labelMainFont, Font valueMainFont,
                                                   bool fixedNameColumns,
                                                   float xLeft, float rightBorder,
                                                   float valueRight)
        {
            switch (_settings.Mode)
            {
                case SplitDetailMode.CurrentSplit:
                case SplitDetailMode.CurrentSegment:
                {
                    float labelSmallPt = Math.Max(MinSmallFontPt, labelMainFont.Size * SmallFontScale);
                    float valueSmallPt = Math.Max(MinSmallFontPt, valueMainFont.Size * SmallFontScale);
                    using (var labelSmallFont = new Font(labelMainFont.FontFamily, labelSmallPt, labelMainFont.Style))
                    using (var valueSmallFont = new Font(valueMainFont.FontFamily, valueSmallPt, valueMainFont.Style))
                    {
                        bool twoLines = (_settings.ComparisonCount == 2);
                        return CalculateStackedMiddleLayout(
                            g, labelSmallFont, valueSmallFont,
                            _cs_cmp1Label + ":", _cs_cmp1Time,
                            twoLines ? _cs_cmp2Label + ":" : string.Empty,
                            twoLines ? _cs_cmp2Time : string.Empty,
                            xLeft, rightBorder, valueRight);
                    }
                }

                case SplitDetailMode.PriorSplit:
                case SplitDetailMode.PriorSubsplit:
                    if (UsePriorStackedMiddle(fixedNameColumns))
                    {
                        bool twoLines = (_settings.ComparisonCount == 2 && !string.IsNullOrEmpty(_pr_delta2));
                        if (fixedNameColumns)
                        {
                            float labelSmallPt = Math.Max(MinSmallFontPt, labelMainFont.Size * SmallFontScale);
                            float valueSmallPt = Math.Max(MinSmallFontPt, valueMainFont.Size * SmallFontScale);
                            using (var labelSmallFont = new Font(labelMainFont.FontFamily, labelSmallPt, labelMainFont.Style))
                            using (var valueSmallFont = new Font(valueMainFont.FontFamily, valueSmallPt, valueMainFont.Style))
                            {
                                return CalculateStackedMiddleLayout(
                                    g, labelSmallFont, valueSmallFont,
                                    _cs_cmp1Label + ":", _pr_delta1,
                                    twoLines ? _cs_cmp2Label + ":" : string.Empty,
                                    twoLines ? _pr_delta2 : string.Empty,
                                    xLeft, rightBorder, valueRight);
                            }
                        }

                        float singleLabelSmallPt = Math.Max(MinSmallFontPt, labelMainFont.Size * SmallFontScale);
                        using (var labelSmallFont = new Font(labelMainFont.FontFamily, singleLabelSmallPt, labelMainFont.Style))
                        {
                            return CalculateStackedMiddleLayout(
                                g, labelSmallFont, valueMainFont,
                                _cs_cmp1Label + ":", _pr_delta1,
                                string.Empty, string.Empty,
                                xLeft, rightBorder, valueRight);
                        }
                    }

                    return CalculateCompactMiddleLayout(g, valueMainFont, xLeft, valueRight);

                default:
                    return new MiddleLayout(valueRight, xLeft, 0f, xLeft);
            }
        }

        private MiddleLayout CalculateStackedMiddleLayout(Graphics g, Font labelFont, Font valueFont,
                                                          string label1, string value1,
                                                          string label2, string value2,
                                                          float xLeft,
                                                          float rightBorder,
                                                          float valueRight)
        {
            float labelW = Math.Max(MeasureTextWidth(g, label1, labelFont),
                                    MeasureTextWidth(g, label2, labelFont));
            float valueW = Math.Max(MeasureTextWidth(g, value1, valueFont),
                                    MeasureTextWidth(g, value2, valueFont));

            if (labelW <= 0f)
            {
                float left = Math.Max(xLeft, valueRight - valueW);
                return new MiddleLayout(valueRight, left, 0f, left);
            }

            float labelValueGap = Math.Max(0f, _settings.MiddleLabelValueGap);
            float labelRightLimit = Math.Min(
                rightBorder - Math.Max(0f, _settings.MiddleLabelRightOffset),
                valueRight - valueW - labelValueGap);
            labelRightLimit = Math.Min(labelRightLimit, valueRight - labelValueGap);

            bool linkLabels = _settings.LinkMiddleLabels &&
                              _settings.MiddleColumnLinkGroup > 0;
            float labelLeftLimit = labelRightLimit - labelW;
            float labelX = linkLabels
                ? SplitDetailLayoutLinks.ResolveLabelLeft(
                    _settings.MiddleColumnLinkGroup, _layoutLinkId, labelLeftLimit)
                : labelLeftLimit;
            labelX = Math.Min(labelX, labelLeftLimit);

            labelX = Math.Max(xLeft, labelX);
            float safeLabelW = Math.Max(0f, Math.Min(labelW, valueRight - valueW - labelValueGap - labelX));
            return new MiddleLayout(valueRight, labelX, safeLabelW, labelX, 0f);
        }

        private MiddleLayout CalculateCompactMiddleLayout(Graphics g, Font font,
                                                          float xLeft, float valueRight)
        {
            float blockW = MeasurePriorCompactMiddleWidth(g, font);
            float left = Math.Max(xLeft, valueRight - blockW);
            return new MiddleLayout(valueRight, left, 0f, left);
        }

        private float MeasureMinimumMiddleWidth(Graphics g, Font mainFont, bool fixedNameColumns)
        {
            switch (_settings.Mode)
            {
                case SplitDetailMode.CurrentSplit:
                case SplitDetailMode.CurrentSegment:
                    return MeasureCurrentSplitMiddleWidth(g, mainFont);
                case SplitDetailMode.PriorSplit:
                case SplitDetailMode.PriorSubsplit:
                    return fixedNameColumns
                        ? MeasurePriorStackedMiddleWidth(g, mainFont)
                        : MeasurePriorCompactMiddleWidth(g, mainFont);
                default:
                    return MinMiddleColumnWidth;
            }
        }

        private float MeasureCurrentSplitMiddleWidth(Graphics g, Font mainFont)
        {
            float smallPt = Math.Max(MinSmallFontPt, mainFont.Size * SmallFontScale);
            using (var smallFont = new Font(mainFont.FontFamily, smallPt, mainFont.Style))
            {
                bool twoLines = (_settings.ComparisonCount == 2);
                string lbl1 = _cs_cmp1Label + ":";
                string lbl2 = twoLines ? _cs_cmp2Label + ":" : string.Empty;
                return MeasureLabelValueBlockWidth(g, smallFont,
                    lbl1, _cs_cmp1Time,
                    lbl2, twoLines ? _cs_cmp2Time : string.Empty);
            }
        }

        private float MeasurePriorStackedMiddleWidth(Graphics g, Font mainFont)
        {
            bool twoLines = (_settings.ComparisonCount == 2 && !string.IsNullOrEmpty(_pr_delta2));

            if (!twoLines)
                return MeasureLabelValueBlockWidth(g, mainFont, _cs_cmp1Label + ":", _pr_delta1, string.Empty, string.Empty);

            float smallPt = Math.Max(MinSmallFontPt, mainFont.Size * SmallFontScale);
            using (var smallFont = new Font(mainFont.FontFamily, smallPt, mainFont.Style))
            {
                return MeasureLabelValueBlockWidth(g, smallFont,
                    _cs_cmp1Label + ":", _pr_delta1,
                    _cs_cmp2Label + ":", _pr_delta2);
            }
        }

        private float MeasurePriorCompactMiddleWidth(Graphics g, Font font)
        {
            string sep = _settings.Separator;
            bool hasSep = !string.IsNullOrEmpty(sep);
            bool onlyOne = (_settings.ComparisonCount == 1 || string.IsNullOrEmpty(_pr_delta2));

            float d1W = MeasureTextWidth(g, _pr_delta1, font);
            if (onlyOne)
                return d1W;

            float d2W = MeasureTextWidth(g, _pr_delta2, font);
            if (hasSep)
                return d1W + MeasureTextWidth(g, sep, font) + d2W + 2f;

            return d1W + d2W + 1f;
        }

        private static float MeasureLabelValueBlockWidth(Graphics g, Font font,
                                                         string label1, string value1,
                                                         string label2, string value2)
        {
            float labelW = Math.Max(MeasureTextWidth(g, label1, font), MeasureTextWidth(g, label2, font));
            float valueW = Math.Max(MeasureTextWidth(g, value1, font), MeasureTextWidth(g, value2, font));

            if (labelW <= 0f) return valueW;
            if (valueW <= 0f) return labelW;
            return labelW + MiddleTextGap + valueW;
        }

        private static float MeasureTextWidth(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return g.MeasureString(text, font).Width + 1f;
        }

        private static void DrawLabelValueLine(Graphics g, Font labelFont, Font valueFont,
                                               string label, Color labelColor,
                                               string value, Color valueColor,
                                               float x, float y, float width, float height,
                                               StringFormat fmtLeft, StringFormat fmtRight,
                                               LiveSplit.Options.LayoutSettings ls)
        {
            float labelW = Math.Min(width, MeasureTextWidth(g, label, labelFont));
            var layout = new MiddleLayout(x + width, x, labelW, x);
            DrawLabelValueLine(g, labelFont, valueFont, label, labelColor, value, valueColor,
                               layout, y, height,
                               false, false,
                               fmtLeft, fmtRight, ls);
        }

        private static void DrawLabelValueLine(Graphics g, Font labelFont, Font valueFont,
                                               string label, Color labelColor,
                                               string value, Color valueColor,
                                               MiddleLayout layout,
                                               float y, float height,
                                               bool shortenDelta,
                                               bool fitValue,
                                               StringFormat fmtLeft, StringFormat fmtRight,
                                               LiveSplit.Options.LayoutSettings ls,
                                               bool alignValueByPathRight = false)
        {
            if (layout.ValueRight <= layout.LabelX + 1f)
                return;

            float labelW = layout.LabelW;
            float gap = (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value))
                ? layout.LabelValueGap
                : 0f;
            float labelRight = layout.LabelX + labelW;
            float valueAvailableW = Math.Max(0f, layout.ValueRight - labelRight - gap);
            string drawValue = shortenDelta
                ? ShortenDeltaToFit(g, value, valueFont, valueAvailableW)
                : value;
            float valueW = Math.Min(valueAvailableW, MeasureTextWidth(g, drawValue, valueFont));
            float valueRight = layout.ValueRight;
            if (alignValueByPathRight && !fitValue)
                valueRight += MeasureRightAlignmentAdjustment(g, drawValue, valueFont);
            float valueX = valueRight - valueW;

            if (!string.IsNullOrEmpty(label) && labelW > 0f)
            {
                float labelH = Math.Min(height, labelFont.GetHeight(g));
                float labelY = y + Math.Max(0f, (height - labelH) / 2f);
                DrawTextWithEffectsClipped(g, label, labelFont, labelColor,
                                           new RectangleF(layout.LabelX, labelY, labelW, height),
                                           fmtLeft, ls);
            }

            if (!string.IsNullOrEmpty(drawValue) && valueW > 0f)
            {
                RectangleF valueRect = new RectangleF(valueX, y, valueW, height);
                if (fitValue)
                    DrawTextWithEffectsFit(g, drawValue, valueFont, valueColor, valueRect, fmtRight, ls);
                else
                    DrawTextWithEffectsClipped(g, drawValue, valueFont, valueColor, valueRect, fmtRight, ls);
            }
        }

        private static float MeasureRightAlignmentAdjustment(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return 0f;

            float measuredW = g.MeasureString(text, font).Width;
            float visualRight = MeasureTextPathRight(g, text, font);
            if (visualRight <= 0f)
                return 0f;

            return Math.Max(0f, measuredW - visualRight);
        }

        private static float MeasureTextPathRight(Graphics g, string text, Font font)
        {
            try
            {
                using (var path = new GraphicsPath())
                using (var format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Near;
                    format.Trimming = StringTrimming.None;
                    format.FormatFlags = StringFormatFlags.NoWrap;

                    path.AddString(text, font.FontFamily, (int)font.Style, GetFontSize(g, font),
                        new RectangleF(0f, 0f, 9999f, 9999f), format);
                    return path.GetBounds().Right;
                }
            }
            catch (ArgumentException)
            {
                return 0f;
            }
        }

        // ── Current Split: stacked small comparison lines ─────────────────────
        //
        //   PB:    1:36.55       ← line 1 (cmp1)
        //   Best:  1:21.35       ← line 2 (cmp2, only if ComparisonCount == 2)
        //
        private void DrawCurrentSplitMiddle(Graphics g, Font labelMainFont, Font valueMainFont,
                                     Color textColor, Color timeColor,
                                     MiddleLayout layout, float height,
                                     StringFormat fmtLeft, StringFormat fmtRight,
                                     LiveSplit.Options.LayoutSettings ls)
        {
            float labelSmallPt = Math.Max(MinSmallFontPt, labelMainFont.Size * SmallFontScale);
            float valueSmallPt = Math.Max(MinSmallFontPt, valueMainFont.Size * SmallFontScale);
            using (var labelSmallFont = new Font(labelMainFont.FontFamily, labelSmallPt, labelMainFont.Style))
            using (var valueSmallFont = new Font(valueMainFont.FontFamily, valueSmallPt, valueMainFont.Style))
            {
                bool twoLines = (_settings.ComparisonCount == 2);

                float lineH    = Math.Max(labelSmallFont.GetHeight(g), valueSmallFont.GetHeight(g));
                float lineStep = lineH * 0.68f;
                float totalH   = twoLines ? (lineH + lineStep) : lineH;
                float y1       = (height - totalH) / 2f;
                float y2       = y1 + lineStep;

                string lbl1 = _cs_cmp1Label + ":";
                string lbl2 = twoLines ? _cs_cmp2Label + ":" : string.Empty;

                Color cmp1LabelColor = ComparisonLabelColor(1);
                Color cmp2LabelColor = ComparisonLabelColor(2);
                // Line 1: cmp1 label + cmp1 time
                DrawLabelValueLine(g, labelSmallFont, valueSmallFont,
                                   lbl1, cmp1LabelColor,
                                   _cs_cmp1Time, timeColor,
                                   layout, y1, lineH,
                                   false, true,
                                   fmtLeft, fmtRight, ls);

                // Line 2: cmp2 label + cmp2 time (only if two comparisons)
                if (twoLines)
                {
                    DrawLabelValueLine(g, labelSmallFont, valueSmallFont,
                                       lbl2, cmp2LabelColor,
                                       _cs_cmp2Time, timeColor,
                                       layout, y2, lineH,
                                       false, true,
                                       fmtLeft, fmtRight, ls);
                }
            }
        }

        // ── Prior modes in fixed name columns: stacked comparison deltas ──────
        //
        //   PB:    +1.23
        //   Best:  +1.11
        //
        private void DrawPriorMiddleStacked(Graphics g, Font labelMainFont, Font valueMainFont,
                                     MiddleLayout layout, float height,
                                     float textY, float fontH,
                                     bool smallStyle,
                                     StringFormat fmtLeft, StringFormat fmtRight,
                                     LiveSplit.Options.LayoutSettings ls)
        {
            bool twoLines = (_settings.ComparisonCount == 2 && !string.IsNullOrEmpty(_pr_delta2));

            if (!smallStyle)
            {
                string label = _cs_cmp1Label + ":";
                float singleLabelSmallPt = Math.Max(MinSmallFontPt, labelMainFont.Size * SmallFontScale);
                using (var labelSmallFont = new Font(labelMainFont.FontFamily, singleLabelSmallPt, labelMainFont.Style))
                {
                    float lineH = Math.Max(labelSmallFont.GetHeight(g), valueMainFont.GetHeight(g));
                    float lineY = Math.Max(0f, (height - lineH) / 2f);
                    DrawLabelValueLine(g, labelSmallFont, valueMainFont,
                                       label, ComparisonLabelColor(1),
                                       _pr_delta1, _pr_delta1Color,
                                       layout, lineY, lineH,
                                       true, false,
                                       fmtLeft, fmtRight, ls,
                                       true);
                }
                return;
            }

            float labelSmallPt = Math.Max(MinSmallFontPt, labelMainFont.Size * SmallFontScale);
            float valueSmallPt = Math.Max(MinSmallFontPt, valueMainFont.Size * SmallFontScale);
            using (var labelSmallFont = new Font(labelMainFont.FontFamily, labelSmallPt, labelMainFont.Style))
            using (var valueSmallFont = new Font(valueMainFont.FontFamily, valueSmallPt, valueMainFont.Style))
            {
                float lineH    = Math.Max(labelSmallFont.GetHeight(g), valueSmallFont.GetHeight(g));
                float lineStep = lineH * 0.68f;
                float totalH   = twoLines ? lineH + lineStep : lineH;
                float y1       = (height - totalH) / 2f;
                float y2       = y1 + lineStep;

                string lbl1 = _cs_cmp1Label + ":";

                DrawLabelValueLine(g, labelSmallFont, valueSmallFont,
                                   lbl1, ComparisonLabelColor(1),
                                   _pr_delta1, _pr_delta1Color,
                                   layout, y1, lineH,
                                   true, false,
                                   fmtLeft, fmtRight, ls);

                if (twoLines)
                {
                    string lbl2 = _cs_cmp2Label + ":";
                    DrawLabelValueLine(g, labelSmallFont, valueSmallFont,
                                       lbl2, ComparisonLabelColor(2),
                                       _pr_delta2, _pr_delta2Color,
                                       layout, y2, lineH,
                                       true, false,
                                       fmtLeft, fmtRight, ls);
                }
            }
        }

        // ── Prior modes: compact delta block ──────────────────────────────────
        //
        //   Without separator:  [delta1]  [delta2]
        //   With separator:     [delta1] [sep] [delta2]
        //
        //   The whole block starts after the label/column gap.
        //   Priority delta gets space first; the other is shortened if needed.
        //   Font size is NEVER changed here.
        //
        private void DrawPriorMiddle(Graphics g, Font font, Color textColor,
                              MiddleLayout layout, float textY, float fontH,
                              StringFormat fmtLeft, StringFormat fmtRight,
                              LiveSplit.Options.LayoutSettings ls)
        {
            string sep    = _settings.Separator;          // may be empty
            bool   hasSep = !string.IsNullOrEmpty(sep);

            // Measure separator if present
            float sepW = hasSep ? g.MeasureString(sep, font).Width + 1f : 0f;

            // Fixed internal spacing; ColumnSpacing only moves the block after the label.
            const float DeltaGapNoSeparator = 1f;
            const float SeparatorPad = 1f;
            float deltaGap = hasSep ? SeparatorPad : DeltaGapNoSeparator;

            bool   onlyOne  = (_settings.ComparisonCount == 1 || string.IsNullOrEmpty(_pr_delta2));
            bool   prio2    = (_settings.PriorityDelta == 2);   // true = prioritize delta2

            string d1Text = _pr_delta1;
            string d2Text = onlyOne ? string.Empty : _pr_delta2;

            // The row geometry already keeps the middle column 5px away from
            // the right-side column, so this block can use the full middle width.
            float usableMidW = Math.Max(0f, layout.ValueRight - layout.LeftBound);

            // Total space: usable middle width minus separator/gaps.
            float gapTotal = hasSep && !onlyOne ? (sepW + deltaGap * 2f) : (onlyOne ? 0f : deltaGap);
            float available = Math.Max(0f, usableMidW - gapTotal);

            float d1NaturalW = string.IsNullOrEmpty(d1Text)
                ? 0f : g.MeasureString(d1Text, font).Width + 1f;
            float d2NaturalW = string.IsNullOrEmpty(d2Text)
                ? 0f : g.MeasureString(d2Text, font).Width + 1f;

            float d1W, d2W;

            if (onlyOne)
            {
                // Only delta1 (or whichever is non-empty)
                d1W = Math.Min(d1NaturalW, usableMidW);
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

            float blockX = Math.Max(layout.LeftBound, layout.ValueRight - blockW);

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

        // Fit is only used for the small Current Split/Seg. comparison block.
        private static void DrawTextWithEffectsFit(Graphics g, string text, Font font,
                                           Color textColor, RectangleF rect,
                                           StringFormat format,
                                           LiveSplit.Options.LayoutSettings settings)
        {
            if (string.IsNullOrEmpty(text) || font == null || rect.Width <= 1f)
                return;

            float naturalW = g.MeasureString(text, font).Width + 1f;
            if (naturalW <= rect.Width)
            {
                DrawTextWithEffectsClipped(g, text, font, textColor, rect, format, settings);
                return;
            }

            float fitPt = Math.Max(MinSmallFontPt, font.Size * rect.Width / naturalW);
            using (var fitFont = new Font(font.FontFamily, fitPt, font.Style))
            {
                DrawTextWithEffectsClipped(g, text, fitFont, textColor, rect, format, settings);
            }
        }

        // Reflection helper (version-safe property access).
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
        private void DrawBackground(Graphics g, float width, float height)
        {
            if (!_settings.BackgroundEnabled)
                return;

            if (width <= 0f || height <= 0f)
                return;

            SplitDetailBackgroundMode mode = _settings.BackgroundMode;
            bool deltaMode = IsDeltaBackgroundMode(mode);
            bool plainMode = mode == SplitDetailBackgroundMode.Plain ||
                             mode == SplitDetailBackgroundMode.PlainWithDeltaColor;
            bool horizontal = mode == SplitDetailBackgroundMode.Horizontal ||
                              mode == SplitDetailBackgroundMode.HorizontalWithDeltaColor;

            Color color1 = _settings.BackgroundColor;
            Color color2 = _settings.BackgroundColor2;
            Color color3 = _settings.BackgroundColor3;
            int colorCount = _settings.BackgroundColorCount == 3 ? 3 : 2;

            if (deltaMode)
            {
                if (_backgroundDeltaColor.A <= 0)
                    return;

                Color deltaColor = MakeDeltaBackgroundColor(_backgroundDeltaColor);
                if (plainMode)
                {
                    color1 = Color.FromArgb(_backgroundDeltaColor.A * 7 / 12, deltaColor);
                    color2 = color1;
                    color3 = color1;
                }
                else if (colorCount == 3)
                {
                    color1 = Color.FromArgb(_backgroundDeltaColor.A / 6, deltaColor);
                    color2 = Color.FromArgb(_backgroundDeltaColor.A * 7 / 12, deltaColor);
                    color3 = Color.FromArgb(_backgroundDeltaColor.A, deltaColor);
                }
                else
                {
                    color1 = Color.FromArgb(_backgroundDeltaColor.A / 6, deltaColor);
                    color2 = Color.FromArgb(_backgroundDeltaColor.A, deltaColor);
                }
            }

            if (plainMode)
            {
                if (color1.A > 0)
                {
                    using (var brush = new SolidBrush(color1))
                        FillBackground(g, brush, width, height,
                                       _settings.BackgroundCornerRadius,
                                       _settings.BackgroundCorners);
                }
                return;
            }

            bool hasVisibleColor = color1.A > 0 || color2.A > 0 || (colorCount == 3 && color3.A > 0);
            if (!hasVisibleColor)
                return;

            PointF endPoint = horizontal ? new PointF(width, 0f) : new PointF(0f, height);
            using (var brush = new LinearGradientBrush(new PointF(0f, 0f), endPoint, color1, colorCount == 3 ? color3 : color2))
            {
                if (colorCount == 3)
                {
                    brush.InterpolationColors = new ColorBlend
                    {
                        Positions = new[] { 0f, 0.5f, 1f },
                        Colors = new[] { color1, color2, color3 },
                    };
                }

                FillBackground(g, brush, width, height,
                               _settings.BackgroundCornerRadius,
                               _settings.BackgroundCorners);
            }
        }

        private static void FillBackground(Graphics g, Brush brush, float width, float height,
                                           float radius, SplitDetailBackgroundCorners corners)
        {
            if (radius <= 0f)
            {
                g.FillRectangle(brush, 0, 0, width, height);
                return;
            }

            SmoothingMode oldSmoothing = g.SmoothingMode;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = CreateRoundedRectanglePath(
                    new RectangleF(0f, 0f, width, height), radius, corners))
                {
                    g.FillPath(brush, path);
                }
            }
            finally
            {
                g.SmoothingMode = oldSmoothing;
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius,
                                                               SplitDetailBackgroundCorners corners)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(Math.Min(radius * 2f, rect.Width), rect.Height);
            if (diameter <= 0f)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            bool roundTop = corners == SplitDetailBackgroundCorners.All ||
                            corners == SplitDetailBackgroundCorners.Top;
            bool roundBottom = corners == SplitDetailBackgroundCorners.All ||
                               corners == SplitDetailBackgroundCorners.Bottom;

            path.StartFigure();
            if (roundTop)
            {
                path.AddArc(rect.Left, rect.Top, diameter, diameter, 180f, 90f);
                path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270f, 90f);
            }
            else
            {
                path.AddLine(rect.Left, rect.Top, rect.Right, rect.Top);
            }

            if (roundBottom)
            {
                path.AddLine(rect.Right, roundTop ? rect.Top + diameter : rect.Top,
                             rect.Right, rect.Bottom - diameter);
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
                path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            }
            else
            {
                path.AddLine(rect.Right, roundTop ? rect.Top + diameter : rect.Top,
                             rect.Right, rect.Bottom);
                path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
                path.AddLine(rect.Left, rect.Bottom,
                             rect.Left, roundTop ? rect.Top + diameter : rect.Top);
            }

            path.CloseFigure();
            return path;
        }

        private static bool IsDeltaBackgroundMode(SplitDetailBackgroundMode mode)
        {
            return mode == SplitDetailBackgroundMode.PlainWithDeltaColor ||
                   mode == SplitDetailBackgroundMode.VerticalWithDeltaColor ||
                   mode == SplitDetailBackgroundMode.HorizontalWithDeltaColor;
        }

        private static Color MakeDeltaBackgroundColor(Color deltaColor)
        {
            ToHsv(deltaColor, out double hue, out double saturation, out double value);
            return FromHsv(hue, saturation * 0.5, value * 0.25);
        }

        private static void ToHsv(Color color, out double hue, out double saturation, out double value)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));

            hue = color.GetHue();
            saturation = max == 0 ? 0d : 1d - (1d * min / max);
            value = max / 255d;
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60d)) % 6;
            double f = hue / 60d - Math.Floor(hue / 60d);

            value *= 255d;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1d - saturation));
            int q = Convert.ToInt32(value * (1d - f * saturation));
            int t = Convert.ToInt32(value * (1d - (1d - f) * saturation));

            switch (hi)
            {
                case 0: return Color.FromArgb(255, v, t, p);
                case 1: return Color.FromArgb(255, q, v, p);
                case 2: return Color.FromArgb(255, p, v, t);
                case 3: return Color.FromArgb(255, p, q, v);
                case 4: return Color.FromArgb(255, t, p, v);
                default: return Color.FromArgb(255, v, p, q);
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

        private static string ShortenLabelToFit(Graphics g, string text, Font font,
                                                float maxWidth,
                                                SplitDetailNameShortening shortening)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (maxWidth <= 1f) return string.Empty;
            if (g.MeasureString(text, font).Width <= maxWidth) return text;

            if (shortening == SplitDetailNameShortening.RemoveLeadingParts)
                return ShortenByRemovingLeadingParts(g, text, font, maxWidth);

            return EllipsizeEndToFit(g, text, font, maxWidth);
        }

        private static string ShortenByRemovingLeadingParts(Graphics g, string text,
                                                            Font font, float maxWidth)
        {
            string[] parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
                return EllipsizeEndToFit(g, text, font, maxWidth);

            for (int start = 1; start < parts.Length; start++)
            {
                string candidate = string.Join(" ", parts, start, parts.Length - start);
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }

            return EllipsizeEndToFit(g, parts[parts.Length - 1], font, maxWidth);
        }

        private static string EllipsizeEndToFit(Graphics g, string text, Font font, float maxWidth)
        {
            const string Ellipsis = "...";
            if (g.MeasureString(Ellipsis, font).Width > maxWidth)
                return string.Empty;

            for (int len = text.Length - 1; len >= 1; len--)
            {
                string candidate = text.Substring(0, len) + Ellipsis;
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }

            return Ellipsis;
        }

        /// <summary>
        /// Shortens a delta string to fit within maxWidth pixels.
        ///
        /// Handles two format types differently:
        ///
        /// A) Colon format (minute/hour deltas: "M:SS", "H:MM:SS"):
        ///    - Try full text.
        ///    - Try compact unit notation (sign + hours + "h" or sign + minutes + "m").
        ///    - If neither fits, return empty (never truncate colons).
        ///
        /// B) Decimal/seconds format (no colon: "S", "S.ff"):
        ///    - Try full text.
        ///    - Try without decimals.
        ///    - Try shorter digit sequences.
        ///    - Return empty if only sign would remain.
        ///
        /// Never returns lone signs ("+", "−", "-") or dangling punctuation like "+3:".
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

            // Check if body contains colon (minute/hour format)
            bool hasColon = body.Contains(":");

            if (hasColon)
            {
                // ── Colon format (M:SS or H:MM:SS) ──
                // Parse to get total minutes/hours for compact notation
                int totalMinutes = 0;
                int totalHours = 0;
                ParseDeltaBody(body, out totalMinutes, out totalHours);

                // Try compact unit notation: "Xh" or "Xm"
                string candidate;
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

                // Neither full nor compact form fits → return empty
                // (never return partial forms like "+3:" or "+3:5")
                return string.Empty;
            }
    else
    {
        // ── Decimal/seconds format (S, S.f, S.ff, S.fff) ──
        // Full text was already tested above. Now reduce decimal precision gradually:
        // +57.530 → +57.53 → +57.5 → +57
        // Only after that do we shorten whole digits.
        int dot = body.IndexOf('.');
        string whole = dot > 0 ? body.Substring(0, dot) : body;
        string decimals = dot > 0 ? body.Substring(dot + 1) : string.Empty;

        string candidate;

        if (!string.IsNullOrEmpty(decimals))
        {
            for (int decCount = decimals.Length - 1; decCount >= 1; decCount--)
            {
                candidate = sign + whole + "." + decimals.Substring(0, decCount);
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                    return candidate;
            }
        }

        // Then try whole seconds.
        candidate = sign + whole;
        if (!string.IsNullOrEmpty(whole) &&
            g.MeasureString(candidate, font).Width <= maxWidth)
            return candidate;

        // Last useful fallback: shorten whole digits, but never return sign only.
        for (int len = whole.Length - 1; len >= 1; len--)
        {
            candidate = sign + whole.Substring(0, len);
            if (g.MeasureString(candidate, font).Width <= maxWidth)
                return candidate;
        }

        // No useful form fits → return empty.
        return string.Empty;
    }
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

        private Color DeltaColor(LiveSplitState state, TimeSpan? delta, int comparisonIndex)
        {
            if (delta == null) return _settings.TextColor;

            Color fallback = delta.Value.Ticks > 0
                ? state.LayoutSettings.BehindLosingTimeColor
                : state.LayoutSettings.AheadGainingTimeColor;
            return DeltaOverrideColor(comparisonIndex, fallback);
        }

        private Color ComparisonLabelColor(int comparisonIndex)
        {
            if (comparisonIndex == 1 && _settings.OverrideComparison1Color)
                return _settings.Comparison1Color;
            if (comparisonIndex == 2 && _settings.OverrideComparison2Color)
                return _settings.Comparison2Color;
            return Color.White;
        }

        private Color DeltaOverrideColor(int comparisonIndex, Color fallback)
        {
            if (comparisonIndex == 1 && _settings.OverrideDelta1Color)
                return _settings.Delta1Color;
            if (comparisonIndex == 2 && _settings.OverrideDelta2Color)
                return _settings.Delta2Color;
            return fallback;
        }

        private static string AbbreviateComparison(string comparison)
        {
            switch (comparison)
            {
                case "Best Segments":    return "Best";
                case "Best Pace":        return "Pace";
                case "Personal Best":    return "PB";
                case "Average Segments": return "Avg";
                case "Balanced PB":      return "Bal";
                case "Latest Run":       return "Last";
                default:
                    return comparison.Length > 8
                        ? comparison.Substring(0, 7) + "…"
                        : comparison;
            }
        }
    }
}
