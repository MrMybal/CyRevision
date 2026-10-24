using System.Collections;
using System.Text.Json;
using CyRevision.Desktop.Native;

namespace CyRevision.Core.Tests;

public sealed class NativeTableTests
{
    private sealed record Run(int Number, string Name, string ApiToken = "fixture-token");

    private static JsonElement Page(IList list, int skip = 0, int take = 100, string filter = "", string sort = "", bool descending = false) =>
        JsonSerializer.SerializeToElement(NativeTable.Page(list, "Runs", skip, take, filter, sort, descending));

    [Fact]
    public void NumbersAreSortedNumericallyAndSourceIndexesArePreserved()
    {
        var result = Page(new[] { new Run(3,"third"), new Run(222,"latest"), new Run(29,"older") }, sort:"Number", descending:true);
        var rows = result.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(new[] { 222,29,3 }, rows.Select(row => row.GetProperty("values").GetProperty("Number").GetInt32()));
        Assert.Equal(new[] { 1,2,0 }, rows.Select(row => row.GetProperty("index").GetInt32()));
    }

    [Fact]
    public void FilteringDoesNotRenumberSourceRows()
    {
        var result = Page(new[] { new Run(1,"skip"), new Run(2,"MATCH"), new Run(3,"match again") }, take:1, filter:"match");
        Assert.Equal(2,result.GetProperty("total").GetInt32());
        Assert.Equal(1,Assert.Single(result.GetProperty("rows").EnumerateArray()).GetProperty("index").GetInt32());
    }

    [Fact]
    public void RowKeysAreStableAndRejectAReplacementWithIdenticalFields()
    {
        var row = new Run(1,"same");
        Assert.Equal(NativeTable.Key(row),NativeTable.Key(row));
        Assert.NotEqual(NativeTable.Key(row),NativeTable.Key(new Run(1,"same")));
    }

    [Fact]
    public void SecretFieldsAreNotTransferredOrSearched()
    {
        var rows = new[] { new Run(1,"run","never-transfer-this") };
        var result = Page(rows);
        Assert.DoesNotContain("ApiToken",result.GetRawText());
        Assert.DoesNotContain("never-transfer-this",result.GetRawText());
        Assert.Equal(0,Page(rows,filter:"never-transfer-this").GetProperty("total").GetInt32());
    }

    [Fact]
    public void APagePastTheNewEndReturnsTheLastPage()
    {
        var result = Page(Enumerable.Range(1,125).Select(number => new Run(number,"run")).ToArray(),skip:200);
        Assert.Equal(100,result.GetProperty("skip").GetInt32());
        Assert.Equal(25,result.GetProperty("rows").GetArrayLength());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteValuesDoNotBreakResponseSerialization(double value)
    {
        Assert.Null(NativeTable.Value(value));
        Assert.Equal("null",JsonSerializer.Serialize(NativeTable.Value(value)));
    }

    [Fact]
    public void LargeUnfilteredCollectionsReadOnlyTheVisiblePage()
    {
        var rows = Enumerable.Range(1,60000).Select(number => new CountedRow(number)).ToArray();
        var result = Page(rows,skip:30000,take:100);
        Assert.Equal(60000,result.GetProperty("total").GetInt32());
        Assert.Equal(100,result.GetProperty("rows").GetArrayLength());
        Assert.Equal(100,rows.Sum(row => row.Reads));
    }

    private sealed class CountedRow(int number)
    {
        public int Reads;
        public int Number { get { Reads++; return number; } }
    }
}
