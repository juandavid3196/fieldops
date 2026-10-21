using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalSearchUnaccent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SA-23 (global-search): accent-insensitive matching. unaccent is only STABLE, so the search calls this IMMUTABLE
            // wrapper, which pins the dictionary. No index and no column is added.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.fieldops_unaccent(value text) RETURNS text
                  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                  AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, value) $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.fieldops_unaccent(text);");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS unaccent;");
        }
    }
}
