using DeptChat.Domain.Entities;
using DeptChat.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentManager> DepartmentManagers => Set<DepartmentManager>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =====================================================================
        // 0. Client Configuration (Multi-Tenancy)
        // =====================================================================
        modelBuilder.Entity<Client>()
            .HasIndex(c => c.Code)
            .IsUnique();

        modelBuilder.Entity<Department>()
            .HasOne(d => d.Client)
            .WithMany(c => c.Departments)
            .HasForeignKey(d => d.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasOne(u => u.Client)
                .WithMany(c => c.Users)
                .HasForeignKey(u => u.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(u => u.Email)
                .HasMaxLength(256);

            entity.Property(u => u.Username)
                .HasMaxLength(100);

            entity.Property(u => u.PhoneNumber)
                .HasMaxLength(50);

            entity.HasIndex(u => u.Email)
                .IsUnique()
                .HasFilter("[Email] IS NOT NULL");

            entity.HasIndex(u => u.PhoneNumber)
                .IsUnique()
                .HasFilter("[PhoneNumber] IS NOT NULL");

            entity.HasIndex(u => new { u.ClientId, u.Username })
                .IsUnique()
                .HasFilter("[Username] IS NOT NULL");
        });

        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.Client)
            .WithMany(cl => cl.Conversations)
            .HasForeignKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // =====================================================================
        // 1. DepartmentManager (Many-to-Many Composite Key)
        // =====================================================================
        modelBuilder.Entity<DepartmentManager>()
            .HasKey(dm => new { dm.DepartmentId, dm.UserId });

        modelBuilder.Entity<DepartmentManager>()
            .HasOne(dm => dm.Department)
            .WithMany(d => d.Managers)
            .HasForeignKey(dm => dm.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DepartmentManager>()
            .HasOne(dm => dm.User)
            .WithMany(u => u.DepartmentAssignments)
            .HasForeignKey(dm => dm.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // =====================================================================
        // 2. Conversation Configuration (One direct room per User per Dept)
        // =====================================================================
        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.DepartmentId, c.CreatedByUserId })
            .IsUnique(); // Guarantees a user has at most one room per department

        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.Department)
            .WithMany(d => d.Conversations)
            .HasForeignKey(c => c.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.CreatedByUser)
            .WithMany(u => u.CreatedConversations)
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // =====================================================================
        // 3. Message Configuration (Deduplication & Monotonic Sequences)
        // =====================================================================
        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.ConversationId, m.ClientGeneratedId })
            .IsUnique(); // Guarantees ZERO duplicate bubbles on flaky network retries

        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.ConversationId, m.SequenceNumber }); // High-speed delta sync index

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // =====================================================================
        // 4. SEED DATA (Default Client, Admin, Manager, and Default Departments)
        // =====================================================================
        var defaultClientId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var adminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var managerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var complaintsDeptId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var itDeptId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var billingDeptId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        // Seed Default Client
        modelBuilder.Entity<Client>().HasData(
            new Client
            {
                Id = defaultClientId,
                Name = "Main Organization",
                Code = "MAIN",
                Description = "Default organization workspace",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // Seed Users
        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = adminId,
                ClientId = defaultClientId,
                FullName = "System Admin",
                Username = "admin",
                Email = "admin@deptchat.com",
                PasswordHash = "HBFHCe+c2/4A7nyGOlPqeQ==:U/DqBR8Pec8uVGI+nbX02p0SRXsy8UjaWvogXMTsRd4=", // Password: Admin123!
                Role = UserRole.Admin,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = managerId,
                ClientId = defaultClientId,
                FullName = "Alex Manager",
                Username = "manager",
                Email = "manager@deptchat.com",
                PasswordHash = "HBFHCe+c2/4A7nyGOlPqeQ==:BfR+wSV3hUcPqCjeqhx9dCRNyQv+DVJF+EufQ/AMMI8=", // Password: Manager123!
                Role = UserRole.Manager,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // Seed Departments
        modelBuilder.Entity<Department>().HasData(
            new Department
            {
                Id = complaintsDeptId,
                ClientId = defaultClientId,
                Name = "Complaints & Feedback",
                Description = "Customer complaints, damaged deliveries, and service disputes",
                IsActive = true
            },
            new Department
            {
                Id = itDeptId,
                ClientId = defaultClientId,
                Name = "IT Support",
                Description = "Password resets, VPN, email access, and hardware help",
                IsActive = true
            },
            new Department
            {
                Id = billingDeptId,
                ClientId = defaultClientId,
                Name = "Billing & Accounts",
                Description = "Invoices, payment receipts, and refund requests",
                IsActive = true
            }
        );

        // Assign Alex Manager to Complaints & Feedback
        modelBuilder.Entity<DepartmentManager>().HasData(
            new DepartmentManager
            {
                DepartmentId = complaintsDeptId,
                UserId = managerId
            }
        );
    }
}
