using Aegis.Architecture.Analysis;
using Aegis.Architecture.Analysis.Integrations.Data;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProjectArchitectureDataAccessIntegrationTests
{
    [Fact]
    public void Detect_projects_Entity_Framework_Core_reads_and_writes_into_the_shared_registry()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "Orders.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        var sourceFile = Path.Combine(directory.Path, "Orders.cs");
        File.WriteAllText(sourceFile, """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;
            using System.Threading.Tasks;

            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext
                {
                    public void Add<T>(T entity) { }
                }

                public class DbSet<T> : IQueryable<T>
                {
                    public Type ElementType => typeof(T);
                    public Expression Expression => Array.Empty<T>().AsQueryable().Expression;
                    public IQueryProvider Provider => Array.Empty<T>().AsQueryable().Provider;
                    public IEnumerator<T> GetEnumerator() => Array.Empty<T>().AsEnumerable().GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }

                public static class EntityFrameworkQueryableExtensions
                {
                    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
                        Task.FromResult(new List<T>());
                }
            }

            namespace Orders
            {
                using Microsoft.EntityFrameworkCore;

                public sealed class Order
                {
                    public Guid Id { get; init; }
                }

                public sealed class OrdersDbContext : DbContext
                {
                    public DbSet<Order> Orders { get; } = new();

                    public Task<List<Order>> FindOrdersAsync(Guid id) =>
                        Orders.Where(order => order.Id == id).ToListAsync();

                    public void AddOrder(Order order) => Add(order);
                }
            }
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        var registry = Assert.IsType<IntegrationEvidenceRegistry<DataAccessEvidence>>(
            context.DataAccessEvidenceRegistry);
        Assert.Equal(2, registry.Count);

        var read = Assert.Single(registry.Evidence, item =>
            item.Operation == DataIntegrationOperation.Read);
        Assert.Equal("Entity Framework Core", read.Technology);
        Assert.Equal("global::Orders.Order", read.DataObjectIdentity);
        Assert.Equal("global::Orders.OrdersDbContext", read.StoreIdentity);
        Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, read.ResolutionStatus);
        Assert.Equal(ArchitectureRelationshipKind.DataRead, read.RelationshipKind);

        var write = Assert.Single(registry.Evidence, item =>
            item.Operation == DataIntegrationOperation.Write);
        Assert.Equal("global::Orders.Order", write.DataObjectIdentity);
        Assert.Equal("global::Orders.OrdersDbContext", write.StoreIdentity);
        Assert.Equal(ArchitectureRelationshipKind.DataWrite, write.RelationshipKind);
    }

    [Fact]
    public void Detects_Entity_Framework_Core_relational_entity_to_table_mappings()
    {
        using var directory = new TemporaryDirectory();
        var sourceFile = Path.Combine(directory.Path, "Mappings.cs");
        File.WriteAllText(sourceFile, """
            namespace Microsoft.EntityFrameworkCore
            {
                public sealed class EntityTypeBuilder<T> { }
                public sealed class ModelBuilder
                {
                    public EntityTypeBuilder<T> Entity<T>() => new();
                }
                public static class RelationalEntityTypeBuilderExtensions
                {
                    public static EntityTypeBuilder<T> ToTable<T>(this EntityTypeBuilder<T> builder, string name, string? schema = null) => builder;
                }
            }
            namespace Persistence
            {
                using Microsoft.EntityFrameworkCore;
                public sealed class Order { }
                public sealed class OrdersDbContext
                {
                    protected void OnModelCreating(ModelBuilder modelBuilder) =>
                        modelBuilder.Entity<Order>().ToTable("orders", "sales");
                }
            }
            """);

        var evidence = new CSharpEntityFrameworkDataAccessProvider()
            .Discover([sourceFile], directory.Path);

        var mapping = Assert.Single(evidence);
        Assert.Equal(DataIntegrationOperation.Mapping, mapping.Operation);
        Assert.Equal("global::Persistence.Order", mapping.SourceDataIdentity);
        Assert.Equal("sales.orders", mapping.TargetDataIdentity);
        Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, mapping.ResolutionStatus);
        Assert.Equal(ArchitectureRelationshipKind.DataMapping, mapping.RelationshipKind);
    }
    [Fact]
    public void Detects_Entity_Framework_Core_relational_entity_to_view_mappings()
    {
        using var directory = new TemporaryDirectory();
        var sourceFile = Path.Combine(directory.Path, "ViewMappings.cs");
        File.WriteAllText(sourceFile, """
            namespace Microsoft.EntityFrameworkCore
            {
                public sealed class EntityTypeBuilder<T> { }
                public sealed class ModelBuilder
                {
                    public EntityTypeBuilder<T> Entity<T>() => new();
                }
                public static class RelationalEntityTypeBuilderExtensions
                {
                    public static EntityTypeBuilder<T> ToView<T>(this EntityTypeBuilder<T> builder, string name, string? schema = null) => builder;
                }
            }
            namespace Persistence
            {
                using Microsoft.EntityFrameworkCore;
                public sealed class OrderSummary { }
                public sealed class ReportingDbContext
                {
                    protected void OnModelCreating(ModelBuilder modelBuilder) =>
                        modelBuilder.Entity<OrderSummary>().ToView(name: "order_summary", schema: "reporting");
                }
            }
            """);

        var evidence = new CSharpEntityFrameworkDataAccessProvider()
            .Discover([sourceFile], directory.Path);

        var mapping = Assert.Single(evidence);
        Assert.Equal(DataIntegrationOperation.Mapping, mapping.Operation);
        Assert.Equal("global::Persistence.OrderSummary", mapping.SourceDataIdentity);
        Assert.Equal("reporting.order_summary", mapping.TargetDataIdentity);
        Assert.Equal("global::Persistence.ReportingDbContext", mapping.StoreIdentity);
        Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, mapping.ResolutionStatus);
        Assert.Equal(ArchitectureRelationshipKind.DataMapping, mapping.RelationshipKind);
    }

    [Fact]
    public void Does_not_classify_query_composition_or_unrelated_methods_as_data_access()
    {
        using var directory = new TemporaryDirectory();
        var sourceFile = Path.Combine(directory.Path, "Unrelated.cs");
        File.WriteAllText(sourceFile, """
            using System.Linq;

            namespace Unrelated
            {
                public sealed class Order { public int Id { get; init; } }

                public sealed class QueryService
                {
                    public IQueryable<Order> Filter(IQueryable<Order> orders) =>
                        orders.Where(order => order.Id > 0);

                    public void Add(Order order) { }
                }
            }
            """);

        var evidence = new CSharpEntityFrameworkDataAccessProvider()
            .Discover([sourceFile], directory.Path);

        Assert.Empty(evidence);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "aegis-efcore-data-access",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
