using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;
using Nop.Plugin.Integration.OrderPublisher.Domain;

namespace Nop.Plugin.Integration.OrderPublisher.Data.Migrations;

[NopMigration("2026-05-17 00:00:00", "Integration.OrderPublisher schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<OutboxMessage>();
    }

    public override void Down()
    {
        Delete.Table(NameCompatibilityManager.GetTableName(typeof(OutboxMessage)));
    }
}
