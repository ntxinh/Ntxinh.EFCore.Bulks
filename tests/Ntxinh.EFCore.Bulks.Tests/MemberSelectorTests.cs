using System.Linq.Expressions;

using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class MemberSelectorTests
{
    private static readonly TableMap OrderMap = CreateOrderMap();

    private static TableMap CreateOrderMap()
    {
        using var db = TestDbContext.Create();
        return TableMap.For(db, typeof(Order));
    }

    private static string[] Columns(Expression<Func<Order, object>> selector) =>
        MemberSelector.Columns(selector, OrderMap, "set").Select(c => c.ColumnName).ToArray();

    [Fact]
    public void AcceptsASingleValueTypeMember()
    {
        Assert.Equal(new[] { "Total" }, Columns(x => x.Total));
    }

    [Fact]
    public void AcceptsAnAnonymousObjectOfMembers()
    {
        Assert.Equal(new[] { "Total", "note_text" }, Columns(x => new { x.Total, x.Note }));
    }

    [Fact]
    public void RepeatedMembersCountOnce()
    {
        Assert.Equal(new[] { "Total" }, Columns(x => new { x.Total, Again = x.Total }));
    }

    [Fact]
    public void RejectsMethodCalls()
    {
        Assert.Equal("set", Assert.Throws<ArgumentException>(() => Columns(x => x.Name.Trim())).ParamName);
    }

    [Fact]
    public void RejectsMembersOfOtherObjects()
    {
        var other = new Order();

        Assert.Throws<ArgumentException>(() => Columns(x => other.Total));
    }

    [Fact]
    public void RejectsUnmappedAndComputedMembers()
    {
        Assert.Throws<ArgumentException>(() => Columns(x => x.Display));
        Assert.Throws<ArgumentException>(() => Columns(x => x.NameLength));
    }

    [Fact]
    public void RejectsAnEmptySelection()
    {
        Assert.Throws<ArgumentException>(() => Columns(x => new { }));
    }
}
