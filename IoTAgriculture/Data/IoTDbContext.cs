using IoTAgriculture.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Data
{
    public class IoTDbContext : DbContext
    {
        public IoTDbContext(DbContextOptions<IoTDbContext> options)
            : base(options)
        {
        }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<AppUser> Users => Set<AppUser>();
        public DbSet<UserSession> UserSessions => Set<UserSession>();
        public DbSet<UserDevice> UserDevices => Set<UserDevice>();
        public DbSet<UserActivity> UserActivities => Set<UserActivity>();
        public DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Device>()
                .HasIndex(d => d.MacAddress)
                .IsUnique();

            modelBuilder.Entity<Device>()
                .HasIndex(d => d.DeviceKey)
                .IsUnique();

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.PhoneNumber)
                .IsUnique();

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.Email)
                .IsUnique()
                .HasFilter("[Email] <> ''");

            modelBuilder.Entity<EmailVerificationCode>()
                .HasIndex(x => new { x.Email, x.Purpose, x.Code });

            modelBuilder.Entity<UserSession>()
                .HasIndex(s => s.Token)
                .IsUnique();

            modelBuilder.Entity<UserSession>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserDevice>()
                .HasIndex(x => new { x.UserId, x.DeviceKey })
                .IsUnique();

            modelBuilder.Entity<UserDevice>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserActivity>()
                .HasIndex(x => new { x.UserId, x.CreatedAt });

            modelBuilder.Entity<UserActivity>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

        }
    }
}
