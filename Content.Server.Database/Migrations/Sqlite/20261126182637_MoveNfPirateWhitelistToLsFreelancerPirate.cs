using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MoveNfPirateWhitelistToLsFreelancerPirate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO role_whitelists (player_user_id, role_id)
                SELECT source_whitelist.player_user_id, 'LSFreelancerPirate'
                FROM role_whitelists AS source_whitelist
                WHERE source_whitelist.role_id = 'NFPirate'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM role_whitelists AS existing_whitelist
                    WHERE existing_whitelist.player_user_id = source_whitelist.player_user_id
                      AND existing_whitelist.role_id = 'LSFreelancerPirate'
                  );

                DELETE FROM role_whitelists
                WHERE role_id = 'NFPirate';

                INSERT INTO role_whitelists (player_user_id, role_id)
                SELECT source_whitelist.player_user_id, 'LSEscort'
                FROM role_whitelists AS source_whitelist
                WHERE source_whitelist.role_id = 'Escort'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM role_whitelists AS existing_whitelist
                    WHERE existing_whitelist.player_user_id = source_whitelist.player_user_id
                      AND existing_whitelist.role_id = 'LSEscort'
                  );

                DELETE FROM role_whitelists
                WHERE role_id = 'Escort';

                INSERT INTO role_whitelists (player_user_id, role_id)
                SELECT source_whitelist.player_user_id, 'LSSmuggler'
                FROM role_whitelists AS source_whitelist
                WHERE source_whitelist.role_id = 'Smuggler'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM role_whitelists AS existing_whitelist
                    WHERE existing_whitelist.player_user_id = source_whitelist.player_user_id
                      AND existing_whitelist.role_id = 'LSSmuggler'
                  );

                DELETE FROM role_whitelists
                WHERE role_id = 'Smuggler';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO role_whitelists (player_user_id, role_id)
                SELECT source_whitelist.player_user_id, 'NFPirate'
                FROM role_whitelists AS source_whitelist
                WHERE source_whitelist.role_id = 'LSFreelancerPirate'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM role_whitelists AS existing_whitelist
                    WHERE existing_whitelist.player_user_id = source_whitelist.player_user_id
                      AND existing_whitelist.role_id = 'NFPirate'
                  );

                DELETE FROM role_whitelists
                WHERE role_id = 'LSFreelancerPirate';

                INSERT INTO role_whitelists (player_user_id, role_id)
                SELECT source_whitelist.player_user_id, 'Escort'
                FROM role_whitelists AS source_whitelist
                WHERE source_whitelist.role_id = 'LSEscort'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM role_whitelists AS existing_whitelist
                    WHERE existing_whitelist.player_user_id = source_whitelist.player_user_id
                      AND existing_whitelist.role_id = 'Escort'
                  );

                DELETE FROM role_whitelists
                WHERE role_id = 'LSEscort';

                INSERT INTO role_whitelists (player_user_id, role_id)
                SELECT source_whitelist.player_user_id, 'Smuggler'
                FROM role_whitelists AS source_whitelist
                WHERE source_whitelist.role_id = 'LSSmuggler'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM role_whitelists AS existing_whitelist
                    WHERE existing_whitelist.player_user_id = source_whitelist.player_user_id
                      AND existing_whitelist.role_id = 'Smuggler'
                  );

                DELETE FROM role_whitelists
                WHERE role_id = 'LSSmuggler';
                """);
        }
    }
}
