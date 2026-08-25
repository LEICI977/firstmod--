using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace VivantValley.Menus;

/// <summary>A compact, bottom-anchored NPC dialogue box which accepts streamed text.</summary>
public sealed class AiStreamingDialogueMenu : IClickableMenu
{
    private const int HorizontalMargin = 16;
    private const int BottomMargin = 16;

    private readonly string npcName;
    private readonly string npcDisplayName;
    private readonly int maximumCharacters;
    private readonly float conversationUiScale;
    private readonly Action onCancel;
    private readonly Action onContinue;
    private readonly Action onClosed;
    private readonly StringBuilder content = new();
    private readonly ClickableComponent continueButton = new(Rectangle.Empty, "continue");
    private readonly ClickableComponent closeButton = new(Rectangle.Empty, "close");
    private Rectangle portraitBounds;
    private Rectangle textBounds;
    private int reasoningCharacters;
    private int firstVisibleLine;
    private bool completed;
    private bool failed;
    private bool closed;
    private bool awaitingMoveConfirmation;
    private string errorText = string.Empty;
    private string moveConfirmationText = string.Empty;
    private Action? onApproveMove;
    private Action? onDeclineMove;

    public AiStreamingDialogueMenu(
        string npcName,
        string npcDisplayName,
        int maximumCharacters,
        float conversationUiScale,
        Action onCancel,
        Action onContinue,
        Action onClosed)
    {
        this.npcName = npcName;
        this.npcDisplayName = npcDisplayName;
        this.maximumCharacters = Math.Max(100, maximumCharacters);
        this.conversationUiScale = ConversationUiLayout.ClampScale(conversationUiScale);
        this.onCancel = onCancel;
        this.onContinue = onContinue;
        this.onClosed = onClosed;
        Reposition();
    }

    public bool IsGenerating => !completed && !failed && !closed && !awaitingMoveConfirmation;

    public bool CanContinue => completed && !failed && !closed;

    public bool IsAwaitingMoveConfirmation => awaitingMoveConfirmation && !closed;

    public void SetActionConfirmation(
        string confirmationText,
        Action onApprove,
        Action onDecline)
    {
        if (closed)
            return;

        content.Clear();
        completed = false;
        failed = false;
        errorText = string.Empty;
        awaitingMoveConfirmation = true;
        moveConfirmationText = string.IsNullOrWhiteSpace(confirmationText)
            ? "Confirm this action?"
            : confirmationText.Trim();
        onApproveMove = onApprove ?? throw new ArgumentNullException(nameof(onApprove));
        onDeclineMove = onDecline ?? throw new ArgumentNullException(nameof(onDecline));
        firstVisibleLine = 0;
        Game1.playSound("smallSelect");
    }

    public void SetMoveConfirmation(
        string npcDisplayName,
        string destinationDisplayName,
        Action onApprove,
        Action onDecline)
    {
        if (closed)
            return;

        content.Clear();
        completed = false;
        failed = false;
        errorText = string.Empty;
        awaitingMoveConfirmation = true;
        moveConfirmationText = $"和{npcDisplayName}一起去{destinationDisplayName}吗？你来带路。";
        onApproveMove = onApprove ?? throw new ArgumentNullException(nameof(onApprove));
        onDeclineMove = onDecline ?? throw new ArgumentNullException(nameof(onDecline));
        firstVisibleLine = 0;
        Game1.playSound("smallSelect");
    }

    public void AppendChunk(DeepSeekStreamChunk chunk)
    {
        if (!IsGenerating)
            return;

        reasoningCharacters += chunk.ReasoningDelta.Length;
        if (chunk.ContentDelta.Length == 0 || content.Length >= maximumCharacters)
            return;

        int remaining = maximumCharacters - content.Length;
        content.Append(chunk.ContentDelta.AsSpan(0, Math.Min(remaining, chunk.ContentDelta.Length)));
    }

    public void SetCompleted(string finalText)
    {
        if (closed)
            return;

        content.Clear();
        string normalized = (finalText ?? string.Empty).Trim();
        if (normalized.Length > maximumCharacters)
            normalized = normalized[..maximumCharacters] + "……";
        content.Append(normalized);
        completed = true;
        failed = false;
        awaitingMoveConfirmation = false;
        ClearMoveCallbacks();
        firstVisibleLine = 0;
        Game1.playSound("smallSelect");
    }

    public void SetError(string message)
    {
        if (closed)
            return;

        failed = true;
        completed = false;
        awaitingMoveConfirmation = false;
        ClearMoveCallbacks();
        errorText = string.IsNullOrWhiteSpace(message) ? "AI 请求失败。" : message.Trim();
        firstVisibleLine = 0;
        Game1.playSound("cancel");
    }

    public void Dismiss()
    {
        Close(cancelRequest: false);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        Reposition();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            if (IsAwaitingMoveConfirmation)
            {
                ResolveMoveConfirmation(approved: false);
                return;
            }
            Close(cancelRequest: IsGenerating);
            return;
        }

        if (IsAwaitingMoveConfirmation && key is Keys.Enter or Keys.Space)
        {
            ResolveMoveConfirmation(approved: true);
            return;
        }

        if (CanContinue && key == Keys.Enter)
        {
            ContinueConversation();
            return;
        }

        if (!IsGenerating && key is Keys.Enter or Keys.Space)
        {
            Close(cancelRequest: false);
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button == Buttons.B)
        {
            if (IsAwaitingMoveConfirmation)
            {
                ResolveMoveConfirmation(approved: false);
                return;
            }
            Close(cancelRequest: IsGenerating);
            return;
        }

        if (IsAwaitingMoveConfirmation && button == Buttons.A)
        {
            ResolveMoveConfirmation(approved: true);
            return;
        }

        if (CanContinue && button == Buttons.A)
        {
            ContinueConversation();
            return;
        }

        if (!IsGenerating && button == Buttons.A)
        {
            Close(cancelRequest: false);
            return;
        }

        base.receiveGamePadButton(button);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (IsAwaitingMoveConfirmation)
        {
            if (continueButton.containsPoint(x, y))
                ResolveMoveConfirmation(approved: true);
            else if (closeButton.containsPoint(x, y))
                ResolveMoveConfirmation(approved: false);
            return;
        }

        if (CanContinue && continueButton.containsPoint(x, y))
        {
            ContinueConversation();
            return;
        }

        if (closeButton.containsPoint(x, y))
            Close(cancelRequest: IsGenerating);
    }

    public override void receiveScrollWheelAction(int direction)
    {
        string[] lines = GetWrappedLines(out int maximumVisibleLines);
        int maximumStart = Math.Max(0, lines.Length - maximumVisibleLines);
        if (direction > 0)
            firstVisibleLine = Math.Max(0, firstVisibleLine - 1);
        else if (direction < 0)
            firstVisibleLine = Math.Min(maximumStart, firstVisibleLine + 1);
    }

    public override void performHoverAction(int x, int y)
    {
        continueButton.scale = (CanContinue || IsAwaitingMoveConfirmation) && continueButton.containsPoint(x, y)
            ? 1.04f
            : 1f;
        closeButton.scale = closeButton.containsPoint(x, y) ? 1.04f : 1f;
    }

    public override void draw(SpriteBatch b)
    {
        // Keep the game visible and use the same wood/parchment box treatment as
        // Stardew's native menus and dialogue UI.
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        DrawPortrait(b);
        DrawHeader(b);
        DrawText(b);
        DrawActionButtons(b);
        drawMouse(b);
    }

    protected override void cleanupBeforeExit()
    {
        if (!closed)
        {
            bool shouldCancel = IsGenerating;
            Action? declineMove = IsAwaitingMoveConfirmation ? onDeclineMove : null;
            closed = true;
            awaitingMoveConfirmation = false;
            ClearMoveCallbacks();
            if (shouldCancel)
                onCancel();
            declineMove?.Invoke();
            onClosed();
        }

        base.cleanupBeforeExit();
    }

    private void DrawHeader(SpriteBatch b)
    {
        string status = GetStatusText();
        Vector2 statusSize = Game1.smallFont.MeasureString(status);
        int headerX = textBounds.X;
        float maximumNameWidth = Math.Max(80, textBounds.Width - statusSize.X - 20);
        string displayName = FitSingleLine(npcDisplayName, Game1.dialogueFont, maximumNameWidth);

        b.DrawString(
            Game1.dialogueFont,
            displayName,
            new Vector2(headerX, yPositionOnScreen + 18),
            Game1.textColor);

        b.DrawString(
            Game1.smallFont,
            status,
            new Vector2(textBounds.Right - statusSize.X, yPositionOnScreen + 28),
            failed ? new Color(170, 45, 35) : Color.Gray);
    }

    private void DrawPortrait(SpriteBatch b)
    {
        NPC? npc = Game1.getCharacterFromName(npcName, mustBeVillager: false, includeEventActors: true);
        Texture2D? portrait = null;
        try
        {
            portrait = npc?.Portrait;
        }
        catch
        {
            // A custom NPC may not expose a portrait asset.
        }

        drawTextureBox(
            b,
            portraitBounds.X - 8,
            portraitBounds.Y - 8,
            portraitBounds.Width + 16,
            portraitBounds.Height + 16,
            Color.White);

        if (portrait is not null && portrait.Width >= 64 && portrait.Height >= 64)
        {
            b.Draw(
                portrait,
                portraitBounds,
                new Rectangle(0, 0, 64, 64),
                Color.White);
        }
        else
        {
            Vector2 size = Game1.dialogueFont.MeasureString("?");
            b.DrawString(
                Game1.dialogueFont,
                "?",
                new Vector2(
                    portraitBounds.Center.X - size.X / 2f,
                    portraitBounds.Center.Y - size.Y / 2f),
                Color.Gray);
        }
    }

    private void DrawText(SpriteBatch b)
    {
        string[] lines = GetWrappedLines(out int maximumVisibleLines);
        int maximumStart = Math.Max(0, lines.Length - maximumVisibleLines);
        if (IsGenerating)
            firstVisibleLine = maximumStart;
        else
            firstVisibleLine = Math.Clamp(firstVisibleLine, 0, maximumStart);

        Color color = failed ? new Color(170, 45, 35) : Game1.textColor;
        int lineSpacing = Game1.dialogueFont.LineSpacing;
        for (int index = 0; index < maximumVisibleLines && firstVisibleLine + index < lines.Length; index++)
        {
            b.DrawString(
                Game1.dialogueFont,
                lines[firstVisibleLine + index],
                new Vector2(textBounds.X, textBounds.Y + index * lineSpacing),
                color);
        }

        if (lines.Length > maximumVisibleLines)
        {
            string position = $"{firstVisibleLine + 1}-{Math.Min(lines.Length, firstVisibleLine + maximumVisibleLines)}/{lines.Length}";
            b.DrawString(
                Game1.smallFont,
                position,
                new Vector2(textBounds.X, closeButton.bounds.Y + 5),
                Color.Gray);
        }
    }

    private string[] GetWrappedLines(out int maximumVisibleLines)
    {
        maximumVisibleLines = Math.Max(
            1,
            textBounds.Height / Math.Max(1, Game1.dialogueFont.LineSpacing));

        string source = failed
            ? errorText
            : IsAwaitingMoveConfirmation
                ? moveConfirmationText
            : content.Length > 0
                ? content.ToString()
                : GetWaitingText();
        string wrapped = Game1.parseText(source, Game1.dialogueFont, Math.Max(160, textBounds.Width));
        return wrapped.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
    }

    private static string GetWaitingText()
    {
        int dots = 1 + (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 350d) % 3;
        return new string('…', dots);
    }

    private string GetStatusText()
    {
        if (failed)
            return "失败";
        if (IsAwaitingMoveConfirmation)
            return "等待确认";
        if (completed)
            return "完成";
        if (content.Length > 0)
            return "回复中…";
        if (reasoningCharacters > 0)
            return "思考中…";
        return "处理中…";
    }

    private void DrawActionButtons(SpriteBatch b)
    {
        if (IsAwaitingMoveConfirmation)
        {
            DrawActionButton(b, continueButton, "出发");
            DrawActionButton(b, closeButton, "暂不");
            return;
        }

        if (CanContinue)
            DrawActionButton(b, continueButton, "继续");

        DrawActionButton(b, closeButton, IsGenerating ? "取消" : "关闭");
    }

    private static void DrawActionButton(SpriteBatch b, ClickableComponent button, string label)
    {
        Rectangle bounds = button.bounds;
        int scaledWidth = (int)(bounds.Width * button.scale);
        int scaledHeight = (int)(bounds.Height * button.scale);
        int x = bounds.Center.X - scaledWidth / 2;
        int y = bounds.Center.Y - scaledHeight / 2;
        drawTextureBox(b, x, y, scaledWidth, scaledHeight, Color.White);

        Vector2 size = Game1.smallFont.MeasureString(label);
        b.DrawString(
            Game1.smallFont,
            label,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            Game1.textColor);
    }

    private void ContinueConversation()
    {
        if (!CanContinue)
            return;

        closed = true;
        onClosed();
        if (ReferenceEquals(Game1.activeClickableMenu, this))
            exitThisMenuNoSound();
        onContinue();
    }

    private void ResolveMoveConfirmation(bool approved)
    {
        if (!IsAwaitingMoveConfirmation)
            return;

        Action? callback = approved ? onApproveMove : onDeclineMove;
        awaitingMoveConfirmation = false;
        moveConfirmationText = string.Empty;
        content.Clear();
        completed = false;
        failed = false;
        firstVisibleLine = 0;
        ClearMoveCallbacks();
        Game1.playSound(approved ? "smallSelect" : "bigDeSelect");
        callback?.Invoke();
    }

    private void ClearMoveCallbacks()
    {
        onApproveMove = null;
        onDeclineMove = null;
    }

    private void Close(bool cancelRequest)
    {
        if (closed)
            return;

        closed = true;
        if (cancelRequest)
            onCancel();
        onClosed();
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
            viewportRatio: 0.30f,
            minimumHeight: 218);
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(8, Game1.uiViewport.Height - height - BottomMargin);

        int portraitSize = width < 620 ? 92 : 112;
        portraitBounds = new Rectangle(
            xPositionOnScreen + 24,
            yPositionOnScreen + 68,
            portraitSize,
            portraitSize);

        int textX = portraitBounds.Right + 24;
        int footerY = yPositionOnScreen + height - 54;
        textBounds = new Rectangle(
            textX,
            yPositionOnScreen + 66,
            Math.Max(160, xPositionOnScreen + width - 28 - textX),
            Math.Max(54, footerY - (yPositionOnScreen + 66) - 8));

        closeButton.bounds = new Rectangle(
            xPositionOnScreen + width - 100,
            footerY + 5,
            72,
            38);
        continueButton.bounds = new Rectangle(
            closeButton.bounds.X - 92,
            footerY + 5,
            84,
            38);
    }

    private static string FitSingleLine(string value, SpriteFont font, float maximumWidth)
    {
        string text = value ?? string.Empty;
        if (font.MeasureString(text).X <= maximumWidth)
            return text;

        const string suffix = "…";
        while (text.Length > 0 && font.MeasureString(text + suffix).X > maximumWidth)
            text = text[..^1];
        return text + suffix;
    }
}
