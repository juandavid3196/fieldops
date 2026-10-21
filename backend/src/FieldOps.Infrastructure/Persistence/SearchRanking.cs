using System.Linq.Expressions;
using System.Reflection;
using FieldOps.Application.Features.Search;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The columns of one searchable entity, read once. Searched text goes through <c>T1</c> to <c>T4</c> (meaning per type is in
/// <see cref="SearchStore"/>); the rest is what the item content needs.
/// </summary>
internal sealed class SearchCandidate<TStatus>
{
    public Guid Id { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public long Number { get; init; }

    public TStatus Status { get; init; } = default!;

    public Guid? BranchId { get; init; }

    public string? T1 { get; init; }

    public string? T2 { get; init; }

    public string? T3 { get; init; }

    public string? T4 { get; init; }

    public string? Kind { get; init; }

    public long RequestNumber { get; init; }

    public decimal Total { get; init; }

    public string? Currency { get; init; }

    public DateOnly? DueDate { get; init; }
}

/// <summary>A candidate with its BR-07 rank (0 when it does not match).</summary>
internal sealed class SearchRanked<TStatus>
{
    public SearchCandidate<TStatus> Row { get; init; } = default!;

    public int Rank { get; init; }
}

/// <summary>Wraps a value so EF sends it as a query parameter instead of inlining it.</summary>
internal sealed class SearchParameter<TValue>(TValue value)
{
    public TValue Value { get; } = value;
}

/// <summary>
/// Builds the BR-06/BR-07 rank expression: exact 1, prefix 2, contains 3, otherwise 0; the best rank across the fields wins.
/// Both sides of every text comparison go through the SA-23 unaccent function and lower-casing, and LIKE patterns arrive
/// already escaped (backslash escape).
/// </summary>
internal static class SearchRanking
{
    private const string EscapeCharacter = "\\";

    private static readonly MethodInfo Unaccent =
        typeof(SearchDbFunctions).GetMethod(nameof(SearchDbFunctions.Unaccent), [typeof(string)])!;

    private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo LongToString = typeof(long).GetMethod(nameof(long.ToString), Type.EmptyTypes)!;

    private static readonly MethodInfo Like = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    private static readonly MethodInfo LikeUnescaped = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private static readonly Expression Functions = Expression.Constant(EF.Functions);

    /// <summary>The rank selector over a candidate: <paramref name="fields"/> are the searched texts, <paramref name="digits"/> the number.</summary>
    public static Expression<Func<SearchCandidate<TStatus>, SearchRanked<TStatus>>> Rank<TStatus>(
        SearchTerms terms,
        string? digits,
        params Expression<Func<SearchCandidate<TStatus>, string?>>[] fields)
    {
        var row = Expression.Parameter(typeof(SearchCandidate<TStatus>), "row");
        var texts = fields.Select(field => Fold(Replace(field, row))).ToList();
        var number = digits is null ? null : Expression.Call(Expression.Property(row, nameof(SearchCandidate<TStatus>.Number)), LongToString);

        var folded = Fold(Parameter(terms.Text));
        var prefix = Fold(Parameter(terms.PrefixPattern));
        var contains = Fold(Parameter(terms.ContainsPattern));

        var exact = Any(texts.Select(text => (Expression)Expression.Equal(text, folded)));
        var starts = Any(texts.Select(text => LikeCall(text, prefix)));
        var inside = Any(texts.Select(text => LikeCall(text, contains)));

        if (number is not null)
        {
            var exactNumber = Expression.Equal(number, Parameter(digits!));
            var startsNumber = LikeNumber(number, Parameter(digits + "%"));
            var insideNumber = LikeNumber(number, Parameter("%" + digits + "%"));

            exact = Expression.OrElse(exact, exactNumber);
            starts = Expression.OrElse(starts, startsNumber);
            inside = Expression.OrElse(inside, insideNumber);
        }

        var rank = Expression.Condition(
            exact,
            Expression.Constant(1),
            Expression.Condition(starts, Expression.Constant(2), Expression.Condition(inside, Expression.Constant(3), Expression.Constant(0))));

        var body = Expression.MemberInit(
            Expression.New(typeof(SearchRanked<TStatus>)),
            Expression.Bind(typeof(SearchRanked<TStatus>).GetProperty(nameof(SearchRanked<TStatus>.Row))!, row),
            Expression.Bind(typeof(SearchRanked<TStatus>).GetProperty(nameof(SearchRanked<TStatus>.Rank))!, rank));

        return Expression.Lambda<Func<SearchCandidate<TStatus>, SearchRanked<TStatus>>>(body, row);
    }

    private static MemberExpression Parameter(string value) =>
        Expression.Property(Expression.Constant(new SearchParameter<string>(value)), nameof(SearchParameter<string>.Value));

    // lower(unaccent(value)): the SA-23 function and lower-casing on both sides of every text comparison.
    private static Expression Fold(Expression value) =>
        Expression.Call(Expression.Call(Unaccent, value), ToLower);

    private static Expression LikeCall(Expression text, Expression pattern) =>
        Expression.Call(Like, Functions, text, pattern, Expression.Constant(EscapeCharacter));

    // Digits need no escaping.
    private static Expression LikeNumber(Expression text, Expression pattern) =>
        Expression.Call(LikeUnescaped, Functions, text, pattern);

    private static Expression Any(IEnumerable<Expression> conditions) =>
        conditions.Aggregate((left, right) => Expression.OrElse(left, right));

    private static Expression Replace<TStatus>(
        Expression<Func<SearchCandidate<TStatus>, string?>> field, ParameterExpression row) =>
        new ParameterReplacer(field.Parameters[0], row).Visit(field.Body);

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
