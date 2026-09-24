using FieldOps.Application.Authentication;

namespace FieldOps.UnitTests.Authentication;

public class SignInCommandValidatorTests
{
    private const string ValidEmail = "user@example.com";

    private const string ValidPassword = "secret";

    private readonly SignInCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidValues_HasNoErrors()
    {
        var result = _validator.Validate(Command(ValidEmail, ValidPassword));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmailWithSpacesAndUppercase_IsNormalizedAndValid()
    {
        var result = _validator.Validate(Command(" User@Example.COM ", ValidPassword));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyEmail_ReturnsRequiredMessage(string? email)
    {
        AssertSingleError(
            Command(email, ValidPassword),
            "email",
            "Enter your email address.");
    }

    [Theory]
    [InlineData("user")]
    [InlineData("@example.com")]
    [InlineData("user@example")]
    [InlineData("user@@example.com")]
    [InlineData("us@er@example.com")]
    [InlineData("us er@example.com")]
    [InlineData("user@exa\tmple.com")]
    public void Validate_EmailWithInvalidFormat_ReturnsInvalidMessage(string email)
    {
        AssertSingleError(
            Command(email, ValidPassword),
            "email",
            "Enter a valid email address, for example name@company.com.");
    }

    [Fact]
    public void Validate_EmailLongerThan254_ReturnsInvalidMessage()
    {
        var email = new string('a', 243) + "@example.com";
        Assert.Equal(255, email.Length);

        AssertSingleError(
            Command(email, ValidPassword),
            "email",
            "Enter a valid email address, for example name@company.com.");
    }

    [Fact]
    public void Validate_EmailOf254Characters_IsValid()
    {
        var email = new string('a', 242) + "@example.com";

        Assert.True(_validator.Validate(Command(email, ValidPassword)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_EmptyPassword_ReturnsRequiredMessage(string? password)
    {
        AssertSingleError(
            Command(ValidEmail, password),
            "password",
            "Enter your password.");
    }

    [Fact]
    public void Validate_PasswordLongerThan128_ReturnsTooLongMessage()
    {
        AssertSingleError(
            Command(ValidEmail, new string('p', 129)),
            "password",
            "Use 128 characters or fewer.");
    }

    [Fact]
    public void Validate_PasswordOf128CharactersOrWhitespaceOnly_IsValid()
    {
        Assert.True(_validator.Validate(Command(ValidEmail, new string('p', 128))).IsValid);
        Assert.True(_validator.Validate(Command(ValidEmail, "   ")).IsValid);
    }

    [Fact]
    public void Validate_BothFieldsEmpty_ReturnsOneMessagePerField()
    {
        var result = _validator.Validate(Command(null, null));

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.PropertyName == "email");
        Assert.Contains(result.Errors, error => error.PropertyName == "password");
    }

    private static SignInCommand Command(string? email, string? password) =>
        new(email, password, RememberMe: false, ClientIp: null);

    private void AssertSingleError(SignInCommand command, string key, string message)
    {
        var result = _validator.Validate(command);

        var error = Assert.Single(result.Errors);
        Assert.Equal(key, error.PropertyName);
        Assert.Equal(message, error.ErrorMessage);
    }
}
