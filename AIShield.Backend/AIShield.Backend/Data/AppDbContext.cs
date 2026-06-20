using AIShield.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace AIShield.Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        // 审计日志表
        public DbSet<AuditRecord> AuditRecords { get; set; }

        // Agent 接入应用表
        public DbSet<AgentApp> AgentApps { get; set; }

        // 安全规则表
        public DbSet<SecurityRule> SecurityRules { get; set; }

        // Agent 与安全规则的绑定关系表
        public DbSet<AgentRule> AgentRules { get; set; }

        // Agent 长期记忆表
        public DbSet<MemoryRecord> MemoryRecords { get; set; }

        // Agent 记忆同步动作表
        public DbSet<MemorySyncAction> MemorySyncActions { get; set; }

        // 配置实体字段、索引和关联关系
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureAgent(modelBuilder);
            ConfigureSecurityRule(modelBuilder);
            ConfigureAgentRule(modelBuilder);
            ConfigureAuditRecord(modelBuilder);
            ConfigureMemoryRecord(modelBuilder);
            ConfigureMemorySyncAction(modelBuilder);
        }

        // 配置 Agent 表结构
        private static void ConfigureAgent(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AgentApp>(entity =>
            {
                entity.ToTable("Agents");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();

                entity.Property(x => x.AgentName)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.Scenario)
                    .HasMaxLength(500);

                entity.Property(x => x.AgentKeyHash)
                    .HasMaxLength(128)
                    .IsRequired();

                entity.Property(x => x.AgentKeyFingerprint)
                    .HasMaxLength(128);

                entity.Property(x => x.AgentKeyPreview)
                    .HasMaxLength(32)
                    .IsRequired();

                entity.Property(x => x.AgentKeySalt)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(x => x.Enabled)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.HasIndex(x => x.AgentKeyFingerprint)
                    .IsUnique()
                    .HasFilter("[AgentKeyFingerprint] IS NOT NULL");
            });
        }

        // 配置安全规则表结构
        private static void ConfigureSecurityRule(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SecurityRule>(entity =>
            {
                entity.ToTable("SecurityRules");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();

                entity.Property(x => x.RuleId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(x => x.Name)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.RuleType)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.MatchType)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Pattern)
                    .HasColumnType("nvarchar(max)")
                    .IsRequired();

                entity.Property(x => x.RiskLevel)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.Action)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.Replacement)
                    .HasMaxLength(500);

                entity.HasIndex(x => x.RuleId)
                    .IsUnique();
            });
        }

        // 配置 Agent 与规则的多对多绑定表
        private static void ConfigureAgentRule(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AgentRule>(entity =>
            {
                entity.ToTable("AgentRules");

                entity.HasKey(x => new { x.AgentId, x.RuleId });

                entity.HasOne(x => x.Agent)
                    .WithMany(x => x.AgentRules)
                    .HasForeignKey(x => x.AgentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Rule)
                    .WithMany(x => x.AgentRules)
                    .HasForeignKey(x => x.RuleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        // 配置审计日志表结构
        private static void ConfigureAuditRecord(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditRecord>(entity =>
            {
                entity.ToTable("AuditRecords");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).ValueGeneratedOnAdd();

                entity.Property(x => x.AgentName)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.SubjectHash)
                    .HasMaxLength(128);

                entity.Property(x => x.Direction)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.OriginalContent)
                    .HasColumnType("nvarchar(max)");

                entity.Property(x => x.ProcessedContent)
                    .HasColumnType("nvarchar(max)");

                entity.Property(x => x.RiskLevel)
                    .HasConversion<string>()
                    .HasMaxLength(30);

                entity.Property(x => x.Action)
                    .HasConversion<string>()
                    .HasMaxLength(30);

                entity.Property(x => x.HitRules)
                    .HasMaxLength(500);

                entity.Property(x => x.Reason)
                    .HasMaxLength(1000);

                entity.Property(x => x.ClientIp)
                    .HasMaxLength(100);

                entity.HasIndex(x => x.AgentId);
                entity.HasIndex(x => x.CreatedAt);
            });
        }

        // 配置长期记忆表结构
        private static void ConfigureMemoryRecord(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MemoryRecord>(entity =>
            {
                entity.ToTable("MemoryRecords");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.ExternalMemoryId)
                    .HasMaxLength(128)
                    .IsRequired();

                entity.HasOne(x => x.Agent)
                    .WithMany(x => x.MemoryRecords)
                    .HasForeignKey(x => x.AgentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(x => x.Content)
                    .HasColumnType("nvarchar(max)")
                    .IsRequired();

                entity.Property(x => x.ContentHash)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(x => x.Source)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.MemoryKey)
                    .HasMaxLength(200);

                entity.Property(x => x.EmbeddingJson)
                    .HasColumnType("nvarchar(max)");

                entity.Property(x => x.ArchiveReason)
                    .HasMaxLength(100);

                entity.Property(x => x.RowVersion)
                    .IsRowVersion();

                entity.HasIndex(x => new { x.AgentId, x.ExternalMemoryId })
                    .IsUnique();

                entity.HasIndex(x => new
                {
                    x.AgentId,
                    x.Source,
                    x.MemoryKey,
                    x.IsArchived
                });

                entity.HasIndex(x => new { x.AgentId, x.ContentHash });

                entity.HasIndex(x => new
                {
                    x.AgentId,
                    x.IsArchived,
                    x.UpdatedAt
                });
            });
        }

        // 配置记忆同步动作表。
        private static void ConfigureMemorySyncAction(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MemorySyncAction>(entity =>
            {
                entity.ToTable("MemorySyncActions");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.ActionId)
                    .IsRequired();

                entity.Property(x => x.ExternalMemoryId)
                    .HasMaxLength(128)
                    .IsRequired();

                entity.Property(x => x.ActionType)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.Status)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Reason)
                    .HasMaxLength(100);

                entity.Property(x => x.FailureMessage)
                    .HasMaxLength(500);

                entity.Property(x => x.RowVersion)
                    .IsRowVersion();

                entity.HasOne(x => x.Agent)
                    .WithMany(x => x.MemorySyncActions)
                    .HasForeignKey(x => x.AgentId)
                    .OnDelete(DeleteBehavior.Cascade);

                // 对外操作 ID 必须唯一，用于幂等确认。
                entity.HasIndex(x => x.ActionId)
                    .IsUnique();

                // 用户 Agent 拉取待处理动作时使用。
                entity.HasIndex(x => new
                {
                    x.AgentId,
                    x.Status,
                    x.Id
                });

                // 查询指定记忆的同步历史时使用。
                entity.HasIndex(x => new
                {
                    x.AgentId,
                    x.ExternalMemoryId,
                    x.CreatedAt
                });
            });
        }
    }
}
