using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wolverine.Persistence.Durability;
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
        
        // TODO: make this a ModelBuilder extension as well for users not wanting to use AddDbContextWithWolverineIntegration?
        // TODO: should we always add the tables to make config errors less likely?
        // Because some people have a separate migrations tool, that would need to be configured exactly same way if we use conditionals here.
        // Things like CommandQueuesEnabled are not obvious to cause schema differences.
        if (settings.Role == MessageStoreRole.Main)
        {
            modelBuilder.Entity<WolverineNodeEntity>(eb =>
            {
                eb.ToTable(DatabaseConstants.NodeTableName, settings.SchemaName, ConditionalMigrationTableBuilder);
                eb.Property(x => x.Id).HasColumnName(DatabaseConstants.Id);
                eb.HasKey(x => x.Id);

                eb.Property(x => x.AssignedNodeNumber).HasColumnName(DatabaseConstants.NodeNumber).IsRequired().ValueGeneratedOnAdd(); // TODO: NOT NULL IDENTITY
                eb.HasIndex(e => e.AssignedNodeNumber).IsUnique();
                
                eb.Property(x => x.Description).HasColumnName(DatabaseConstants.Description).IsRequired();
                eb.Property(x => x.Uri).HasColumnName(DatabaseConstants.Uri).IsRequired().HasMaxLength(500);
                eb.Property(x => x.StartedAt).HasColumnName(DatabaseConstants.Started).IsRequired().ValueGeneratedOnAdd(); // TODO: default current time utc
                eb.Property(x => x.LastHealthCheck).HasColumnName(DatabaseConstants.HealthCheck).IsRequired().ValueGeneratedOnAdd(); // TODO: default current time utc
                eb.Property(x => x.Version).HasColumnName(DatabaseConstants.Version).IsRequired().HasMaxLength(100);
                eb.Property(x => x.Capabilities).HasColumnName(DatabaseConstants.Capabilities);

                eb.HasMany(x => x.NodeAssignments)
                    .WithOne(x => x.Node)
                    .HasForeignKey(x => x.NodeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<NodeAgentAssignmentEntity>(eb =>
            {
                eb.ToTable(DatabaseConstants.NodeAssignmentsTableName, settings.SchemaName, ConditionalMigrationTableBuilder);
                eb.Property(x => x.Id).HasColumnName(DatabaseConstants.Id).HasMaxLength(500);
                eb.HasKey(x => x.Id);

                eb.Property(x => x.NodeId).HasColumnName(DatabaseConstants.NodeId).IsRequired();
                eb.Property(x => x.StartedAt).HasColumnName(DatabaseConstants.Started).IsRequired().ValueGeneratedOnAdd();
            });

            // TODO:
            // if (_settings.CommandQueuesEnabled)
            // {
            //     var queueTable = new Table(new DbObjectName(SchemaName, DatabaseConstants.ControlQueueTableName));
            //     queueTable.AddColumn<Guid>("id").AsPrimaryKey();
            //     queueTable.AddColumn<string>("message_type").NotNull();
            //     queueTable.AddColumn<Guid>("node_id").NotNull();
            //     queueTable.AddColumn(DatabaseConstants.Body, "varbinary(max)").NotNull();
            //     queueTable.AddColumn<DateTimeOffset>("posted").NotNull().DefaultValueByExpression("GETUTCDATE()");
            //     queueTable.AddColumn<DateTimeOffset>("expires");
            //
            //     yield return queueTable;
            // }
            //
            // if (_settings.AddTenantLookupTable)
            // {
            //     var tenantTable = new Table(new DbObjectName(SchemaName, DatabaseConstants.TenantsTableName));
            //     tenantTable.AddColumn(StorageConstants.TenantIdColumn, "varchar(100)").AsPrimaryKey();
            //     tenantTable.AddColumn(StorageConstants.ConnectionStringColumn, "varchar(500)").NotNull();
            //     yield return tenantTable;
            // }

            modelBuilder.Entity<AgentRestrictionEntity>(eb =>
            {
                eb.ToTable(DatabaseConstants.AgentRestrictionsTableName, settings.SchemaName, ConditionalMigrationTableBuilder);
                eb.Property(x => x.Id).HasColumnName(DatabaseConstants.Id);
                eb.HasKey(x => x.Id);

                eb.Property(x => x.Uri).HasColumnName(DatabaseConstants.Uri).IsRequired();
                eb.Property(x => x.Type).HasColumnName("type").IsRequired();
                eb.Property(x => x.NodeNumber).HasColumnName("node").IsRequired().HasDefaultValue(0);
            });

            modelBuilder.Entity<NodeRecordEntity>(eb =>
            {
                eb.ToTable(DatabaseConstants.NodeRecordTableName, settings.SchemaName, ConditionalMigrationTableBuilder);
                eb.Property(x => x.Id).HasColumnName(DatabaseConstants.Id);
                eb.HasKey(x => x.Id);

                eb.Property(x => x.NodeNumber).HasColumnName(DatabaseConstants.NodeNumber).IsRequired();
                eb.Property(x => x.EventName).HasColumnName("event_name").IsRequired().HasMaxLength(500);
                eb.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired().ValueGeneratedOnAdd();
                eb.Property(x => x.Description).HasColumnName(DatabaseConstants.Description).HasMaxLength(500);
            });
        }
        
        void ConditionalMigrationTableBuilder<TEntity>(TableBuilder<TEntity> tableBuilder) where TEntity : class
        {
            // TODO: Should be exclude by default for BC. How can we make this configurable with nice DX?
            tableBuilder.ExcludeFromMigrations();
        }
    }
}

