using Hanglish.Core;

namespace Hanglish;

/// <summary>The notification-area icon and its menu; owns the engine and the keyboard hook.</summary>
sealed class TrayApp : ApplicationContext
{
    string Help =>
        "영문 자판 상태에서 두벌식 자리 그대로 치면 됩니다.\n" +
        "띄어쓰기·엔터·문장부호를 누르는 순간 한글인지 영어인지 골라서 확정합니다.\n\n" +
        $"• {FlipKeys.Name(settings.FlipKeyCode)}: 지금 치는 단어나 방금 확정된 단어를 반대 언어로 바꿉니다.\n" +
        "   바로 한 번 더 누르면 되돌립니다. 바꾼 결과는 같은 앞 단어 뒤에서만 기억하고,\n" +
        "   서로 다른 두 곳에서 같은 교정을 하면 어디서나 적용합니다. 키는 설정에서 바꿀 수 있습니다.\n" +
        "• 뒤 단어로 언어가 분명해지면 앞의 애매한 단어도 고칩니다 (내 good → so good).\n" +
        "• Esc: 치던 단어를 영문 그대로 확정합니다.\n" +
        "• 커서 위 말풍선: 치는 동안 어떻게 확정될지 미리 보여 줍니다 (메뉴에서 끌 수 있음).\n" +
        "• 트레이 아이콘 두 번 클릭: 설정 열기\n\n" +
        "판단은 이 PC 안에서만 하며, 입력 내용은 어디에도 보내지 않습니다.";

    readonly Settings settings = Settings.Load();
    readonly CorrectionStore corrections = new(Path.Combine(Settings.Folder, "corrections.json"));
    readonly Engine engine;
    readonly KeyboardHook hook;
    readonly NotifyIcon tray;
    readonly CaretBubble bubble = new();
    // Waits for the app to draw the injected text before reading the caret position.
    readonly System.Windows.Forms.Timer bubbleDelay = new() { Interval = 35 };
    readonly Icon onIcon, offIcon;
    SettingsForm? settingsForm;

    public Settings Settings => settings;
    public CorrectionStore Corrections => corrections;
    public string? LastApp => hook.LastApp;

    public TrayApp(Decider decider)
    {
        using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("tray.ico")!)
            onIcon = new Icon(stream, SystemInformation.SmallIconSize);
        offIcon = Grayscale(onIcon);
        engine = new Engine(decider, corrections) { Preview = settings.Preview };
        hook = new KeyboardHook(engine)
        {
            Enabled = settings.Enabled, FlipKey = settings.FlipKeyCode, SkipSensitiveFields = settings.SkipSensitiveFields,
        };
        foreach (var app in settings.ExcludedApps) hook.ExcludedApps.Add(app);
        hook.KeyHandled += () => { bubbleDelay.Stop(); bubbleDelay.Start(); };
        bubbleDelay.Tick += (_, _) =>
        {
            bubbleDelay.Stop();
            bubble.Present(settings.Enabled && settings.CaretPreview ? engine.Guess() : null, settings.BubbleStyle);
        };
        hook.HangulKeyPressed += () =>
        {
            if (settings.ShownHangulKeyTip) return;
            settings.ShownHangulKeyTip = true;
            settings.Save();
            tray!.ShowBalloonTip(6000, "한/영 키는 안 눌러도 돼요",
                $"한글리쉬가 켜져 있으면 그냥 치면 됩니다. 틀린 단어는 {FlipKeys.Name(settings.FlipKeyCode)}를 누르세요. 한/영 키로 바꾸고 싶다면 설정 → 뒤집기 키에서 고를 수 있어요.", ToolTipIcon.Info);
        };

        tray = new NotifyIcon { Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        tray.DoubleClick += (_, _) => OpenSettings(0);
        BuildMenu();
        UpdateIcon();

        if (!settings.ShownWelcome)
        {
            tray.ShowBalloonTip(8000, "한글리쉬 실행 중",
                $"영문 자판으로 두고 그냥 치세요. 틀린 단어는 {FlipKeys.Name(settings.FlipKeyCode)}를 한 번 눌러 바꿔요. 이 키는 설정에서 한/영 키 등 원하는 키로 바꿀 수 있어요.", ToolTipIcon.Info);
            settings.ShownWelcome = true;
            settings.Save();
        }
    }

    void BuildMenu()
    {
        var menu = tray.ContextMenuStrip!;
        menu.Items.Clear();
        var open = Item("설정 열기...", false, OpenSettings);
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("한글리쉬 사용", settings.Enabled, () => SetEnabled(!settings.Enabled)));
        menu.Items.Add(Item("커서 위에 결과 미리 보기", settings.CaretPreview, () => SetCaretPreview(!settings.CaretPreview)));
        if (hook.LastApp is { } app)
        {
            bool excluded = hook.ExcludedApps.Contains(app);
            menu.Items.Add(Item($"{app}에서는 쓰지 않기", excluded, () => SetExcluded(app, !excluded)));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("사용법", false, () => MessageBox.Show(Help, "한글리쉬")));
        menu.Items.Add(Item("끝내기", false, ExitThread));
    }

    static ToolStripMenuItem Item(string text, bool check, Action click)
    {
        var item = new ToolStripMenuItem(text) { Checked = check };
        item.Click += (_, _) => click();
        return item;
    }

    public void OpenSettings() => OpenSettings(0);

    /// <summary>Opens the settings window whenever another launch of the app signals it.</summary>
    public void ListenForShow(EventWaitHandle signal)
    {
        var ui = SynchronizationContext.Current!;
        var thread = new Thread(() =>
        {
            while (signal.WaitOne()) ui.Post(_ => OpenSettings(0), null);
        }) { IsBackground = true, Name = "Hanglish show signal" };
        thread.Start();
    }

    public void OpenSettings(int section)
    {
        if (settingsForm is { IsDisposed: false }) { settingsForm.Activate(); return; }
        settingsForm = new SettingsForm(this, section);
        settingsForm.Show();
        settingsForm.Activate();
    }

    public void SetEnabled(bool on)
    {
        settings.Enabled = hook.Enabled = on;
        engine.Reset();
        if (!on) bubble.Hide();
        settings.Save();
        UpdateIcon();
    }

    public void SetPreview(PreviewMode mode)
    {
        settings.Preview = engine.Preview = mode;
        engine.Reset();
        settings.Save();
    }

    /// <summary>Waits for the next key press and makes it the flip key; reports the key, or null if cancelled.</summary>
    public void RecordFlipKey(Action<int?> done)
    {
        var ui = SynchronizationContext.Current!;
        hook.RecordNextKey = vk => ui.Post(_ =>
        {
            if (vk is int key) SetFlip(key);
            done(vk);
        }, null);
    }

    public void CancelRecording() => hook.RecordNextKey = null;

    public void SetFlip(int vk)
    {
        settings.FlipKeyCode = hook.FlipKey = vk;
        settings.Save();
    }

    public void SetCaretPreview(bool on)
    {
        settings.CaretPreview = on;
        if (!on) bubble.Hide();
        settings.Save();
    }

    public void SetSkipSensitiveFields(bool on)
    {
        settings.SkipSensitiveFields = hook.SkipSensitiveFields = on;
        settings.Save();
    }

    public void SetBubbleStyle(BubbleStyle style)
    {
        settings.BubbleStyle = style;
        settings.Save();
    }

    public void SetExcluded(string app, bool excluded)
    {
        if (excluded) { hook.ExcludedApps.Add(app); if (!settings.ExcludedApps.Contains(app)) settings.ExcludedApps.Add(app); }
        else { hook.ExcludedApps.Remove(app); settings.ExcludedApps.Remove(app); }
        settings.Save();
    }

    void UpdateIcon()
    {
        tray.Icon = settings.Enabled ? onIcon : offIcon;
        tray.Text = settings.Enabled ? "한글리쉬: 켜짐" : "한글리쉬: 꺼짐";
    }

    /// <summary>The tray icon washed out to gray, for the "off" state.</summary>
    static Icon Grayscale(Icon icon)
    {
        using var source = icon.ToBitmap();
        using var gray = new Bitmap(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                int v = (int)(c.R * 0.3 + c.G * 0.59 + c.B * 0.11);
                v = 120 + v / 3;   // lighter and flatter, so it reads as disabled
                gray.SetPixel(x, y, Color.FromArgb(c.A, v, v, v));
            }
        var handle = gray.GetHicon();
        var result = (Icon)Icon.FromHandle(handle).Clone();
        Native.DestroyIcon(handle);
        return result;
    }

    protected override void ExitThreadCore()
    {
        hook.Dispose();
        bubble.Close();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}
