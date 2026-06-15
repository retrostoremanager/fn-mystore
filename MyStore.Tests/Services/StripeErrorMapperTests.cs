using FluentAssertions;
using MyStore.Services;
using Stripe;
using Xunit;

namespace MyStore.Tests.Services;

public class StripeErrorMapperTests
{
    [Theory]
    [InlineData("expired_card", "Your card has expired. Please use a different card.")]
    [InlineData("card_declined", "Your card was declined. Please try a different card or contact your bank.")]
    [InlineData("incorrect_cvc", "The security code (CVC) is incorrect. Please check and try again.")]
    [InlineData("insufficient_funds", "Your card has insufficient funds. Please try a different card.")]
    public void ToUserFriendlyMessage_WithDeclineCode_ReturnsMappedMessage(string declineCode, string expectedMessage)
    {
        var stripeError = new StripeError { DeclineCode = declineCode, Message = "Technical message" };
        var ex = new StripeException(System.Net.HttpStatusCode.BadRequest, stripeError, "Technical");

        var result = StripeErrorMapper.ToUserFriendlyMessage(ex);

        result.Should().Be(expectedMessage);
    }

    [Theory]
    [InlineData("card_declined", "Your card was declined. Please try a different card or contact your bank.")]
    [InlineData("expired_card", "Your card has expired. Please use a different card.")]
    [InlineData("api_connection_error", "We couldn't connect to the payment processor. Please try again in a moment.")]
    public void ToUserFriendlyMessage_WithCode_ReturnsMappedMessage(string code, string expectedMessage)
    {
        var stripeError = new StripeError { Code = code, Message = "Technical message" };
        var ex = new StripeException(System.Net.HttpStatusCode.BadRequest, stripeError, "Technical");

        var result = StripeErrorMapper.ToUserFriendlyMessage(ex);

        result.Should().Be(expectedMessage);
    }

    [Fact]
    public void ToUserFriendlyMessage_WithNullStripeError_ReturnsFallbackMessage()
    {
        var ex = new StripeException("Technical error message");

        var result = StripeErrorMapper.ToUserFriendlyMessage(ex);

        result.Should().Be("Technical error message");
    }
}
