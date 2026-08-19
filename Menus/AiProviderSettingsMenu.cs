using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using VivantValley.Services;
using StardewValley;
using StardewValley.Menus;

namespace VivantValley.Menus;

public readonly record struct AiSettingsSaveResult(bool Success, string Message);

/// <summary>Native in-game editor for the mod-wide AI connection profiles.</summary>
public sealed class AiProviderSettingsMenu : IClickableMenu
{
    private const int PanelMargin = 24;
    private const int ControlHeight = 48;
    private const int RowGap = 58;

    private readonly Func<AiProviderSettingsDraft, AiSettingsSaveResult> onSave;
    private readonly Func<AiProviderSettingsDraft, CancellationToken, Task<string>> onTest;
    private readonly Action onCancel;
    private readonly Dictionary<string, DraftState> drafts;
    private readonly TextBox baseUrlBox;
    private readonly TextBox modelBox;
    private readonly TextBox apiKeyBox;
    private readonly ClickableComponent deepSeekTab = new(Rectangle.Empty, "DeepSeek");
    private readonly ClickableComponent openAiTab = new(Rectangle.Empty, "OpenAI");
    private readonly ClickableComponent saveButton = new(Rectangle.Empty, "save");
    private readonly ClickableComponent testButton = new(Rectangle.Empty, "test");
    private readonly ClickableComponent clearKeyButton = new(Rectangle.Empty, "clear-key");
    private CancellationTokenSource testCancellation = new();
    private Task<string>? pendingTest;
    private string activeProvider;
    private string statusText = string.Empty;
    private Color statusColor = Game1.textColor;
    private bool closed;

    public AiProviderSettingsMenu(
        AiProviderSettings settings,
        Func<AiProviderSettingsDraft, AiSettingsSaveResult> onSave,
        Func<AiProviderSettingsDraft, CancellationToken, Task<string>> onTest,
        Action onCancel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.onSave = onSave ?? throw new ArgumentNullException(nameof(onSave));
        this.onTest = onTest ?? throw new ArgumentNullException(nameof(onTest));
        this.onCancel = onCancel ?? throw new ArgumentNullException(nameof(onCancel));

        drafts = new Dictionary<string, DraftState>(StringComparer.Ordinal)
        {
            [AiProviderNames.DeepSeek] = DraftState.From(settings.DeepSeek),
            [AiProviderNames.OpenAI] = DraftState.From(settings.OpenAI),
        };
        activeProvider = AiProviderNames.Normalize(settings.ActiveProvider);

        Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
        baseUrlBox = CreateTextBox(textBoxTexture, 512, password: false);
        modelBox = CreateTextBox(textBoxTexture, 160, password: false);
        apiKeyBox = CreateTextBox(textBoxTexture, 512, password: true);

        Reposition();
        LoadActiveDraft();
        SelectTextBox(baseUrlBox);
    }

    public override void update(GameTime time)
    {
        base.update(time);
        if (pendingTest is null || !pendingTest.IsCompleted)
            return;

        Task<string> completed = pendingTest;
        pendingTest = null;
        try
        {
            statusText = completed.GetAwaiter().GetResult();
            statusColor = Color.DarkGreen;
            Game1.playSound("coin");
        }
        catch (OperationCanceledException)
        {
            statusText = "测试已取消。";
            statusColor = Color.Gray;
        }
        catch (Exception ex)
        {
            statusText = LimitStatus(ex.Message);
            statusColor = Color.DarkRed;
            Game1.playSound("cancel");
        }
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        Reposition();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton?.containsPoint(x, y) == true)
        {
            Cancel();
            return;
        }
        if (deepSeekTab.containsPoint(x, y))
        {
            SwitchProvider(AiProviderNames.DeepSeek);
            return;
        }
        if (openAiTab.containsPoint(x, y))
        {
            SwitchProvider(AiProviderNames.OpenAI);
            return;
        }
        if (saveButton.containsPoint(x, y))
        {
            Save();
            return;
        }
        if (testButton.containsPoint(x, y))
        {
            StartTest();
            return;
        }
        if (clearKeyButton.containsPoint(x, y))
        {
            DraftState state = drafts[activeProvider];
            state.ClearSavedKey = !state.ClearSavedKey;
            state.ReplacementKey = string.Empty;
            apiKeyBox.Text = string.Empty;
            statusText = state.ClearSavedKey ? "保存后会清除当前提供商的 Key。" : string.Empty;
            statusColor = state.ClearSavedKey ? Color.DarkRed : Game1.textColor;
            Game1.playSound("smallSelect");
            return;
        }

        if (Contains(baseUrlBox, x, y))
            SelectTextBox(baseUrlBox);
        else if (Contains(modelBox, x, y))
            SelectTextBox(modelBox);
        else if (Contains(apiKeyBox, x, y))
            SelectTextBox(apiKeyBox);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            Cancel();
            return;
        }
        if (key == Keys.Tab)
        {
            if (baseUrlBox.Selected)
                SelectTextBox(modelBox);
            else if (modelBox.Selected)
                SelectTextBox(apiKeyBox);
            else
                SelectTextBox(baseUrlBox);
            return;
        }

        if (!baseUrlBox.Selected && !modelBox.Selected && !apiKeyBox.Selected)
            base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button == Buttons.B)
        {
            Cancel();
            return;
        }
        base.receiveGamePadButton(button);
    }

    public override void performHoverAction(int x, int y)
    {
        baseUrlBox.Hover(x, y);
        modelBox.Hover(x, y);
        apiKeyBox.Hover(x, y);
        SetHoverScale(deepSeekTab, x, y);
        SetHoverScale(openAiTab, x, y);
        SetHoverScale(saveButton, x, y);
        SetHoverScale(testButton, x, y);
        SetHoverScale(clearKeyButton, x, y);
        upperRightCloseButton?.tryHover(x, y, 0.2f);
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(
            Game1.fadeToBlackRect,
            new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
            Color.Black * 0.45f);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        b.DrawString(
            Game1.dialogueFont,
            "AI 提供商设置",
            new Vector2(xPositionOnScreen + PanelMargin, yPositionOnScreen + 20),
            Game1.textColor);

        DrawTab(b, deepSeekTab, "DeepSeek", activeProvider == AiProviderNames.DeepSeek);
        DrawTab(b, openAiTab, "OpenAI (GPT)", activeProvider == AiProviderNames.OpenAI);

        DrawLabel(b, "API 基础地址（Base URL）", baseUrlBox.X, baseUrlBox.Y - 26);
        baseUrlBox.Draw(b, drawShadow: false);
        DrawLabel(b, "模型", modelBox.X, modelBox.Y - 26);
        modelBox.Draw(b, drawShadow: false);
        DrawLabel(b, GetKeyLabel(), apiKeyBox.X, apiKeyBox.Y - 26);
        apiKeyBox.Draw(b, drawShadow: false);

        DrawButton(b, clearKeyButton, drafts[activeProvider].ClearSavedKey ? "保留 Key" : "清除 Key", false);
        DrawButton(b, testButton, pendingTest is null ? "测试连接" : "测试中…", pendingTest is not null);
        DrawButton(b, saveButton, "保存", false);

        if (statusText.Length > 0)
        {
            b.DrawString(
                Game1.smallFont,
                LimitStatus(statusText),
                new Vector2(xPositionOnScreen + PanelMargin, saveButton.bounds.Y - 36),
                statusColor);
        }

        upperRightCloseButton?.draw(b);
        drawMouse(b);
    }

    protected override void cleanupBeforeExit()
    {
        ClearKeyboardSubscriber();
        testCancellation.Cancel();
        testCancellation.Dispose();
        if (!closed)
        {
            closed = true;
            onCancel();
        }
        base.cleanupBeforeExit();
    }

    private void Save()
    {
        if (pendingTest is not null)
            return;
        StoreActiveDraft();
        AiSettingsSaveResult result = onSave(CreateDraft());
        if (!result.Success)
        {
            statusText = LimitStatus(result.Message);
            statusColor = Color.DarkRed;
            Game1.playSound("cancel");
            return;
        }

        statusText = result.Message;
        statusColor = Color.DarkGreen;
        closed = true;
        Game1.playSound("coin");
        if (ReferenceEquals(Game1.activeClickableMenu, this))
            exitThisMenuNoSound();
    }

    private void StartTest()
    {
        if (pendingTest is not null)
            return;
        StoreActiveDraft();
        statusText = "正在连接…";
        statusColor = Color.Gray;
        testCancellation.Cancel();
        testCancellation.Dispose();
        testCancellation = new CancellationTokenSource();
        pendingTest = onTest(CreateDraft(), testCancellation.Token);
        Game1.playSound("smallSelect");
    }

    private void Cancel()
    {
        if (closed)
            return;
        closed = true;
        Game1.playSound("bigDeSelect");
        if (ReferenceEquals(Game1.activeClickableMenu, this))
            exitThisMenuNoSound();
        onCancel();
    }

    private void SwitchProvider(string provider)
    {
        if (activeProvider == provider)
            return;
        StoreActiveDraft();
        activeProvider = provider;
        LoadActiveDraft();
        statusText = string.Empty;
        Game1.playSound("smallSelect");
    }

    private void StoreActiveDraft()
    {
        DraftState state = drafts[activeProvider];
        state.BaseUrl = (baseUrlBox.Text ?? string.Empty).Trim();
        state.Model = (modelBox.Text ?? string.Empty).Trim();
        state.ReplacementKey = (apiKeyBox.Text ?? string.Empty).Trim();
        if (state.ReplacementKey.Length > 0)
            state.ClearSavedKey = false;
    }

    private void LoadActiveDraft()
    {
        DraftState state = drafts[activeProvider];
        baseUrlBox.Text = state.BaseUrl;
        modelBox.Text = state.Model;
        apiKeyBox.Text = state.ReplacementKey;
    }

    private AiProviderSettingsDraft CreateDraft()
    {
        DraftState state = drafts[activeProvider];
        return new AiProviderSettingsDraft(
            activeProvider,
            state.BaseUrl,
            state.Model,
            state.ReplacementKey,
            state.ClearSavedKey);
    }

    private string GetKeyLabel()
    {
        DraftState state = drafts[activeProvider];
        if (state.ClearSavedKey)
            return "API Key（将清除）";
        if (state.HasSavedKey && state.ReplacementKey.Length == 0)
            return "API Key（已保存，留空保持不变）";
        return "API Key";
    }

    private void Reposition()
    {
        width = Math.Min(860, Math.Max(600, Game1.uiViewport.Width - 48));
        height = Math.Min(550, Math.Max(500, Game1.uiViewport.Height - 48));
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;

        int innerWidth = width - PanelMargin * 2;
        int tabsY = yPositionOnScreen + 78;
        int tabWidth = Math.Min(220, (innerWidth - 12) / 2);
        deepSeekTab.bounds = new Rectangle(xPositionOnScreen + PanelMargin, tabsY, tabWidth, 44);
        openAiTab.bounds = new Rectangle(deepSeekTab.bounds.Right + 12, tabsY, tabWidth, 44);

        int fieldX = xPositionOnScreen + PanelMargin;
        int fieldWidth = innerWidth;
        int firstFieldY = tabsY + 76;
        PositionTextBox(baseUrlBox, fieldX, firstFieldY, fieldWidth);
        PositionTextBox(modelBox, fieldX, firstFieldY + RowGap + 24, fieldWidth);

        int keyY = firstFieldY + (RowGap + 24) * 2;
        int clearWidth = 126;
        PositionTextBox(apiKeyBox, fieldX, keyY, Math.Max(280, fieldWidth - clearWidth - 12));
        clearKeyButton.bounds = new Rectangle(apiKeyBox.X + apiKeyBox.Width + 12, keyY, clearWidth, ControlHeight);

        int buttonY = yPositionOnScreen + height - 72;
        saveButton.bounds = new Rectangle(xPositionOnScreen + width - PanelMargin - 126, buttonY, 126, 48);
        testButton.bounds = new Rectangle(saveButton.bounds.X - 150, buttonY, 138, 48);
        initializeUpperRightCloseButton();
    }

    private static TextBox CreateTextBox(Texture2D texture, int limit, bool password)
        => new(texture, null, Game1.smallFont, Game1.textColor)
        {
            textLimit = limit,
            limitWidth = false,
            PasswordBox = password,
            Selected = false,
            TitleText = string.Empty,
        };

    private static void PositionTextBox(TextBox box, int x, int y, int width)
    {
        box.X = x;
        box.Y = y;
        box.Width = width;
        box.Height = ControlHeight;
    }

    private void SelectTextBox(TextBox selected)
    {
        baseUrlBox.Selected = ReferenceEquals(selected, baseUrlBox);
        modelBox.Selected = ReferenceEquals(selected, modelBox);
        apiKeyBox.Selected = ReferenceEquals(selected, apiKeyBox);
        selected.SelectMe();
        Game1.keyboardDispatcher.Subscriber = selected;
    }

    private void ClearKeyboardSubscriber()
    {
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, baseUrlBox)
            || ReferenceEquals(Game1.keyboardDispatcher.Subscriber, modelBox)
            || ReferenceEquals(Game1.keyboardDispatcher.Subscriber, apiKeyBox))
        {
            Game1.keyboardDispatcher.Subscriber = null;
        }
    }

    private static bool Contains(TextBox box, int x, int y)
        => new Rectangle(box.X, box.Y, box.Width, box.Height).Contains(x, y);

    private static void DrawLabel(SpriteBatch b, string label, int x, int y)
        => b.DrawString(Game1.smallFont, label, new Vector2(x, y), Game1.textColor);

    private static void DrawTab(SpriteBatch b, ClickableComponent component, string label, bool selected)
    {
        DrawControl(b, component, label, selected ? Color.Wheat : Color.White, false);
    }

    private static void DrawButton(
        SpriteBatch b,
        ClickableComponent component,
        string label,
        bool disabled)
    {
        DrawControl(b, component, label, disabled ? Color.Gray : Color.White, disabled);
    }

    private static void DrawControl(
        SpriteBatch b,
        ClickableComponent component,
        string label,
        Color tint,
        bool disabled)
    {
        Rectangle bounds = component.bounds;
        float scale = disabled ? 1f : component.scale;
        int scaledWidth = (int)(bounds.Width * scale);
        int scaledHeight = (int)(bounds.Height * scale);
        int x = bounds.Center.X - scaledWidth / 2;
        int y = bounds.Center.Y - scaledHeight / 2;
        drawTextureBox(b, x, y, scaledWidth, scaledHeight, tint);
        Vector2 size = Game1.smallFont.MeasureString(label);
        b.DrawString(
            Game1.smallFont,
            label,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            disabled ? Color.DarkGray : Game1.textColor);
    }

    private static void SetHoverScale(ClickableComponent component, int x, int y)
        => component.scale = component.containsPoint(x, y) ? 1.03f : 1f;

    private string LimitStatus(string value)
    {
        string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        int approximateLimit = Math.Max(40, (width - PanelMargin * 2) / 12);
        return normalized.Length <= approximateLimit
            ? normalized
            : normalized[..approximateLimit] + "…";
    }

    private sealed class DraftState
    {
        public string BaseUrl { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string ReplacementKey { get; set; } = string.Empty;

        public bool HasSavedKey { get; init; }

        public bool ClearSavedKey { get; set; }

        public static DraftState From(AiConnectionProfile profile)
            => new()
            {
                BaseUrl = profile.BaseUrl ?? string.Empty,
                Model = profile.Model ?? string.Empty,
                HasSavedKey = !string.IsNullOrWhiteSpace(profile.ApiKey),
            };
    }
}
