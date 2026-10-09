namespace Debits.Domain.Entities;

public sealed class Shop : IHasCreatedAt, IHasUpdatedAt
{
    public const int DefaultDueDaysDefault = 30;
    public const string DefaultTimezone = "Asia/Tashkent";
    public const int DefaultReminderHour = 9;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public int DefaultDueDays { get; set; } = DefaultDueDaysDefault;
    public AppLanguage Language { get; set; } = AppLanguage.UzLatn;
    public string Timezone { get; set; } = DefaultTimezone;
    public ReminderRecipients ReminderRecipients { get; set; } = ReminderRecipients.All;
    public int ReminderHour { get; set; } = DefaultReminderHour;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ShopMember> Members { get; set; } = [];
    public ICollection<Customer> Customers { get; set; } = [];
}
