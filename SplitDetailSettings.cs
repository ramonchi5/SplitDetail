// ============================================================================
// SplitDetailSettings.cs
// Settings panel for the SplitDetail component.
//
// All controls built in code — no .Designer.cs / .resx needed.
//
// Color pickers use SettingsHelper.ColorButtonClick (FlatStyle.Popup),
// matching the style used by standard LiveSplit components.
//
// XML compatibility:
//   Version 2 (old): stored "Comparison" (was Comparison2), TextColor, TimeColor
//   Version 3 (new): stores Comparison1, Comparison2, plus all new fields
//   Version 6: adds optional per-comparison delta colors
//   Version 7: stores one active Label plus split/segment-name label options
//   Version 8: item names auto-clean subsplit prefixes and brace labels
//   Version 9: adds fixed name columns, name shortening, and separate delta colors
//   Version 10: adds per-column bold controls
//   Version 11: adds per-instance background colors and gradients
//   Version 12: adds a dynamic Current Comparison choice
//   Version 13: adds background corner radius
//   Version 14: adds background corner scope and leading-number name cleanup
//   Version 15: adds linked middle-column groups
//   Version 16: adds Previous Segment short-subsplit filtering
//   Version 17: anchors middle labels/values from stable right edges
//   Version 18: splits middle label/value bold controls and fixes label linking to start anchors
//   Version 19: compacts middle layout controls and adds linked bold font settings
//   Version 20: updates layout defaults and one-comparison full-size behavior
//   Version 21: adds an explicit background enable setting
//   Version 22: updates new-instance defaults for linked stacked layouts
//   SetSettings reads both old and new field names for backward compatibility.
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components
{
    public enum SplitDetailNameShortening
    {
        EndEllipsis,
        RemoveLeadingParts
    }

    public enum SplitDetailBackgroundMode
    {
        Plain,
        Vertical,
        Horizontal,
        PlainWithDeltaColor,
        VerticalWithDeltaColor,
        HorizontalWithDeltaColor
    }

    public enum SplitDetailBackgroundCorners
    {
        All,
        Top,
        Bottom
    }

    public class SplitDetailSettings : UserControl
    {
        public const string CurrentComparisonChoice = "Current Comparison";

        // =====================================================================
        // Public properties  (read by SplitDetailComponent every tick)
        // =====================================================================

        public SplitDetailMode Mode             { get; private set; } = SplitDetailMode.PriorSubsplit;

        // Two independently configurable comparisons.
        // Comparison1 follows LiveSplit's selected comparison by default.
        // Comparison2 defaults to Best Segments (right delta / bottom line).
        public string Comparison1               { get; private set; } = CurrentComparisonChoice;
        public string Comparison2               { get; private set; } = "Best Segments";

        // 1 = show only Comparison1 delta / line.
        // 2 = show both comparisons (default).
        public int    ComparisonCount           { get; private set; } = 2;

        // Which delta gets space priority when the middle column is tight.
        // 1 = prioritize Comparison1 delta.
        // 2 = prioritize Comparison2 delta (default — usually the Best/highlight one).
        public int    PriorityDelta             { get; private set; } = 2;

        // One active label per component instance. Prior modes derive the temporary
        // Live label from this value so the settings panel only shows what matters.
        public string Label                     { get; private set; } = "Prev Seg.";
        public bool   UseItemName               { get; private set; } = true;
        public bool   AutoFitNameColumns        { get; private set; } = false;
        public bool   AlwaysRemoveLeadingNumbers { get; private set; } = true;
        public SplitDetailNameShortening NameShortening { get; private set; } = SplitDetailNameShortening.RemoveLeadingParts;
        public bool   IgnoreShortSubsplits      { get; private set; } = false;
        public double IgnoreShortSubsplitSeconds { get; private set; } = 3d;

        // Empty string = no separator (default, compact layout).
        // Non-empty    = drawn between the two deltas with fixed compact padding.
        public string Separator                 { get; private set; } = string.Empty;

        // Distance from the row's right edge to the right edge of the middle
        // comparison/delta values. The actual right-side timer can still push
        // this left to preserve MiddleValueTimeGap.
        public float  ColumnSpacing             { get; private set; } = 100f;
        public int    MiddleColumnLinkGroup     { get; private set; } = 1;
        public float  MiddleValueTimeGap        { get; private set; } = 5f;
        public float  MiddleLabelRightOffset    { get; private set; } = 100f;
        public float  MiddleLabelValueGap       { get; private set; } = 5f;
        public bool   LinkMiddleLabels          { get; private set; } = true;
        public bool   LinkBoldFonts             { get; private set; } = true;
        public bool   BackgroundEnabled         { get; private set; } = false;
        public Color  BackgroundColor           { get; private set; } = Color.Transparent;
        public Color  BackgroundColor2          { get; private set; } = Color.Transparent;
        public Color  BackgroundColor3          { get; private set; } = Color.Transparent;
        public int    BackgroundColorCount      { get; private set; } = 2;
        public SplitDetailBackgroundMode BackgroundMode { get; private set; } = SplitDetailBackgroundMode.Plain;
        public float  BackgroundCornerRadius    { get; private set; } = 0f;
        public SplitDetailBackgroundCorners BackgroundCorners { get; private set; } = SplitDetailBackgroundCorners.All;

        // Decimal accuracy for displayed times and deltas.
        public TimeAccuracy Accuracy            { get; private set; } = TimeAccuracy.Hundredths;

        // Colors.
        public Color  TextColor                 { get; private set; } = Color.White;
        public Color  TimeColor                 { get; private set; } = Color.White;
        public bool   OverrideComparison1Color  { get; private set; } = false;
        public Color  Comparison1Color          { get; private set; } = Color.White;
        public bool   OverrideComparison2Color  { get; private set; } = false;
        public Color  Comparison2Color          { get; private set; } = Color.White;
        public bool   OverrideDelta1Color       { get; private set; } = false;
        public Color  Delta1Color               { get; private set; } = Color.White;
        public bool   OverrideDelta2Color       { get; private set; } = false;
        public Color  Delta2Color               { get; private set; } = Color.White;
        public bool   LeftColumnBold            { get; private set; } = false;
        public bool   MiddleLabelBold           { get; private set; } = false;
        public bool   MiddleValueBold           { get; private set; } = true;
        public bool   RightColumnBold           { get; private set; } = false;

        // =====================================================================
        // Private controls
        // =====================================================================
        private readonly LiveSplitState _state;

        private ComboBox      _modeCombo;
        private ComboBox      _cmp1Combo;
        private ComboBox      _cmp2Combo;
        private ComboBox      _cmpCountCombo;
        private ComboBox      _priorityCombo;
        private TextBox       _labelBox;
        private CheckBox      _useItemNameChk;
        private CheckBox      _autoFitNameColumnsChk;
        private CheckBox      _alwaysRemoveLeadingNumbersChk;
        private ComboBox      _nameShorteningCombo;
        private Label         _ignoreShortSubsplitsLbl;
        private FlowLayoutPanel _ignoreShortSubsplitsRow;
        private CheckBox      _ignoreShortSubsplitsChk;
        private TextBox       _ignoreShortSubsplitsBox;
        private TextBox       _sepBox;
        private NumericUpDown _spacingNum;
        private ComboBox      _middleColumnLinkCombo;
        private NumericUpDown _middleValueTimeGapNum;
        private NumericUpDown _middleLabelRightOffsetNum;
        private NumericUpDown _middleLabelValueGapNum;
        private CheckBox      _linkMiddleLabelsChk;
        private CheckBox      _linkBoldFontsChk;
        private CheckBox      _backgroundEnabledChk;
        private Button        _backgroundColorBtn;
        private Button        _backgroundColor2Btn;
        private Button        _backgroundColor3Btn;
        private ComboBox      _backgroundColorCountCombo;
        private ComboBox      _backgroundModeCombo;
        private NumericUpDown _backgroundCornerRadiusNum;
        private ComboBox      _backgroundCornersCombo;
        private RadioButton   _rdoHundredths, _rdoTenths, _rdoSeconds, _rdoMilliseconds;
        private Button        _textColorBtn;
        private Button        _timeColorBtn;
        private CheckBox      _comparison1ColorChk;
        private Button        _comparison1ColorBtn;
        private CheckBox      _comparison2ColorChk;
        private Button        _comparison2ColorBtn;
        private CheckBox      _delta1ColorChk;
        private Button        _delta1ColorBtn;
        private CheckBox      _delta2ColorChk;
        private Button        _delta2ColorBtn;
        private CheckBox      _leftColumnBoldChk;
        private CheckBox      _middleLabelBoldChk;
        private CheckBox      _middleValueBoldChk;
        private CheckBox      _rightColumnBoldChk;
        private bool          _syncingLinkedLayoutValue;
        private bool          _syncingLinkedBoldFonts;
        private bool          _loadingSettings;

        // ── Constructor ───────────────────────────────────────────────────────
        public SplitDetailSettings(LiveSplitState state)
        {
            _state = state;
            BuildUI();
            SplitDetailLayoutLinks.Register(this);
        }

        // =====================================================================
        // UI construction
        // =====================================================================
        private void BuildUI()
        {
            SuspendLayout();
            Controls.Clear();

            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            Dock = DockStyle.Fill;
            Padding = new Padding(7);
            Size = new Size(476, 520);

            // Outer flow: sections stack top-to-bottom
            var flow = new FlowLayoutPanel
            {
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                Dock          = DockStyle.Top,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            flow.Controls.Add(MakeSection("Mode & Labels",    BuildModeLabelSection()));
            flow.Controls.Add(MakeSection("Comparisons",      BuildComparisonSection()));
            flow.Controls.Add(MakeSection("Layout",           BuildLayoutSection()));
            flow.Controls.Add(MakeSection("Accuracy",         BuildAccuracySection()));
            flow.Controls.Add(MakeSection("Colors",           BuildColorSection()));
            flow.Controls.Add(MakeSection("Font",             BuildFontSection()));

            Controls.Add(flow);
            ResumeLayout(false);
            PerformLayout();
        }

        // ── Section builder ───────────────────────────────────────────────────
        private static GroupBox MakeSection(string title, Control content)
        {
            int sectionWidth = 440;
            int contentWidth = sectionWidth - 18;
            Size preferred = content.GetPreferredSize(new Size(contentWidth, 0));
            content.Location = new Point(8, 19);
            content.Size = new Size(contentWidth, preferred.Height);

            var gb = new GroupBox
            {
                Text     = title,
                Margin   = new Padding(0, 0, 0, 6),
                Padding  = new Padding(6),
                Size     = new Size(sectionWidth, Math.Max(48, preferred.Height + 30)),
            };
            gb.Controls.Add(content);
            return gb;
        }

        private static TableLayoutPanel MakeGrid(int rows)
        {
            var t = new TableLayoutPanel
            {
                AutoSize    = true,
                ColumnCount = 2,
                RowCount    = rows,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100f));
            return t;
        }

        // ── Mode & Labels ─────────────────────────────────────────────────────
        private Control BuildModeLabelSection()
        {
            var t = MakeGrid(5);

            // Mode
            t.Controls.Add(MakeLbl("Mode:"), 0, 0);
            _modeCombo = MakeCombo("Current Split", "Current Segment", "Previous Split", "Previous Segment");
            _modeCombo.SelectedIndex = (int)Mode;
            _modeCombo.SelectedIndexChanged += (s, e) =>
            {
                Mode = (SplitDetailMode)_modeCombo.SelectedIndex;
                Label = DefaultLabelForMode(Mode);
                _labelBox.Text = Label;
                UpdateModeLabelControls();
            };
            t.Controls.Add(_modeCombo, 1, 0);

            t.Controls.Add(MakeLbl("Label:"), 0, 1);
            _labelBox = MakeTextBox(Label, 40);
            _labelBox.TextChanged += (s, e) => Label = _labelBox.Text;
            t.Controls.Add(_labelBox, 1, 1);

            var options = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            _useItemNameChk = MakeCompactCheck(string.Empty, UseItemName);
            _useItemNameChk.CheckedChanged += (s, e) =>
            {
                UseItemName = _useItemNameChk.Checked;
                UpdateModeLabelControls();
            };
            options.Controls.Add(_useItemNameChk);

            _autoFitNameColumnsChk = MakeCompactCheck("Full size comparison/delta", AutoFitNameColumns);
            _autoFitNameColumnsChk.CheckedChanged += (s, e) =>
            {
                AutoFitNameColumns = _autoFitNameColumnsChk.Checked;
                UpdateModeLabelControls();
            };
            options.Controls.Add(_autoFitNameColumnsChk);

            options.Controls.Add(new Label
            {
                Text      = "Sep:",
                AutoSize  = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin    = new Padding(2, 5, 2, 0),
            });
            _sepBox = new TextBox
            {
                Text      = Separator,
                MaxLength = 5,
                Width     = 45,
                Margin    = new Padding(0, 2, 0, 0),
            };
            _sepBox.TextChanged += (s, e) => Separator = _sepBox.Text;
            options.Controls.Add(_sepBox);

            t.SetColumnSpan(options, 2);
            t.Controls.Add(options, 0, 2);

            t.Controls.Add(MakeLbl("Abreviation method:"), 0, 3);
            var shorteningRow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            _nameShorteningCombo = MakeCombo("End ellipsis", "Remove leading parts");
            _nameShorteningCombo.Width = 152;
            _nameShorteningCombo.DropDownWidth = 170;
            _nameShorteningCombo.SelectedIndex = (int)NameShortening;
            _nameShorteningCombo.SelectedIndexChanged += (s, e) =>
                NameShortening = (SplitDetailNameShortening)_nameShorteningCombo.SelectedIndex;
            shorteningRow.Controls.Add(_nameShorteningCombo);

            _alwaysRemoveLeadingNumbersChk =
                MakeCompactCheck("Always remove leading numbers", AlwaysRemoveLeadingNumbers);
            _alwaysRemoveLeadingNumbersChk.CheckedChanged += (s, e) =>
                AlwaysRemoveLeadingNumbers = _alwaysRemoveLeadingNumbersChk.Checked;
            shorteningRow.Controls.Add(_alwaysRemoveLeadingNumbersChk);

            t.Controls.Add(shorteningRow, 1, 3);

            _ignoreShortSubsplitsLbl = MakeLbl("Prev Seg. filter:");
            t.Controls.Add(_ignoreShortSubsplitsLbl, 0, 4);
            _ignoreShortSubsplitsRow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            _ignoreShortSubsplitsChk =
                MakeCompactCheck("Ignore short subsplits", IgnoreShortSubsplits);
            _ignoreShortSubsplitsChk.CheckedChanged += (s, e) =>
            {
                IgnoreShortSubsplits = _ignoreShortSubsplitsChk.Checked;
                UpdateModeLabelControls();
            };
            _ignoreShortSubsplitsRow.Controls.Add(_ignoreShortSubsplitsChk);

            _ignoreShortSubsplitsRow.Controls.Add(MakeInlineLbl("Under:"));
            _ignoreShortSubsplitsBox = new TextBox
            {
                Text   = FormatSeconds(IgnoreShortSubsplitSeconds),
                Width  = 54,
                Margin = new Padding(0, 2, 2, 0),
            };
            _ignoreShortSubsplitsBox.TextChanged += (s, e) =>
            {
                double seconds;
                if (TryParseSeconds(_ignoreShortSubsplitsBox.Text, out seconds))
                    IgnoreShortSubsplitSeconds = Math.Max(0d, seconds);
            };
            _ignoreShortSubsplitsRow.Controls.Add(_ignoreShortSubsplitsBox);
            _ignoreShortSubsplitsRow.Controls.Add(MakeInlineLbl("sec"));

            t.Controls.Add(_ignoreShortSubsplitsRow, 1, 4);

            UpdateModeLabelControls();

            return t;
        }

        // ── Comparisons ───────────────────────────────────────────────────────
        private Control BuildComparisonSection()
        {
            var t = new TableLayoutPanel
            {
                AutoSize    = true,
                ColumnCount = 4,
                RowCount    = 2,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));

            // Comparison 1
            t.Controls.Add(MakeLbl("Comparison 1:"), 0, 0);
            _cmp1Combo = MakeNarrowCombo();
            _cmp1Combo.SelectedIndexChanged += (s, e) =>
            {
                if (_cmp1Combo.SelectedItem != null)
                    Comparison1 = _cmp1Combo.SelectedItem.ToString();
            };
            t.Controls.Add(_cmp1Combo, 1, 0);

            // Comparison 2
            t.Controls.Add(MakeLbl("Comparison 2:"), 2, 0);
            _cmp2Combo = MakeNarrowCombo();
            _cmp2Combo.SelectedIndexChanged += (s, e) =>
            {
                if (_cmp2Combo.SelectedItem != null)
                    Comparison2 = _cmp2Combo.SelectedItem.ToString();
            };
            t.Controls.Add(_cmp2Combo, 3, 0);

            // How many comparisons to show
            t.Controls.Add(MakeLbl("Show:"), 0, 1);
            _cmpCountCombo = MakeNarrowCombo("1 (Comparison 1 only)", "2 (both)");
            _cmpCountCombo.SelectedIndex = ComparisonCount - 1;
            _cmpCountCombo.SelectedIndexChanged += (s, e) =>
                ComparisonCount = _cmpCountCombo.SelectedIndex + 1;
            t.Controls.Add(_cmpCountCombo, 1, 1);

            // Priority delta
            t.Controls.Add(MakeLbl("Priority:"), 2, 1);
            _priorityCombo = MakeNarrowCombo("Comparison 1", "Comparison 2");
            _priorityCombo.SelectedIndex = PriorityDelta - 1;
            _priorityCombo.SelectedIndexChanged += (s, e) =>
                PriorityDelta = _priorityCombo.SelectedIndex + 1;
            t.Controls.Add(_priorityCombo, 3, 1);

            return t;
        }

        // ── Layout ────────────────────────────────────────────────────────────
        private Control BuildLayoutSection()
        {
            var t = new TableLayoutPanel
            {
                AutoSize    = true,
                ColumnCount = 4,
                RowCount    = 5,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));

            t.Controls.Add(MakeLbl("Move Values:"), 0, 0);
            _spacingNum = new NumericUpDown
            {
                Minimum       = 0,
                Maximum       = 9999,
                DecimalPlaces = 0,
                Value         = (decimal)ColumnSpacing,
                Width         = 58,
            };
            _spacingNum.ValueChanged += (s, e) =>
            {
                ColumnSpacing = (float)_spacingNum.Value;
                if (!_syncingLinkedLayoutValue)
                    SplitDetailLayoutLinks.PublishSpacing(this, MiddleColumnLinkGroup, ColumnSpacing);
            };
            t.Controls.Add(_spacingNum, 1, 0);

            t.Controls.Add(MakeLbl("Min Padding:"), 2, 0);
            _middleValueTimeGapNum = MakeLayoutNumber(MiddleValueTimeGap);
            _middleValueTimeGapNum.ValueChanged += (s, e) =>
            {
                MiddleValueTimeGap = (float)_middleValueTimeGapNum.Value;
                if (!_syncingLinkedLayoutValue)
                    SplitDetailLayoutLinks.PublishMiddleValueTimeGap(
                        this, MiddleColumnLinkGroup, MiddleValueTimeGap);
            };
            t.Controls.Add(_middleValueTimeGapNum, 3, 0);

            t.Controls.Add(MakeLbl("Move Labels:"), 0, 1);
            _middleLabelRightOffsetNum = MakeLayoutNumber(MiddleLabelRightOffset);
            _middleLabelRightOffsetNum.ValueChanged += (s, e) =>
            {
                MiddleLabelRightOffset = (float)_middleLabelRightOffsetNum.Value;
                if (!_syncingLinkedLayoutValue)
                    SplitDetailLayoutLinks.PublishMiddleLabelRightOffset(
                        this, MiddleColumnLinkGroup, MiddleLabelRightOffset);
            };
            t.Controls.Add(_middleLabelRightOffsetNum, 1, 1);

            t.Controls.Add(MakeLbl("Min Padding:"), 2, 1);
            _middleLabelValueGapNum = MakeLayoutNumber(MiddleLabelValueGap);
            _middleLabelValueGapNum.ValueChanged += (s, e) =>
            {
                MiddleLabelValueGap = (float)_middleLabelValueGapNum.Value;
                if (!_syncingLinkedLayoutValue)
                    SplitDetailLayoutLinks.PublishMiddleLabelValueGap(
                        this, MiddleColumnLinkGroup, MiddleLabelValueGap);
            };
            t.Controls.Add(_middleLabelValueGapNum, 3, 1);

            t.Controls.Add(MakeLbl("Link:"), 0, 2);
            var linkRow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            _middleColumnLinkCombo = MakeCombo("Unlinked", "Link 1", "Link 2", "Link 3", "Link 4");
            _middleColumnLinkCombo.Width = 82;
            _middleColumnLinkCombo.DropDownWidth = 100;
            _middleColumnLinkCombo.SelectedIndex = MiddleColumnLinkGroup;
            _middleColumnLinkCombo.SelectedIndexChanged += (s, e) =>
            {
                MiddleColumnLinkGroup = _middleColumnLinkCombo.SelectedIndex;
                PublishLinkedLayoutValues();
                PublishLinkedBoldFonts(false);
            };
            linkRow.Controls.Add(_middleColumnLinkCombo);

            _linkMiddleLabelsChk = MakeCompactCheck("Also link labels", LinkMiddleLabels);
            _linkMiddleLabelsChk.CheckedChanged += (s, e) =>
                LinkMiddleLabels = _linkMiddleLabelsChk.Checked;
            linkRow.Controls.Add(_linkMiddleLabelsChk);

            _linkBoldFontsChk = MakeCompactCheck("Also link bold fonts", LinkBoldFonts);
            _linkBoldFontsChk.CheckedChanged += (s, e) =>
            {
                LinkBoldFonts = _linkBoldFontsChk.Checked;
                if (LinkBoldFonts)
                    PublishLinkedBoldFonts(true);
            };
            linkRow.Controls.Add(_linkBoldFontsChk);

            t.SetColumnSpan(linkRow, 3);
            t.Controls.Add(linkRow, 1, 2);

            t.Controls.Add(MakeLbl("Background Color:"), 0, 3);
            var backgroundRow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            _backgroundEnabledChk = MakeCompactCheck("Enable", BackgroundEnabled);
            _backgroundEnabledChk.CheckedChanged += (s, e) =>
            {
                BackgroundEnabled = _backgroundEnabledChk.Checked;
                UpdateBackgroundControlStates();
            };
            backgroundRow.Controls.Add(_backgroundEnabledChk);

            _backgroundColorBtn = MakeTightColorBtn(BackgroundColor);
            _backgroundColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_backgroundColorBtn, this);
                BackgroundColor = _backgroundColorBtn.BackColor;
            };
            backgroundRow.Controls.Add(_backgroundColorBtn);

            _backgroundColor2Btn = MakeTightColorBtn(BackgroundColor2);
            _backgroundColor2Btn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_backgroundColor2Btn, this);
                BackgroundColor2 = _backgroundColor2Btn.BackColor;
            };
            backgroundRow.Controls.Add(_backgroundColor2Btn);

            _backgroundColor3Btn = MakeTightColorBtn(BackgroundColor3);
            _backgroundColor3Btn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_backgroundColor3Btn, this);
                BackgroundColor3 = _backgroundColor3Btn.BackColor;
            };
            backgroundRow.Controls.Add(_backgroundColor3Btn);

            _backgroundColorCountCombo = MakeCombo("2 colors", "3 colors");
            _backgroundColorCountCombo.Width = 58;
            _backgroundColorCountCombo.SelectedIndex = BackgroundColorCount == 3 ? 1 : 0;
            _backgroundColorCountCombo.SelectedIndexChanged += (s, e) =>
            {
                BackgroundColorCount = _backgroundColorCountCombo.SelectedIndex == 1 ? 3 : 2;
                UpdateBackgroundControlStates();
            };
            backgroundRow.Controls.Add(_backgroundColorCountCombo);

            _backgroundModeCombo = MakeCombo(
                "Plain",
                "Vertical",
                "Horizontal",
                "Plain With Delta Color",
                "Vertical With Delta Color",
                "Horizontal With Delta Color");
            _backgroundModeCombo.Width = 145;
            _backgroundModeCombo.DropDownWidth = 190;
            _backgroundModeCombo.SelectedIndex = (int)BackgroundMode;
            _backgroundModeCombo.SelectedIndexChanged += (s, e) =>
            {
                BackgroundMode = (SplitDetailBackgroundMode)_backgroundModeCombo.SelectedIndex;
                UpdateBackgroundControlStates();
            };
            backgroundRow.Controls.Add(_backgroundModeCombo);

            t.SetColumnSpan(backgroundRow, 3);
            t.Controls.Add(backgroundRow, 1, 3);
            UpdateBackgroundControlStates();

            t.Controls.Add(MakeLbl("Radius:"), 0, 4);
            var radiusRow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            _backgroundCornerRadiusNum = new NumericUpDown
            {
                Minimum       = 0,
                Maximum       = 200,
                DecimalPlaces = 0,
                Value         = (decimal)BackgroundCornerRadius,
                Width         = 54,
            };
            _backgroundCornerRadiusNum.ValueChanged += (s, e) =>
                BackgroundCornerRadius = (float)_backgroundCornerRadiusNum.Value;
            radiusRow.Controls.Add(_backgroundCornerRadiusNum);

            _backgroundCornersCombo = MakeCombo("All corners", "Top corners", "Bottom corners");
            _backgroundCornersCombo.Width = 112;
            _backgroundCornersCombo.DropDownWidth = 130;
            _backgroundCornersCombo.SelectedIndex = (int)BackgroundCorners;
            _backgroundCornersCombo.SelectedIndexChanged += (s, e) =>
                BackgroundCorners = (SplitDetailBackgroundCorners)_backgroundCornersCombo.SelectedIndex;
            radiusRow.Controls.Add(_backgroundCornersCombo);

            t.SetColumnSpan(radiusRow, 3);
            t.Controls.Add(radiusRow, 1, 4);

            return t;
        }

        // ── Accuracy ─────────────────────────────────────────────────────────
        private Control BuildAccuracySection()
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
            };

            _rdoSeconds     = new RadioButton { Text = "Seconds",      AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _rdoTenths      = new RadioButton { Text = "Tenths",       AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _rdoHundredths  = new RadioButton { Text = "Hundredths",   AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _rdoMilliseconds= new RadioButton { Text = "Milliseconds", AutoSize = true };

            _rdoHundredths.Checked = true;

            _rdoSeconds.CheckedChanged      += (s, e) => { if (_rdoSeconds.Checked)      Accuracy = TimeAccuracy.Seconds; };
            _rdoTenths.CheckedChanged       += (s, e) => { if (_rdoTenths.Checked)       Accuracy = TimeAccuracy.Tenths; };
            _rdoHundredths.CheckedChanged   += (s, e) => { if (_rdoHundredths.Checked)   Accuracy = TimeAccuracy.Hundredths; };
            _rdoMilliseconds.CheckedChanged += (s, e) => { if (_rdoMilliseconds.Checked) Accuracy = TimeAccuracy.Milliseconds; };

            flow.Controls.Add(_rdoSeconds);
            flow.Controls.Add(_rdoTenths);
            flow.Controls.Add(_rdoHundredths);
            flow.Controls.Add(_rdoMilliseconds);
            return flow;
        }

        // ── Colors ────────────────────────────────────────────────────────────
        private Control BuildColorSection()
        {
            var t = new TableLayoutPanel
            {
                AutoSize    = true,
                ColumnCount = 6,
                RowCount    = 2,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));

            // Text Color (label, separator)
            t.Controls.Add(MakeLbl("Text Color:"), 0, 0);
            _textColorBtn = MakeColorBtn(TextColor);
            _textColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_textColorBtn, this);
                TextColor = _textColorBtn.BackColor;
            };
            t.Controls.Add(_textColorBtn, 1, 0);

            // Time Color (right time and Current mode comparison values)
            t.Controls.Add(MakeLbl("Time Color:"), 0, 1);
            _timeColorBtn = MakeColorBtn(TimeColor);
            _timeColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_timeColorBtn, this);
                TimeColor = _timeColorBtn.BackColor;
            };
            t.Controls.Add(_timeColorBtn, 1, 1);

            _comparison1ColorChk = MakeCompactCheck("Comparison 1", OverrideComparison1Color);
            _comparison1ColorChk.CheckedChanged += (s, e) =>
            {
                OverrideComparison1Color = _comparison1ColorChk.Checked;
                UpdateComparisonColorControlStates();
            };
            t.Controls.Add(_comparison1ColorChk, 2, 0);

            _comparison1ColorBtn = MakeColorBtn(Comparison1Color);
            _comparison1ColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_comparison1ColorBtn, this);
                Comparison1Color = _comparison1ColorBtn.BackColor;
            };
            t.Controls.Add(_comparison1ColorBtn, 3, 0);

            _delta1ColorChk = MakeCompactCheck("Delta 1", OverrideDelta1Color);
            _delta1ColorChk.CheckedChanged += (s, e) =>
            {
                OverrideDelta1Color = _delta1ColorChk.Checked;
                UpdateComparisonColorControlStates();
            };
            t.Controls.Add(_delta1ColorChk, 4, 0);

            _delta1ColorBtn = MakeColorBtn(Delta1Color);
            _delta1ColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_delta1ColorBtn, this);
                Delta1Color = _delta1ColorBtn.BackColor;
            };
            t.Controls.Add(_delta1ColorBtn, 5, 0);

            _comparison2ColorChk = MakeCompactCheck("Comparison 2", OverrideComparison2Color);
            _comparison2ColorChk.CheckedChanged += (s, e) =>
            {
                OverrideComparison2Color = _comparison2ColorChk.Checked;
                UpdateComparisonColorControlStates();
            };
            t.Controls.Add(_comparison2ColorChk, 2, 1);

            _comparison2ColorBtn = MakeColorBtn(Comparison2Color);
            _comparison2ColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_comparison2ColorBtn, this);
                Comparison2Color = _comparison2ColorBtn.BackColor;
            };
            t.Controls.Add(_comparison2ColorBtn, 3, 1);

            _delta2ColorChk = MakeCompactCheck("Delta 2", OverrideDelta2Color);
            _delta2ColorChk.CheckedChanged += (s, e) =>
            {
                OverrideDelta2Color = _delta2ColorChk.Checked;
                UpdateComparisonColorControlStates();
            };
            t.Controls.Add(_delta2ColorChk, 4, 1);

            _delta2ColorBtn = MakeColorBtn(Delta2Color);
            _delta2ColorBtn.Click += (s, e) =>
            {
                SettingsHelper.ColorButtonClick(_delta2ColorBtn, this);
                Delta2Color = _delta2ColorBtn.BackColor;
            };
            t.Controls.Add(_delta2ColorBtn, 5, 1);

            UpdateComparisonColorControlStates();
            return t;
        }

        private void UpdateComparisonColorControlStates()
        {
            if (_comparison1ColorBtn != null) _comparison1ColorBtn.Enabled = OverrideComparison1Color;
            if (_comparison2ColorBtn != null) _comparison2ColorBtn.Enabled = OverrideComparison2Color;
            if (_delta1ColorBtn != null) _delta1ColorBtn.Enabled = OverrideDelta1Color;
            if (_delta2ColorBtn != null) _delta2ColorBtn.Enabled = OverrideDelta2Color;
        }

        private Control BuildFontSection()
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            _leftColumnBoldChk = MakeCompactCheck("Left bold", LeftColumnBold);
            _leftColumnBoldChk.CheckedChanged += (s, e) =>
            {
                LeftColumnBold = _leftColumnBoldChk.Checked;
                PublishLinkedBoldFonts(false);
            };
            flow.Controls.Add(_leftColumnBoldChk);

            _middleLabelBoldChk = MakeCompactCheck("Middle label bold", MiddleLabelBold);
            _middleLabelBoldChk.CheckedChanged += (s, e) =>
            {
                MiddleLabelBold = _middleLabelBoldChk.Checked;
                PublishLinkedBoldFonts(false);
            };
            flow.Controls.Add(_middleLabelBoldChk);

            _middleValueBoldChk = MakeCompactCheck("Middle value bold", MiddleValueBold);
            _middleValueBoldChk.CheckedChanged += (s, e) =>
            {
                MiddleValueBold = _middleValueBoldChk.Checked;
                PublishLinkedBoldFonts(false);
            };
            flow.Controls.Add(_middleValueBoldChk);

            _rightColumnBoldChk = MakeCompactCheck("Right bold", RightColumnBold);
            _rightColumnBoldChk.CheckedChanged += (s, e) =>
            {
                RightColumnBold = _rightColumnBoldChk.Checked;
                PublishLinkedBoldFonts(false);
            };
            flow.Controls.Add(_rightColumnBoldChk);

            return flow;
        }

        private void UpdateModeLabelControls()
        {
            bool segmentMode = IsSegmentMode(Mode);
            bool priorSegmentMode = Mode == SplitDetailMode.PriorSubsplit;
            if (_useItemNameChk != null)
                _useItemNameChk.Text = segmentMode ? "Use subsplit name" : "Use split name";

            if (_labelBox != null)
                _labelBox.Enabled = !UseItemName;

            if (_autoFitNameColumnsChk != null)
                _autoFitNameColumnsChk.Enabled = UseItemName;

            if (_nameShorteningCombo != null)
                _nameShorteningCombo.Enabled = UseItemName;
            if (_alwaysRemoveLeadingNumbersChk != null)
                _alwaysRemoveLeadingNumbersChk.Enabled = UseItemName;

            if (_ignoreShortSubsplitsLbl != null)
                _ignoreShortSubsplitsLbl.Enabled = priorSegmentMode;
            if (_ignoreShortSubsplitsRow != null)
                _ignoreShortSubsplitsRow.Enabled = priorSegmentMode;
            if (_ignoreShortSubsplitsChk != null)
                _ignoreShortSubsplitsChk.Enabled = priorSegmentMode;
            if (_ignoreShortSubsplitsBox != null)
                _ignoreShortSubsplitsBox.Enabled = priorSegmentMode && IgnoreShortSubsplits;
        }

        private void UpdateBackgroundControlStates()
        {
            bool enabled = BackgroundEnabled;
            bool deltaMode = IsDeltaBackgroundMode(BackgroundMode);
            bool gradientMode =
                BackgroundMode == SplitDetailBackgroundMode.Vertical ||
                BackgroundMode == SplitDetailBackgroundMode.Horizontal ||
                BackgroundMode == SplitDetailBackgroundMode.VerticalWithDeltaColor ||
                BackgroundMode == SplitDetailBackgroundMode.HorizontalWithDeltaColor;
            bool useThreeColors = BackgroundColorCount == 3;

            if (_backgroundColorBtn != null) _backgroundColorBtn.Enabled = enabled && !deltaMode;
            if (_backgroundColor2Btn != null) _backgroundColor2Btn.Enabled = enabled && !deltaMode && gradientMode;
            if (_backgroundColor3Btn != null) _backgroundColor3Btn.Enabled = enabled && !deltaMode && gradientMode && useThreeColors;
            if (_backgroundColorCountCombo != null) _backgroundColorCountCombo.Enabled = enabled && gradientMode;
            if (_backgroundModeCombo != null) _backgroundModeCombo.Enabled = enabled;
            if (_backgroundCornerRadiusNum != null) _backgroundCornerRadiusNum.Enabled = enabled;
            if (_backgroundCornersCombo != null) _backgroundCornersCombo.Enabled = enabled;
        }

        private static bool IsDeltaBackgroundMode(SplitDetailBackgroundMode mode)
        {
            return mode == SplitDetailBackgroundMode.PlainWithDeltaColor ||
                   mode == SplitDetailBackgroundMode.VerticalWithDeltaColor ||
                   mode == SplitDetailBackgroundMode.HorizontalWithDeltaColor;
        }

        private bool HasActiveBackgroundSettings()
        {
            return BackgroundMode != SplitDetailBackgroundMode.Plain ||
                   BackgroundColor.A > 0 ||
                   BackgroundColor2.A > 0 ||
                   (BackgroundColorCount == 3 && BackgroundColor3.A > 0);
        }

        public string LabelForDisplay(bool live)
        {
            if (live && (Mode == SplitDetailMode.PriorSplit || Mode == SplitDetailMode.PriorSubsplit))
                return ToLiveLabel(Label);
            return Label;
        }

        public string MeasureLabelText(string currentLabel)
        {
            if (!UseItemName && (Mode == SplitDetailMode.PriorSplit || Mode == SplitDetailMode.PriorSubsplit))
            {
                string liveLabel = LabelForDisplay(live: true);
                return liveLabel.Length > Label.Length ? liveLabel : Label;
            }

            return currentLabel;
        }

        public string ComponentLabel => Label;

        internal void ApplyLinkedColumnSpacing(float spacing)
        {
            ApplyLinkedLayoutNumber(
                _spacingNum, spacing,
                value => ColumnSpacing = value);
        }

        internal void ApplyLinkedMiddleValueTimeGap(float gap)
        {
            ApplyLinkedLayoutNumber(
                _middleValueTimeGapNum, gap,
                value => MiddleValueTimeGap = value);
        }

        internal void ApplyLinkedMiddleLabelRightOffset(float offset)
        {
            ApplyLinkedLayoutNumber(
                _middleLabelRightOffsetNum, offset,
                value => MiddleLabelRightOffset = value);
        }

        internal void ApplyLinkedMiddleLabelValueGap(float gap)
        {
            ApplyLinkedLayoutNumber(
                _middleLabelValueGapNum, gap,
                value => MiddleLabelValueGap = value);
        }

        internal void ApplyLinkedBoldFonts(bool left, bool middleLabel,
                                           bool middleValue, bool right,
                                           bool enableLink)
        {
            if (enableLink)
                LinkBoldFonts = true;

            LeftColumnBold = left;
            MiddleLabelBold = middleLabel;
            MiddleValueBold = middleValue;
            RightColumnBold = right;

            _syncingLinkedBoldFonts = true;
            try
            {
                if (_linkBoldFontsChk != null && enableLink)
                    _linkBoldFontsChk.Checked = true;
                if (_leftColumnBoldChk != null)
                    _leftColumnBoldChk.Checked = left;
                if (_middleLabelBoldChk != null)
                    _middleLabelBoldChk.Checked = middleLabel;
                if (_middleValueBoldChk != null)
                    _middleValueBoldChk.Checked = middleValue;
                if (_rightColumnBoldChk != null)
                    _rightColumnBoldChk.Checked = right;
            }
            finally
            {
                _syncingLinkedBoldFonts = false;
            }
        }

        private void PublishLinkedLayoutValues()
        {
            if (_syncingLinkedLayoutValue || _loadingSettings)
                return;

            SplitDetailLayoutLinks.PublishSpacing(this, MiddleColumnLinkGroup, ColumnSpacing);
            SplitDetailLayoutLinks.PublishMiddleValueTimeGap(
                this, MiddleColumnLinkGroup, MiddleValueTimeGap);
            SplitDetailLayoutLinks.PublishMiddleLabelRightOffset(
                this, MiddleColumnLinkGroup, MiddleLabelRightOffset);
            SplitDetailLayoutLinks.PublishMiddleLabelValueGap(
                this, MiddleColumnLinkGroup, MiddleLabelValueGap);
        }

        private void PublishLinkedBoldFonts(bool enableLinkedRecipients)
        {
            if (_syncingLinkedBoldFonts || _loadingSettings || !LinkBoldFonts)
                return;

            SplitDetailLayoutLinks.PublishBoldFonts(
                this, MiddleColumnLinkGroup,
                LeftColumnBold, MiddleLabelBold, MiddleValueBold, RightColumnBold,
                enableLinkedRecipients);
        }

        private void ApplyLinkedLayoutNumber(NumericUpDown control, float value,
                                             Action<float> apply)
        {
            float clamped = Math.Max(0f, value);
            apply(clamped);

            if (control == null)
                return;

            decimal controlValue = Math.Min(control.Maximum, (decimal)clamped);
            if (control.Value == controlValue)
                return;

            _syncingLinkedLayoutValue = true;
            try
            {
                control.Value = controlValue;
            }
            finally
            {
                _syncingLinkedLayoutValue = false;
            }
        }

        private static bool IsSegmentMode(SplitDetailMode mode)
        {
            return mode == SplitDetailMode.CurrentSegment || mode == SplitDetailMode.PriorSubsplit;
        }

        private static string DefaultLabelForMode(SplitDetailMode mode)
        {
            switch (mode)
            {
                case SplitDetailMode.CurrentSegment: return "Current Seg.";
                case SplitDetailMode.PriorSplit:     return "Prev Split";
                case SplitDetailMode.PriorSubsplit:  return "Prev Seg.";
                default:                             return "Current Split";
            }
        }

        private static string ToLiveLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return "Live";
            if (label.StartsWith("Prev ")) return "Live " + label.Substring(5);
            if (label.StartsWith("Previous ")) return "Live " + label.Substring(9);
            return "Live " + label;
        }

        private static string InferLegacyLabel(XmlNode node, SplitDetailMode mode)
        {
            switch (mode)
            {
                case SplitDetailMode.CurrentSplit:
                    return R(node, "LabelCurrentSplit") ?? DefaultLabelForMode(mode);

                case SplitDetailMode.CurrentSegment:
                {
                    string segment = R(node, "LabelSegment") ?? InferLegacySuffix(R(node, "LabelPrevSeg"), "Seg.");
                    return "Current " + segment;
                }

                case SplitDetailMode.PriorSplit:
                    return R(node, "LabelPrevSplit") ?? ("Prev " + (R(node, "LabelSplit") ?? "Split"));

                case SplitDetailMode.PriorSubsplit:
                    return R(node, "LabelPrevSeg") ?? ("Prev " + (R(node, "LabelSegment") ?? "Seg."));

                default:
                    return DefaultLabelForMode(mode);
            }
        }

        private static string InferLegacySuffix(string oldLabel, string fallback)
        {
            if (!string.IsNullOrEmpty(oldLabel) && oldLabel.StartsWith("Prev "))
                return oldLabel.Substring(5);
            return fallback;
        }

        // ── UI helpers ────────────────────────────────────────────────────────
        private static bool TryParseSeconds(string text, out double seconds)
        {
            seconds = 0d;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();
            if (double.TryParse(value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out seconds))
            {
                return !double.IsNaN(seconds) && !double.IsInfinity(seconds);
            }

            value = value.Replace(',', '.');
            return double.TryParse(value,
                                   System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out seconds) &&
                   !double.IsNaN(seconds) &&
                   !double.IsInfinity(seconds);
        }

        private static string FormatSeconds(double seconds)
        {
            return Math.Max(0d, seconds).ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static Label MakeLbl(string text) => new Label
        {
            Text      = text,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        private static Label MakeInlineLbl(string text) => new Label
        {
            Text      = text,
            AutoSize  = true,
            Margin    = new Padding(8, 3, 2, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        private static NumericUpDown MakeLayoutNumber(float value)
        {
            return new NumericUpDown
            {
                Minimum       = 0,
                Maximum       = 9999,
                DecimalPlaces = 0,
                Value         = (decimal)Math.Max(0f, value),
                Width         = 58,
                Margin        = new Padding(0, 1, 4, 0),
            };
        }

        private static ComboBox MakeCombo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            c.Items.AddRange(items);
            return c;
        }

        private static ComboBox MakeNarrowCombo(params string[] items)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width         = 120,
                DropDownWidth = 180,
                Anchor        = AnchorStyles.Left,
                Margin        = new Padding(0, 1, 4, 0),
            };
            c.Items.AddRange(items);
            return c;
        }

        private static CheckBox MakeCompactCheck(string text, bool isChecked)
        {
            return new CheckBox
            {
                Text      = text,
                Checked   = isChecked,
                AutoSize  = true,
                Anchor    = AnchorStyles.Left,
                Margin    = new Padding(0, 4, 6, 0),
            };
        }

        private static TextBox MakeTextBox(string text, int maxLength)
            => new TextBox { Text = text, MaxLength = maxLength, Dock = DockStyle.Fill };

        /// <summary>
        /// Creates a color-picker button in LiveSplit's standard Popup style.
        /// Wired to SettingsHelper.ColorButtonClick in the section builder above.
        /// </summary>
        private static Button MakeColorBtn(Color initial) => new Button
        {
            BackColor            = initial,
            FlatStyle            = FlatStyle.Popup,
            UseVisualStyleBackColor = false,
            Width                = 23,
            Height               = 23,
            Anchor               = AnchorStyles.Left,
            Margin               = new Padding(0, 1, 8, 0),
        };

        private static Button MakeTightColorBtn(Color initial) => new Button
        {
            BackColor               = initial,
            FlatStyle               = FlatStyle.Popup,
            UseVisualStyleBackColor = false,
            Width                   = 22,
            Height                  = 22,
            Anchor                  = AnchorStyles.Left,
            Margin                  = new Padding(0, 1, 4, 0),
        };

        // =====================================================================
        // Comparison list refresh
        // =====================================================================

        /// <summary>
        /// Repopulates both comparison dropdowns from the currently loaded run.
        /// Must be called each time the settings panel is opened.
        /// </summary>
        public void RefreshComparisons()
        {
            PopulateCombo(_cmp1Combo, Comparison1);
            PopulateCombo(_cmp2Combo, Comparison2);
        }

        private void PopulateCombo(ComboBox combo, string selected)
        {
            combo.Items.Clear();
            combo.Items.Add(CurrentComparisonChoice);
            if (_state?.Run != null)
            {
                foreach (string comp in _state.Run.Comparisons)
                {
                    if (!string.Equals(comp, CurrentComparisonChoice, StringComparison.Ordinal))
                        combo.Items.Add(comp);
                }
            }
            if (!string.IsNullOrEmpty(selected) && combo.Items.Contains(selected))
                combo.SelectedItem = selected;
            else if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        // =====================================================================
        // XML persistence
        // =====================================================================

        public XmlNode GetSettings(XmlDocument document)
        {
            XmlElement root = document.CreateElement("Settings");
            W(document, root, "Version",           "22");
            W(document, root, "Mode",              Mode.ToString());
            W(document, root, "Comparison1",       Comparison1);
            W(document, root, "Comparison2",       Comparison2);
            W(document, root, "ComparisonCount",   ComparisonCount.ToString());
            W(document, root, "PriorityDelta",     PriorityDelta.ToString());
            W(document, root, "Label",             Label);
            W(document, root, "UseItemName",       UseItemName.ToString());
            W(document, root, "AutoFitNameColumns", AutoFitNameColumns.ToString());
            W(document, root, "AlwaysRemoveLeadingNumbers", AlwaysRemoveLeadingNumbers.ToString());
            W(document, root, "NameShortening",    NameShortening.ToString());
            W(document, root, "IgnoreShortSubsplits", IgnoreShortSubsplits.ToString());
            W(document, root, "IgnoreShortSubsplitSeconds",
              IgnoreShortSubsplitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "Separator",         Separator);
            W(document, root, "ColumnSpacing",     ColumnSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "MiddleColumnLinkGroup", MiddleColumnLinkGroup.ToString());
            W(document, root, "MiddleValueTimeGap",
              MiddleValueTimeGap.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "MiddleLabelRightOffset",
              MiddleLabelRightOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "MiddleLabelValueGap",
              MiddleLabelValueGap.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "LinkMiddleLabels", LinkMiddleLabels.ToString());
            W(document, root, "LinkBoldFonts", LinkBoldFonts.ToString());
            W(document, root, "BackgroundEnabled", BackgroundEnabled.ToString());
            W(document, root, "BackgroundColor",       ColorToHex(BackgroundColor));
            W(document, root, "BackgroundColor2",      ColorToHex(BackgroundColor2));
            W(document, root, "BackgroundColor3",      ColorToHex(BackgroundColor3));
            W(document, root, "BackgroundColorCount",  BackgroundColorCount.ToString());
            W(document, root, "BackgroundMode",        BackgroundMode.ToString());
            W(document, root, "BackgroundCornerRadius",
              BackgroundCornerRadius.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "BackgroundCorners", BackgroundCorners.ToString());
            W(document, root, "Accuracy",          Accuracy.ToString());
            W(document, root, "TextColor",         ColorToHex(TextColor));
            W(document, root, "TimeColor",         ColorToHex(TimeColor));
            W(document, root, "OverrideComparison1Color", OverrideComparison1Color.ToString());
            W(document, root, "Comparison1Color",         ColorToHex(Comparison1Color));
            W(document, root, "OverrideComparison2Color", OverrideComparison2Color.ToString());
            W(document, root, "Comparison2Color",         ColorToHex(Comparison2Color));
            W(document, root, "OverrideDelta1Color",      OverrideDelta1Color.ToString());
            W(document, root, "Delta1Color",              ColorToHex(Delta1Color));
            W(document, root, "OverrideDelta2Color",      OverrideDelta2Color.ToString());
            W(document, root, "Delta2Color",              ColorToHex(Delta2Color));
            W(document, root, "LeftColumnBold",           LeftColumnBold.ToString());
            W(document, root, "MiddleLabelBold",          MiddleLabelBold.ToString());
            W(document, root, "MiddleValueBold",          MiddleValueBold.ToString());
            W(document, root, "RightColumnBold",          RightColumnBold.ToString());
            return root;
        }

        public void SetSettings(XmlNode node)
        {
            if (node == null) return;

            _loadingSettings = true;
            try
            {
            // Mode
            string modeStr = R(node, "Mode");
            if (modeStr != null && Enum.TryParse(modeStr, out SplitDetailMode m))
            {
                Mode = m;
                _modeCombo.SelectedIndex = (int)Mode;
            }

            // Comparison1 — new field.  Fall back to Current Comparison if absent.
            string c1 = R(node, "Comparison1");
            if (!string.IsNullOrEmpty(c1))
            {
                Comparison1 = c1;
                if (_cmp1Combo.Items.Contains(c1)) _cmp1Combo.SelectedItem = c1;
            }

            // Comparison2 — new field.  For backward compat also try old "Comparison".
            string c2 = R(node, "Comparison2") ?? R(node, "Comparison");
            if (!string.IsNullOrEmpty(c2))
            {
                Comparison2 = c2;
                if (_cmp2Combo.Items.Contains(c2)) _cmp2Combo.SelectedItem = c2;
            }

            // ComparisonCount
            string ccStr = R(node, "ComparisonCount");
            if (int.TryParse(ccStr, out int cc) && (cc == 1 || cc == 2))
            {
                ComparisonCount = cc;
                _cmpCountCombo.SelectedIndex = cc - 1;
            }

            // PriorityDelta
            string prStr = R(node, "PriorityDelta");
            if (int.TryParse(prStr, out int pr) && (pr == 1 || pr == 2))
            {
                PriorityDelta = pr;
                _priorityCombo.SelectedIndex = pr - 1;
            }

            // Label. Fall back to the older per-mode/suffix label fields.
            string label = R(node, "Label") ?? InferLegacyLabel(node, Mode);
            if (!string.IsNullOrEmpty(label))
            {
                Label = label;
                _labelBox.Text = label;
            }

            string useNameStr = R(node, "UseItemName");
            if (bool.TryParse(useNameStr, out bool useItemName))
            {
                UseItemName = useItemName;
                _useItemNameChk.Checked = useItemName;
            }

            string autoFitNameStr = R(node, "AutoFitNameColumns");
            if (bool.TryParse(autoFitNameStr, out bool autoFitNameColumns))
            {
                AutoFitNameColumns = autoFitNameColumns;
                _autoFitNameColumnsChk.Checked = autoFitNameColumns;
            }

            string removeLeadingNumbersStr = R(node, "AlwaysRemoveLeadingNumbers");
            if (bool.TryParse(removeLeadingNumbersStr, out bool removeLeadingNumbers))
            {
                AlwaysRemoveLeadingNumbers = removeLeadingNumbers;
                _alwaysRemoveLeadingNumbersChk.Checked = removeLeadingNumbers;
            }

            string shorteningStr = R(node, "NameShortening");
            if (!string.IsNullOrEmpty(shorteningStr) &&
                Enum.TryParse(shorteningStr, out SplitDetailNameShortening shortening))
            {
                NameShortening = shortening;
                _nameShorteningCombo.SelectedIndex = (int)shortening;
            }
            else if (string.Equals(shorteningStr, "RemoveFirstFive", StringComparison.Ordinal))
            {
                NameShortening = SplitDetailNameShortening.RemoveLeadingParts;
                _nameShorteningCombo.SelectedIndex = (int)NameShortening;
            }

            string ignoreShortStr = R(node, "IgnoreShortSubsplits");
            if (bool.TryParse(ignoreShortStr, out bool ignoreShort))
            {
                IgnoreShortSubsplits = ignoreShort;
                _ignoreShortSubsplitsChk.Checked = ignoreShort;
            }

            string ignoreSecondsStr = R(node, "IgnoreShortSubsplitSeconds");
            double ignoreSeconds;
            if (TryParseSeconds(ignoreSecondsStr, out ignoreSeconds))
            {
                IgnoreShortSubsplitSeconds = Math.Max(0d, ignoreSeconds);
                _ignoreShortSubsplitsBox.Text = FormatSeconds(IgnoreShortSubsplitSeconds);
            }

            UpdateModeLabelControls();

            // Separator — allow empty string (no separator is valid)
            string sep = node["Separator"]?.InnerText;   // don't use R() which skips null
            if (sep != null) { Separator = sep; _sepBox.Text = sep; }

            // Column spacing
            string spacStr = R(node, "ColumnSpacing");
            if (!string.IsNullOrEmpty(spacStr) &&
                float.TryParse(spacStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float sp))
            {
                ColumnSpacing    = Math.Max(0f, sp);
                _spacingNum.Value = Math.Min(_spacingNum.Maximum, (decimal)ColumnSpacing);
            }

            string linkGroupStr = R(node, "MiddleColumnLinkGroup");
            if (int.TryParse(linkGroupStr, out int linkGroup))
            {
                MiddleColumnLinkGroup = Math.Max(0, Math.Min(4, linkGroup));
                _middleColumnLinkCombo.SelectedIndex = MiddleColumnLinkGroup;
            }

            string valueTimeGapStr = R(node, "MiddleValueTimeGap");
            if (!string.IsNullOrEmpty(valueTimeGapStr) &&
                float.TryParse(valueTimeGapStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float valueTimeGap))
            {
                MiddleValueTimeGap = Math.Max(0f, valueTimeGap);
                _middleValueTimeGapNum.Value =
                    Math.Min(_middleValueTimeGapNum.Maximum, (decimal)MiddleValueTimeGap);
            }

            string labelRightOffsetStr = R(node, "MiddleLabelRightOffset");
            if (!string.IsNullOrEmpty(labelRightOffsetStr) &&
                float.TryParse(labelRightOffsetStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float labelRightOffset))
            {
                MiddleLabelRightOffset = Math.Max(0f, labelRightOffset);
                _middleLabelRightOffsetNum.Value =
                    Math.Min(_middleLabelRightOffsetNum.Maximum, (decimal)MiddleLabelRightOffset);
            }

            string labelValueGapStr = R(node, "MiddleLabelValueGap");
            if (!string.IsNullOrEmpty(labelValueGapStr) &&
                float.TryParse(labelValueGapStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float labelValueGap))
            {
                MiddleLabelValueGap = Math.Max(0f, labelValueGap);
                _middleLabelValueGapNum.Value =
                    Math.Min(_middleLabelValueGapNum.Maximum, (decimal)MiddleLabelValueGap);
            }

            PublishLinkedLayoutValues();

            string linkLabelsStr = R(node, "LinkMiddleLabels");
            if (bool.TryParse(linkLabelsStr, out bool linkLabels))
            {
                LinkMiddleLabels = linkLabels;
                _linkMiddleLabelsChk.Checked = linkLabels;
            }

            string linkBoldFontsStr = R(node, "LinkBoldFonts");
            if (bool.TryParse(linkBoldFontsStr, out bool linkBoldFonts))
            {
                LinkBoldFonts = linkBoldFonts;
                _syncingLinkedBoldFonts = true;
                try
                {
                    _linkBoldFontsChk.Checked = linkBoldFonts;
                }
                finally
                {
                    _syncingLinkedBoldFonts = false;
                }
            }

            string bg1 = R(node, "BackgroundColor");
            if (!string.IsNullOrEmpty(bg1))
            {
                BackgroundColor = HexToColor(bg1);
                _backgroundColorBtn.BackColor = BackgroundColor;
            }

            string bg2 = R(node, "BackgroundColor2");
            if (!string.IsNullOrEmpty(bg2))
            {
                BackgroundColor2 = HexToColor(bg2);
                _backgroundColor2Btn.BackColor = BackgroundColor2;
            }

            string bg3 = R(node, "BackgroundColor3");
            if (!string.IsNullOrEmpty(bg3))
            {
                BackgroundColor3 = HexToColor(bg3);
                _backgroundColor3Btn.BackColor = BackgroundColor3;
            }

            string bgCountStr = R(node, "BackgroundColorCount");
            if (int.TryParse(bgCountStr, out int bgCount))
            {
                BackgroundColorCount = bgCount == 3 ? 3 : 2;
                _backgroundColorCountCombo.SelectedIndex = BackgroundColorCount == 3 ? 1 : 0;
            }

            string bgModeStr = R(node, "BackgroundMode") ?? R(node, "BackgroundGradient");
            if (!string.IsNullOrEmpty(bgModeStr) &&
                Enum.TryParse(bgModeStr.Replace(" ", string.Empty), out SplitDetailBackgroundMode bgMode))
            {
                BackgroundMode = bgMode;
                _backgroundModeCombo.SelectedIndex = (int)BackgroundMode;
            }

            string bgRadiusStr = R(node, "BackgroundCornerRadius");
            if (!string.IsNullOrEmpty(bgRadiusStr) &&
                float.TryParse(bgRadiusStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float bgRadius))
            {
                BackgroundCornerRadius = Math.Max(0f, bgRadius);
                _backgroundCornerRadiusNum.Value =
                    Math.Min(_backgroundCornerRadiusNum.Maximum, (decimal)BackgroundCornerRadius);
            }

            string bgCornersStr = R(node, "BackgroundCorners");
            if (!string.IsNullOrEmpty(bgCornersStr) &&
                Enum.TryParse(bgCornersStr, out SplitDetailBackgroundCorners bgCorners))
            {
                BackgroundCorners = bgCorners;
                _backgroundCornersCombo.SelectedIndex = (int)BackgroundCorners;
            }

            string backgroundEnabledStr = R(node, "BackgroundEnabled");
            if (bool.TryParse(backgroundEnabledStr, out bool backgroundEnabled))
                BackgroundEnabled = backgroundEnabled;
            else
                BackgroundEnabled = HasActiveBackgroundSettings();
            _backgroundEnabledChk.Checked = BackgroundEnabled;

            UpdateBackgroundControlStates();

            // Accuracy
            string accStr = R(node, "Accuracy");
            if (!string.IsNullOrEmpty(accStr) &&
                Enum.TryParse(accStr, out TimeAccuracy acc))
            {
                Accuracy = acc;
                _rdoSeconds.Checked      = acc == TimeAccuracy.Seconds;
                _rdoTenths.Checked       = acc == TimeAccuracy.Tenths;
                _rdoHundredths.Checked   = acc == TimeAccuracy.Hundredths;
                _rdoMilliseconds.Checked = acc == TimeAccuracy.Milliseconds;
            }

            // Colors
            string tc = R(node, "TextColor");
            if (!string.IsNullOrEmpty(tc))
            {
                TextColor = HexToColor(tc);
                _textColorBtn.BackColor = TextColor;
            }

            string mc = R(node, "TimeColor");
            if (!string.IsNullOrEmpty(mc))
            {
                TimeColor = HexToColor(mc);
                _timeColorBtn.BackColor = TimeColor;
            }

            string oc1 = R(node, "OverrideComparison1Color");
            if (bool.TryParse(oc1, out bool overrideComparison1))
            {
                OverrideComparison1Color = overrideComparison1;
                _comparison1ColorChk.Checked = overrideComparison1;
            }

            string cc1 = R(node, "Comparison1Color");
            if (!string.IsNullOrEmpty(cc1))
            {
                Comparison1Color = HexToColor(cc1);
                _comparison1ColorBtn.BackColor = Comparison1Color;
            }

            string oc2 = R(node, "OverrideComparison2Color");
            if (bool.TryParse(oc2, out bool overrideComparison2))
            {
                OverrideComparison2Color = overrideComparison2;
                _comparison2ColorChk.Checked = overrideComparison2;
            }

            string cc2 = R(node, "Comparison2Color");
            if (!string.IsNullOrEmpty(cc2))
            {
                Comparison2Color = HexToColor(cc2);
                _comparison2ColorBtn.BackColor = Comparison2Color;
            }

            string od1 = R(node, "OverrideDelta1Color");
            if (bool.TryParse(od1, out bool overrideDelta1))
            {
                OverrideDelta1Color = overrideDelta1;
                _delta1ColorChk.Checked = overrideDelta1;
            }

            string dc1 = R(node, "Delta1Color");
            if (!string.IsNullOrEmpty(dc1))
            {
                Delta1Color = HexToColor(dc1);
                _delta1ColorBtn.BackColor = Delta1Color;
            }

            string od2 = R(node, "OverrideDelta2Color");
            if (bool.TryParse(od2, out bool overrideDelta2))
            {
                OverrideDelta2Color = overrideDelta2;
                _delta2ColorChk.Checked = overrideDelta2;
            }

            string dc2 = R(node, "Delta2Color");
            if (!string.IsNullOrEmpty(dc2))
            {
                Delta2Color = HexToColor(dc2);
                _delta2ColorBtn.BackColor = Delta2Color;
            }

            _syncingLinkedBoldFonts = true;
            try
            {
                string leftBoldStr = R(node, "LeftColumnBold");
                if (bool.TryParse(leftBoldStr, out bool leftBold))
                {
                    LeftColumnBold = leftBold;
                    _leftColumnBoldChk.Checked = leftBold;
                }

                string middleBoldStr = R(node, "MiddleColumnBold");
                if (bool.TryParse(middleBoldStr, out bool middleBold))
                {
                    MiddleLabelBold = middleBold;
                    MiddleValueBold = middleBold;
                    _middleLabelBoldChk.Checked = middleBold;
                    _middleValueBoldChk.Checked = middleBold;
                }

                string middleLabelBoldStr = R(node, "MiddleLabelBold");
                if (bool.TryParse(middleLabelBoldStr, out bool middleLabelBold))
                {
                    MiddleLabelBold = middleLabelBold;
                    _middleLabelBoldChk.Checked = middleLabelBold;
                }

                string middleValueBoldStr = R(node, "MiddleValueBold");
                if (bool.TryParse(middleValueBoldStr, out bool middleValueBold))
                {
                    MiddleValueBold = middleValueBold;
                    _middleValueBoldChk.Checked = middleValueBold;
                }

                string rightBoldStr = R(node, "RightColumnBold");
                if (bool.TryParse(rightBoldStr, out bool rightBold))
                {
                    RightColumnBold = rightBold;
                    _rightColumnBoldChk.Checked = rightBold;
                }
            }
            finally
            {
                _syncingLinkedBoldFonts = false;
            }

            UpdateComparisonColorControlStates();
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        // ── XML helpers ───────────────────────────────────────────────────────
        private static void W(XmlDocument doc, XmlElement parent, string name, string value)
        {
            var el = doc.CreateElement(name);
            el.InnerText = value ?? string.Empty;
            parent.AppendChild(el);
        }

        private static string R(XmlNode parent, string childName)
        {
            string s = parent[childName]?.InnerText;
            return string.IsNullOrEmpty(s) ? null : s;
        }

        // ── Color serialization (ARGB hex, e.g. "FFFFFFFF") ─────────────────
        private static string ColorToHex(Color c)  => c.ToArgb().ToString("X8");
        private static Color  HexToColor(string s)
        {
            try   { return Color.FromArgb(unchecked((int)Convert.ToUInt32(s, 16))); }
            catch { return Color.White; }
        }
    }
}
