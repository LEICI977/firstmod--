using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace VivantValley.Menus;

public sealed record AiProactiveChoice(string Id, string Text, bool IsDefer);

/// <summary>A native-looking proactive encounter with data-driven branch choices.</summary>
public sealed class AiProactiveEncounterMenu : IClickableMenu
{
    private const int HorizontalMargin = 16;
    private const int BottomMargin = 16;

    private readonly string npcName;
    private readonly string npcDisplayName;
    private readonly string dialogueText;
    private readonly IReadOnlyList<AiProactiveChoice> choices;
    private readonly Func<string, bool> onChoose;
    private readonly Action onCancel;
    private readonly Action onClosed;
    private readonly List<ClickableComponent> choiceButtons = new();
    private Rectangle portraitBounds;
    private Rectangle textBounds;
    private int firstVisibleLine;
    private bool closed;
    private bool resolutionHandled;
    private string statusText;

    public AiProactiveEncounterMenu(
        string npcName,
        string npcDisplayName,
        string dialogueText,
        IReadOnlyList<AiProactiveChoice> choices,
        Func<string, bool> onChoose,
        Action onCancel,
        Action onClosed)
    {
        this.npcName = npcName;
        this.npcDisplayName = npcDisplayName;
        statusText = $"{npcDisplayName} 主动和你聊了起来";
        this.dialogueText = dialogueText;
        this.choices = choices?.Where(choice => choice is not null).Take(4).ToArray()
            ?? throw new ArgumentNullException(nameof(choices));
        if (this.choices.Count == 0)
            throw new ArgumentException("At least one proactive choice is required.", nameof(choices));
        this.onChoose = onChoose ?? throw new ArgumentNullException(nameof(onChoose));
        this.onCancel = onCancel ?? throw new ArgumentNullException(nameof(onCancel));
        this.onClosed = onClosed;
        for (int index = 0; index < this.choices.Count; index++)
            choiceButtons.Add(new ClickableComponent(Rectangle.Empty, this.choices[index].Id));
        Reposition();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        Reposition();
    }

    /// <summary>Closes an interrupted scene after its owner has already handled persistence.</summary>
    public void Dismiss()
    {
        if (closed)
            return;

        resolutionHandled = true;
        Close();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        for (int index = 0; index < choiceButtons.Count; index++)
        {
            if (choiceButtons[index].containsPoint(x, y))
            {
                TryChoose(index);
                return;
            }
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            CancelOrChooseDefer();
            return;
        }

        if (key == Keys.Enter || key == Keys.Space)
        {
            TryChoose(0);
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button == Buttons.A)
        {
            TryChoose(0);
            return;
        }

        if (button == Buttons.B)
        {
            CancelOrChooseDefer();
            return;
        }

        base.receiveGamePadButton(button);
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
        foreach (ClickableComponent button in choiceButtons)
            button.scale = button.containsPoint(x, y) ? 1.04f : 1f;
    }

    public override void draw(SpriteBatch b)
    {
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
        DrawPortrait(b);
        DrawHeader(b);
        DrawDialogue(b);
        for (int index = 0; index < choiceButtons.Count; index++)
            DrawButton(b, choiceButtons[index], choices[index].Text);
        drawMouse(b);
    }

    protected override void cleanupBeforeExit()
    {
        if (!closed)
        {
            closed = true;
            if (!resolutionHandled)
                onCancel();
            onClosed();
        }

        base.cleanupBeforeExit();
    }

    private void TryChoose(int index)
    {
        if (closed || resolutionHandled || index < 0 || index >= choices.Count)
            return;

        AiProactiveChoice choice = choices[index];
        if (!onChoose(choice.Id))
        {
            statusText = "现在无法执行这个选择；请检查背包空间。";
            Game1.playSound("cancel");
            return;
        }

        resolutionHandled = true;
        Game1.playSound(choice.IsDefer ? "bigDeSelect" : "coin");
        Close();
    }

    private void CancelOrChooseDefer()
    {
        if (closed)
            return;

        int deferIndex = choices.ToList().FindIndex(choice => choice.IsDefer);
        if (deferIndex >= 0)
        {
            TryChoose(deferIndex);
            return;
        }

        resolutionHandled = true;
        onCancel();
        Game1.playSound("bigDeSelect");
        Close();
    }

    private void Close()
    {
        if (closed)
            return;
        closed = true;
        onClosed();
        if (ReferenceEquals(Game1.activeClickableMenu, this))
            exitThisMenuNoSound();
    }

    private void DrawHeader(SpriteBatch b)
    {
        b.DrawString(
            Game1.dialogueFont,
            npcDisplayName,
            new Vector2(textBounds.X, yPositionOnScreen + 18),
            Game1.textColor);

        Vector2 statusSize = Game1.smallFont.MeasureString(statusText);
        string fitted = FitSingleLine(statusText, Game1.smallFont, textBounds.Width);
        statusSize = Game1.smallFont.MeasureString(fitted);
        b.DrawString(
            Game1.smallFont,
            fitted,
            new Vector2(textBounds.Right - statusSize.X, yPositionOnScreen + 30),
            Color.Gray);
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
            // Custom NPCs may not provide a portrait.
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
            b.Draw(portrait, portraitBounds, new Rectangle(0, 0, 64, 64), Color.White);
        }
        else
        {
            Vector2 size = Game1.dialogueFont.MeasureString("?");
            b.DrawString(
                Game1.dialogueFont,
                "?",
                new Vector2(portraitBounds.Center.X - size.X / 2f, portraitBounds.Center.Y - size.Y / 2f),
                Color.Gray);
        }
    }

    private void DrawDialogue(SpriteBatch b)
    {
        string[] lines = GetWrappedLines(out int maximumVisibleLines);
        firstVisibleLine = Math.Clamp(firstVisibleLine, 0, Math.Max(0, lines.Length - maximumVisibleLines));
        for (int index = 0; index < maximumVisibleLines && firstVisibleLine + index < lines.Length; index++)
        {
            b.DrawString(
                Game1.dialogueFont,
                lines[firstVisibleLine + index],
                new Vector2(textBounds.X, textBounds.Y + index * Game1.dialogueFont.LineSpacing),
                Game1.textColor);
        }
    }

    private string[] GetWrappedLines(out int maximumVisibleLines)
    {
        maximumVisibleLines = Math.Max(1, textBounds.Height / Math.Max(1, Game1.dialogueFont.LineSpacing));
        string wrapped = Game1.parseText(dialogueText, Game1.dialogueFont, Math.Max(160, textBounds.Width));
        return wrapped.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
    }

    private static void DrawButton(SpriteBatch b, ClickableComponent button, string label)
    {
        Rectangle bounds = button.bounds;
        int scaledWidth = (int)(bounds.Width * button.scale);
        int scaledHeight = (int)(bounds.Height * button.scale);
        int x = bounds.Center.X - scaledWidth / 2;
        int y = bounds.Center.Y - scaledHeight / 2;
        drawTextureBox(b, x, y, scaledWidth, scaledHeight, Color.White);

        string fitted = FitSingleLine(label, Game1.smallFont, bounds.Width - 12);
        Vector2 size = Game1.smallFont.MeasureString(fitted);
        b.DrawString(
            Game1.smallFont,
            fitted,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            Game1.textColor);
    }

    private void Reposition()
    {
        int availableWidth = Math.Max(440, Game1.uiViewport.Width - HorizontalMargin * 2);
        width = Math.Min(960, availableWidth);
        int rows = (choiceButtons.Count + 1) / 2;
        int desiredHeight = 300 + Math.Max(0, rows - 1) * 54;
        height = Math.Min(desiredHeight, Math.Max(248, Game1.uiViewport.Height - BottomMargin * 2));
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(8, Game1.uiViewport.Height - height - BottomMargin);

        int portraitSize = width < 640 ? 92 : 112;
        portraitBounds = new Rectangle(xPositionOnScreen + 24, yPositionOnScreen + 72, portraitSize, portraitSize);
        int textX = portraitBounds.Right + 24;
        const int buttonHeight = 44;
        const int rowGap = 8;
        int footerHeight = rows * buttonHeight + Math.Max(0, rows - 1) * rowGap;
        int footerY = yPositionOnScreen + height - 18 - footerHeight;
        textBounds = new Rectangle(
            textX,
            yPositionOnScreen + 72,
            Math.Max(180, xPositionOnScreen + width - 28 - textX),
            Math.Max(70, footerY - yPositionOnScreen - 82));

        const int gap = 12;
        int availableButtonWidth = width - 56;
        int buttonWidth = Math.Min(310, (availableButtonWidth - gap) / 2);
        int gridWidth = buttonWidth * 2 + gap;
        int buttonsX = xPositionOnScreen + width - 28 - gridWidth;
        for (int index = 0; index < choiceButtons.Count; index++)
        {
            int column = index % 2;
            int row = index / 2;
            choiceButtons[index].bounds = new Rectangle(
                buttonsX + column * (buttonWidth + gap),
                footerY + row * (buttonHeight + rowGap),
                buttonWidth,
                buttonHeight);
        }
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
