using System.Linq.Expressions;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>Turns <c>x =&gt; x.Prop</c> or <c>x =&gt; new { x.A, x.B }</c> into mapped columns.</summary>
internal static class MemberSelector
{
    public static IReadOnlyList<ColumnMap> Columns<T>(Expression<Func<T, object>> selector, TableMap map, string argumentName)
    {
        // Value-type members are boxed to object: x => (object)x.Total.
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : selector.Body;
        IEnumerable<Expression> members = body is NewExpression anonymous ? anonymous.Arguments : [body];

        var columns = members
            .Select(member => member is MemberExpression access && access.Expression == selector.Parameters[0]
                ? map.Column(access.Member.Name, argumentName)
                : throw new ArgumentException(
                    $"'{member}' is not a property of {typeof(T).Name}. Use x => x.Prop or x => new {{ x.A, x.B }}.",
                    argumentName))
            .Distinct()
            .ToList();

        return columns.Count > 0
            ? columns
            : throw new ArgumentException("Select at least one property.", argumentName);
    }
}
