namespace VivantValley.Menus;

internal static class ConversationUiLayout
{
    public const float MinimumScale = 0.75f;
    public const float MaximumScale = 1.5f;

    public static float ClampScale(float value)
        => float.IsFinite(value)
            ? Math.Clamp(value, MinimumScale, MaximumScale)
            : 1f;

    public static int CalculateWidth(
        int viewportWidth,
        int margin,
        float scale,
        float viewportRatio,
        int minimumWidth)
    {
        int available = Math.Max(1, viewportWidth - margin * 2);
        int minimum = Math.Min(Math.Max(1, minimumWidth), available);
        int desired = (int)MathF.Round(viewportWidth * viewportRatio * ClampScale(scale));
        return Math.Clamp(desired, minimum, available);
    }

    public static int CalculateHeight(
        int viewportHeight,
        int margin,
        float scale,
        float viewportRatio,
        int minimumHeight)
    {
        int available = Math.Max(1, viewportHeight - margin * 2);
        int minimum = Math.Min(Math.Max(1, minimumHeight), available);
        int desired = (int)MathF.Round(viewportHeight * viewportRatio * ClampScale(scale));
        return Math.Clamp(desired, minimum, available);
    }

    public static int ScaleWithinBounds(int baseSize, float scale, int minimum, int maximum)
    {
        int lower = Math.Min(Math.Max(1, minimum), Math.Max(1, maximum));
        int upper = Math.Max(lower, maximum);
        int desired = (int)MathF.Round(baseSize * ClampScale(scale));
        return Math.Clamp(desired, lower, upper);
    }
}
