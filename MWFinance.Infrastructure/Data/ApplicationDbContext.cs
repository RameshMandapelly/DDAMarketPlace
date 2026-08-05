using Microsoft.EntityFrameworkCore;
using MWFinance.Domain.Entities;






namespace MWFinance.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<DirectDebitAuthority> DirectDebitAuthorities { get; set; } //TODO
        public DbSet<FintechClientApi> FintechClienstApi { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DirectDebitAuthority>(entity =>
            {
                // Fix decimal truncation warnings
                entity.Property(e => e.MinAmount)
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.MaxAmount)
                      .HasColumnType("decimal(18,2)");
            });
            // ── NEW: ApiClient config ─────────────────────────────────────────
            modelBuilder.Entity<FintechClientApi>(entity =>
            {
                // ClientId must be unique — no two Fintech companies share the same ID
                entity.HasIndex(e => e.ClientId).IsUnique();

                // Max lengths to avoid unbounded text columns
                entity.Property(e => e.ClientId).HasMaxLength(100).IsRequired();
                entity.Property(e => e.ClientSecretHash).HasMaxLength(255).IsRequired();
                entity.Property(e => e.CompanyName).HasMaxLength(200).IsRequired();
            });
        }
    }
}
