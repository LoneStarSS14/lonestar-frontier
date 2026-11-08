using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite;

// LoneStar: Persist user-selected colorable layer variants for loadout items.
[DbContext(typeof(SqliteServerDbContext))]
[Migration("20261108024000_LoadoutSelectedVariants")]
public sealed class LoadoutSelectedVariants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "variants",
            table: "profile_loadout",
            type: "TEXT",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "variants",
            table: "profile_loadout");
    }
}
