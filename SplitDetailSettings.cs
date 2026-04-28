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

        // User-configurable mode labels.
        // CurrentSplit is independently editable.
        // For Prev/Live modes, we use suffix/name-based generation:
        //   Prev Split = "Prev " + LabelSplit
        //   Live Split = "Live " + LabelSplit
        //   Prev Seg.  = "Prev " + LabelSegment
        //   Live Seg.  = "Live " + LabelSegment
        // This keeps layouts clean and avoids overlap issues.
        public string LabelCurrentSplit         { get; private set; } = "Current Split";
        public string LabelSplit                { get; private set; } = "Split";
        public string LabelSegment              { get; private set; } = "Seg.";

        // ── Generated labels (read-only, computed from suffixes) ──────────────
        public string LabelPrevSplit            => "Prev " + LabelSplit;
        public string LabelLiveSplit            => "Live " + LabelSplit;
        public string LabelPrevSeg              => "Prev " + LabelSegment;
        public string LabelLiveSeg              => "Live " + LabelSegment;

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

        // =====================================================================
        // Private controls
        // =====================================================================
        private readonly LiveSplitState _state;

        private ComboBox      _modeCombo;
        private ComboBox      _cmp1Combo;
        private ComboBox      _cmp2Combo;
        private ComboBox      _cmpCountCombo;
        private ComboBox      _priorityCombo;
        private TextBox       _labelCurrentBox;
        private TextBox       _labelSplitBox;
        private TextBox       _labelSegmentBox;
        private TextBox       _sepBox;
        private NumericUpDown _spacingNum;
        private RadioButton   _rdoHundredths, _rdoTenths, _rdoSeconds, _rdoMilliseconds;
        private Button        _textColorBtn;
        private Button        _timeColorBtn;

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
            AutoSize   = true;
            AutoScroll = true;
            Padding    = new Padding(10);

            // Outer flow: sections stack top-to-bottom
            var flow = new FlowLayoutPanel
            {
                AutoSize      = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                Dock          = DockStyle.Fill,
            };

            flow.Controls.Add(MakeSection("Mode & Labels",    BuildModeLabelSection()));
            flow.Controls.Add(MakeSection("Comparisons",      BuildComparisonSection()));
            flow.Controls.Add(MakeSection("Layout",           BuildLayoutSection()));
            flow.Controls.Add(MakeSection("Accuracy",         BuildAccuracySection()));
            flow.Controls.Add(MakeSection("Colors",           BuildColorSection()));

            Controls.Add(flow);
        }

        // ── Section builder ───────────────────────────────────────────────────
        private static GroupBox MakeSection(string title, Control content)
        {
            var gb = new GroupBox
            {
                Text     = title,
                AutoSize = true,
                Margin   = new Padding(0, 0, 0, 6),
                Padding  = new Padding(6),
            };
            content.Dock = DockStyle.Fill;
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
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100f));
            return t;
        }

        // ── Mode & Labels ─────────────────────────────────────────────────────
        private Control BuildModeLabelSection()
        {
            var t = MakeGrid(4);

            // Mode
            t.Controls.Add(MakeLbl("Mode:"), 0, 0);
            _modeCombo = MakeCombo("Current Split", "Prev Split", "Prev Seg.");
            _modeCombo.SelectedIndex = (int)Mode;
            _modeCombo.SelectedIndexChanged += (s, e) =>
                Mode = (SplitDetailMode)_modeCombo.SelectedIndex;
            t.Controls.Add(_modeCombo, 1, 0);

            // Label: Current Split (independently editable)
            t.Controls.Add(MakeLbl("Label – Current:"), 0, 1);
            _labelCurrentBox = MakeTextBox(LabelCurrentSplit, 30);
            _labelCurrentBox.TextChanged += (s, e) => LabelCurrentSplit = _labelCurrentBox.Text;
            t.Controls.Add(_labelCurrentBox, 1, 1);

            // Label: Split suffix (generates Prev Split / Live Split)
            t.Controls.Add(MakeLbl("Label – Split:"), 0, 2);
            _labelSplitBox = MakeTextBox(LabelSplit, 20);
            _labelSplitBox.TextChanged += (s, e) => LabelSplit = _labelSplitBox.Text;
            t.Controls.Add(_labelSplitBox, 1, 2);

            // Label: Segment suffix (generates Prev Seg. / Live Seg.)
            t.Controls.Add(MakeLbl("Label – Segment:"), 0, 3);
            _labelSegmentBox = MakeTextBox(LabelSegment, 20);
            _labelSegmentBox.TextChanged += (s, e) => LabelSegment = _labelSegmentBox.Text;
            t.Controls.Add(_labelSegmentBox, 1, 3);

            return t;
        }

        // ── Comparisons ───────────────────────────────────────────────────────
        private Control BuildComparisonSection()
        {
            var t = MakeGrid(5);

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

            var note = new Label
            {
                Text      = "Priority delta keeps its full value when space is tight.\n" +
                            "Comparison 1 = left delta / top line.\n" +
                            "Comparison 2 = right delta / bottom line.",
                AutoSize  = true,
                ForeColor = SystemColors.GrayText,
            };
            t.SetColumnSpan(note, 2);
            t.Controls.Add(note, 0, 4);

            return t;
        }

        // ── Layout ────────────────────────────────────────────────────────────
        private Control BuildLayoutSection()
        {
            var t = MakeGrid(3);

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

            var note = new Label
            {
                Text      = "Separator: empty = no separator, just a small gap.\n" +
                            "Column spacing: gap between label and delta block.\n" +
                            "Outer padding is not affected.",
                AutoSize  = true,
                ForeColor = SystemColors.GrayText,
            };
            t.SetColumnSpan(note, 2);
            t.Controls.Add(note, 0, 2);

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
            var t = MakeGrid(2);

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

            return t;
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
            W(document, root, "Version",           "5");
            W(document, root, "Mode",              Mode.ToString());
            W(document, root, "Comparison1",       Comparison1);
            W(document, root, "Comparison2",       Comparison2);
            W(document, root, "ComparisonCount",   ComparisonCount.ToString());
            W(document, root, "PriorityDelta",     PriorityDelta.ToString());
            W(document, root, "LabelCurrentSplit", LabelCurrentSplit);
            W(document, root, "LabelSplit",        LabelSplit);
            W(document, root, "LabelSegment",      LabelSegment);
            W(document, root, "Separator",         Separator);
            W(document, root, "ColumnSpacing",     ColumnSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W(document, root, "Accuracy",          Accuracy.ToString());
            W(document, root, "TextColor",         ColorToHex(TextColor));
            W(document, root, "TimeColor",         ColorToHex(TimeColor));
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

            // Labels
            string lcs = R(node, "LabelCurrentSplit");
            if (!string.IsNullOrEmpty(lcs)) { LabelCurrentSplit = lcs; _labelCurrentBox.Text = lcs; }

            // Backward compatibility: if old LabelPrevSplit/LabelPrevSeg exist, try to infer the suffix
            string labelSplit = R(node, "LabelSplit");
            if (!string.IsNullOrEmpty(labelSplit))
            {
                LabelSplit = labelSplit;
                _labelSplitBox.Text = labelSplit;
            }
            else
            {
                // Try to infer from old LabelPrevSplit field (e.g., "Prev Split" → "Split")
                string oldPrevSplit = R(node, "LabelPrevSplit");
                if (!string.IsNullOrEmpty(oldPrevSplit) && oldPrevSplit.StartsWith("Prev "))
                {
                    string inferred = oldPrevSplit.Substring(5);  // remove "Prev "
                    LabelSplit = inferred;
                    _labelSplitBox.Text = inferred;
                }
            }

            string labelSegment = R(node, "LabelSegment");
            if (!string.IsNullOrEmpty(labelSegment))
            {
                LabelSegment = labelSegment;
                _labelSegmentBox.Text = labelSegment;
            }
            else
            {
                // Try to infer from old LabelPrevSeg field (e.g., "Prev Seg." → "Seg.")
                string oldPrevSeg = R(node, "LabelPrevSeg");
                if (!string.IsNullOrEmpty(oldPrevSeg) && oldPrevSeg.StartsWith("Prev "))
                {
                    string inferred = oldPrevSeg.Substring(5);  // remove "Prev "
                    LabelSegment = inferred;
                    _labelSegmentBox.Text = inferred;
                }
            }

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
