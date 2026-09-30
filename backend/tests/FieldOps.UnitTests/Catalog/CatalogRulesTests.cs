using System.Text;
using FieldOps.Application.Features.Catalog;
using FieldOps.Domain.Catalog;

namespace FieldOps.UnitTests.Catalog;

public class CatalogRulesTests
{
    // BR-09: (price - cost) / price * 100, half away from zero to one decimal; price 0 has no margin.
    [Theory]
    [InlineData(20, 100, 80.0)]
    [InlineData(3, 8, 62.5)]
    [InlineData(1, 3, 66.7)]
    [InlineData(50, 40, -25.0)]
    [InlineData(12.5, 8, -56.3)]
    [InlineData(0, 8, 100.0)]
    [InlineData(10, 0, null)]
    [InlineData(0, 0, null)]
    public void EstimatedMarginPercent_AppliesRoundingAndPriceZeroRule(double cost, double price, double? expected) =>
        Assert.Equal(
            expected is null ? null : (decimal?)Math.Round((decimal)expected.Value, 1),
            CatalogItem.EstimatedMarginPercent((decimal)cost, (decimal)price));

    // BR-07 / BR-08: trimmed, internally collapsed name; lower-cased comparison key; update/no-op semantics.
    [Fact]
    public void NameNormalizationAndUpdate_CollapseWhitespaceCompareCaseInsensitiveAndDetectNoOp()
    {
        Assert.Equal("Drain Cleaning", CatalogItem.CollapseName("  Drain \t  Cleaning\n"));
        Assert.Equal("drain cleaning", CatalogItem.NormalizeName("  DRAIN   cleaning "));
        Assert.Equal(string.Empty, CatalogItem.CollapseName("   "));

        var item = CatalogItem.Create(Guid.NewGuid(), CatalogItemType.Service, " Hose  pipe ", "  ", 5m, 10m, true, true);
        Assert.Equal("Hose pipe", item.Name);
        Assert.Null(item.Description);
        var stamp = item.UpdatedAt;
        var later = stamp.AddMinutes(5);

        Assert.False(item.Update(CatalogItemType.Service, "Hose   pipe", null, 5m, 10.00m, true, true, later));
        Assert.Equal(stamp, item.UpdatedAt);
        Assert.True(item.Update(CatalogItemType.Product, "Hose pipe", "d", 5m, 10m, false, true, later));
        Assert.Equal(later, item.UpdatedAt);
        Assert.False(item.SetActive(true, later.AddMinutes(1)));
        Assert.True(item.SetActive(false, later.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => item.Update(CatalogItemType.Product, "x", null, -1m, 1m, true, true, later));
    }

    // BR-07 money/body rules shared by the JSON body and the CSV import.
    [Theory]
    [InlineData("0", true)]
    [InlineData("999999999999.99", true)]
    [InlineData("12.50", true)]
    [InlineData("1.234", false)]
    [InlineData("-1", false)]
    [InlineData("1,000", false)]
    [InlineData("$5", false)]
    [InlineData("1e3", false)]
    [InlineData("1000000000000", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseMoney_AcceptsOnlyInvariantNonNegativeTwoDecimalAmounts(string? text, bool expected) =>
        Assert.Equal(expected, CatalogItemRules.TryParseMoney(text, out _));

    // BR-13: header, row count and per-row rules with the file line as the row.
    [Fact]
    public void ParseImport_HeaderAndRowTable_ReturnsFileErrorsRowErrorsOrItems()
    {
        static CatalogImportParse Parse(string text) => CatalogCsv.ParseImport(new UTF8Encoding(false).GetBytes(text));

        const string header = "type,name,description,unit_cost,unit_price,taxable,active";

        Assert.Equal(CatalogCsv.HeaderMessage, Parse("type,name\r\nservice,A\r\n").FileError);
        Assert.Equal(CatalogCsv.HeaderMessage, Parse(header + ",type\r\n").FileError);
        Assert.Equal(CatalogCsv.NoItemsMessage, Parse(header + "\r\n\r\n").FileError);
        Assert.Equal(CatalogCsv.NotCsvMessage, Parse("\"unterminated,name\r\n").FileError);
        Assert.Equal(CatalogCsv.NotCsvMessage, CatalogCsv.ParseImport([0xC3, 0x28]).FileError);
        Assert.Equal(
            CatalogCsv.TooManyItemsMessage,
            Parse(header + "\r\n" + string.Concat(Enumerable.Range(0, 501).Select(i => $"service,N{i},,1,1,,\r\n"))).FileError);

        // A BOM, reordered mixed-case columns, quoted multi-line cells and empty flags are accepted.
        var ok = Parse("﻿ACTIVE,Taxable,Type,NAME,unit_price,Unit_Cost,Description\r\n,no,Product,\"Two\r\nLines\",9.5,1,\"a, \"\"b\"\"\"\r\n");
        Assert.Null(ok.FileError);
        var item = Assert.Single(ok.Items);
        Assert.Equal(new CatalogItemValues(CatalogItemType.Product, "Two Lines", "a, \"b\"", 1m, 9.5m, false, true), item);

        // Row errors carry the start line of the record, even after a multi-line quoted cell.
        var bad = Parse(
            header + "\r\n"
            + "service,\"A\r\nB\",,1,1,true,true\r\n"
            + "gadget,X,,1,1,true,true\r\n"
            + "service,Y,,1,1,true\r\n"
            + "service,a b,,1,1,true,true\r\n");
        Assert.Equal(
            [(4, "type"), (5, "row"), (6, "name")],
            bad.RowErrors.Select(e => (e.Row, e.Column)));
    }
}
