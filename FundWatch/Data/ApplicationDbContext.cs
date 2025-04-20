using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FundWatch.Models
{
    public partial class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<AppUserStock> UserStocks { get; set; }
        public DbSet<AppStockSimulation> StockSimulations { get; set; }
        public DbSet<AppStockTransaction> StockTransactions { get; set; }
        public DbSet<AppWatchlist> Watchlists { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure table names without underscores
            modelBuilder.Entity<AppUserStock>().ToTable("AppUserStocks");
            modelBuilder.Entity<AppStockSimulation>().ToTable("AppStockSimulations");
            modelBuilder.Entity<AppStockTransaction>().ToTable("AppStockTransactions");
            modelBuilder.Entity<AppWatchlist>().ToTable("AppWatchlists");

            // Configure relationships and Id generation for all entities
            modelBuilder.Entity<AppUserStock>(entity =>
            {
                // Configure primary key with identity column
                entity.Property(e => e.Id)
                      .ValueGeneratedOnAdd();

                // Configure the relationship with IdentityUser
                //entity.HasOne(us => us.User)
                //      .WithMany()
                //      .HasForeignKey(us => us.UserId)
                //      .OnDelete(DeleteBehavior.Cascade);

                // Configure decimal precision
                entity.Property(e => e.PurchasePrice)
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.CurrentPrice)
                      .HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<AppStockSimulation>(entity =>
            {
                // Configure primary key with identity column
                entity.Property(e => e.Id)
                      .ValueGeneratedOnAdd();

                // Configure the relationship with IdentityUser
                //entity.HasOne(ss => ss.User)
                //      .WithMany()
                //      .HasForeignKey(ss => ss.UserId)
                //      .OnDelete(DeleteBehavior.Cascade);

                // Configure decimal precision
                entity.Property(e => e.SimulatedPurchasePrice)
                      .HasColumnType("decimal(18,2)");

                entity.Property(e => e.SimulatedCurrentPrice)
                      .HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<AppStockTransaction>(entity =>
            {
                // Configure primary key with identity column
                entity.Property(e => e.Id)
                      .ValueGeneratedOnAdd();

                // Configure the relationship with IdentityUser
                //entity.HasOne(st => st.User)
                //      .WithMany()
                //      .HasForeignKey(st => st.UserId)
                //      .OnDelete(DeleteBehavior.Cascade);

                // Configure decimal precision
                entity.Property(e => e.PricePerShare)
                      .HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<AppWatchlist>(entity =>
            {
                // Configure primary key with identity column
                entity.Property(e => e.Id)
                      .ValueGeneratedOnAdd();

                // Configure the relationship with IdentityUser
                //entity.HasOne(wl => wl.User)
                //      .WithMany()
                //      .HasForeignKey(wl => wl.UserId)
                //      .OnDelete(DeleteBehavior.Cascade);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
