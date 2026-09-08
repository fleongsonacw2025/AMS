using Microsoft.EntityFrameworkCore;
using AMS.Models; 

namespace AMS.Data
{
    public class AmsDbContext : DbContext
    {
        public AmsDbContext(DbContextOptions<AmsDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Site> Sites { get; set; } = null!;
        public DbSet<PunchRecord> PunchRecords { get; set; } = null!;
        public DbSet<CorrectionRequest> CorrectionRequests { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<BreakRecord> BreakRecords { get; set; } = null!;
        public DbSet<LeaveRequest> LeaveRequests { get; set; } = null!;
        public DbSet<NotificationItem> Notifications { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();

            modelBuilder.Entity<User>().HasData(new User
            {
                Id = 1,
                Name = "TECH",
                FullName = "Tech Admin",
                Email = "admin@ams.com",
                PhoneNumber = "09157337337",
                Password = "password123", 
                Role = UserRole.Admin,
                IsActive = true
            });

            modelBuilder.Entity<Site>().HasData(
                new Site { Id = 1, Name = "ACW Head Office", Address = "Sturdee Street, Linden Park, Adelaide, SA 5065", Latitude = -34.9429, Longitude = 138.6384, RadiusMeters = 200 },
                new Site { Id = 2, Name = "FIELD", Address = "LOCATION TRACKED", Latitude = 0, Longitude = 0, RadiusMeters = 1000 }
            );
        }
    }
}