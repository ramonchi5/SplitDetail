// ============================================================================
// SplitDetailSettings.cs
// Settings UserControl for the SplitDetail component.
//
// All controls are created in code (no .Designer.cs / .resx needed).
//
// Stores:
//   Mode        — SplitDetailMode enum
//   Comparison  — any comparison available in the current run
//   Separator   — character(s) shown between the two deltas
//   TextColor   — color for left label, "PB"/"Best" labels, separator
//   TimeColor   — color for right-side time and PB/Best time values
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;

namespace LiveSplit.UI.Components
{
    public class SplitDetailSettings : UserControl
    {
        // ── Public properties (read by the component each draw/update) ────────
        public SplitDetailMode Mode       { get; private set; } = SplitDetailMode.CurrentSplit;
        public string          Comparison { get; private set; } = "Best Segments";
        public string          Separator  { get; private set; } = "|";

        // Colors — default to white to match typical LiveSplit text colors.
        public Color           TextColor  { get; private set; } = Color.White;
        public Color           TimeColor  { get; private set; } = Color.White;

        // ── Private ───────────────────────────────────────────────────────────
        private readonly LiveSplitState _state;

        private ComboBox _modeCombo;
        private ComboBox _compCombo;
        private TextBox  _sepBox;
        private Button   _textColorBtn;
        private Button   _timeColorBtn;

        // ── Constructor ───────────────────────────────────────────────────────
        public SplitDetailSettings(LiveSplitState state)
        {
            _state = state;
            BuildUI();
        }

        // =====================================================================
        // UI Construction
        // =====================================================================
        private void BuildUI()
        {
            AutoSize   = true;
            AutoScroll = true;
            Padding    = new Padding(10);

            var table = new TableLayoutPanel
            {
                AutoSize    = true,
                ColumnCount = 2,
                RowCount    = 5,   // Mode / Comparison / Separator / TextColor / TimeColor
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100f));

            int row = 0;

            // ── Mode ─────────────────────────────────────────────────────────
            table.Controls.Add(MakeLabel("Mode:"), 0, row);

            _modeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock          = DockStyle.Fill,
            };
            _modeCombo.Items.AddRange(new object[]
            {
                "Current Split",
                "Prior Split",
                "Prior Subsplit",
            });
            _modeCombo.SelectedIndex = (int)Mode;
            _modeCombo.SelectedIndexChanged += (s, e) =>
                Mode = (SplitDetailMode)_modeCombo.SelectedIndex;
            table.Controls.Add(_modeCombo, 1, row);
            row++;

            // ── Comparison ───────────────────────────────────────────────────
            table.Controls.Add(MakeLabel("Comparison:"), 0, row);

            _compCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock          = DockStyle.Fill,
            };
            _compCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_compCombo.SelectedItem != null)
                    Comparison = _compCombo.SelectedItem.ToString();
            };
            table.Controls.Add(_compCombo, 1, row);
            row++;

            // ── Separator ────────────────────────────────────────────────────
            table.Controls.Add(MakeLabel("Separator:"), 0, row);

            _sepBox = new TextBox { Text = Separator, MaxLength = 5, Width = 40 };
            _sepBox.TextChanged += (s, e) =>
                Separator = string.IsNullOrEmpty(_sepBox.Text) ? "|" : _sepBox.Text;
            table.Controls.Add(_sepBox, 1, row);
            row++;

            // ── Text Color ───────────────────────────────────────────────────
            // Applies to: mode label, "PB"/"Best" labels, separator.
            table.Controls.Add(MakeLabel("Text Color:"), 0, row);

            _textColorBtn = MakeColorButton(TextColor, () => TextColor,
                                            c => TextColor = c);
            table.Controls.Add(_textColorBtn, 1, row);
            row++;

            // ── Time Color ───────────────────────────────────────────────────
            // Applies to: right-side time, PB/Best time values.
            table.Controls.Add(MakeLabel("Time Color:"), 0, row);

            _timeColorBtn = MakeColorButton(TimeColor, () => TimeColor,
                                            c => TimeColor = c);
            table.Controls.Add(_timeColorBtn, 1, row);
            row++;

            // ── Help text ────────────────────────────────────────────────────
            var help = new Label
            {
                Text =
                    "Subsplits are detected by the \"-\" prefix on segment names.\r\n" +
                    "If your splits use a different prefix, change SubsplitPrefix\r\n" +
                    "in SplitDetailComponent.cs.\r\n\r\n" +
                    "Delta colors always follow LiveSplit's ahead/behind colors.",
                AutoSize  = true,
                ForeColor = SystemColors.GrayText,
                Margin    = new Padding(0, 10, 0, 0),
            };
            table.SetColumnSpan(help, 2);
            table.Controls.Add(help, 0, row);

            Controls.Add(table);
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        private static Label MakeLabel(string text) => new Label
        {
            Text      = text,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        /// <summary>
        /// Creates a Button whose BackColor reflects the current color.
        /// Clicking it opens a ColorDialog and writes the result to the property.
        /// </summary>
        private Button MakeColorButton(Color initial,
                                        Func<Color> getter,
                                        Action<Color> setter)
        {
            var btn = new Button
            {
                BackColor = initial,
                Width     = 50,
                Height    = 23,
                FlatStyle = FlatStyle.Flat,
            };
            btn.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = getter() })
                {
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        setter(dlg.Color);
                        btn.BackColor = dlg.Color;
                    }
                }
            };
            return btn;
        }

        // =====================================================================
        // Comparison list refresh
        // =====================================================================

        /// <summary>
        /// Populates the Comparison drop-down from the currently loaded run.
        /// Call every time the settings panel is opened.
        /// </summary>
        public void RefreshComparisons()
        {
            string selected = Comparison;
            _compCombo.Items.Clear();

            if (_state?.Run != null)
            {
                foreach (string comp in _state.Run.Comparisons)
                    _compCombo.Items.Add(comp);
            }

            if (!string.IsNullOrEmpty(selected) && _compCombo.Items.Contains(selected))
                _compCombo.SelectedItem = selected;
            else if (_compCombo.Items.Count > 0)
                _compCombo.SelectedIndex = 0;
        }

        // =====================================================================
        // XML persistence
        // =====================================================================

        public XmlNode GetSettings(XmlDocument document)
        {
            XmlElement root = document.CreateElement("Settings");
            Append(document, root, "Version",    "2");
            Append(document, root, "Mode",       Mode.ToString());
            Append(document, root, "Comparison", Comparison);
            Append(document, root, "Separator",  Separator);
            Append(document, root, "TextColor",  ColorToString(TextColor));
            Append(document, root, "TimeColor",  ColorToString(TimeColor));
            return root;
        }

        public void SetSettings(XmlNode settings)
        {
            if (settings == null) return;

            // Mode
            string modeStr = Read(settings, "Mode");
            if (modeStr != null && Enum.TryParse(modeStr, out SplitDetailMode m))
            {
                Mode = m;
                _modeCombo.SelectedIndex = (int)Mode;
            }

            // Comparison (combo may not be populated yet — RefreshComparisons handles it)
            string comp = Read(settings, "Comparison");
            if (!string.IsNullOrEmpty(comp))
            {
                Comparison = comp;
                if (_compCombo.Items.Contains(comp))
                    _compCombo.SelectedItem = comp;
            }

            // Separator
            string sep = Read(settings, "Separator");
            if (!string.IsNullOrEmpty(sep))
            {
                Separator    = sep;
                _sepBox.Text = sep;
            }

            // Text Color
            string textColorStr = Read(settings, "TextColor");
            if (!string.IsNullOrEmpty(textColorStr))
            {
                Color c = StringToColor(textColorStr);
                TextColor          = c;
                _textColorBtn.BackColor = c;
            }

            // Time Color
            string timeColorStr = Read(settings, "TimeColor");
            if (!string.IsNullOrEmpty(timeColorStr))
            {
                Color c = StringToColor(timeColorStr);
                TimeColor          = c;
                _timeColorBtn.BackColor = c;
            }
        }

        // ── XML helpers ───────────────────────────────────────────────────────
        private static void Append(XmlDocument doc, XmlElement parent,
                                    string name, string value)
        {
            var el = doc.CreateElement(name);
            el.InnerText = value ?? string.Empty;
            parent.AppendChild(el);
        }

        private static string Read(XmlNode parent, string childName) =>
            parent[childName]?.InnerText;

        // ── Color serialization ───────────────────────────────────────────────
        // Stored as ARGB hex: "FFFFFFFF" = opaque white.

        private static string ColorToString(Color c) =>
            c.ToArgb().ToString("X8");

        private static Color StringToColor(string s)
        {
            try
            {
                int argb = Convert.ToInt32(s, 16);
                return Color.FromArgb(argb);
            }
            catch
            {
                return Color.White; // fallback on parse error
            }
        }
    }
}
