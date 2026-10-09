using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Debits.Infrastructure.Persistence.Converters;

public sealed class AppLanguageConverter() : ValueConverter<AppLanguage, string>(
    v => v.ToCode(),
    s => AppLanguageCodes.FromCode(s));
