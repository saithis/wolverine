using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wolverine.RDBMS;

namespace Wolverine.EntityFrameworkCore.Internals;

public class WolverineModelCustomizer : RelationalModelCustomizer
{
    public WolverineModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var settings = context.Database.GetService<DatabaseSettings>();

        modelBuilder.MapWolverineEnvelopeStorage(settings.SchemaName);
        
        // Configure node-related entities
        modelBuilder.Entity<WolverineNodeEntity>(entity =>
        {
            entity.HasKey(e => e.NodeId);
            entity.HasIndex(e => e.AssignedNodeNumber).IsUnique();
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.ControlUri).HasMaxLength(500);
            entity.Property(e => e.Version).HasMaxLength(50);
        });
        
        modelBuilder.Entity<NodeRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.NodeNumber, e.Timestamp });
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ServiceName).HasMaxLength(100);
            entity.Property(e => e.AgentUri).HasMaxLength(500);
        });
        
        modelBuilder.Entity<AgentRestrictionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AgentUri, e.Type, e.NodeNumber });
            entity.Property(e => e.AgentUri).HasMaxLength(500);
        });
        
        modelBuilder.Entity<NodeAgentAssignmentEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NodeId);
            entity.HasIndex(e => new { e.NodeId, e.AgentUri }).IsUnique();
            entity.Property(e => e.AgentUri).HasMaxLength(500);
            
            entity.HasOne(e => e.Node)
                .WithMany(n => n.AgentAssignments)
                .HasForeignKey(e => e.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
    }
}

