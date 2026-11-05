using FluentValidation;

namespace NexCrm.Application.Common;

public static class StringExtensions
{
    /// <summary>Trims optional text and turns blanks into null, so the database never stores "".</summary>
    public static string? Clean(this string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Business dates stay within a window a sales team can actually plan in.</summary>
public static class DateRules
{
    public const int YearsBack = 5;
    public const int YearsAhead = 10;
    public const string Message = "Choose a date within the last 5 or the next 10 years.";
}

public static class RuleExtensions
{
    public static IRuleBuilderOptions<T, string?> PhoneNumber<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(30).Matches(@"^[0-9+\-() ]*$").WithMessage("Phone may contain digits, spaces, +, - and brackets.");

    public static IRuleBuilderOptions<T, int> Owner<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThan(0).WithMessage("Choose an owner.");
}
