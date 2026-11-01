using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Certificates;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CertificateEntity> Certificates => Set<CertificateEntity>();
    public DbSet<CertificateRequest> CertificateRequests => Set<CertificateRequest>();
    public DbSet<CertificateNumberReservation> CertificateNumberReservations => Set<CertificateNumberReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 业务和审批共用一个 DbContext，保持回调与审批状态的本地事务。
        modelBuilder.ConfigureApproval();
        modelBuilder.ConfigureCertificates();
    }
}
