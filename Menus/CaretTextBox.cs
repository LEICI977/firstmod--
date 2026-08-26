using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using VivantValley.Services;

namespace VivantValley.Menus;

/// <summary>A native-looking single-line text box with mouse-positioned Unicode caret editing.</summary>
internal sealed class CaretTextBox : IKeyboardSubscriber
{
    private const int HorizontalPadding = 16;
    private const int CaretWidth = 2;
    private const double CaretBlinkMilliseconds = 500d;

    private readonly TextBox frame;
    private readonly SpriteFont font;
    private readonly Color textColor;
    private readonly SingleLineTextBuffer buffer;
    private int viewStartIndex;
    private double caretActivityMilliseconds;

    public CaretTextBox(
        Texture2D textBoxTexture,
        SpriteFont font,
        Color textColor,
        int textLimit)
    {
        this.font = font;
        this.textColor = textColor;
        buffer = new SingleLineTextBuffer(textLimit);
        frame = new TextBox(textBoxTexture, null, font, textColor)
        {
            Selected = false,
            Text = string.Empty,
            TitleText = string.Empty,
        };
    }

    public event Action? EnterPressed;

    public int X
    {
        get => frame.X;
        set => frame.X = value;
    }

    public int Y
    {
        get => frame.Y;
        set => frame.Y = value;
    }

    public int Width
    {
        get => frame.Width;
        set
        {
            frame.Width = value;
            EnsureCaretVisible();
        }
    }

    public int Height
    {
        get => frame.Height;
        set => frame.Height = value;
    }

    public string Text
    {
        get => buffer.Text;
        set
        {
            buffer.Text = value;
            viewStartIndex = 0;
            MarkCaretActivity();
            EnsureCaretVisible();
        }
    }

    public bool Selected { get; set; }

    public Rectangle Bounds => new(X, Y, Width, Height);

    public void SelectAt(int mouseX)
    {
        Selected = true;
        int availableWidth = GetAvailableTextWidth();
        int visibleEnd = FindVisibleEnd(viewStartIndex, availableWidth);
        float target = Math.Clamp(mouseX - (X + HorizontalPadding), 0, availableWidth);
        int selectedIndex = viewStartIndex;
        int previousBoundary = viewStartIndex;
        float accumulatedWidth = 0f;

        foreach (int boundary in buffer.GetBoundaries())
        {
            if (boundary <= viewStartIndex)
                continue;
            if (boundary > visibleEnd)
                break;

            float elementWidth = Measure(previousBoundary, boundary);
            if (target < accumulatedWidth + (elementWidth / 2f))
                break;

            selectedIndex = boundary;
            accumulatedWidth += elementWidth;
            previousBoundary = boundary;
        }

        buffer.SetCaret(selectedIndex);
        MarkCaretActivity();
        EnsureCaretVisible();
    }

    public void Hover(int x, int y) => frame.Hover(x, y);

    public void Draw(SpriteBatch spriteBatch)
    {
        frame.Draw(spriteBatch, drawShadow: false);

        int availableWidth = GetAvailableTextWidth();
        EnsureCaretVisible();
        int visibleEnd = FindVisibleEnd(viewStartIndex, availableWidth);
        string visibleText = buffer.Text[viewStartIndex..visibleEnd];
        float textY = Y + Math.Max(0f, (Height - font.LineSpacing) / 2f);
        spriteBatch.DrawString(
            font,
            visibleText,
            new Vector2(X + HorizontalPadding, textY),
            textColor);

        if (!Selected || !ShouldDrawCaret())
            return;

        float caretOffset = Measure(viewStartIndex, buffer.CaretIndex);
        int caretHeight = Math.Max(12, font.LineSpacing - 6);
        int caretY = Y + Math.Max(2, (Height - caretHeight) / 2);
        spriteBatch.Draw(
            Game1.staminaRect,
            new Rectangle(
                X + HorizontalPadding + (int)MathF.Round(caretOffset),
                caretY,
                CaretWidth,
                caretHeight),
            textColor);
    }

    public void RecieveTextInput(char inputChar)
    {
        if (!Selected || char.IsControl(inputChar))
            return;

        Edit(() => buffer.Insert(inputChar.ToString()));
    }

    public void RecieveTextInput(string text)
    {
        if (!Selected)
            return;

        Edit(() => buffer.Insert(text));
    }

    public void RecieveCommandInput(char command)
    {
        if (!Selected)
            return;

        switch (command)
        {
            case '\b':
                Edit(buffer.Backspace);
                break;
            case '\r':
                EnterPressed?.Invoke();
                break;
        }
    }

    public void RecieveSpecialInput(Keys key)
    {
        if (!Selected)
            return;

        switch (key)
        {
            case Keys.Left:
                Move(buffer.MoveLeft);
                break;
            case Keys.Right:
                Move(buffer.MoveRight);
                break;
            case Keys.Home:
                buffer.MoveHome();
                MarkCaretActivity();
                EnsureCaretVisible();
                break;
            case Keys.End:
                buffer.MoveEnd();
                MarkCaretActivity();
                EnsureCaretVisible();
                break;
            case Keys.Delete:
                Edit(buffer.Delete);
                break;
        }
    }

    private void Edit(Func<bool> operation)
    {
        if (operation())
        {
            MarkCaretActivity();
            EnsureCaretVisible();
        }
    }

    private void Move(Func<bool> operation)
    {
        if (operation())
        {
            MarkCaretActivity();
            EnsureCaretVisible();
        }
    }

    private void EnsureCaretVisible()
    {
        int availableWidth = GetAvailableTextWidth();
        int[] boundaries = buffer.GetBoundaries();
        if (!boundaries.Contains(viewStartIndex) || viewStartIndex > buffer.CaretIndex)
            viewStartIndex = buffer.CaretIndex;

        while (viewStartIndex < buffer.CaretIndex
               && Measure(viewStartIndex, buffer.CaretIndex) > availableWidth)
        {
            int next = boundaries.First(boundary => boundary > viewStartIndex);
            viewStartIndex = next;
        }

        if (buffer.CaretIndex == 0)
            viewStartIndex = 0;
    }

    private int FindVisibleEnd(int startIndex, int availableWidth)
    {
        int visibleEnd = startIndex;
        int previousBoundary = startIndex;
        float accumulatedWidth = 0f;
        foreach (int boundary in buffer.GetBoundaries())
        {
            if (boundary <= startIndex)
                continue;

            float elementWidth = Measure(previousBoundary, boundary);
            if (accumulatedWidth + elementWidth > availableWidth)
                break;

            accumulatedWidth += elementWidth;
            visibleEnd = boundary;
            previousBoundary = boundary;
        }

        return visibleEnd;
    }

    private float Measure(int startIndex, int endIndex)
    {
        if (endIndex <= startIndex)
            return 0f;
        return font.MeasureString(buffer.Text[startIndex..endIndex]).X;
    }

    private int GetAvailableTextWidth() => Math.Max(1, Width - (HorizontalPadding * 2) - CaretWidth);

    private void MarkCaretActivity()
        => caretActivityMilliseconds = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d;

    private bool ShouldDrawCaret()
    {
        double now = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d;
        return ((now - caretActivityMilliseconds) % (CaretBlinkMilliseconds * 2d)) < CaretBlinkMilliseconds;
    }
}
