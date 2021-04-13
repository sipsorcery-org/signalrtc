using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace signalrtc.DataAccess
{
    public partial class SIPAssetsDbContext : DbContext
    {
        //public SIPAssetsDbContext()
        //{
        //}

        public SIPAssetsDbContext(DbContextOptions<SIPAssetsDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<CDR> CDRs { get; set; }
        public virtual DbSet<SIPAccount> SIPAccounts { get; set; }
        public virtual DbSet<SIPCall> SIPCalls { get; set; }
        public virtual DbSet<SIPDialPlan> SIPDialPlans { get; set; }
        public virtual DbSet<SIPDomain> SIPDomains { get; set; }
        public virtual DbSet<SIPRegistrarBinding> SIPRegistrarBindings { get; set; }
        public virtual DbSet<SessionCache> SessionCaches { get; set; }
        public virtual DbSet<WebRTCSignal> WebRTCSignals { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Name=ConnectionStrings.SIPAssetsLite");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CDR>(entity =>
            {
                entity.ToTable("CDR");

                entity.Property(e => e.ID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.AnsweredAt).HasColumnType("nvarchar");

                entity.Property(e => e.AnsweredReason).HasColumnType("varchar(512)");

                entity.Property(e => e.AnsweredStatus).HasColumnType("int");

                entity.Property(e => e.BridgeID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.CallID)
                    .IsRequired()
                    .HasColumnType("varchar(256)");

                entity.Property(e => e.Created)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.Direction)
                    .IsRequired()
                    .HasColumnType("varchar(3)");

                entity.Property(e => e.DstHost)
                    .IsRequired()
                    .HasColumnType("varchar(128)");

                entity.Property(e => e.DstUri)
                    .IsRequired()
                    .HasColumnType("varchar(1024)");

                entity.Property(e => e.DstUser).HasColumnType("varchar(128)");

                entity.Property(e => e.Duration).HasColumnType("int");

                entity.Property(e => e.FromHeader).HasColumnType("varchar(1024)");

                entity.Property(e => e.FromName).HasColumnType("varchar(128)");

                entity.Property(e => e.FromUser).HasColumnType("varchar(128)");

                entity.Property(e => e.HungupAt).HasColumnType("nvarchar");

                entity.Property(e => e.HungupReason).HasColumnType("varchar(512)");

                entity.Property(e => e.InProgressAt).HasColumnType("nvarchar");

                entity.Property(e => e.InProgressReason).HasColumnType("varchar(512)");

                entity.Property(e => e.InProgressStatus).HasColumnType("int");

                entity.Property(e => e.Inserted)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.LocalSocket).HasColumnType("varchar(64)");

                entity.Property(e => e.RemoteSocket).HasColumnType("varchar(64)");

                entity.Property(e => e.RingDuration).HasColumnType("int");
            });

            modelBuilder.Entity<SIPAccount>(entity =>
            {
                entity.HasIndex(e => new { e.SIPUsername, e.DomainID }, "IX_SIPAccounts_SIPUsername_DomainID")
                    .IsUnique();

                entity.Property(e => e.ID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.DomainID)
                    .IsRequired()
                    .HasColumnType("nvarchar(36)");

                entity.Property(e => e.HA1Digest)
                    .IsRequired()
                    .HasColumnType("nvarchar(32)");

                entity.Property(e => e.Inserted)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.IsDisabled).HasColumnType("int(2)");

                entity.Property(e => e.SIPDialPlanID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.SIPUsername)
                    .IsRequired()
                    .HasColumnType("nvarchar(32)");

                entity.HasOne(d => d.Domain)
                    .WithMany(p => p.SIPAccounts)
                    .HasForeignKey(d => d.DomainID);

                entity.HasOne(d => d.SIPDialPlan)
                    .WithMany(p => p.SIPAccounts)
                    .HasForeignKey(d => d.SIPDialPlanID)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SIPCall>(entity =>
            {
                entity.Property(e => e.ID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.BridgeID)
                    .IsRequired()
                    .HasColumnType("nvarchar(36)");

                entity.Property(e => e.CDRID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.CSeq).HasColumnType("int");

                entity.Property(e => e.CallDurationLimit).HasColumnType("int");

                entity.Property(e => e.CallID)
                    .IsRequired()
                    .HasColumnType("varchar(128)");

                entity.Property(e => e.Direction)
                    .IsRequired()
                    .HasColumnType("varchar(3)");

                entity.Property(e => e.Inserted)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.LocalTag)
                    .IsRequired()
                    .HasColumnType("varchar(64)");

                entity.Property(e => e.LocalUserField)
                    .IsRequired()
                    .HasColumnType("varchar(512)");

                entity.Property(e => e.ProxySendFrom).HasColumnType("varchar(64)");

                entity.Property(e => e.RemoteSocket)
                    .IsRequired()
                    .HasColumnType("varchar(64)");

                entity.Property(e => e.RemoteTag)
                    .IsRequired()
                    .HasColumnType("varchar(64)");

                entity.Property(e => e.RemoteTarget)
                    .IsRequired()
                    .HasColumnType("varchar(256)");

                entity.Property(e => e.RemoteUserField)
                    .IsRequired()
                    .HasColumnType("varchar(512)");

                entity.Property(e => e.RouteSet).HasColumnType("varchar(512)");

                entity.HasOne(d => d.CDR)
                    .WithMany(p => p.SIPCalls)
                    .HasForeignKey(d => d.CDRID)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SIPDialPlan>(entity =>
            {
                entity.HasIndex(e => e.DialPlanName, "IX_SIPDialPlans_DialPlanName")
                    .IsUnique();

                entity.Property(e => e.ID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.AcceptNonInvite).HasColumnType("int(2)");

                entity.Property(e => e.DialPlanName)
                    .IsRequired()
                    .HasColumnType("varchar(64)");

                entity.Property(e => e.DialPlanScript).HasColumnType("varchar");

                entity.Property(e => e.Inserted)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.LastUpdate)
                    .IsRequired()
                    .HasColumnType("nvarchar");
            });

            modelBuilder.Entity<SIPDomain>(entity =>
            {
                entity.HasIndex(e => e.Domain, "IX_SIPDomains_Domain")
                    .IsUnique();

                entity.Property(e => e.ID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.AliasList).HasColumnType("nvarchar(1024)");

                entity.Property(e => e.Domain)
                    .IsRequired()
                    .HasColumnType("nvarchar(128)");

                entity.Property(e => e.Inserted)
                    .IsRequired()
                    .HasColumnType("nvarchar");
            });

            modelBuilder.Entity<SIPRegistrarBinding>(entity =>
            {
                entity.Property(e => e.ID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.ContactURI)
                    .IsRequired()
                    .HasColumnType("nvarchar(767)");

                entity.Property(e => e.Expiry).HasColumnType("int");

                entity.Property(e => e.ExpiryTime)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.LastUpdate)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.ProxySIPSocket).HasColumnType("nvarchar(64)");

                entity.Property(e => e.RegistrarSIPSocket).HasColumnType("nvarchar(64)");

                entity.Property(e => e.RemoteSIPSocket)
                    .IsRequired()
                    .HasColumnType("nvarchar(64)");

                entity.Property(e => e.SIPAccountID)
                    .IsRequired()
                    .HasColumnType("nvarchar(36)");

                entity.Property(e => e.UserAgent).HasColumnType("nvarchar(1024)");

                entity.HasOne(d => d.SIPAccount)
                    .WithMany(p => p.SIPRegistrarBindings)
                    .HasForeignKey(d => d.SIPAccountID)
                    .OnDelete(DeleteBehavior.ClientSetNull);
            });

            modelBuilder.Entity<SessionCache>(entity =>
            {
                entity.ToTable("SessionCache");

                entity.Property(e => e.Id).HasColumnType("nvarchar");

                entity.Property(e => e.AbsoluteExpiration).HasColumnType("nvarcharoffset");

                entity.Property(e => e.ExpiresAtTime)
                    .IsRequired()
                    .HasColumnType("nvarcharoffset");

                entity.Property(e => e.SlidingExpirationInSeconds).HasColumnType("bigint");

                entity.Property(e => e.Value)
                    .IsRequired()
                    .HasColumnType("varbinary");
            });

            modelBuilder.Entity<WebRTCSignal>(entity =>
            {
                entity.Property(e => e.ID).HasColumnType("nvarchar(36)");

                entity.Property(e => e.DeliveredAt).HasColumnType("nvarchar");

                entity.Property(e => e.From)
                    .IsRequired()
                    .HasColumnType("varchar(256)");

                entity.Property(e => e.Inserted)
                    .IsRequired()
                    .HasColumnType("nvarchar");

                entity.Property(e => e.Signal)
                    .IsRequired()
                    .HasColumnType("varchar");

                entity.Property(e => e.SignalType)
                    .IsRequired()
                    .HasColumnType("varchar(16)");

                entity.Property(e => e.To)
                    .IsRequired()
                    .HasColumnType("varchar(256)");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
