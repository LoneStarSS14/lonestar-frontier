using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres;

// LoneStar: Persist user-selected colorizer colors for loadout items.
[DbContext(typeof(PostgresServerDbContext))]
[Migration("20261107145005_LoadoutSelectedColors")]
public sealed class LoadoutSelectedColors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "colors",
            table: "profile_loadout",
            type: "text",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "colors",
            table: "profile_loadout");
    }
}
