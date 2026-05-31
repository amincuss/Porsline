using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PorslineClone.Infrastructure.Migrations;

/// <summary>
/// Baseline for an existing database — schema already created by earlier migrations / schema patcher.
/// Do not recreate tables here; only marks EF history so future incremental migrations apply cleanly.
/// </summary>
    public partial class BaselineRestore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        // Intentionally empty — database already exists.
    }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        // Intentionally empty.
    }
}
