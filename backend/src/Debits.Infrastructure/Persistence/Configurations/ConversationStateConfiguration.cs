using Debits.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Debits.Infrastructure.Persistence.Configurations;

public sealed class ConversationStateConfiguration : IEntityTypeConfiguration<ConversationState>
{
    public void Configure(EntityTypeBuilder<ConversationState> b)
    {
        b.ToTable("conversation_states");
        b.HasKey(s => s.UserId);
        b.Property(s => s.State).HasMaxLength(64).IsRequired();
        b.Property(s => s.Data).HasColumnType("jsonb").IsRequired();
        b.HasOne<AppUser>().WithOne().HasForeignKey<ConversationState>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
