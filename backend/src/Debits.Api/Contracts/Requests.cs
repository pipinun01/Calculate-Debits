using Debits.Application.Members;
using Debits.Application.Shops;
using Debits.Domain.Entities;
using FluentValidation;

namespace Debits.Api.Contracts;

public sealed record TelegramAuthRequest(string? InitData);

public sealed record MeDto(
    Guid Id,
    long TelegramUserId,
    string FirstName,
    string? Username,
    AppLanguage Language,
    Guid? ActiveShopId,
    IReadOnlyList<ShopMembershipInfo> Shops)
{
    public static MeDto From(AppUser user, IReadOnlyList<ShopMembershipInfo> shops) =>
        new(user.Id, user.TelegramUserId, user.FirstName, user.Username, user.Language, user.ActiveShopId, shops);
}

public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, MeDto User);

public sealed record UpdateMeRequest(AppLanguage? Language, Guid? ActiveShopId);

public sealed record CreateShopRequest(string Name, int? DefaultDueDays, AppLanguage? Language);

public sealed record CreateShopResponse(ShopDto Shop, ShopMembershipInfo Membership);

public sealed record UpdateShopRequest(string? Name, int? DefaultDueDays, AppLanguage? Language, string? Timezone, ReminderRecipients? ReminderRecipients, int? ReminderHour);

public sealed record UpdateMemberRequest(MemberRole Role);

public sealed record MemberResponse(
    Guid Id,
    Guid UserId,
    long TelegramUserId,
    string FirstName,
    string? Username,
    MemberRole Role,
    bool NotificationsBlocked,
    DateTime CreatedAt,
    bool IsMe)
{
    public static MemberResponse From(MemberDto m, Guid? myMemberId) =>
        new(m.Id, m.UserId, m.TelegramUserId, m.FirstName, m.Username, m.Role, m.NotificationsBlocked, m.CreatedAt, m.Id == myMemberId);
}

public sealed record CreateInviteRequest(MemberRole? Role);

public sealed record InviteResponse(Guid Id, MemberRole Role, DateTime ExpiresAt, DateTime CreatedAt, string? CreatedByName, string Link);

public sealed record CreateCustomerRequest(string Name, string? Tag, string? Phone, string? Notes);

/// <summary>Null = unchanged; "" clears Tag / Phone / Notes.</summary>
public sealed record UpdateCustomerRequest(string? Name, string? Tag, string? Phone, string? Notes, bool? IsArchived);

public sealed record CreateTransactionRequest(Guid CustomerId, TransactionType Type, long Amount, string? Comment, DateOnly? DueDate, Guid? TargetDebtId);

/// <summary>Null = unchanged; Comment "" clears it; TargetDebtId = empty GUID clears the target.</summary>
public sealed record UpdateTransactionRequest(long? Amount, string? Comment, DateOnly? DueDate, Guid? TargetDebtId);

public sealed class CreateShopRequestValidator : AbstractValidator<CreateShopRequest>
{
    public CreateShopRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ShopService.MaxNameLength);
        RuleFor(x => x.DefaultDueDays).InclusiveBetween(ShopService.MinDueDays, ShopService.MaxDueDays).When(x => x.DefaultDueDays is not null);
    }
}

public sealed class UpdateShopRequestValidator : AbstractValidator<UpdateShopRequest>
{
    public UpdateShopRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ShopService.MaxNameLength).When(x => x.Name is not null);
        RuleFor(x => x.DefaultDueDays).InclusiveBetween(ShopService.MinDueDays, ShopService.MaxDueDays).When(x => x.DefaultDueDays is not null);
        RuleFor(x => x.ReminderHour).InclusiveBetween(0, 23).When(x => x.ReminderHour is not null);
        RuleFor(x => x.Timezone).NotEmpty().MaximumLength(64).When(x => x.Timezone is not null);
    }
}

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Tag).MaximumLength(64);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.Notes).MaximumLength(1024);
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128).When(x => x.Name is not null);
        RuleFor(x => x.Tag).MaximumLength(64);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.Notes).MaximumLength(1024);
    }
}

public sealed class CreateTransactionRequestValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Comment).MaximumLength(512);
        RuleFor(x => x.Type).IsInEnum();
    }
}

public sealed class UpdateTransactionRequestValidator : AbstractValidator<UpdateTransactionRequest>
{
    public UpdateTransactionRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).When(x => x.Amount is not null);
        RuleFor(x => x.Comment).MaximumLength(512);
    }
}

public sealed class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator() => RuleFor(x => x.Role).IsInEnum();
}

/// <summary>Lenient enum parsing for query strings: "due_soon", "dueSoon", "DueSoon" all match.</summary>
public static class QueryEnum
{
    public static T Parse<T>(string? value, T fallback) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Replace("_", string.Empty).Replace("-", string.Empty);
        return Enum.TryParse<T>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new Domain.DomainException("query.invalid_enum", $"Unknown value '{value}' for {typeof(T).Name}");
    }
}

/// <summary>Inclusive local date range; both null = the current month.</summary>
public sealed record PeriodRequest(DateOnly? From, DateOnly? To);
