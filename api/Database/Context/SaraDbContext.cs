using api.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace api.Database.Context
{
    public class SaraDbContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<PlantData> PlantData { get; set; } = null!;

        public DbSet<Workflow> Workflow { get; set; } = null!;

        public DbSet<WorkflowStep> WorkflowStep { get; set; } = null!;

        public DbSet<AnonymizationData> AnonymizationData { get; set; } = null!;

        public DbSet<CLOEData> CLOEData { get; set; } = null!;

        public DbSet<FencillaData> FencillaData { get; set; } = null!;

        public DbSet<ThermalReadingData> ThermalReadingData { get; set; } = null!;

        public DbSet<AnalysisMapping> AnalysisMapping { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            AddConverterForListOfEnums(
                modelBuilder.Entity<AnalysisMapping>().Property(r => r.AnalysesToBeRun)
            );

            modelBuilder
                .Entity<PlantData>()
                .HasOne(plantData => plantData.Workflow)
                .WithOne()
                .HasForeignKey<PlantData>(plantData => plantData.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<Workflow>()
                .HasMany(workflow => workflow.WorkflowSteps)
                .WithOne(step => step.Workflow)
                .HasForeignKey(step => step.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WorkflowStep>().OwnsOne(step => step.SourceBlobStorageLocation);
            modelBuilder
                .Entity<WorkflowStep>()
                .OwnsOne(step => step.DestinationBlobStorageLocation);

            modelBuilder
                .Entity<WorkflowStep>()
                .HasIndex(step => new { step.WorkflowId, step.Type })
                .IsUnique();

            modelBuilder
                .Entity<WorkflowStep>()
                .HasOne(step => step.AnonymizationData)
                .WithOne(data => data.WorkflowStep)
                .HasForeignKey<AnonymizationData>(data => data.WorkflowStepId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<WorkflowStep>()
                .HasOne(step => step.CLOEData)
                .WithOne(data => data.WorkflowStep)
                .HasForeignKey<CLOEData>(data => data.WorkflowStepId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<WorkflowStep>()
                .HasOne(step => step.FencillaData)
                .WithOne(data => data.WorkflowStep)
                .HasForeignKey<FencillaData>(data => data.WorkflowStepId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<WorkflowStep>()
                .HasOne(step => step.ThermalReadingData)
                .WithOne(data => data.WorkflowStep)
                .HasForeignKey<ThermalReadingData>(data => data.WorkflowStepId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<AnonymizationData>()
                .OwnsOne(data => data.PreProcessedBlobStorageLocation);

            modelBuilder
                .Entity<AnalysisMapping>()
                .HasIndex(am => new { am.Tag, am.InspectionDescription })
                .IsUnique();

            modelBuilder
                .Entity<PlantData>()
                .HasIndex(p => new { p.DateCreated, p.Id })
                .IsDescending(true, true)
                .HasDatabaseName("IX_PlantData_DateCreated_Id_Desc");
        }

        private static void AddConverterForListOfEnums<T>(PropertyBuilder<List<T>> propertyBuilder)
            where T : Enum
        {
            propertyBuilder.HasConversion(
                r => r != null ? string.Join(';', r) : "",
                r =>
                    r.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => (T)Enum.Parse(typeof(T), r))
                        .ToList()
            );
        }
    }
}
