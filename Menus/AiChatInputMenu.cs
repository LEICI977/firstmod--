using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace VivantValley.Menus;

/// <summary>A small, native-looking chat composer anchored to the bottom of the viewport.</summary>
public sealed class AiChatInputMenu : IClickableMenu
{
    private const int HorizontalMargin = 20;
    private const int BottomMargin = 16;
    private const int ComposerHeight = 126;

    private readonly string npcDisplayName;
    private readonly float conversationUiScale;
    private readonly Action<string> onSubmit;
    private readonly Action onCancel;
    private readonly Action onOpenSettings;
    private readonly TextBox textBox;
    private readonly ClickableComponent sendButton = new(Rectangle.Empty, "send");
    private readonly ClickableComponent settingsButton = new(Rectangle.Empty, "settings");
    private bool closed;
    private bool submitDispatched;
    private bool keyboardSpaceGestureActive;
    private string voiceStatus = string.Empty;

    public AiChatInputMenu(
        string npcDisplayName,
        Action<string> onSubmit,
        Action onCancel,
        Action onOpenSettings,
        float conversationUiScale = 1f)
    {
        this.npcDisplayName = npcDisplayName;
        this.conversationUiScale = ConversationUiLayout.ClampScale(conversationUiScale);
        this.onSubmit = onSubmit;
        this.onCancel = onCancel;
        this.onOpenSettings = onOpenSettings;

        Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
        textBox = new TextBox(textBoxTexture, null, Game1.smallFont, Game1.textColor)
        {
            textLimit = 800,
            limitWidth = true,
            Selected = true,
            TitleText = string.Empty,
        };

        // Use the same keyboard-dispatcher path as Stardew's NamingMenu. This keeps
        // Enter reliable without treating an IME candidate-confirm key as a submit.
        textBox.OnEnterPressed += _ => Submit();

        Reposition();
        Game1.keyboardDispatcher.Subscriber = textBox;
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

        if (sendButton.containsPoint(x, y))
        {
            Submit();
            return;
        }

        if (settingsButton.containsPoint(x, y))
        {
            OpenSettings();
            return;
        }

        if (new Rectangle(textBox.X, textBox.Y, textBox.Width, textBox.Height).Contains(x, y))
        {
            textBox.SelectMe();
            Game1.keyboardDispatcher.Subscriber = textBox;
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            Cancel();
            return;
        }

        if (key == Keys.Space && keyboardSpaceGestureActive)
            return;

        // TextBox receives typing, Enter, and IME composition through
        // Game1.keyboardDispatcher. Avoid processing the same physical key twice.
        if (!textBox.Selected)
            base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button == Buttons.A)
        {
            Submit();
            return;
        }

        if (button == Buttons.B)
        {
            Cancel();
            return;
        }

        base.receiveGamePadButton(button);
    }

    /// <summary>Remove the last character when the controller B button is tapped.</summary>
    public void DeleteLastCharacter()
    {
        if (closed)
            return;

        string value = textBox.Text ?? string.Empty;
        if (value.Length == 0)
        {
            Game1.playSound("cancel");
            return;
        }

        textBox.Text = value[..^1];
        textBox.SelectMe();
        Game1.keyboardDispatcher.Subscriber = textBox;
        Game1.playSound("smallSelect");
    }

    /// <summary>Submit the text currently in the composer from a short controller A press.</summary>
    public void SubmitControllerText() => Submit();

    /// <summary>Pause TextBox character repeat while keyboard space is being classified.</summary>
    public void BeginKeyboardSpaceGesture()
    {
        if (closed)
            return;

        keyboardSpaceGestureActive = true;
        textBox.Selected = false;
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, textBox))
            Game1.keyboardDispatcher.Subscriber = null;
    }

    /// <summary>End keyboard-space classification and restore normal typing.</summary>
    public void EndKeyboardSpaceGesture(bool restoreFocus = true)
    {
        keyboardSpaceGestureActive = false;
        if (restoreFocus && !closed)
        {
            textBox.SelectMe();
            Game1.keyboardDispatcher.Subscriber = textBox;
        }
    }

    /// <summary>Restore a suppressed short keyboard-space press to the composer.</summary>
    public void AppendSpace()
    {
        if (closed)
            return;

        EndKeyboardSpaceGesture(restoreFocus: false);
        string value = textBox.Text ?? string.Empty;
        if (value.Length >= textBox.textLimit)
            return;

        textBox.Text = value + " ";
        textBox.SelectMe();
        Game1.keyboardDispatcher.Subscriber = textBox;
    }

    /// <summary>Submit a completed speech transcription through the normal conversation callback.</summary>
    public void SubmitRecognizedText(string value)
    {
        if (closed)
            return;

        string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length == 0)
        {
            SetVoiceError("未识别到语音。");
            return;
        }

        textBox.Text = normalized;
        EndKeyboardSpaceGesture(restoreFocus: false);
        Submit();
    }

    public void SetVoiceRecordingState() => voiceStatus = "正在录音...松开按键发送";

    public void SetVoiceProcessingState() => voiceStatus = "正在识别语音...";

    public void SetVoiceError(string message)
    {
        voiceStatus = string.IsNullOrWhiteSpace(message) ? "语音输入不可用。" : message.Trim();
        textBox.SelectMe();
        Game1.keyboardDispatcher.Subscriber = textBox;
        Game1.playSound("cancel");
    }

    public override void performHoverAction(int x, int y)
    {
        textBox.Hover(x, y);
        sendButton.scale = sendButton.containsPoint(x, y) ? 1.04f : 1f;
        settingsButton.scale = settingsButton.containsPoint(x, y) ? 1.04f : 1f;
        upperRightCloseButton?.tryHover(x, y, 0.2f);
    }

    public override void draw(SpriteBatch b)
    {
        // The standard texture box already provides Stardew's wood-and-parchment
        // treatment, so the world remains visible instead of using a dark overlay.
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        b.DrawString(
            Game1.smallFont,
            npcDisplayName,
            new Vector2(xPositionOnScreen + 24, yPositionOnScreen + 18),
            Game1.textColor);

        string hint = "Enter 发送";
        Vector2 hintSize = Game1.smallFont.MeasureString(hint);
        b.DrawString(
            Game1.smallFont,
            hint,
            new Vector2(xPositionOnScreen + width - 76 - hintSize.X, yPositionOnScreen + 18),
            Color.Gray);

        if (!string.IsNullOrWhiteSpace(voiceStatus))
        {
            b.DrawString(
                Game1.smallFont,
                voiceStatus,
                new Vector2(xPositionOnScreen + 24, yPositionOnScreen + 48),
                Color.DarkSlateGray);
        }

        textBox.Draw(b, drawShadow: false);
        DrawSendButton(b);
        DrawSettingsButton(b);
        upperRightCloseButton?.draw(b);
        drawMouse(b);
    }

    protected override void cleanupBeforeExit()
    {
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, textBox))
            Game1.keyboardDispatcher.Subscriber = null;

        if (!closed)
        {
            closed = true;
            onCancel();
        }

        base.cleanupBeforeExit();
    }

    private void Submit()
    {
        if (closed || submitDispatched)
            return;

        string text = (textBox.Text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
        {
            Game1.playSound("cancel");
            return;
        }

        submitDispatched = true;
        closed = true;
        Game1.playSound("smallSelect");
        ExitComposer();
        onSubmit(text);
    }

    private void Cancel()
    {
        if (closed)
            return;

        closed = true;
        Game1.playSound("bigDeSelect");
        ExitComposer();
        onCancel();
    }

    private void OpenSettings()
    {
        if (closed)
            return;

        closed = true;
        Game1.playSound("smallSelect");
        ExitComposer();
        onOpenSettings();
    }

    private void ExitComposer()
    {
        if (ReferenceEquals(Game1.activeClickableMenu, this))
            exitThisMenuNoSound();
    }

    private void Reposition()
    {
        width = ConversationUiLayout.CalculateWidth(
            Game1.uiViewport.Width,
            HorizontalMargin,
            conversationUiScale,
            viewportRatio: 0.68f,
            minimumWidth: 560);
        height = ConversationUiLayout.CalculateHeight(
            Game1.uiViewport.Height,
            BottomMargin,
            conversationUiScale,
            viewportRatio: 0.14f,
            minimumHeight: Math.Min(108, ComposerHeight));
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(8, Game1.uiViewport.Height - height - BottomMargin);

        const int innerMargin = 24;
        const int buttonWidth = 72;
        const int controlHeight = 48;
        const int controlGap = 12;
        int controlY = yPositionOnScreen + height - controlHeight - 20;

        sendButton.bounds = new Rectangle(
            xPositionOnScreen + width - innerMargin - buttonWidth,
            controlY,
            buttonWidth,
            controlHeight);

        textBox.X = xPositionOnScreen + innerMargin;
        textBox.Y = controlY;
        textBox.Width = Math.Max(160, sendButton.bounds.X - controlGap - textBox.X);
        textBox.Height = controlHeight;

        settingsButton.bounds = new Rectangle(
            xPositionOnScreen + width - 164,
            yPositionOnScreen + 10,
            72,
            38);

        initializeUpperRightCloseButton();
    }

    private void DrawSendButton(SpriteBatch b)
    {
        Rectangle bounds = sendButton.bounds;
        int scaledWidth = (int)(bounds.Width * sendButton.scale);
        int scaledHeight = (int)(bounds.Height * sendButton.scale);
        int x = bounds.Center.X - scaledWidth / 2;
        int y = bounds.Center.Y - scaledHeight / 2;
        drawTextureBox(b, x, y, scaledWidth, scaledHeight, Color.White);

        const string label = "发送";
        Vector2 size = Game1.smallFont.MeasureString(label);
        b.DrawString(
            Game1.smallFont,
            label,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            Game1.textColor);
    }

    private void DrawSettingsButton(SpriteBatch b)
    {
        Rectangle bounds = settingsButton.bounds;
        int scaledWidth = (int)(bounds.Width * settingsButton.scale);
        int scaledHeight = (int)(bounds.Height * settingsButton.scale);
        int x = bounds.Center.X - scaledWidth / 2;
        int y = bounds.Center.Y - scaledHeight / 2;
        drawTextureBox(b, x, y, scaledWidth, scaledHeight, Color.White);

        const string label = "设置";
        Vector2 size = Game1.smallFont.MeasureString(label);
        b.DrawString(
            Game1.smallFont,
            label,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            Game1.textColor);
    }

}
