using MyApp.Migrations;
using MyApp.ServiceModel;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.OrmLite;

namespace MyApp.Tests;

/// <summary>
/// Migrations create their tables from their own copies of the App's models, as they were when the migration was
/// written. Running every migration has to create the tables of the App's models as they are now.
/// </summary>
public class MigrationSchemaTests
{
    [Test]
    public void Migrations_create_the_tables_of_the_Apps_models()
    {
        // PostgreSQL when TEST_POSTGRES_CONNECTION is set
        using var database = TestDatabase.Create();
        var result = new Migrator(database.Factory, typeof(Migration1000).Assembly).Run();
        Assert.That(result.Succeeded, result.Error?.ToString());

        // The App's model of each table, matched by name to the copies the migrations declare
        var tables = Migrator.GetMigrationTables([typeof(Migration1000).Assembly], [typeof(Workspace).Assembly]);
        // Every table has a model, apart from the class Migration1000 reads plans.json into
        Assert.That(tables.Where(x => x.ModelType == null).Map(x => x.Table), Is.EqualTo(new[] { "PlanSeed" }));
        var modelTypes = tables.Where(x => x.ModelType != null).Map(x => x.ModelType!);
        Assert.That(modelTypes.Count, Is.EqualTo(28));

        using var db = database.Factory.Open();
        var diff = db.GetSchemaDiff(modelTypes.ToArray());
        Assert.That(diff.HasChanges, Is.False, diff.ToString());
        Assert.That(diff.Warnings, Is.Empty);
    }
}
