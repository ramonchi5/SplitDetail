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
    public class SplitDetailSettings : UserControl
    {
        // =====================================================================
        // Public properties  (read by SplitDetailComponent every tick)
        // =====================================================================

        public SplitDetailMode Mode             { get; private set; } = SplitDetailMode.CurrentSplit;

        // Two independently configurable comparisons.
        // Comparison1 defaults to PB (left delta / top line).
        // Comparison2 defaults to Best Segments (right delta / bottom line).
        public string Comparison1               { get; private set; } = "Personal Best";
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
        public string Label                     { get; private set; } = "Current Split";
        public bool   UseItemName               { get; private set; } = false;

        // Empty string = no separator (default, compact layout).
        // Non-empty    = drawn between the two deltas with fixed compact padding.
        public string Separator                 { get; private set; } = string.Empty;

        // Horizontal gap between the label column and delta block (px).
        // Does NOT affect outer padding or internal delta/separator spacing.
        public float  ColumnSpacing             { get; private set; } = 3f;

        // Decimal accuracy for displayed times and deltas.
        public TimeAccuracy Accuracy            { get; private set; } = TimeAccuracy.Hundredths;

        // Colors.
        public Color  TextColor                 { get; private set; } = Color.White;
        public Color  TimeColor                 { get; private set; } = Color.White;
        public bool   OverrideComparison1Color  { get; private set; } = false;
        public Color  Comparison1Color          { get; private set; } = Color.White;
        public bool   OverrideComparison2Color  { get; private set; } = false;
        public Color  Comparison2Color          { get; private set; } = Color.White;

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
        private TextBox       _sepBox;
        private NumericUpDown _spacingNum;
        private RadioButton   _rdoHundredths, _rdoTenths, _rdoSeconds, _rdoMilliseconds;
        private Button        _textColorBtn;
        private Button        _timeColorBtn;
        private CheckBox      _comparison1ColorChk;
        private Button        _comparison1ColorBtn;
        private CheckBox      _comparison2ColorChk;
        private Button        _comparison2ColorBtn;

        // ── Constructor ───────────────────────────────────────────────────────
        public SplitDetailSettings(LiveSplitState state)
        {
            _state = state;
            BuildUI();
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
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                Dock          = DockStyle.Fill,
                Margin        = Padding.Empty,
                Padding       = Padding.Empty,
            };

            flow.Controls.Add(MakeSection("Mode & Labels",    BuildModeLabelSection()));
            flow.Controls.Add(MakeSection("Comparisons",      BuildComparisonSection()));
            flow.Controls.Add(MakeSection("Layout",           BuildLayoutSection()));
            flow.Controls.Add(MakeSection("Accuracy",         BuildAccuracySection()));
            flow.Controls.Add(MakeSection("Colors",           BuildColorSection()));

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
            var t = MakeGrid(3);

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

            _useItemNameChk = MakeCompactCheck(string.Empty, UseItemName);
            _useItemNameChk.CheckedChanged += (s, e) =>
            {
                UseItemName = _useItemNameChk.Checked;
                UpdateModeLabelControls();
            };
            t.Controls.Add(_useItemNameChk, 1, 2);

            UpdateModeLabelControls();

            return t;
        }

        // ── Comparisons ───────────────────────────────────────────────────────
        private Control BuildComparisonSection()
        {
            var t = MakeGrid(4);

            // Comparison 1
            t.Controls.Add(MakeLbl("Comparison 1:"), 0, 0);
            _cmp1Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cmp1Combo.SelectedIndexChanged += (s, e) =>
            {
                if (_cmp1Combo.SelectedItem != null)
                    Comparison1 = _cmp1Combo.SelectedItem.ToString();
            };
            t.Controls.Add(_cmp1Combo, 1, 0);

            // Comparison 2
            t.Controls.Add(MakeLbl("Comparison 2:"), 0, 1);
            _cmp2Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cmp2Combo.SelectedIndexChanged += (s, e) =>
            {
                if (_cmp2Combo.SelectedItem != null)
                    Comparison2 = _cmp2Combo.SelectedItem.ToString();
            };
            t.Controls.Add(_cmp2Combo, 1, 1);

            // How many comparisons to show
            t.Controls.Add(MakeLbl("Show comparisons:"), 0, 2);
            _cmpCountCombo = MakeCombo("1 (Comparison 1 only)", "2 (both)");
            _cmpCountCombo.SelectedIndex = ComparisonCount - 1;
            _cmpCountCombo.SelectedIndexChanged += (s, e) =>
                ComparisonCount = _cmpCountCombo.SelectedIndex + 1;
            t.Controls.Add(_cmpCountCombo, 1, 2);

            // Priority delta
            t.Controls.Add(MakeLbl("Priority delta:"), 0, 3);
            _priorityCombo = MakeCombo("Comparison 1", "Comparison 2");
            _priorityCombo.SelectedIndex = PriorityDelta - 1;
            _priorityCombo.SelectedIndexChanged += (s, e) =>
                PriorityDelta = _priorityCombo.SelectedIndex + 1;
            t.Controls.Add(_priorityCombo, 1, 3);

            return t;
        }

        // ── Layout ────────────────────────────────────────────────────────────
        private Control BuildLayoutSection()
        {
            var t = MakeGrid(2);

            // Separator
            t.Controls.Add(MakeLbl("Separator:"), 0, 0);
            _sepBox = MakeTextBox(Separator, 5);
            _sepBox.TextChanged += (s, e) => Separator = _sepBox.Text; // allow empty
            t.Controls.Add(_sepBox, 1, 0);

            // Column spacing
            t.Controls.Add(MakeLbl("Column spacing (px):"), 0, 1);
            _spacingNum = new NumericUpDown
            {
                Minimum       = 0,
                Maximum       = 30,
                DecimalPlaces = 0,
                Value         = (decimal)ColumnSpacing,
                Width         = 60,
            };
            _spacingNum.ValueChanged += (s, e) => ColumnSpacing = (float)_spacingNum.Value;
            t.Controls.Add(_spacingNum, 1, 1);

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
                ColumnCount = 4,
                RowCount    = 2,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172f));
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

            // Time Color (right time, PB/Best values)
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

            UpdateComparisonColorControlStates();
            return t;
        }

        private void UpdateComparisonColorControlStates()
        {
            if (_comparison1ColorBtn != null) _comparison1ColorBtn.Enabled = OverrideComparison1Color;
            if (_comparison2ColorBtn != null) _comparison2ColorBtn.Enabled = OverrideComparison2Color;
        }

        private void UpdateModeLabelControls()
        {
            bool segmentMode = IsSegmentMode(Mode);
            if (_useItemNameChk != null)
                _useItemNameChk.Text = segmentMode ? "Use seg. name" : "Use split name";

            if (_labelBox != null)
                _labelBox.Enabled = !UseItemName;

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
        private static Label MakeLbl(string text) => new Label
        {
            Text      = text,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        private static ComboBox MakeCombo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
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
            if (_state?.Run != null)
            {
                foreach (string comp in _state.Run.Comparisons)
                    combo.Items.Add(comp);
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
            W(document, root, "Version",           "8");
            W(document, root, "Mode",              Mode.ToString());
            W(document, root, "Comparison1",       Comparison1);
            W(document, root, "Comparison2",       Comparison2);
            W(document, root, "ComparisonCount",   ComparisonCount.ToString());
            W(document, root, "PriorityDelta",     PriorityDelta.ToString());
            W(document, root, "Label",             Label);
            W(document, root, "UseItemName",       UseItemName.ToString());
            W(document, root, "Separator",         Separator);
            W(document, root, "ColumnSpacing",     ColumnSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "Accuracy",          Accuracy.ToString());
            W(document, root, "TextColor",         ColorToHex(TextColor));
            W(document, root, "TimeColor",         ColorToHex(TimeColor));
            W(document, root, "OverrideComparison1Color", OverrideComparison1Color.ToString());
            W(document, root, "Comparison1Color",         ColorToHex(Comparison1Color));
            W(document, root, "OverrideComparison2Color", OverrideComparison2Color.ToString());
            W(document, root, "Comparison2Color",         ColorToHex(Comparison2Color));
            return root;
        }

        public void SetSettings(XmlNode node)
        {
            if (node == null) return;

            // Mode
            string modeStr = R(node, "Mode");
            if (modeStr != null && Enum.TryParse(modeStr, out SplitDetailMode m))
            {
                Mode = m;
                _modeCombo.SelectedIndex = (int)Mode;
            }

            // Comparison1 — new field.  Fall back to "Personal Best" if absent.
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
                _spacingNum.Value = (decimal)Math.Min(30f, ColumnSpacing);
            }

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

            string oc1 = R(node, "OverrideComparison1Color") ?? R(node, "OverrideDelta1Color");
            if (bool.TryParse(oc1, out bool overrideComparison1))
            {
                OverrideComparison1Color = overrideComparison1;
                _comparison1ColorChk.Checked = overrideComparison1;
            }

            string cc1 = R(node, "Comparison1Color") ?? R(node, "Delta1Color");
            if (!string.IsNullOrEmpty(cc1))
            {
                Comparison1Color = HexToColor(cc1);
                _comparison1ColorBtn.BackColor = Comparison1Color;
            }

            string oc2 = R(node, "OverrideComparison2Color") ?? R(node, "OverrideDelta2Color");
            if (bool.TryParse(oc2, out bool overrideComparison2))
            {
                OverrideComparison2Color = overrideComparison2;
                _comparison2ColorChk.Checked = overrideComparison2;
            }

            string cc2 = R(node, "Comparison2Color") ?? R(node, "Delta2Color");
            if (!string.IsNullOrEmpty(cc2))
            {
                Comparison2Color = HexToColor(cc2);
                _comparison2ColorBtn.BackColor = Comparison2Color;
            }

            UpdateComparisonColorControlStates();
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
            try   { return Color.FromArgb(Convert.ToInt32(s, 16)); }
            catch { return Color.White; }
        }
    }
}
