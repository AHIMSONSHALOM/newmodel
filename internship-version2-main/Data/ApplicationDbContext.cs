using Microsoft.EntityFrameworkCore;
using ProductHub_MVC.Models;

namespace ProductHub_MVC.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UserSession> UserSessions => Set<UserSession>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserSession>(entity =>
            {
                entity.ToTable("UserSessions");
                entity.HasKey(e => e.SessionId);
                entity.Property(e => e.SessionId).HasColumnName("SessionId");
                entity.Property(e => e.UserId).HasColumnName("UserId");
                entity.Property(e => e.Email).HasColumnName("Email").HasMaxLength(256).IsRequired();
                entity.Property(e => e.DeviceId).HasColumnName("DeviceId").HasMaxLength(128).IsRequired();
                entity.Property(e => e.BrowserInfo).HasColumnName("BrowserInfo").HasMaxLength(512).IsRequired();
                entity.Property(e => e.IpAddress).HasColumnName("IpAddress").HasMaxLength(64).IsRequired();
                entity.Property(e => e.LoginTime).HasColumnName("LoginTime");
                entity.Property(e => e.LogoutTime).HasColumnName("LogoutTime");
                entity.Property(e => e.IsActive).HasColumnName("IsActive");
                entity.HasIndex(e => e.UserId)
                    .HasDatabaseName("UX_UserSessions_OneActivePerUser")
                    .IsUnique()
                    .HasFilter("[IsActive] = 1");
            });
        }
    }
}
