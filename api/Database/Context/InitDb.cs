using api.Database.Context;
using api.Database.Models;

namespace Api.Database.Context
{
    public static class InitDb
    {
        private static readonly List<AnalysisMapping> analysisMappings = GetAnalysisMappings();
        private static readonly List<PlantData> plantData = GetPlantData();

        private static List<AnalysisMapping> GetAnalysisMappings()
        {
            var mapping1 = new AnalysisMapping("tag", "fencilla", [AnalysisType.Fencilla]);

            var mapping2 = new AnalysisMapping("tag", "cloe", [AnalysisType.ConstantLevelOiler]);

            return new List<AnalysisMapping>([mapping1, mapping2]);
        }

        private static List<PlantData> GetPlantData()
        {
            var workflow = new Workflow();
            var anonymizationStep = workflow.EnsureStep(WorkflowStepType.Anonymization);
            anonymizationStep.SourceBlobStorageLocation = new BlobStorageLocation
            {
                StorageAccount = "",
                BlobContainer = "",
                BlobName = "",
            };
            anonymizationStep.DestinationBlobStorageLocation = new BlobStorageLocation
            {
                StorageAccount = "",
                BlobContainer = "",
                BlobName = "",
            };
            anonymizationStep.AnonymizationData = new AnonymizationData();

            var fencillaStep = workflow.EnsureStep(WorkflowStepType.FencillaAnalysis);
            fencillaStep.SourceBlobStorageLocation = new BlobStorageLocation
            {
                StorageAccount = "",
                BlobContainer = "",
                BlobName = "",
            };
            fencillaStep.DestinationBlobStorageLocation = new BlobStorageLocation
            {
                StorageAccount = "",
                BlobContainer = "",
                BlobName = "",
            };
            fencillaStep.FencillaData = new FencillaData { IsBreak = true, Confidence = 90 };

            var data1 = new PlantData
            {
                InspectionId = "9df55f01-215e-407e-9778-9a6f3f5dc647",
                InstallationCode = "nls",
                Tag = "tag",
                InspectionDescription = "fencilla",
                Workflow = workflow,
            };

            return new List<PlantData>([data1]);
        }

        public static void PopulateDb(SaraDbContext context)
        {
            context.AddRange(analysisMappings);
            context.AddRange(plantData);

            context.SaveChanges();
            context.ChangeTracker.Clear();
        }
    }
}
