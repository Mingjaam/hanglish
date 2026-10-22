using System.Diagnostics;
using System.Reflection;
using Hanglish.Core;

namespace Hanglish;

/// <summary>The settings window: a sidebar of sections, each applying its changes immediately.</summary>
sealed class SettingsForm : Form
{
    static readonly Color Sidebar = Color.FromArgb(246, 247, 250), Ink = Color.FromArgb(25, 27, 38),
        Muted = Color.FromArgb(118, 124, 145), Accent = Color.FromArgb(255, 107, 74), Line = Color.FromArgb(232, 234, 241);

    readonly TrayApp app;
    readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true };
    readonly FlowLayoutPanel nav = new() { Dock = DockStyle.Left, BackColor = Sidebar, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly List<Button> navButtons = new();

    /// <summary>
    /// Layout is written in 96-DPI pixels; this scales them to the display, since controls built in
    /// code after the form exists are not auto-scaled by WinForms.
    /// </summary>
    static float scale = 1;
    static int P(float pixels) => (int)Math.Round(pixels * scale);
    static Padding P(int l, int t, int r, int b) => new(P(l), P(t), P(r), P(b));

    public SettingsForm(TrayApp app, int section = 0)
    {
        this.app = app;
        scale = DeviceDpi / 96f;
        content.Padding = P(28, 22, 28, 22);
        nav.Width = P(176);
        nav.Padding = P(10, 16, 10, 10);
        Text = "한글리쉬 설정";
        Font = new Font("Malgun Gothic", 9.75f);
        BackColor = Color.White;
        ForeColor = Ink;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(P(720), P(540));
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")!) Icon = new Icon(stream);

        Controls.Add(content);
        Controls.Add(nav);
        nav.Controls.Add(new Label
        {
            Text = "한글리쉬", Font = new Font(Font.FontFamily, 13f, FontStyle.Bold), AutoSize = true,
            Margin = P(8, 0, 0, 14),
        });
        AddSection("일반", General);
        AddSection("키", Keys);
        AddSection("말풍선", Bubble);
        AddSection("쓰지 않는 앱", Apps);
        AddSection("저장된 교정", Corrections);
        AddSection("정보", About);
        Show(Math.Clamp(section, 0, navButtons.Count - 1));
        // The preview toggle key changes what the 일반 page shows.
        Action follow = () => { if (shown == 0) Show(0); };
        app.PreviewChanged += follow;
        FormClosed += (_, _) => { app.PreviewChanged -= follow; app.CancelRecording(); };
    }

    void AddSection(string title, Action<Page> build)
    {
        int index = navButtons.Count;
        var button = new Button
        {
            Text = title, Width = P(154), Height = P(36), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
            Padding = P(8, 0, 0, 0), Margin = P(0, 0, 0, 4), Cursor = Cursors.Hand, Tag = build,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 238, 245);
        button.Click += (_, _) => Show(index);
        navButtons.Add(button);
        nav.Controls.Add(button);
    }

    int shown = -1;

    void Show(int index)
    {
        shown = index;
        for (int i = 0; i < navButtons.Count; i++)
        {
            navButtons[i].BackColor = i == index ? Color.White : Sidebar;
            navButtons[i].Font = new Font(Font, i == index ? FontStyle.Bold : FontStyle.Regular);
            navButtons[i].ForeColor = i == index ? Ink : Muted;
        }
        content.SuspendLayout();
        content.Controls.Clear();
        var page = new Page(content);
        page.Heading(navButtons[index].Text);
        ((Action<Page>)navButtons[index].Tag!)(page);
        content.ResumeLayout();
    }

    void General(Page page)
    {
        page.Check("한글리쉬 사용", app.Settings.Enabled, v => app.SetEnabled(v));
        page.Check("Windows 시작할 때 실행", Settings.StartsWithWindows, v => Settings.StartsWithWindows = v);
        page.Check("비밀번호·인증번호 칸에서는 쉬기", app.Settings.SkipSensitiveFields, v => app.SetSkipSensitiveFields(v));
        page.Subheading("입력하는 동안");
        page.Radio(new[] { ("한글로 미리 보기", "치는 동안 한글로 보이고, 단어가 끝날 때 영어면 바뀝니다.") ,
                           ("영문 그대로 보이기", "치는 동안 영문 그대로 보이고, 한글이면 단어 끝에서 바뀝니다.") },
            app.Settings.Preview == PreviewMode.Hangul ? 0 : 1,
            i => app.SetPreview(i == 0 ? PreviewMode.Hangul : PreviewMode.Raw));
        if (app.Settings.PreviewKeyCode != FlipKeys.None)
            page.Note($"치는 중에도 {FlipKeys.Name(app.Settings.PreviewKeyCode)}로 바로 바꿀 수 있어요. 키는 '키' 메뉴에서 바꿀 수 있어요.");
    }

    void Keys(Page page)
    {
        page.Subheading("뒤집기 키");
        page.Note("지금 치는 단어나 방금 확정된 단어를 반대 언어로 바꾸는 키입니다. 기본은 오른쪽 Ctrl이고, Caps Lock 등 원하는 키로 바꿀 수 있어요. Ctrl·Alt 같은 키는 혼자 눌렀다 뗄 때만 동작해서 단축키에는 영향이 없어요.");
        KeyPicker(page, FlipKeys.Presets, FlipKeys.Default, () => app.Settings.FlipKeyCode, app.SetFlip,
            () => app.Settings.PreviewKeyCode, "미리보기 전환 키");
        page.Subheading("미리보기 전환 키");
        page.Note("치는 동안 한글로 보일지 영문 그대로 보일지 바꾸는 키입니다. 단어를 치는 중에 눌러도 그 단어가 바로 바뀌어 보여요. 기본은 한/영 키이고, 다른 키로 바꾸거나 끌 수 있어요.");
        KeyPicker(page, FlipKeys.PreviewPresets, FlipKeys.PreviewDefault, () => app.Settings.PreviewKeyCode, app.SetPreviewKey,
            () => app.Settings.FlipKeyCode, "뒤집기 키");
    }

    /// <summary>
    /// Preset radios plus "pick another key" for one key setting. A key the other setting already uses
    /// cannot be chosen, so the two never collide.
    /// </summary>
    void KeyPicker(Page page, int[] presets, int defaultKey, Func<int> current, Action<int> set, Func<int> other, string otherName)
    {
        var custom = new Label { AutoSize = true, ForeColor = Muted, Margin = P(0, 6, 0, 0) };
        void ShowCurrent(string? problem = null) =>
            custom.Text = problem ?? $"지금 키: {FlipKeys.Name(current())}";
        ShowCurrent();
        foreach (int vk in presets)
        {
            bool taken = FlipKeys.Same(vk, other());
            var radio = new RadioButton
            {
                Text = FlipKeys.Name(vk) + (vk == defaultKey ? " (기본)" : "") + (taken ? $" — {otherName}로 사용 중" : ""),
                Checked = vk == current(), Enabled = !taken, AutoSize = true, Margin = P(0, 2, 0, 0),
            };
            // Rebuilt afterwards so the other picker sees the change.
            radio.CheckedChanged += (_, _) => { if (radio.Checked && vk != current()) { set(vk); BeginInvoke(() => Show(shown)); } };
            page.Add(radio);
        }
        Button? record = null;
        record = page.Button("다른 키로 바꾸기...", () =>
        {
            record!.Text = "바꿀 키를 누르세요 (Esc: 취소)";
            record.Enabled = false;
            app.RecordKey(vk =>
            {
                if (IsDisposed) return;
                if (vk is int key && FlipKeys.Same(key, other()))
                {
                    record.Text = "다른 키로 바꾸기...";
                    record.Enabled = true;
                    ShowCurrent($"{FlipKeys.Name(key)}은(는) 이미 {otherName}로 쓰고 있어요");
                    return;
                }
                if (vk is int chosen) set(chosen);
                Show(shown);
            });
        });
        page.Row(record, custom);
    }

    void Bubble(Page page)
    {
        page.Check("커서 위에 결과 미리 보기", app.Settings.CaretPreview, v => app.SetCaretPreview(v));
        page.Note("단어를 다 치기 전에 어떻게 확정될지 커서 바로 위에 보여 줍니다.");
        page.Subheading("디자인");
        var styles = new[] { (BubbleStyle.White, "흰색"), (BubbleStyle.Dark, "어두운색"), (BubbleStyle.Minimal, "간단히") };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = P(0, 4, 0, 0) };
        var sample = new Guess("김민재", true, "rlaalswo", 0.99);
        var cards = new List<Panel>();
        foreach (var (style, name) in styles)
        {
            var card = new Panel
            {
                Size = new Size(P(150), P(84)), Margin = P(0, 0, 10, 10), Cursor = Cursors.Hand,
                BackColor = style == BubbleStyle.Dark ? Color.FromArgb(232, 235, 242) : Color.FromArgb(241, 243, 248),
            };
            var picture = new PictureBox
            {
                Image = BubbleRenderer.Render(sample, style, scale), SizeMode = PictureBoxSizeMode.CenterImage,
                Bounds = new Rectangle(P(4), P(4), P(142), P(50)), Cursor = Cursors.Hand, BackColor = Color.Transparent,
            };
            var label = new Label { Text = name, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(0, P(56), P(150), P(24)) };
            card.Controls.Add(picture);
            card.Controls.Add(label);
            void Select()
            {
                app.SetBubbleStyle(style);
                foreach (var c in cards) c.Invalidate();
            }
            card.Click += (_, _) => Select();
            picture.Click += (_, _) => Select();
            label.Click += (_, _) => Select();
            card.Paint += (_, e) =>
            {
                if (app.Settings.BubbleStyle != style) return;
                float width = 2 * scale;
                using var pen = new Pen(Accent, width);
                e.Graphics.DrawRectangle(pen, width / 2, width / 2, card.Width - width, card.Height - width);
            };
            cards.Add(card);
            row.Controls.Add(card);
        }
        page.Add(row);
    }

    void Apps(Page page)
    {
        page.Note("여기 있는 프로그램에서는 한글리쉬가 키를 건드리지 않습니다. 게임, vim처럼 글자 키를 단축키로 쓰는 프로그램에 좋아요.");
        var list = new ListBox { Width = P(440), Height = P(220), IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        void Refresh() { list.Items.Clear(); foreach (var a in app.Settings.ExcludedApps) list.Items.Add(a); }
        Refresh();
        page.Add(list);
        var input = new TextBox { Width = P(220), PlaceholderText = "프로그램 이름 (예: notepad)" };
        var add = page.Button("추가", () =>
        {
            var name = Path.GetFileNameWithoutExtension(input.Text.Trim());
            if (name.Length == 0) return;
            app.SetExcluded(name, true);
            input.Clear();
            Refresh();
        });
        var remove = page.Button("선택한 앱 다시 쓰기", () =>
        {
            if (list.SelectedItem is string name) { app.SetExcluded(name, false); Refresh(); }
        });
        page.Row(input, add, remove);
        if (app.LastApp is { } last && !app.Settings.ExcludedApps.Contains(last))
            page.Row(page.Button($"방금 쓰던 앱 추가: {last}", () => { app.SetExcluded(last, true); Refresh(); }));
    }

    void Corrections(Page page)
    {
        page.Note("뒤집기 키로 바꾼 단어들입니다. \"앞 단어 뒤에서만\"은 그 앞 단어 다음에 칠 때만 적용됩니다.");
        var view = new ListView
        {
            View = View.Details, FullRowSelect = true, Width = P(470), Height = P(250), BorderStyle = BorderStyle.FixedSingle,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        view.Columns.Add("친 키", P(130));
        view.Columns.Add("결과", P(130));
        view.Columns.Add("적용", P(190));
        void Refresh()
        {
            view.Items.Clear();
            foreach (var c in app.Corrections.Entries)
                view.Items.Add(new ListViewItem(new[]
                {
                    c.Raw, c.Preferred,
                    c.Reach == CorrectionReach.Everywhere ? "어디서나" : c.Before.Length == 0 ? "맨 앞에서" : $"'{c.Before}' 뒤에서만",
                }) { Tag = c });
        }
        Refresh();
        page.Add(view);
        page.Row(
            page.Button("선택 지우기", () =>
            {
                foreach (ListViewItem item in view.SelectedItems) app.Corrections.Remove((Correction)item.Tag!);
                Refresh();
            }),
            page.Button("모두 지우기", () =>
            {
                if (app.Corrections.Count == 0) return;
                if (MessageBox.Show(this, $"기억한 교정 {app.Corrections.Count}개를 모두 지울까요?", "한글리쉬",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    app.Corrections.Clear();
                    Refresh();
                }
            }));
    }

    void About(Page page)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon-128.png")!)
            page.Add(new PictureBox { Image = Image.FromStream(stream), Size = new Size(P(80), P(80)), SizeMode = PictureBoxSizeMode.Zoom });
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        page.Subheading($"한글리쉬 Hanglish {version?.ToString(3)}");
        page.Note("영문 자판 그대로 두벌식 자리를 치면, 단어가 끝날 때 한글로 쓸지 영어로 쓸지 골라 줍니다. "
                  + "판단은 이 PC 안에서만 하며 입력 내용을 보내거나 저장하지 않습니다.");
        var link = new LinkLabel { Text = "github.com/Mingjaam/hanglish", AutoSize = true, Margin = P(0, 6, 0, 6) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/Mingjaam/hanglish") { UseShellExecute = true });
        page.Add(link);
        page.Note("MIT License · 단어 빈도 데이터: FrequencyWords, wordfreq (CC BY-SA 4.0)");
    }

    /// <summary>Stacks a section's controls top to bottom.</summary>
    sealed class Page
    {
        readonly FlowLayoutPanel flow;

        public Page(Panel host)
        {
            flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            host.Controls.Add(flow);
        }

        public void Add(Control control) => flow.Controls.Add(control);

        public void Heading(string text) => Add(new Label
        {
            Text = text, AutoSize = true, Font = new Font("Malgun Gothic", 15f, FontStyle.Bold), Margin = P(0, 0, 0, 14),
        });

        public void Subheading(string text) => Add(new Label
        {
            Text = text, AutoSize = true, Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold), Margin = P(0, 16, 0, 6),
        });

        public void Note(string text) => Add(new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(P(470), 0), ForeColor = Muted, Margin = P(0, 2, 0, 8),
        });

        public void Check(string text, bool value, Action<bool> changed)
        {
            var box = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = P(0, 4, 0, 6) };
            box.CheckedChanged += (_, _) => changed(box.Checked);
            Add(box);
        }

        public void Radio((string Title, string Detail)[] options, int selected, Action<int> changed)
        {
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var radio = new RadioButton { Text = options[i].Title, Checked = i == selected, AutoSize = true, Margin = P(0, 4, 0, 0) };
                radio.CheckedChanged += (_, _) => { if (radio.Checked) changed(index); };
                Add(radio);
                Add(new Label
                {
                    Text = options[i].Detail, AutoSize = true, MaximumSize = new Size(P(450), 0), ForeColor = Muted,
                    Margin = P(20, 0, 0, 6),
                });
            }
        }

        public Button Button(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, Height = P(30), FlatStyle = FlatStyle.System, Margin = P(0, 8, 8, 0) };
            button.Click += (_, _) => click();
            return button;
        }

        public void Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = P(0, 4, 0, 0) };
            row.Controls.AddRange(controls);
            Add(row);
        }
    }
}
