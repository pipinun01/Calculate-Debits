namespace Debits.Domain.Entities;

public enum MemberRole
{
    Owner,
    Worker,
}

public enum TransactionType
{
    Debt,
    Payment,
}

public enum EntrySource
{
    Manual,
    Voice,
    Text,
}

public enum PendingEntryStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Expired,
}

public enum ReminderKind
{
    DueIn3,
    DueToday,
}

public enum ReminderRecipients
{
    All,
    Owners,
}

public enum AppLanguage
{
    UzLatn,
    UzCyrl,
    Ru,
}

public static class AppLanguageCodes
{
    public const string UzLatn = "uz-Latn";
    public const string UzCyrl = "uz-Cyrl";
    public const string Ru = "ru";

    public static string ToCode(this AppLanguage language) => language switch
    {
        AppLanguage.UzLatn => UzLatn,
        AppLanguage.UzCyrl => UzCyrl,
        AppLanguage.Ru => Ru,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    public static AppLanguage FromCode(string code) => code switch
    {
        UzLatn => AppLanguage.UzLatn,
        UzCyrl => AppLanguage.UzCyrl,
        Ru => AppLanguage.Ru,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown language code"),
    };

    /// <summary>Maps a Telegram <c>language_code</c> to the closest supported language.</summary>
    public static AppLanguage FromTelegram(string? languageCode) =>
        languageCode is null ? AppLanguage.UzLatn
        : languageCode.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? AppLanguage.Ru
        : AppLanguage.UzLatn;
}
