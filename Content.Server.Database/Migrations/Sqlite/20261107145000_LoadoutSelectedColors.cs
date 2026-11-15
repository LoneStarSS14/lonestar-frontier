using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite;

// LoneStar: Persist user-selected colorizer colors for loadout items.
[DbContext(typeof(SqliteServerDbContext))]
[Migration("20261107145000_LoadoutSelectedColors")]
public sealed class LoadoutSelectedColors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "colors",
            table: "profile_loadout",
            type: "TEXT",
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
