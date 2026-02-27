using Stripe;

namespace MyStore.Services;

/// <summary>
/// Maps Stripe API errors to user-friendly messages.
/// Avoids exposing technical details to end users.
/// </summary>
public static class StripeErrorMapper
{
    /// <summary>
    /// Converts a StripeException to a user-friendly error message.
    /// </summary>
    public static string ToUserFriendlyMessage(StripeException ex)
    {
        var code = ex.StripeError?.Code;
        var declineCode = ex.StripeError?.DeclineCode;

        if (!string.IsNullOrEmpty(declineCode))
        {
            return declineCode.ToLowerInvariant() switch
            {
                "expired_card" => "Your card has expired. Please use a different card.",
                "incorrect_cvc" or "invalid_cvc" => "The security code (CVC) is incorrect. Please check and try again.",
                "incorrect_number" or "invalid_number" => "The card number is invalid. Please check and try again.",
                "card_declined" => "Your card was declined. Please try a different card or contact your bank.",
                "insufficient_funds" => "Your card has insufficient funds. Please try a different card.",
                "authentication_required" => "Additional verification is required. Please try again or use a different card.",
                "processing_error" => "There was a problem processing your card. Please try again.",
                "rate_limit_exceeded" => "Too many attempts. Please wait a moment and try again.",
                _ => "Your card was declined. Please try a different card or contact your bank."
            };
        }

        if (!string.IsNullOrEmpty(code))
        {
            return code.ToLowerInvariant() switch
            {
                "card_declined" => "Your card was declined. Please try a different card or contact your bank.",
                "expired_card" => "Your card has expired. Please use a different card.",
                "incorrect_cvc" or "invalid_cvc" => "The security code (CVC) is incorrect. Please check and try again.",
                "incorrect_number" or "invalid_number" => "The card number is invalid. Please check and try again.",
                "resource_missing" => "The payment method could not be found. Please try adding your card again.",
                "invalid_request_error" => "There was a problem with your request. Please check your information and try again.",
                "api_connection_error" => "We couldn't connect to the payment processor. Please try again in a moment.",
                "api_error" => "The payment processor is temporarily unavailable. Please try again in a moment.",
                _ => ex.Message ?? "Your card could not be processed. Please try again or use a different card."
            };
        }

        return ex.Message ?? "Your card could not be processed. Please try again or use a different card.";
    }
}
