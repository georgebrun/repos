using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BreakersOfE.Migrations
{
    /// <inheritdoc />
    public partial class AddSetTypeAndPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ArtSeriesCards",
                columns: table => new
                {
                    ArtSeriesId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScryfallId = table.Column<string>(type: "TEXT", nullable: false),
                    OracleId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TypeLine = table.Column<string>(type: "TEXT", nullable: false),
                    FlavorText = table.Column<string>(type: "TEXT", nullable: false),
                    SetCode = table.Column<string>(type: "TEXT", nullable: false),
                    SetName = table.Column<string>(type: "TEXT", nullable: false),
                    SetType = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false),
                    Artist = table.Column<string>(type: "TEXT", nullable: false),
                    ImageSmallUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ImageNormalUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    IsFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNonFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LocalImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtSeriesCards", x => x.ArtSeriesId);
                });

            migrationBuilder.CreateTable(
                name: "ArtSeriesCollectionEntries",
                columns: table => new
                {
                    ArtSeriesCollectionEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtSeriesCollectionEntries", x => x.ArtSeriesCollectionEntryId);
                });

            migrationBuilder.CreateTable(
                name: "CollectionEntries",
                columns: table => new
                {
                    CollectionEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PoolId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionEntries", x => x.CollectionEntryId);
                });

            migrationBuilder.CreateTable(
                name: "ConspiracyCollectionEntries",
                columns: table => new
                {
                    ConspiracyCollectionEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConspiracyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConspiracyCollectionEntries", x => x.ConspiracyCollectionEntryId);
                });

            migrationBuilder.CreateTable(
                name: "DeckCards",
                columns: table => new
                {
                    DeckCardId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeckId = table.Column<int>(type: "INTEGER", nullable: false),
                    PoolId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCommander = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSideboard = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMaybeboard = table.Column<bool>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckCards", x => x.DeckCardId);
                });

            migrationBuilder.CreateTable(
                name: "Decks",
                columns: table => new
                {
                    DeckId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Format = table.Column<string>(type: "TEXT", nullable: false),
                    DeckType = table.Column<string>(type: "TEXT", nullable: false),
                    CommanderPoolId = table.Column<int>(type: "INTEGER", nullable: true),
                    PartnerCommanderPoolId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompanionPoolId = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decks", x => x.DeckId);
                });

            migrationBuilder.CreateTable(
                name: "PlanarCards",
                columns: table => new
                {
                    PlanarId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScryfallId = table.Column<string>(type: "TEXT", nullable: false),
                    OracleId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TypeLine = table.Column<string>(type: "TEXT", nullable: false),
                    OracleText = table.Column<string>(type: "TEXT", nullable: false),
                    FlavorText = table.Column<string>(type: "TEXT", nullable: false),
                    SetCode = table.Column<string>(type: "TEXT", nullable: false),
                    SetName = table.Column<string>(type: "TEXT", nullable: false),
                    SetType = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false),
                    Artist = table.Column<string>(type: "TEXT", nullable: false),
                    ImageSmallUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ImageNormalUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    IsFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNonFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LocalImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanarCards", x => x.PlanarId);
                });

            migrationBuilder.CreateTable(
                name: "PlanarCollectionEntries",
                columns: table => new
                {
                    PlanarCollectionEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanarId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanarCollectionEntries", x => x.PlanarCollectionEntryId);
                });

            migrationBuilder.CreateTable(
                name: "PoolCards",
                columns: table => new
                {
                    PoolId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScryfallId = table.Column<string>(type: "TEXT", nullable: false),
                    OracleId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ManaCost = table.Column<string>(type: "TEXT", nullable: false),
                    ManaValue = table.Column<double>(type: "REAL", nullable: false),
                    TypeLine = table.Column<string>(type: "TEXT", nullable: false),
                    OracleText = table.Column<string>(type: "TEXT", nullable: false),
                    FlavorText = table.Column<string>(type: "TEXT", nullable: false),
                    Power = table.Column<string>(type: "TEXT", nullable: false),
                    Toughness = table.Column<string>(type: "TEXT", nullable: false),
                    LoyaltyOrDefense = table.Column<string>(type: "TEXT", nullable: false),
                    Colors = table.Column<string>(type: "TEXT", nullable: false),
                    ColorIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    SetCode = table.Column<string>(type: "TEXT", nullable: false),
                    SetName = table.Column<string>(type: "TEXT", nullable: false),
                    SetType = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false),
                    Artist = table.Column<string>(type: "TEXT", nullable: false),
                    ImageSmallUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ImageNormalUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    IsFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNonFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsToken = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMeld = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LegalitiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    LocalImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    Keywords = table.Column<string>(type: "TEXT", nullable: false),
                    PriceUsd = table.Column<decimal>(type: "TEXT", nullable: true),
                    PriceUsdFoil = table.Column<decimal>(type: "TEXT", nullable: true),
                    PriceUsdEtched = table.Column<decimal>(type: "TEXT", nullable: true),
                    PriceEur = table.Column<decimal>(type: "TEXT", nullable: true),
                    PriceEurFoil = table.Column<decimal>(type: "TEXT", nullable: true),
                    PriceTix = table.Column<decimal>(type: "TEXT", nullable: true),
                    PricesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolCards", x => x.PoolId);
                });

            migrationBuilder.CreateTable(
                name: "SchemeCards",
                columns: table => new
                {
                    SchemeId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScryfallId = table.Column<string>(type: "TEXT", nullable: false),
                    OracleId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TypeLine = table.Column<string>(type: "TEXT", nullable: false),
                    OracleText = table.Column<string>(type: "TEXT", nullable: false),
                    FlavorText = table.Column<string>(type: "TEXT", nullable: false),
                    SetCode = table.Column<string>(type: "TEXT", nullable: false),
                    SetName = table.Column<string>(type: "TEXT", nullable: false),
                    SetType = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false),
                    Artist = table.Column<string>(type: "TEXT", nullable: false),
                    ImageSmallUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ImageNormalUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    IsFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNonFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LocalImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemeCards", x => x.SchemeId);
                });

            migrationBuilder.CreateTable(
                name: "SchemeCollectionEntries",
                columns: table => new
                {
                    SchemeCollectionEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SchemeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemeCollectionEntries", x => x.SchemeCollectionEntryId);
                });

            migrationBuilder.CreateTable(
                name: "TokenCards",
                columns: table => new
                {
                    TokenId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScryfallId = table.Column<string>(type: "TEXT", nullable: false),
                    OracleId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TypeLine = table.Column<string>(type: "TEXT", nullable: false),
                    OracleText = table.Column<string>(type: "TEXT", nullable: false),
                    FlavorText = table.Column<string>(type: "TEXT", nullable: false),
                    Power = table.Column<string>(type: "TEXT", nullable: false),
                    Toughness = table.Column<string>(type: "TEXT", nullable: false),
                    Colors = table.Column<string>(type: "TEXT", nullable: false),
                    ColorIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    SetCode = table.Column<string>(type: "TEXT", nullable: false),
                    SetName = table.Column<string>(type: "TEXT", nullable: false),
                    SetType = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false),
                    Artist = table.Column<string>(type: "TEXT", nullable: false),
                    ImageSmallUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ImageNormalUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    IsFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNonFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LocalImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenCards", x => x.TokenId);
                });

            migrationBuilder.CreateTable(
                name: "TokenCollectionEntries",
                columns: table => new
                {
                    TokenCollectionEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenCollectionEntries", x => x.TokenCollectionEntryId);
                });

            migrationBuilder.CreateTable(
                name: "TradeBinderEntries",
                columns: table => new
                {
                    TradeBinderEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PoolId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeBinderEntries", x => x.TradeBinderEntryId);
                });

            migrationBuilder.CreateTable(
                name: "VanguardCards",
                columns: table => new
                {
                    VanguardId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScryfallId = table.Column<string>(type: "TEXT", nullable: false),
                    OracleId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TypeLine = table.Column<string>(type: "TEXT", nullable: false),
                    OracleText = table.Column<string>(type: "TEXT", nullable: false),
                    FlavorText = table.Column<string>(type: "TEXT", nullable: false),
                    SetCode = table.Column<string>(type: "TEXT", nullable: false),
                    SetName = table.Column<string>(type: "TEXT", nullable: false),
                    SetType = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Rarity = table.Column<string>(type: "TEXT", nullable: false),
                    Artist = table.Column<string>(type: "TEXT", nullable: false),
                    ImageSmallUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ImageNormalUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Layout = table.Column<string>(type: "TEXT", nullable: false),
                    IsFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNonFoil = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleasedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LocalImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    HandModifier = table.Column<string>(type: "TEXT", nullable: false),
                    LifeModifier = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VanguardCards", x => x.VanguardId);
                });

            migrationBuilder.CreateTable(
                name: "VanguardCollectionEntries",
                columns: table => new
                {
                    VanguardCollectionEntryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VanguardId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    FoilQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VanguardCollectionEntries", x => x.VanguardCollectionEntryId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtSeriesCards_ScryfallId",
                table: "ArtSeriesCards",
                column: "ScryfallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanarCards_ScryfallId",
                table: "PlanarCards",
                column: "ScryfallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoolCards_ColorIdentity",
                table: "PoolCards",
                column: "ColorIdentity");

            migrationBuilder.CreateIndex(
                name: "IX_PoolCards_Name",
                table: "PoolCards",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PoolCards_Rarity",
                table: "PoolCards",
                column: "Rarity");

            migrationBuilder.CreateIndex(
                name: "IX_PoolCards_ScryfallId",
                table: "PoolCards",
                column: "ScryfallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoolCards_SetCode",
                table: "PoolCards",
                column: "SetCode");

            migrationBuilder.CreateIndex(
                name: "IX_SchemeCards_ScryfallId",
                table: "SchemeCards",
                column: "ScryfallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenCards_Name",
                table: "TokenCards",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TokenCards_ScryfallId",
                table: "TokenCards",
                column: "ScryfallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VanguardCards_ScryfallId",
                table: "VanguardCards",
                column: "ScryfallId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "ArtSeriesCards");

            migrationBuilder.DropTable(
                name: "ArtSeriesCollectionEntries");

            migrationBuilder.DropTable(
                name: "CollectionEntries");

            migrationBuilder.DropTable(
                name: "ConspiracyCollectionEntries");

            migrationBuilder.DropTable(
                name: "DeckCards");

            migrationBuilder.DropTable(
                name: "Decks");

            migrationBuilder.DropTable(
                name: "PlanarCards");

            migrationBuilder.DropTable(
                name: "PlanarCollectionEntries");

            migrationBuilder.DropTable(
                name: "PoolCards");

            migrationBuilder.DropTable(
                name: "SchemeCards");

            migrationBuilder.DropTable(
                name: "SchemeCollectionEntries");

            migrationBuilder.DropTable(
                name: "TokenCards");

            migrationBuilder.DropTable(
                name: "TokenCollectionEntries");

            migrationBuilder.DropTable(
                name: "TradeBinderEntries");

            migrationBuilder.DropTable(
                name: "VanguardCards");

            migrationBuilder.DropTable(
                name: "VanguardCollectionEntries");
        }
    }
}
