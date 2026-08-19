namespace VivantValley.Services;

/// <summary>Builds a dynamic letter with a vanilla click-to-claim item attachment.</summary>
public static class GiftMailContentService
{
    public static string Build(SocialMailGift mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        string body = Sanitize(
            mail.LetterBody,
            "昨天聊完以后，我想把这份礼物寄给你。",
            1200);
        string sender = Sanitize(mail.NpcDisplayName, "村民", 80);
        string attachment = mail.RewardDelivered
            ? string.Empty
            : $"^%item id {ValidateItemId(mail.QualifiedItemId)} {Math.Clamp(mail.Quantity, 1, 999)} %%";
        return $"{body}{attachment}^^-{sender}";
    }

    private static string ValidateItemId(string? value)
    {
        string itemId = (value ?? string.Empty).Trim();
        if (itemId.Length == 0
            || itemId.Any(character => char.IsControl(character)
                                       || char.IsWhiteSpace(character)
                                       || character is '%' or '^' or '[' or ']'))
        {
            throw new InvalidOperationException("The mail attachment item ID isn't safe.");
        }

        return itemId;
    }

    public static string Sanitize(string? value, string fallback, int maximumLength)
    {
        string clean = (value ?? string.Empty)
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("[#]", string.Empty, StringComparison.Ordinal)
            .Replace("^", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (clean.Length == 0)
            clean = fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}
