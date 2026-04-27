using System;
using System.Linq;
using System.Threading.Tasks;
using api.Database.Context;
using api.Database.Models;
using api.Services;
using api.Tests;
using api.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Api.Test.Services;

public class PlantDataServiceTests
{
    private static SaraDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<SaraDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new SaraDbContext(options);
    }

    private static PlantDataService CreateService(SaraDbContext context)
    {
        var analysisMappingService = new AnalysisMappingService(
            context,
            new Mock<ILogger<AnalysisMappingService>>().Object
        );
        return new PlantDataService(
            context,
            analysisMappingService,
            new Mock<IBlobService>().Object,
            new Mock<ILogger<PlantDataService>>().Object
        );
    }

    private static async Task SeedAsync(SaraDbContext context, params DateTime[] dateCreatedValues)
    {
        for (int i = 0; i < dateCreatedValues.Length; i++)
        {
            context.PlantData.Add(
                new PlantData
                {
                    InspectionId = $"inspection-{i}",
                    InstallationCode = "INST",
                    DateCreated = dateCreatedValues[i],
                    Workflow = new Workflow
                    {
                        WorkflowSteps =
                        [
                            WorkflowTestFactory.CreateAnonymizationStep(
                                status: WorkflowStatus.NotStarted,
                                source: WorkflowTestFactory.CreateLocation("b", "sa", "c"),
                                destination: WorkflowTestFactory.CreateLocation("b", "sa", "c")
                            ),
                        ],
                    },
                }
            );
        }
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetPlantData_ReturnsResultsOrderedByDateCreatedDescendingByDefault()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = CreateService(context);
        var oldest = DateTime.UtcNow.AddDays(-3);
        var middle = DateTime.UtcNow.AddDays(-1);
        var newest = DateTime.UtcNow;
        await SeedAsync(context, oldest, newest, middle);

        // Act
        var result = await service.GetPlantData(
            new PlantDataParameters { PageNumber = 1, PageSize = 10 }
        );

        // Assert
        Assert.Equal(3, result.Count);
        Assert.True(result[0].DateCreated >= result[1].DateCreated);
        Assert.True(result[1].DateCreated >= result[2].DateCreated);
        Assert.Equal(newest, result[0].DateCreated);
        Assert.Equal(oldest, result[2].DateCreated);
    }

    [Fact]
    public async Task GetPlantData_PaginationIsStableAcrossPages()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = CreateService(context);
        var baseTime = DateTime.UtcNow;
        var dates = Enumerable.Range(0, 5).Select(i => baseTime.AddMinutes(-i)).ToArray();
        await SeedAsync(context, dates);

        // Act
        var page1 = await service.GetPlantData(
            new PlantDataParameters { PageNumber = 1, PageSize = 2 }
        );
        var page2 = await service.GetPlantData(
            new PlantDataParameters { PageNumber = 2, PageSize = 2 }
        );
        var page3 = await service.GetPlantData(
            new PlantDataParameters { PageNumber = 3, PageSize = 2 }
        );

        // Assert: pages contain disjoint, contiguous, newest-first slices
        var collected = page1.Concat(page2).Concat(page3).Select(p => p.Id).ToList();
        Assert.Equal(5, collected.Distinct().Count());
        for (int i = 0; i < collected.Count - 1; i++)
        {
            var current = page1.Concat(page2).Concat(page3).ElementAt(i);
            var next = page1.Concat(page2).Concat(page3).ElementAt(i + 1);
            Assert.True(current.DateCreated >= next.DateCreated);
        }
    }

    [Fact]
    public async Task GetPlantData_AppliesOrderingAfterFiltering()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = CreateService(context);
        var now = DateTime.UtcNow;
        await SeedAsync(context, now.AddDays(-2), now.AddDays(-1), now);
        // Tag the middle entry so the filter only matches it.
        var middle = await context.PlantData.FirstAsync(
            p => p.InspectionId == "inspection-1",
            cancellationToken: TestContext.Current.CancellationToken
        );
        middle.Tag = "TARGET-TAG";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await service.GetPlantData(
            new PlantDataParameters
            {
                PageNumber = 1,
                PageSize = 10,
                Tag = "TARGET-TAG",
            }
        );

        // Assert
        Assert.Single(result);
        Assert.Equal("TARGET-TAG", result[0].Tag);
    }
}
