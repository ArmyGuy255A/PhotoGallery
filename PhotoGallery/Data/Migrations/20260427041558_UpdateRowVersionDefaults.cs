using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoGallery.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRowVersionDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Photos",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[] { 1 },
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Albums",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[] { 1 },
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "AccessCodes",
                type: "BLOB",
                nullable: false,
                defaultValue: new byte[] { 1 },
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldRowVersion: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Photos",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValue: new byte[] { 1 });

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "Albums",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValue: new byte[] { 1 });

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "AccessCodes",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldDefaultValue: new byte[] { 1 });
        }
    }
}
